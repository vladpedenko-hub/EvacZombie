using System.Collections;
using TMPro;
using UnityEngine;
using DG.Tweening;

// Core Loop v2 wave progress HUD (Docs/CORE_LOOP_V2.md Phase 3): "WAVE n/total", the breather
// countdown + next-wave size hint, and cleared/forced banners. Replaces the visible day timer --
// TimerText's own GameObject is kept (camera framing measures it) but its text is blanked while
// v2 is active; this panel sits directly under it as a sibling. Scene-scoped, rebuilt per level.
public class WaveHudV2 : MonoBehaviour
{
	private static WaveHudV2 instance;

	private RectTransform root;
	private TextMeshProUGUI waveLabel;
	private TextMeshProUGUI breatherLabel;
	private TextMeshProUGUI hintLabel;
	private TextMeshProUGUI bannerLabel;
	private TextMeshProUGUI blankedTimerText;
	private bool builtUi;
	private Coroutine bannerCoroutine;

	// Called from LevelManager.LoadLevel() -- the sole initialization entry point.
	public static void OnLevelLoaded(LevelData data)
	{
		if (instance == null)
		{
			var go = new GameObject("[WaveHudV2]");
			instance = go.AddComponent<WaveHudV2>();
		}

		instance.HandleLevelLoaded(data);
	}

	private void HandleLevelLoaded(LevelData data)
	{
		bool v2 = data != null && data.useCoreLoopV2;

		if (!v2)
		{
			if (builtUi) root.gameObject.SetActive(false);
			return;
		}

		BuildUiIfNeeded();
		root.gameObject.SetActive(true);
		ResetBannerState();

		if (LevelManager.Instance != null)
		{
			LevelManager.Instance.OnV2Banner -= HandleBanner;
			LevelManager.Instance.OnV2Banner += HandleBanner;
			LevelManager.Instance.RegisterTopReservedUI(root);
		}
	}

	private void BuildUiIfNeeded()
	{
		if (builtUi) return;

		GameObject timerTextGo = GameObject.Find("TimerText");
		Transform parent = timerTextGo != null ? timerTextGo.transform.parent : FindSafeAreaContainer();

		root = RuntimeUiKit.CreateRect("WaveHudV2", parent);
		root.anchorMin = new Vector2(0.5f, 1f);
		root.anchorMax = new Vector2(0.5f, 1f);
		root.pivot = new Vector2(0.5f, 1f);
		root.sizeDelta = new Vector2(440f, 96f);
		root.anchoredPosition = new Vector2(0f, -50f); // just under TimerText's own 50px-tall band

		waveLabel = RuntimeUiKit.CreateText("WaveLabel", root, "", 26f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
		RuntimeUiKit.SetAnchors(waveLabel.rectTransform, 0f, 0.55f, 1f, 1f);

		breatherLabel = RuntimeUiKit.CreateText("BreatherLabel", root, "", 20f, new Color(0.6f, 0.85f, 1f), TextAlignmentOptions.Center);
		RuntimeUiKit.SetAnchors(breatherLabel.rectTransform, 0f, 0.3f, 1f, 0.55f);

		hintLabel = RuntimeUiKit.CreateText("HintLabel", root, "", 16f, new Color(0.8f, 0.8f, 0.8f), TextAlignmentOptions.Center);
		RuntimeUiKit.SetAnchors(hintLabel.rectTransform, 0f, 0.1f, 1f, 0.3f);

		bannerLabel = RuntimeUiKit.CreateText("BannerLabel", root, "", 24f, new Color(1f, 0.9f, 0.3f), TextAlignmentOptions.Center, FontStyles.Bold);
		RuntimeUiKit.SetAnchors(bannerLabel.rectTransform, 0f, 0f, 1f, 1f);
		bannerLabel.gameObject.SetActive(false);

		if (timerTextGo != null) blankedTimerText = timerTextGo.GetComponent<TextMeshProUGUI>();

		builtUi = true;
	}

	private void ResetBannerState()
	{
		if (bannerCoroutine != null) { StopCoroutine(bannerCoroutine); bannerCoroutine = null; }
		bannerLabel.gameObject.SetActive(false);
		waveLabel.gameObject.SetActive(true);
		breatherLabel.gameObject.SetActive(true);
		hintLabel.gameObject.SetActive(true);
	}

	private void Update()
	{
		if (!builtUi) return;

		bool v2 = CoreLoopV2.IsActive;
		GameManager gm = GameManager.Instance;
		LevelManager lm = LevelManager.Instance;

		// Blanked for the whole v2 session (Playing AND Night), not just while this panel shows.
		if (v2 && blankedTimerText != null) blankedTimerText.text = "";

		bool showNow = v2 && gm != null && gm.State == GameManager.GameState.Playing;
		if (root.gameObject.activeSelf != showNow) root.gameObject.SetActive(showNow);
		if (!showNow || lm == null) return;

		waveLabel.text = lm.V2TotalWaves > 0 ? $"WAVE {lm.V2CurrentWaveNumber}/{lm.V2TotalWaves}" : "";
		breatherLabel.gameObject.SetActive(lm.V2InBreather && !bannerLabel.gameObject.activeSelf);
		if (lm.V2InBreather) breatherLabel.text = "Next wave in " + Mathf.CeilToInt(Mathf.Max(0f, lm.V2BreatherRemaining)) + "s";
		hintLabel.gameObject.SetActive(lm.V2InBreather && !bannerLabel.gameObject.activeSelf);
		hintLabel.text = lm.V2InBreather ? lm.V2NextWaveHint : "";
	}

	private void HandleBanner(string text)
	{
		if (bannerCoroutine != null) StopCoroutine(bannerCoroutine);
		bannerCoroutine = StartCoroutine(BannerRoutine(text));
	}

	private IEnumerator BannerRoutine(string text)
	{
		bannerLabel.text = text;
		bannerLabel.gameObject.SetActive(true);
		waveLabel.gameObject.SetActive(false);
		breatherLabel.gameObject.SetActive(false);
		hintLabel.gameObject.SetActive(false);

		bannerLabel.transform.localScale = Vector3.one * 0.6f;
		bannerLabel.transform.DOKill();
		bannerLabel.transform.DOScale(1f, 0.25f).SetEase(Ease.OutBack).SetUpdate(true);

		yield return new WaitForSeconds(1.6f);

		bannerLabel.gameObject.SetActive(false);
		waveLabel.gameObject.SetActive(true);
	}

	private Transform FindSafeAreaContainer()
	{
		GameObject go = GameObject.Find("SafeAreaContainer");
		return go != null ? go.transform : null;
	}

	private void OnDestroy()
	{
		if (instance == this) instance = null;
		if (LevelManager.Instance != null) LevelManager.Instance.OnV2Banner -= HandleBanner;
	}
}
