using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Core Loop v2 real Planning phase + fake-door ads (Docs/CORE_LOOP_V2.md Phase 2, HUD rebuild).
// GO + the Planning ad button live in BottomZoneV2's slots (thumb zone); the "PLANNING" strip is
// built by TopHudV2 (top zone). When LevelData.v2PlanningEnabled is false, there is no Planning
// at all -- the level starts itself right after the camera intro shot.
public class PlanningControllerV2 : MonoBehaviour
{
	private static PlanningControllerV2 instance;

	private Button goButton;
	private TextMeshProUGUI goButtonLabel;
	private Button adButton;
	private TextMeshProUGUI adButtonLabel;
	private bool builtUi;

	private Coroutine autoStartCoroutine;
	private Coroutine noPlanningStartCoroutine;
	private bool loseAdButtonInjected;
	private GameManager.GameState lastGmState = GameManager.GameState.Planning;

	private const string GoTargetId = "GoButtonV2";

	// Called from LevelManager.LoadLevel() -- the sole initialization entry point.
	public static void OnLevelLoaded(LevelData data)
	{
		if (instance == null)
		{
			var go = new GameObject("[PlanningControllerV2]");
			instance = go.AddComponent<PlanningControllerV2>();
		}

		instance.HandleLevelLoaded(data);
	}

	private void HandleLevelLoaded(LevelData data)
	{
		bool v2 = data != null && data.useCoreLoopV2;

		if (autoStartCoroutine != null) { StopCoroutine(autoStartCoroutine); autoStartCoroutine = null; }
		if (noPlanningStartCoroutine != null) { StopCoroutine(noPlanningStartCoroutine); noPlanningStartCoroutine = null; }
		loseAdButtonInjected = false;
		lastGmState = GameManager.GameState.Planning;

		BuildUiIfNeeded();
		if (!builtUi) return;

		if (!v2)
		{
			goButton.gameObject.SetActive(false);
			adButton.gameObject.SetActive(false);
			return;
		}

		if (!data.v2PlanningEnabled)
		{
			// No Planning at all: no GO, no countdown, no lock (CardUI reads v2PlanningEnabled
			// itself). The level starts on its own right after the camera intro shot.
			goButton.gameObject.SetActive(false);
			adButton.gameObject.SetActive(false);
			noPlanningStartCoroutine = StartCoroutine(NoPlanningAutoStartRoutine());
			return;
		}

		goButton.gameObject.SetActive(true);
		adButton.gameObject.SetActive(data.v2ShowAdOffer);
		if (data.v2ShowAdOffer) RefreshAdButtonLabel();

		CoreLoopV2Config cfg = CoreLoopV2Config.Active;
		float duration = cfg != null ? cfg.planningMaxDuration : 15f;
		autoStartCoroutine = StartCoroutine(AutoStartRoutine(duration));
	}

	private void BuildUiIfNeeded()
	{
		if (builtUi) return;

		BottomZoneV2.EnsureBuilt();
		if (BottomZoneV2.GoSlot == null) return;

		goButton = RuntimeUiKit.CreateButton("GoButtonV2", BottomZoneV2.GoSlot, "GO", V2Theme.KeyNumberSize * 0.6f, V2Theme.SafeGreen, V2Theme.TextOnDark);
		RuntimeUiKit.SetAnchors(goButton.GetComponent<RectTransform>(), 0.02f, 0.06f, 0.98f, 0.94f);
		goButtonLabel = goButton.GetComponentInChildren<TextMeshProUGUI>();
		goButton.onClick.AddListener(HandleGoPressed);

		TutorialTarget goTarget = goButton.gameObject.AddComponent<TutorialTarget>();
		goTarget.targetId = GoTargetId;

		adButton = RuntimeUiKit.CreateButton("AdButtonV2", BottomZoneV2.AdSlot, "", V2Theme.SmallLabelSize, new Color(0.55f, 0.4f, 0.85f), V2Theme.TextOnDark);
		RuntimeUiKit.SetAnchors(adButton.GetComponent<RectTransform>(), 0.02f, 0.18f, 0.98f, 0.82f);
		adButtonLabel = adButton.GetComponentInChildren<TextMeshProUGUI>();
		adButton.onClick.AddListener(HandleAdPressed);

		goButton.gameObject.SetActive(false);
		adButton.gameObject.SetActive(false);
		builtUi = true;
	}

	private void RefreshAdButtonLabel()
	{
		CoreLoopV2Config cfg = CoreLoopV2Config.Active;
		int grant = cfg != null ? cfg.fakeDoorAdSupplyGrant : 3;
		if (adButtonLabel != null) adButtonLabel.text = $"+{grant} ▶";
		adButton.interactable = true;
	}

	private void HandleAdPressed()
	{
		if (SupplyManager.Instance != null && SupplyManager.Instance.TryUseFakeDoorAd())
		{
			adButton.interactable = false;
			if (adButtonLabel != null) adButtonLabel.text = "USED";
			Feedback.Play(FeedbackEvent.UiReward);
		}
	}

	private void HandleGoPressed()
	{
		if (GameManager.Instance == null || GameManager.Instance.State != GameManager.GameState.Planning) return;
		if (IsGoBlockedByTutorial()) return;
		GameManager.Instance.StartGame();
	}

	// Global tutorial rule (Docs/CORE_LOOP_V2.md Phase 3): GO is non-interactive unless the
	// active tutorial step's target IS GO.
	private bool IsGoBlockedByTutorial()
	{
		return TutorialManager.Instance != null && TutorialManager.Instance.IsActive
			&& TutorialManager.Instance.CurrentTargetId != GoTargetId;
	}

	private IEnumerator AutoStartRoutine(float duration)
	{
		float remaining = duration;
		while (remaining > 0f && GameManager.Instance != null && GameManager.Instance.State == GameManager.GameState.Planning)
		{
			// Global tutorial rule: the auto-start countdown is paused while any step is active.
			bool paused = TutorialManager.Instance != null && TutorialManager.Instance.IsActive;
			if (!paused) remaining -= Time.deltaTime;

			if (goButtonLabel != null) goButtonLabel.text = "GO · " + Mathf.CeilToInt(Mathf.Max(0f, remaining));
			yield return null;
		}

		if (GameManager.Instance != null && GameManager.Instance.State == GameManager.GameState.Planning && !IsGoBlockedByTutorial())
			GameManager.Instance.StartGame();
	}

	// v2PlanningEnabled == false: start right after the camera's level-intro shot finishes, with
	// no Planning UI at all.
	private IEnumerator NoPlanningAutoStartRoutine()
	{
		while (CameraZoomPanController.Instance != null && CameraZoomPanController.Instance.IsIntroActive)
			yield return null;

		if (GameManager.Instance != null && GameManager.Instance.State == GameManager.GameState.Planning)
			GameManager.Instance.StartGame();
	}

	private void Update()
	{
		if (!builtUi) return;

		GameManager gm = GameManager.Instance;
		LevelData data = LevelManager.Instance != null ? LevelManager.Instance.currentData : null;
		bool v2 = CoreLoopV2.IsActive;

		if (v2 && data != null && data.v2PlanningEnabled && gm != null)
		{
			bool isPlanning = gm.State == GameManager.GameState.Planning;
			if (goButton.gameObject.activeSelf != isPlanning) goButton.gameObject.SetActive(isPlanning);

			bool showAd = isPlanning && data.v2ShowAdOffer;
			if (adButton.gameObject.activeSelf != showAd) adButton.gameObject.SetActive(showAd);

			goButton.interactable = !IsGoBlockedByTutorial();

			if (gm.State == GameManager.GameState.Lose && lastGmState != GameManager.GameState.Lose)
				InjectLosePopupAdButton(data);
		}
		else if (v2 && gm != null)
		{
			if (gm.State == GameManager.GameState.Lose && lastGmState != GameManager.GameState.Lose)
				InjectLosePopupAdButton(data);
		}

		lastGmState = gm != null ? gm.State : GameManager.GameState.Planning;
	}

	// Fake-door ad on the Lose popup (brief §5): queues a Supply bonus for the next attempt,
	// then restarts. Added as a plain child of the existing LosePopup GameObject rather than
	// editing UIManager.cs.
	private void InjectLosePopupAdButton(LevelData data)
	{
		if (loseAdButtonInjected) return;
		if (data == null || !data.v2ShowAdOffer) return;
		loseAdButtonInjected = true;

		Transform safeArea = FindSafeAreaContainer();
		if (safeArea == null) return;
		Transform losePopup = safeArea.Find("LosePopup");
		if (losePopup == null) return;
		if (losePopup.Find("RetryWithAdButton") != null) return;

		RectTransform btnRt = RuntimeUiKit.CreateRect("RetryWithAdButton", losePopup);
		btnRt.anchorMin = new Vector2(0.15f, 0.04f);
		btnRt.anchorMax = new Vector2(0.85f, 0.14f);
		btnRt.offsetMin = Vector2.zero;
		btnRt.offsetMax = Vector2.zero;

		CoreLoopV2Config cfg = CoreLoopV2Config.Active;
		int grant = cfg != null ? cfg.loseRetryAdSupplyGrant : 5;

		Button btn = RuntimeUiKit.CreateButton("Bg", btnRt, $"Retry +{grant} Supply ▶ (Ad)", V2Theme.SmallLabelSize, new Color(0.55f, 0.4f, 0.85f), V2Theme.TextOnDark);
		RuntimeUiKit.Stretch(btn.GetComponent<RectTransform>());

		btn.onClick.AddListener(() =>
		{
			SupplyManager.Instance?.QueueRetryBonus();
			Feedback.Play(FeedbackEvent.UiReward);
			GameManager.Instance.RestartLevel();
		});
	}

	private Transform FindSafeAreaContainer()
	{
		GameObject go = GameObject.Find("SafeAreaContainer");
		return go != null ? go.transform : null;
	}
}
