using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

// Core Loop v2 visible goal + stars (Docs/CORE_LOOP_V2.md Phase 5): a rescued/total progress bar
// with ★1/★2 markers and an "alive N" label, replacing the legacy "rescued/required" counter.
// Star math itself needs no change -- GameManager.CalculateEarnedStars() already implements
// exactly the v2 rule set (★1 = rescued>=star1, ★2 = rescued>=star2, ★3 = isPerfect), generically,
// for both legacy and v2 levels. This is visuals only. Hides the legacy RescuedCounter GameObject
// (found by name, not by editing UIManager.cs) rather than leaving it stacked underneath.
public class GoalBarUI : MonoBehaviour
{
	private static GoalBarUI instance;

	private RectTransform root;
	private Image fillImage;
	private RectTransform star1Marker;
	private RectTransform star2Marker;
	private TextMeshProUGUI aliveText;
	private TextMeshProUGUI bannerText;
	private GameObject legacyCounter;

	private bool builtUi;
	private bool star1BannerShown;
	private Coroutine bannerCoroutine;

	// Called from LevelManager.LoadLevel() -- the sole initialization entry point.
	public static void OnLevelLoaded(LevelData data)
	{
		if (instance == null)
		{
			var go = new GameObject("[GoalBarUI]");
			instance = go.AddComponent<GoalBarUI>();
		}

		instance.HandleLevelLoaded(data);
	}

	private void HandleLevelLoaded(LevelData data)
	{
		bool v2 = data != null && data.useCoreLoopV2;
		star1BannerShown = false;
		if (bannerCoroutine != null) { StopCoroutine(bannerCoroutine); bannerCoroutine = null; }

		if (legacyCounter == null) legacyCounter = GameObject.Find("RescuedCounter");

		if (!v2)
		{
			if (builtUi) root.gameObject.SetActive(false);
			if (legacyCounter != null) legacyCounter.SetActive(true);
			return;
		}

		BuildUiIfNeeded();
		root.gameObject.SetActive(true);
		if (bannerText != null) bannerText.gameObject.SetActive(false);
		if (legacyCounter != null) legacyCounter.SetActive(false);

		if (LevelManager.Instance != null) LevelManager.Instance.RegisterTopReservedUI(root);
	}

	private void BuildUiIfNeeded()
	{
		if (builtUi) return;

		GameObject timerTextGo = GameObject.Find("TimerText");
		Transform parent = timerTextGo != null ? timerTextGo.transform.parent : FindSafeAreaContainer();

		root = RuntimeUiKit.CreateRect("GoalBarV2", parent);
		root.anchorMin = new Vector2(0.08f, 1f);
		root.anchorMax = new Vector2(0.92f, 1f);
		root.pivot = new Vector2(0.5f, 1f);
		root.sizeDelta = new Vector2(0f, 60f);
		root.anchoredPosition = new Vector2(0f, -150f); // just under WaveHudV2's reserved band

		Image track = RuntimeUiKit.CreateImage("Track", root, new Color(0.08f, 0.08f, 0.1f, 0.8f));
		RuntimeUiKit.SetAnchors(track.rectTransform, 0f, 0f, 1f, 0.55f);

		fillImage = RuntimeUiKit.CreateImage("Fill", track.transform, new Color(0.35f, 0.85f, 0.45f, 1f), rounded: false);
		fillImage.type = Image.Type.Filled;
		fillImage.fillMethod = Image.FillMethod.Horizontal;
		fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
		fillImage.fillAmount = 0f;
		RuntimeUiKit.Stretch(fillImage.rectTransform);

		star1Marker = BuildStarMarker(track.transform, "Star1Marker");
		star2Marker = BuildStarMarker(track.transform, "Star2Marker");

		aliveText = RuntimeUiKit.CreateText("AliveText", root, "", 18f, new Color(1f, 0.85f, 0.6f), TextAlignmentOptions.Left);
		RuntimeUiKit.SetAnchors(aliveText.rectTransform, 0f, 0.6f, 0.5f, 1f);

		bannerText = RuntimeUiKit.CreateText("Banner", root, "", 20f, new Color(1f, 0.9f, 0.3f), TextAlignmentOptions.Right, FontStyles.Bold);
		RuntimeUiKit.SetAnchors(bannerText.rectTransform, 0.5f, 0.6f, 1f, 1f);
		bannerText.gameObject.SetActive(false);

		builtUi = true;
	}

	private RectTransform BuildStarMarker(Transform parent, string name)
	{
		Image marker = RuntimeUiKit.CreateImage(name, parent, new Color(1f, 0.85f, 0.2f, 0.9f), rounded: false);
		RectTransform rt = marker.rectTransform;
		rt.anchorMin = new Vector2(0f, 0f);
		rt.anchorMax = new Vector2(0f, 1f);
		rt.sizeDelta = new Vector2(4f, 0f);
		return rt;
	}

	private void Update()
	{
		if (!builtUi) return;

		GameManager gm = GameManager.Instance;
		LevelData data = LevelManager.Instance != null ? LevelManager.Instance.currentData : null;
		bool v2 = CoreLoopV2.IsActive;

		bool show = v2 && gm != null && data != null
			&& (gm.State == GameManager.GameState.Playing || gm.State == GameManager.GameState.SuddenDeath);
		if (root.gameObject.activeSelf != show) root.gameObject.SetActive(show);
		if (!show) return;

		int rescued = gm.rescuedHumans + gm.rescuedScientists;
		int total = Mathf.Max(1, gm.totalHumans + gm.totalScientists);
		int star1 = data.GetStar1Target();
		int star2 = data.GetStar2Target();
		int alive = Human.AllHumans.Count + Scientist.AllScientists.Count;

		fillImage.fillAmount = Mathf.Clamp01((float)rescued / total);
		SetMarkerFraction(star1Marker, Mathf.Clamp01((float)star1 / total));
		SetMarkerFraction(star2Marker, Mathf.Clamp01((float)star2 / total));
		aliveText.text = $"alive {alive}";

		if (!star1BannerShown && rescued >= star1)
		{
			star1BannerShown = true;
			ShowBanner("GOAL REACHED — ★1 SECURED");
			Feedback.Play(FeedbackEvent.UiStar);
		}
	}

	private void SetMarkerFraction(RectTransform marker, float frac)
	{
		marker.anchorMin = new Vector2(frac, 0f);
		marker.anchorMax = new Vector2(frac, 1f);
	}

	private void ShowBanner(string text)
	{
		if (bannerCoroutine != null) StopCoroutine(bannerCoroutine);
		bannerCoroutine = StartCoroutine(BannerRoutine(text));
	}

	private IEnumerator BannerRoutine(string text)
	{
		bannerText.text = text;
		bannerText.gameObject.SetActive(true);
		bannerText.transform.localScale = Vector3.one * 0.7f;
		bannerText.transform.DOKill();
		bannerText.transform.DOScale(1f, 0.25f).SetEase(Ease.OutBack).SetUpdate(true);

		yield return new WaitForSeconds(2f);

		bannerText.gameObject.SetActive(false);
	}

	private Transform FindSafeAreaContainer()
	{
		GameObject go = GameObject.Find("SafeAreaContainer");
		return go != null ? go.transform : null;
	}

	private void OnDestroy()
	{
		if (instance == this) instance = null;
	}
}
