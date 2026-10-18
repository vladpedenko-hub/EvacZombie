using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

// Core Loop v2 TOP-zone (Docs/CORE_LOOP_V2.md "top = level state, read-only, always visible").
// Exactly two rows, nothing else: row 1 is "WAVE n/total" + pills on the left (replaced by a
// "LAST CALL" countdown during Night) and "rescued/total" on the right; row 2 is a full-width
// rescue bar (fill = rescued / initial residents) with two star tick marks. A thin "PLANNING"
// strip sits underneath while Planning is active. The settings gear (SettingsButtonInjector,
// top-right corner, 96px) is left alone -- this bar stops short of it. TimerText's own text is
// blanked while v2 is active; GoalBarUI's job of hiding the legacy RescuedCounter is kept here too.
public class TopHudV2 : MonoBehaviour
{
	// Row 1 labels read at a glance mid-combat -- bigger than the shared V2Theme.LabelSize (used
	// elsewhere for smaller UI like the card cost badge) to clear the 36px dark-panel minimum with
	// real margin.
	private const float RowLabelSize = 48f;

	private static TopHudV2 instance;

	private RectTransform mainRow;
	private RectTransform row1Left;
	private RectTransform wavePillsRow;
	private TextMeshProUGUI waveLabel;
	private TextMeshProUGUI nightCountdownText;
	private readonly List<Image> wavePills = new List<Image>();
	private int builtPillCount = -1;

	private TextMeshProUGUI rescueCountText;
	private Image barFill;
	private RectTransform star1Marker;
	private RectTransform star2Marker;

	private RectTransform planningStrip;
	private TextMeshProUGUI planningStripText;

	private TextMeshProUGUI blankedTimerText;
	private GameObject legacyRescueCounter;

	private bool builtUi;
	private bool star1BannerShown;
	private bool wasNight;

	// feature/hud-sandbox-ui: lets SupplyHud (and anything else that needs to sit below this HUD)
	// anchor into the same parent/space without duplicating this class's own layout math. Null/
	// the fallback Y until this HUD has actually built itself (i.e. the first CoreLoopV2 level of
	// the session) -- callers must null-check HudParent before using BelowHudY.
	public static Transform HudParent => instance != null && instance.builtUi ? instance.mainRow.parent : null;

	public static float BelowHudY => instance != null && instance.builtUi
		? instance.planningStrip.anchoredPosition.y - instance.planningStrip.sizeDelta.y - 10f
		: -250f;

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
		mainRow.sizeDelta = new Vector2(0f, 170f);
		mainRow.anchoredPosition = new Vector2(0f, -14f);

		BuildRows();
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

	// Exactly two rows, full width, nothing else: row 1 is "WAVE n/total" + pills (left) and
	// "rescued/total" (right); row 2 is the full-width rescue bar with two star tick marks.
	private void BuildRows()
	{
		Image bg = RuntimeUiKit.CreateImage("Bg", mainRow, V2Theme.PanelBg);
		RuntimeUiKit.Stretch(bg.rectTransform);

		RectTransform row1 = Sub(mainRow, "Row1", 0f, 0.56f, 1f, 1f);
		RectTransform row2 = Sub(mainRow, "Row2", 0f, 0f, 1f, 0.44f);

		// --- Row 1: left = wave pills + label, right = rescue count ---
		row1Left = Sub(row1, "Left", 0.03f, 0f, 0.58f, 1f);
		wavePillsRow = Sub(row1Left, "Pills", 0f, 0.56f, 1f, 1f);

		waveLabel = RuntimeUiKit.CreateText("WaveLabel", row1Left, "", RowLabelSize, V2Theme.TextPrimary, TextAlignmentOptions.Left, FontStyles.Bold);
		RuntimeUiKit.SetAnchors(waveLabel.rectTransform, 0f, 0f, 1f, 0.5f);

		nightCountdownText = RuntimeUiKit.CreateText("NightCountdown", row1Left, "", V2Theme.KeyNumberSize * 0.72f, V2Theme.ThreatAmber, TextAlignmentOptions.Left, FontStyles.Bold);
		RuntimeUiKit.Stretch(nightCountdownText.rectTransform);
		nightCountdownText.gameObject.SetActive(false);

		rescueCountText = RuntimeUiKit.CreateText("RescueCount", row1, "", RowLabelSize, V2Theme.TextPrimary, TextAlignmentOptions.Right, FontStyles.Bold);
		RuntimeUiKit.SetAnchors(rescueCountText.rectTransform, 0.58f, 0f, 0.97f, 1f);

		// --- Row 2: full-width rescue bar with two star tick marks ---
		RectTransform barContainer = Sub(row2, "Bar", 0.03f, 0.2f, 0.97f, 0.8f);
		Image track = RuntimeUiKit.CreateImage("Track", barContainer, V2Theme.PanelBgLight);
		RuntimeUiKit.Stretch(track.rectTransform);

		barFill = RuntimeUiKit.CreateImage("Fill", track.transform, V2Theme.SafeGreen, rounded: false);
		barFill.type = Image.Type.Filled;
		barFill.fillMethod = Image.FillMethod.Horizontal;
		barFill.fillOrigin = (int)Image.OriginHorizontal.Left;
		barFill.fillAmount = 0f;
		RuntimeUiKit.Stretch(barFill.rectTransform);

		star1Marker = BuildStarTick(track.transform, "Star1Marker");
		star2Marker = BuildStarTick(track.transform, "Star2Marker");
	}

	// Parented to the plain background Track (not the clipped/filled Fill sprite -- Image.Type
	// .Filled only partially renders its OWN sprite, it isn't a child mask), so a raw
	// fillAmount-fraction anchor lines the tick up with the bar's coordinate space with zero
	// cross-parent math. Tick only -- no floating numeric label.
	private RectTransform BuildStarTick(Transform trackParent, string name)
	{
		Image marker = RuntimeUiKit.CreateImage(name, trackParent, V2Theme.ThreatAmber, rounded: false);
		RectTransform rt = marker.rectTransform;
		rt.anchorMin = new Vector2(0f, 0f);
		rt.anchorMax = new Vector2(0f, 1f);
		rt.sizeDelta = new Vector2(4f, 0f);
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
		UpdateRow1(data, isNight);
		UpdateRow2(gm, data);
	}

	private void UpdateRow1(LevelData data, bool isNight)
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

	private void UpdateRow2(GameManager gm, LevelData data)
	{
		if (data == null) return;

		int rescued = gm.rescuedHumans + gm.rescuedScientists;
		int total = Mathf.Max(1, gm.totalHumans + gm.totalScientists);
		int star1 = data.GetStar1Target();
		int star2 = data.GetStar2Target();

		rescueCountText.text = $"{rescued} / {total}";
		barFill.fillAmount = Mathf.Clamp01((float)rescued / total);

		SetMarker(star1Marker, Mathf.Clamp01((float)star1 / total));
		SetMarker(star2Marker, Mathf.Clamp01((float)star2 / total));

		if (!star1BannerShown && rescued >= star1)
		{
			star1BannerShown = true;
			CenterBannerV2.Show("GOAL REACHED ★1");
			Feedback.Play(FeedbackEvent.UiStar);
		}
	}

	private void SetMarker(RectTransform marker, float frac)
	{
		marker.anchorMin = new Vector2(frac, 0f);
		marker.anchorMax = new Vector2(frac, 1f);
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
