using DG.Tweening;
using System.Collections;
using TMPro;
using UnityEngine;

public class UIManager : MonoBehaviour
{
	public static UIManager Instance;

	[Header("UI References")]
	[SerializeField] private TextMeshProUGUI timerText;
	[SerializeField] private ResultPopupUI resultPopup;

	[Header("Rescue Counter")]
	[SerializeField] private TextMeshProUGUI ingameRescuedText;
	[SerializeField] private GameObject checkmarkIcon;
	[SerializeField] private Color defaultColor = Color.white;
	[SerializeField] private Color successColor = Color.green;

	[Header("Flying Text Prefab")]
	[SerializeField] private GameObject flyingTextPrefab;
	[SerializeField] private RectTransform counterTarget;

	[Header("Lose Popup")]
	[SerializeField] private GameObject losePopup;
	[SerializeField] private TextMeshProUGUI loseText;

	// feature/hud-sandbox-ui (step 5): lazily built, sandbox-only -- see ShowLosePopup.
	private UnityEngine.UI.Button sandboxKeepPlayingButton;

	[Header("Night Popup")]
	public TextMeshProUGUI centerNightText;
	public AudioSource nightSound;

	// Feedback: fire the goal sound/haptic only once per level
	private bool goalFeedbackPlayed;

	private void Awake() => Instance = this;

	private void Start()
	{
		if (centerNightText != null)
		{
			centerNightText.gameObject.SetActive(false);
			centerNightText.color = new Color(centerNightText.color.r, centerNightText.color.g, centerNightText.color.b, 0);
		}
	}

	public void UpdateTimer(float time, bool isPlanning)
	{
		if (isPlanning)
		{
			timerText.text = "Planning";
			timerText.color = Color.white;
			return;
		}

		int seconds = Mathf.CeilToInt(time);
		timerText.text = seconds.ToString("00");
		if (seconds <= 5) timerText.color = Color.red;
		else timerText.color = Color.white;

		if (seconds <= 0) timerText.text = "Night!";
	}

	public void UpdateRescuedCount(int rescued, int required, bool animate = true)
	{
		if (ingameRescuedText != null)
		{
			ingameRescuedText.text = $"{rescued} / {required}";

			if (animate)
			{
				Feedback.Play(FeedbackEvent.RescueTick);
				ingameRescuedText.transform.DOKill(true);
				ingameRescuedText.transform.DOPunchScale(Vector3.one * 0.2f, 0.3f, 10, 1f);
			}

			if (rescued >= required)
			{
				ingameRescuedText.color = successColor;
				if (animate && !goalFeedbackPlayed)
				{
					goalFeedbackPlayed = true;
					Feedback.Play(FeedbackEvent.GoalReached);
				}
				if (checkmarkIcon != null) checkmarkIcon.SetActive(true);
			}
			else
			{
				ingameRescuedText.color = defaultColor;
				goalFeedbackPlayed = false;
				if (checkmarkIcon != null) checkmarkIcon.SetActive(false);
			}
		}
	}

	public void SpawnFlyingText(Vector3 worldPosition, int amount)
	{
		Feedback.Play(FeedbackEvent.RescueDeparted, worldPosition);

		if (flyingTextPrefab == null || counterTarget == null)
		{
			GameManager.Instance.OnFlyingTextReached(amount);
			return;
		}

		Canvas uiCanvas = counterTarget.GetComponentInParent<Canvas>();
		if (uiCanvas == null)
		{
			GameManager.Instance.OnFlyingTextReached(amount);
			return;
		}

		GameObject ftObj = Instantiate(flyingTextPrefab, uiCanvas.transform);
		FlyingText ft = ftObj.GetComponent<FlyingText>();

		if (ft != null)
		{
			Vector2 screenPos = Camera.main.WorldToScreenPoint(worldPosition);
			ft.Launch($"+{amount}", screenPos, counterTarget, amount);
		}
		else
		{
			GameManager.Instance.OnFlyingTextReached(amount);
		}
	}

	public void ShowNightPopup()
	{
		if (nightSound != null && AudioManager.SfxAllowed) nightSound.Play();
		Feedback.Play(FeedbackEvent.NightStart);
		if (centerNightText != null) StartCoroutine(NightTextRoutine());
	}

	private IEnumerator NightTextRoutine()
	{
		centerNightText.gameObject.SetActive(true);
		Color c = centerNightText.color;

		for (float t = 0; t < 1; t += Time.deltaTime * 2)
		{
			centerNightText.color = new Color(c.r, c.g, c.b, t);
			centerNightText.transform.localScale = Vector3.Lerp(Vector3.one * 0.5f, Vector3.one, t);
			yield return null;
		}

		yield return new WaitForSeconds(1.5f);

		for (float t = 1; t > 0; t -= Time.deltaTime)
		{
			centerNightText.color = new Color(c.r, c.g, c.b, t);
			yield return null;
		}
		centerNightText.gameObject.SetActive(false);
	}

	// feature/hud-sandbox-ui (step 5): reason-specific English title (was a single static,
	// untranslated Cyrillic string baked into the scene's LoseText). Sandbox levels additionally
	// get a "Keep Playing" button (built once at runtime, same no-scene-edit pattern as
	// SandboxButtonInjector) alongside the pre-existing Restart/Menu buttons -- non-sandbox levels
	// are untouched, same two buttons as before.
	public void ShowLosePopup(GameManager.LoseReason reason)
	{
		Feedback.Play(FeedbackEvent.Lose);

		if (loseText != null) loseText.text = GetLoseTitle(reason);

		bool isSandbox = LevelManager.Instance != null && LevelManager.Instance.currentData != null
			&& LevelManager.Instance.currentData.isSandboxLevel;
		if (isSandbox) EnsureSandboxKeepPlayingButton();
		if (sandboxKeepPlayingButton != null) sandboxKeepPlayingButton.gameObject.SetActive(isSandbox);

		if (losePopup != null) losePopup.SetActive(true);
	}

	private static string GetLoseTitle(GameManager.LoseReason reason)
	{
		switch (reason)
		{
			case GameManager.LoseReason.AllCiviliansLost: return "ALL CIVILIANS LOST";
			case GameManager.LoseReason.TimeIsUp: return "TIME IS UP";
			default: return "GOAL UNREACHABLE";
		}
	}

	private void EnsureSandboxKeepPlayingButton()
	{
		if (sandboxKeepPlayingButton != null || losePopup == null) return;

		sandboxKeepPlayingButton = RuntimeUiKit.CreateButton("SandboxKeepPlayingButton", losePopup.transform,
			"KEEP PLAYING", 28f, new Color(0.2f, 0.6f, 0.3f, 0.9f), Color.white);

		RectTransform rt = sandboxKeepPlayingButton.GetComponent<RectTransform>();
		rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
		rt.sizeDelta = new Vector2(240f, 64f);
		rt.anchoredPosition = new Vector2(0f, -90f); // below the existing Restart/Menu row

		sandboxKeepPlayingButton.onClick.AddListener(() =>
		{
			GameManager.Instance?.ContinuePlayingAfterSandboxLose();
			if (losePopup != null) losePopup.SetActive(false);
		});
	}

	public void ShowResultPopup(
	int rescuedHumans,
	int rescuedTotal,
	int rescuedScientists,
	CardData reward = null,
	bool isPerfect = false,
	int earnedStars = 0,
	int previousBestStars = 0)
	{
		Feedback.Play(isPerfect ? FeedbackEvent.PerfectClear : FeedbackEvent.Win);

		if (resultPopup != null)
		{
			resultPopup.Show(
				rescuedHumans,
				rescuedTotal,
				rescuedScientists,
				reward,
				isPerfect,
				earnedStars,
				previousBestStars
			);
		}
	}
}