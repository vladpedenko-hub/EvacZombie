using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Core Loop v2 real Planning phase + fake-door ads (Docs/CORE_LOOP_V2.md Phase 2). Built entirely
// in code, parented into Gameplay.unity/Canvas/SafeAreaContainer (found by name -- a plain scene
// GameObject, not a prefab instance). Scene-scoped: destroyed automatically when Gameplay unloads.
public class PlanningControllerV2 : MonoBehaviour
{
	private static PlanningControllerV2 instance;

	private RectTransform root;
	private Button goButton;
	private TextMeshProUGUI countdownText;
	private Button adButton;
	private TextMeshProUGUI adButtonLabel;

	private Coroutine autoStartCoroutine;
	private bool builtUi;
	private bool loseAdButtonInjected;
	private GameManager.GameState lastGmState = GameManager.GameState.Planning;

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
		loseAdButtonInjected = false;
		lastGmState = GameManager.GameState.Planning;

		if (!v2)
		{
			if (builtUi) root.gameObject.SetActive(false);
			return;
		}

		BuildUiIfNeeded();
		root.gameObject.SetActive(true);

		bool offerAd = data.offerSupplyAd;
		adButton.gameObject.SetActive(offerAd);
		if (offerAd) RefreshAdButtonLabel();

		CoreLoopV2Config cfg = CoreLoopV2Config.Active;
		float duration = cfg != null ? cfg.planningMaxDuration : 15f;
		autoStartCoroutine = StartCoroutine(AutoStartRoutine(duration));
	}

	private void BuildUiIfNeeded()
	{
		if (builtUi) return;

		Transform safeArea = FindSafeAreaContainer();
		root = RuntimeUiKit.CreateRect("PlanningPanelV2", safeArea);
		root.anchorMin = new Vector2(0.5f, 0.5f);
		root.anchorMax = new Vector2(0.5f, 0.5f);
		root.pivot = new Vector2(0.5f, 0.5f);
		root.sizeDelta = new Vector2(620f, 320f);
		root.anchoredPosition = new Vector2(0f, 120f);

		TextMeshProUGUI planningLabel = RuntimeUiKit.CreateText("PlanningLabel", root, "PLANNING", 28f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
		RuntimeUiKit.SetAnchors(planningLabel.rectTransform, 0f, 0.78f, 1f, 1f);

		countdownText = RuntimeUiKit.CreateText("Countdown", root, "", 22f, new Color(1f, 0.85f, 0.4f), TextAlignmentOptions.Center);
		RuntimeUiKit.SetAnchors(countdownText.rectTransform, 0f, 0.6f, 1f, 0.78f);

		goButton = RuntimeUiKit.CreateButton("GoButton", root, "GO", 40f, new Color(0.25f, 0.75f, 0.35f), Color.white);
		RuntimeUiKit.SetAnchors(goButton.GetComponent<RectTransform>(), 0.2f, 0.12f, 0.8f, 0.58f);
		goButton.onClick.AddListener(HandleGoPressed);

		adButton = RuntimeUiKit.CreateButton("AdButton", root, "", 20f, new Color(0.55f, 0.4f, 0.85f), Color.white);
		RuntimeUiKit.SetAnchors(adButton.GetComponent<RectTransform>(), 0.08f, 0f, 0.92f, 0.1f);
		adButtonLabel = adButton.GetComponentInChildren<TextMeshProUGUI>();
		adButton.onClick.AddListener(HandleAdPressed);

		builtUi = true;
	}

	private void RefreshAdButtonLabel()
	{
		CoreLoopV2Config cfg = CoreLoopV2Config.Active;
		int grant = cfg != null ? cfg.fakeDoorAdSupplyGrant : 3;
		if (adButtonLabel != null) adButtonLabel.text = $"+{grant} Supply ▶ (Ad)";
		adButton.interactable = true;
	}

	private void HandleAdPressed()
	{
		if (SupplyManager.Instance != null && SupplyManager.Instance.TryUseFakeDoorAd())
		{
			adButton.interactable = false;
			if (adButtonLabel != null) adButtonLabel.text = "Used";
			Feedback.Play(FeedbackEvent.UiReward);
		}
	}

	private void HandleGoPressed()
	{
		if (GameManager.Instance == null || GameManager.Instance.State != GameManager.GameState.Planning) return;
		GameManager.Instance.StartGame();
	}

	private IEnumerator AutoStartRoutine(float duration)
	{
		float remaining = duration;
		while (remaining > 0f && GameManager.Instance != null && GameManager.Instance.State == GameManager.GameState.Planning)
		{
			if (countdownText != null) countdownText.text = "Starts in " + Mathf.CeilToInt(remaining) + "s";
			yield return null;
			remaining -= Time.deltaTime;
		}

		if (GameManager.Instance != null && GameManager.Instance.State == GameManager.GameState.Planning)
			GameManager.Instance.StartGame();
	}

	private void Update()
	{
		if (!builtUi) return;

		GameManager gm = GameManager.Instance;
		bool v2 = CoreLoopV2.IsActive;

		if (v2 && gm != null)
		{
			bool showPlanningUi = gm.State == GameManager.GameState.Planning;
			if (root.gameObject.activeSelf != showPlanningUi) root.gameObject.SetActive(showPlanningUi);

			if (gm.State == GameManager.GameState.Lose && lastGmState != GameManager.GameState.Lose)
				InjectLosePopupAdButton();
		}

		lastGmState = gm != null ? gm.State : GameManager.GameState.Planning;
	}

	// Fake-door ad on the Lose popup (brief §5): queues a Supply bonus for the next attempt,
	// then restarts. Added as a plain child of the existing LosePopup GameObject rather than
	// editing UIManager.cs (not on the brief's protected-touch list, but no reason to risk its
	// existing layout/wiring when a sibling button does the job).
	private void InjectLosePopupAdButton()
	{
		if (loseAdButtonInjected) return;
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

		Button btn = RuntimeUiKit.CreateButton("Bg", btnRt, $"Retry +{grant} Supply ▶ (Ad)", 22f, new Color(0.55f, 0.4f, 0.85f), Color.white);
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
