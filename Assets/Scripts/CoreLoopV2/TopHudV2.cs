using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

// Core Loop v2 TOP-zone (Docs/CORE_LOOP_V2.md "top = level state, read-only, always visible").
// Replaces the old WaveHudV2 + GoalBarUI with one coherent bar: wave pills + "WAVE n/total" (or,
// during Night, a big "LAST CALL" countdown) on the left; rescued/total + the progress bar with
// star markers + "ALIVE n" in the center; a thin "PLANNING" strip underneath while Planning is
// active. The settings gear (SettingsButtonInjector, top-right corner, 96px) is left alone --
// this bar stops short of it. TimerText's own text is blanked while v2 is active; GoalBarUI's job
// of hiding the legacy RescuedCounter is kept here too.
public class TopHudV2 : MonoBehaviour
{
	private static TopHudV2 instance;

	private RectTransform mainRow;
	private RectTransform leftRegion;
	private RectTransform wavePillsRow;
	private TextMeshProUGUI waveLabel;
	private TextMeshProUGUI nightCountdownText;
	private readonly List<Image> wavePills = new List<Image>();
	private int builtPillCount = -1;

	private TextMeshProUGUI rescueCountText;
	private Image barFill;
	private RectTransform star1Marker;
	private RectTransform star2Marker;
	private TextMeshProUGUI star1Label;
	private TextMeshProUGUI star2Label;
	private TextMeshProUGUI aliveText;

	private RectTransform planningStrip;
	private TextMeshProUGUI planningStripText;

	private TextMeshProUGUI blankedTimerText;
	private GameObject legacyRescueCounter;

	private bool builtUi;
	private bool star1BannerShown;
	private bool wasNight;

	// Called from LevelManager.LoadLevel() -- the sole initialization entry point.
	public static void OnLevelLoaded(LevelData data)
	{
		if (instance == null)
		{
			var go = new GameObject("[TopHudV2]");
			instance = go.AddComponent<TopHudV2>();
		}

		instance.HandleLevelLoaded(data);
	}

	private void HandleLevelLoaded(LevelData data)
	{
		bool v2 = data != null && data.useCoreLoopV2;
		star1BannerShown = false;
		wasNight = false;
		builtPillCount = -1;

		if (legacyRescueCounter == null) legacyRescueCounter = GameObject.Find("RescuedCounter");

		if (!v2)
		{
			if (builtUi) mainRow.gameObject.SetActive(false);
			if (legacyRescueCounter != null) legacyRescueCounter.SetActive(true);
			return;
		}

		BuildUiIfNeeded();
		mainRow.gameObject.SetActive(true);
		if (legacyRescueCounter != null) legacyRescueCounter.SetActive(false);

		if (LevelManager.Instance != null)
		{
			LevelManager.Instance.RegisterTopReservedUI(mainRow);
			LevelManager.Instance.RegisterTopReservedUI(planningStrip);
		}
	}

	private void BuildUiIfNeeded()
	{
		if (builtUi) return;

		GameObject timerTextGo = GameObject.Find("TimerText");
		Transform parent = timerTextGo != null ? timerTextGo.transform.parent : FindSafeAreaContainer();
		if (timerTextGo != null) blankedTimerText = timerTextGo.GetComponent<TextMeshProUGUI>();

		// Main row: stops short of the settings gear (top-right, 96px at reference res).
		mainRow = RuntimeUiKit.CreateRect("TopHudV2", parent);
		mainRow.anchorMin = new Vector2(0.04f, 1f);
		mainRow.anchorMax = new Vector2(0.87f, 1f);
		mainRow.pivot = new Vector2(0.5f, 1f);
		mainRow.sizeDelta = new Vector2(0f, 150f);
		mainRow.anchoredPosition = new Vector2(0f, -14f);

		BuildLeftRegion();
		BuildCenterRegion();
		BuildPlanningStrip(parent);

		builtUi = true;
	}

	private RectTransform Sub(Transform parent, string name, float x0, float y0, float x1, float y1)
	{
		RectTransform rt = RuntimeUiKit.CreateRect(name, parent);
		rt.anchorMin = new Vector2(x0, y0);
		rt.anchorMax = new Vector2(x1, y1);
		rt.offsetMin = Vector2.zero;
		rt.offsetMax = Vector2.zero;
		return rt;
	}

	private void BuildLeftRegion()
	{
		leftRegion = Sub(mainRow, "LeftRegion", 0f, 0f, 0.30f, 1f);

		Image bg = RuntimeUiKit.CreateImage("Bg", leftRegion, V2Theme.PanelBg);
		RuntimeUiKit.Stretch(bg.rectTransform);

		wavePillsRow = Sub(leftRegion, "Pills", 0.08f, 0.46f, 0.92f, 0.84f);

		waveLabel = RuntimeUiKit.CreateText("WaveLabel", leftRegion, "", V2Theme.LabelSize, V2Theme.TextPrimary, TextAlignmentOptions.Center, FontStyles.Bold);
		RuntimeUiKit.SetAnchors(waveLabel.rectTransform, 0.04f, 0.08f, 0.96f, 0.42f);

		nightCountdownText = RuntimeUiKit.CreateText("NightCountdown", leftRegion, "", V2Theme.KeyNumberSize * 0.72f, V2Theme.ThreatAmber, TextAlignmentOptions.Center, FontStyles.Bold);
		RuntimeUiKit.Stretch(nightCountdownText.rectTransform);
		nightCountdownText.gameObject.SetActive(false);
	}

	private void BuildCenterRegion()
	{
		RectTransform centerRegion = Sub(mainRow, "CenterRegion", 0.32f, 0f, 1f, 1f);

		RectTransform topRow = Sub(centerRegion, "TopRow", 0f, 0.5f, 1f, 1f);
		RectTransform bottomRow = Sub(centerRegion, "BottomRow", 0f, 0f, 1f, 0.46f);

		rescueCountText = RuntimeUiKit.CreateText("RescueCount", topRow, "", V2Theme.LabelSize, V2Theme.TextPrimary, TextAlignmentOptions.Left, FontStyles.Bold);
		RuntimeUiKit.SetAnchors(rescueCountText.rectTransform, 0f, 0f, 0.20f, 1f);

		RectTransform barContainer = Sub(topRow, "Bar", 0.23f, 0.15f, 1f, 0.85f);
		Image track = RuntimeUiKit.CreateImage("Track", barContainer, V2Theme.PanelBg);
		RuntimeUiKit.Stretch(track.rectTransform);

		barFill = RuntimeUiKit.CreateImage("Fill", track.transform, V2Theme.SafeGreen, rounded: false);
		barFill.type = Image.Type.Filled;
		barFill.fillMethod = Image.FillMethod.Horizontal;
		barFill.fillOrigin = (int)Image.OriginHorizontal.Left;
		barFill.fillAmount = 0f;
		RuntimeUiKit.Stretch(barFill.rectTransform);

		star1Marker = BuildStarMarker(track.transform, "Star1Marker", out star1Label);
		star2Marker = BuildStarMarker(track.transform, "Star2Marker", out star2Label);

		aliveText = RuntimeUiKit.CreateText("AliveText", bottomRow, "", V2Theme.SmallLabelSize, V2Theme.ThreatAmber, TextAlignmentOptions.Left);
		RuntimeUiKit.SetAnchors(aliveText.rectTransform, 0.23f, 0f, 0.7f, 1f);
	}

	private RectTransform BuildStarMarker(Transform trackParent, string name, out TextMeshProUGUI starLabel)
	{
		// Parented to the plain background Track (not the clipped/filled Fill sprite -- Image.Type
		// .Filled only partially renders its OWN sprite, it isn't a child mask), so both the tick
		// and its label share the bar's exact coordinate space and a raw fillAmount-fraction anchor
		// lines them up correctly with zero cross-parent math.
		Image marker = RuntimeUiKit.CreateImage(name, trackParent, V2Theme.ThreatAmber, rounded: false);
		RectTransform rt = marker.rectTransform;
		rt.anchorMin = new Vector2(0f, 0f);
		rt.anchorMax = new Vector2(0f, 1f);
		rt.sizeDelta = new Vector2(4f, 0f);

		starLabel = RuntimeUiKit.CreateText(name + "Label", trackParent, "", V2Theme.SmallLabelSize, V2Theme.ThreatAmber, TextAlignmentOptions.Center, FontStyles.Bold);
		starLabel.rectTransform.anchorMin = new Vector2(0f, 1f);
		starLabel.rectTransform.anchorMax = new Vector2(0f, 1f);
		starLabel.rectTransform.pivot = new Vector2(0.5f, 0f);
		starLabel.rectTransform.sizeDelta = new Vector2(60f, 28f);

		return rt;
	}

	private void BuildPlanningStrip(Transform parent)
	{
		planningStrip = RuntimeUiKit.CreateRect("PlanningStripV2", parent);
		planningStrip.anchorMin = new Vector2(0.04f, 1f);
		planningStrip.anchorMax = new Vector2(0.87f, 1f);
		planningStrip.pivot = new Vector2(0.5f, 1f);
		planningStrip.sizeDelta = new Vector2(0f, 46f);
		planningStrip.anchoredPosition = new Vector2(0f, -(mainRow.sizeDelta.y + 14f + 6f));

		Image bg = RuntimeUiKit.CreateImage("Bg", planningStrip, V2Theme.PanelBgLight);
		RuntimeUiKit.Stretch(bg.rectTransform);

		planningStripText = RuntimeUiKit.CreateText("Text", planningStrip, "PLANNING — set up defenses", V2Theme.SmallLabelSize, V2Theme.ThreatAmber, TextAlignmentOptions.Center, FontStyles.Bold);
		RuntimeUiKit.Stretch(planningStripText.rectTransform);
		planningStrip.gameObject.SetActive(false);
	}

	private void Update()
	{
		if (!builtUi) return;

		bool v2 = CoreLoopV2.IsActive;
		GameManager gm = GameManager.Instance;
		LevelData data = LevelManager.Instance != null ? LevelManager.Instance.currentData : null;

		if (v2 && blankedTimerText != null) blankedTimerText.text = "";

		bool show = v2 && gm != null && data != null
			&& (gm.State == GameManager.GameState.Playing || gm.State == GameManager.GameState.SuddenDeath);
		if (mainRow.gameObject.activeSelf != show) mainRow.gameObject.SetActive(show);

		bool showPlanningStrip = v2 && data != null && data.v2PlanningEnabled && gm != null && gm.State == GameManager.GameState.Planning;
		if (planningStrip.gameObject.activeSelf != showPlanningStrip) planningStrip.gameObject.SetActive(showPlanningStrip);

		if (!show) return;

		bool isNight = gm.State == GameManager.GameState.SuddenDeath;
		UpdateLeftRegion(data, isNight);
		UpdateCenterRegion(gm, data);
	}

	private void UpdateLeftRegion(LevelData data, bool isNight)
	{
		NightLastCallController nlc = NightLastCallController.Instance;

		if (isNight)
		{
			if (!wasNight)
			{
				wavePillsRow.gameObject.SetActive(false);
				waveLabel.gameObject.SetActive(false);
				nightCountdownText.gameObject.SetActive(true);
			}

			float remaining = nlc != null ? nlc.LastCallRemaining : 0f;
			nightCountdownText.text = "LAST CALL " + FormatMmSs(remaining);
			nightCountdownText.color = remaining <= 5f ? V2Theme.LossRed : V2Theme.ThreatAmber;
		}
		else
		{
			bool showWaves = data != null && data.v2ShowWaveIndicator;
			wavePillsRow.gameObject.SetActive(showWaves);
			waveLabel.gameObject.SetActive(showWaves);
			nightCountdownText.gameObject.SetActive(false);

			if (showWaves && LevelManager.Instance != null)
				UpdateWavePills(LevelManager.Instance);
		}

		wasNight = isNight;
	}

	private static string FormatMmSs(float seconds)
	{
		int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
		return (s / 60).ToString("0") + ":" + (s % 60).ToString("00");
	}

	private void UpdateWavePills(LevelManager lm)
	{
		int total = lm.V2TotalWaves;
		int current = lm.V2CurrentWaveNumber;

		if (total != builtPillCount)
		{
			foreach (Transform child in wavePillsRow) Destroy(child.gameObject);
			wavePills.Clear();

			int count = Mathf.Clamp(total, 0, 8);
			for (int i = 0; i < count; i++)
			{
				Image pill = RuntimeUiKit.CreateImage("Pill" + i, wavePillsRow, V2Theme.TextDim);
				float slot = count > 0 ? 1f / count : 1f;
				RuntimeUiKit.SetAnchors(pill.rectTransform, i * slot + slot * 0.18f, 0.1f, (i + 1) * slot - slot * 0.18f, 0.9f);
				wavePills.Add(pill);
			}

			builtPillCount = total;
		}

		for (int i = 0; i < wavePills.Count; i++)
		{
			int waveNumber = i + 1;
			if (waveNumber < current) wavePills[i].color = V2Theme.SafeGreen;       // ● cleared
			else if (waveNumber == current) wavePills[i].color = V2Theme.ThreatAmber; // ◐ current
			else wavePills[i].color = V2Theme.TextDim;                                // ○ upcoming
		}

		waveLabel.text = total > 0 ? $"WAVE {Mathf.Max(1, current)}/{total}" : "";
	}

	private void UpdateCenterRegion(GameManager gm, LevelData data)
	{
		if (data == null) return;

		int rescued = gm.rescuedHumans + gm.rescuedScientists;
		int total = Mathf.Max(1, gm.totalHumans + gm.totalScientists);
		int star1 = data.GetStar1Target();
		int star2 = data.GetStar2Target();
		int alive = Human.AllHumans.Count + Scientist.AllScientists.Count;

		rescueCountText.text = $"{rescued} / {total}";
		barFill.fillAmount = Mathf.Clamp01((float)rescued / total);

		float star1Frac = Mathf.Clamp01((float)star1 / total);
		float star2Frac = Mathf.Clamp01((float)star2 / total);
		SetMarker(star1Marker, star1Label, star1Frac, star1);
		SetMarker(star2Marker, star2Label, star2Frac, star2);

		aliveText.text = $"ALIVE {alive}";

		if (!star1BannerShown && rescued >= star1)
		{
			star1BannerShown = true;
			CenterBannerV2.Show("GOAL REACHED ★1");
			Feedback.Play(FeedbackEvent.UiStar);
		}
	}

	private void SetMarker(RectTransform marker, TextMeshProUGUI label, float frac, int value)
	{
		marker.anchorMin = new Vector2(frac, 0f);
		marker.anchorMax = new Vector2(frac, 1f);

		label.rectTransform.anchorMin = new Vector2(frac, 1f);
		label.rectTransform.anchorMax = new Vector2(frac, 1f);
		label.text = "★" + value;
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
