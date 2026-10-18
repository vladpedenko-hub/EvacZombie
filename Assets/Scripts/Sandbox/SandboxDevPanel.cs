using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// feature/sandbox-level: dev-only tuning/stress-test panel, built entirely in code (RuntimeUiKit —
// no scene/prefab edits), same spirit as Meta/UI. Injected into the Gameplay scene on load; checks
// LevelManager.currentData.isSandboxLevel one frame later (Awake/Start ordering between this and
// LevelManager/CardManager isn't guaranteed, same reasoning as LevelManager's own
// StartTutorialNextFrame) and quietly self-destructs if this isn't the sandbox level. Every effect
// here (deck override, noCooldowns, infiniteSupply, wave pause) is gated behind that same check, so
// a normal level is never touched by any of this.
//
// feature/hud-sandbox-ui: rebuilt for a phone screen -- closed by default behind one big "DEV"
// button, a big two-column button grid inside a per-tab ScrollRect (General | Cards | AI / Tuning
// | Visual), everything >=96px tall / font >=30 at the 1080 reference. Every control that existed
// before this pass is still here, just moved into a tab's grid instead of a flat row.
public class SandboxDevPanel : MonoBehaviour
{
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Init()
	{
		SceneManager.sceneLoaded -= OnSceneLoaded;
		SceneManager.sceneLoaded += OnSceneLoaded;
	}

	private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		if (scene.name != "Gameplay") return;

		GameObject host = new GameObject("[SandboxDevPanel]");
		SceneManager.MoveGameObjectToScene(host, scene); // dies with the Gameplay scene
		host.AddComponent<SandboxDevPanel>();
	}

	private SandboxConfig config;
	private bool active;

	// Restore-on-exit bookkeeping for PlayerProfile.ownedCardsProgress (Part 3: max level,
	// sandbox only -- never written back to the save).
	private readonly List<CardProgress> injectedProgress = new List<CardProgress>();
	private readonly Dictionary<CardProgress, int> originalLevels = new Dictionary<CardProgress, int>();

	private bool noCooldowns;
	private bool infiniteSupply;
	private bool spawnOnTapActive;
	private bool wavesPausedUi;

	private List<CrowdTuning> tuningPresets;
	private int tuningPresetIndex;

	private List<LevelPalette> palettePresets;
	private int palettePresetIndex;

	// feature/heli-rework: "Old feel"/"New feel" toggles HeliTuning fields in place (no separate
	// assets, unlike the LevelPalette presets above -- see CycleHeliPreset).
	private bool heliOldFeel;
	private TextMeshProUGUI heliPresetLabel;

	// feature/barricade-feel: "Old feel"/"New feel" flips BarricadeFeelTuning.legacyFeelEnabled in
	// place (same spirit as heliOldFeel above -- one bool on the shared asset, not separate presets).
	private bool barricadeOldFeel;
	private TextMeshProUGUI barricadeFeelLabel;

	// feature/sniper-feel: "Old feel"/"New feel" toggles SniperFeelTuning fields in place, same
	// pattern as heliOldFeel (Old = pre-feature aim timing/targeting/visuals, byte-for-byte).
	private bool sniperOldFeel;
	private TextMeshProUGUI sniperFeelLabel;

	// feature/bait-rework: "Old"/"New" flips BaitTuning.legacyFeelEnabled in place (same spirit as
	// barricadeOldFeel -- one bool on the shared asset, not separate presets).
	private bool baitOldFeel;
	private TextMeshProUGUI baitFeelLabel;

	// feature/card-scale: cycles CardScaleTuning between Auto and 4 fixed manual multipliers.
	// -1 = Auto (autoFromStreets = true); 0..3 = index into cardScalePresets (autoFromStreets =
	// false, manualMult = that value). Re-runs CardScale.OnLevelLoaded immediately so the change
	// applies live (to the drag ring and to newly placed cards) without a level reload.
	private static readonly float[] cardScalePresets = { 0.3f, 0.5f, 0.75f, 1f };
	private int cardScalePresetIndex = -1;
	private TextMeshProUGUI cardScaleLabel;

	// feature/bomb-combo: "Old"/"New" flips ComboTuning.legacyFeelEnabled plus useStreetBlast/
	// sparesOwnUnits in place (same spirit as barricadeOldFeel/sniperOldFeel's multi-field presets).
	private bool bombOldFeel;
	private TextMeshProUGUI bombFeelLabel;

	// fix/civilian-flee: "Old"/"New" flips CrowdTuning.civAiLegacyBehavior in place on the
	// currently active tuning asset (same spirit as barricadeOldFeel/baitOldFeel) -- every spawned
	// Human/Scientist already shares that one asset reference, so no per-instance loop is needed.
	private bool civAiOldFeel;
	private TextMeshProUGUI civAiFeelLabel;

	// feature/hud-sandbox-ui (step 4): every CardData the Cards tab knows about, and which of them
	// are currently enabled -- persisted in PlayerPrefs (sandbox only), applied through the
	// existing CardManager.SandboxAllCards + RefreshDeck path.
	private const string EnabledCardsPrefKey = "Sandbox_EnabledCards_v1";
	private List<CardData> allKnownCards;
	private readonly HashSet<string> enabledCardNames = new HashSet<string>();
	private readonly Dictionary<string, Button> cardToggleButtons = new Dictionary<string, Button>();
	private static readonly Color CardOnColor = new Color(0.22f, 0.55f, 0.28f, 0.95f);
	private static readonly Color CardOffColor = new Color(0.25f, 0.25f, 0.3f, 0.9f);

	// feature/hud-sandbox-ui (step 3): closed by default behind one big DEV button; the panel
	// itself is a two-column grid per tab inside a ScrollRect, stopping well above the card row.
	private RectTransform panelRoot;
	private bool panelOpen;
	private RectTransform[] tabPages;
	private int activeTab;

	private TextMeshProUGUI countersText;
	private TextMeshProUGUI presetLabel;
	private TextMeshProUGUI paletteLabel;
	private Button pauseButton;
	private Button noCooldownsButton;
	private Button infiniteSupplyButton;
	private Button spawnOnTapButton;
	private Button gizmoButton;
	private Button overlayButton;

	private float fps;
	private float counterRefreshTimer;

	private void Awake()
	{
		StartCoroutine(InitAfterLevelLoad());
	}

	private IEnumerator InitAfterLevelLoad()
	{
		yield return null; // let every Start() in the scene (LevelManager, CardManager, ...) run first

		if (LevelManager.Instance == null || LevelManager.Instance.currentData == null ||
			!LevelManager.Instance.currentData.isSandboxLevel)
		{
			Destroy(gameObject);
			yield break;
		}

		config = Resources.Load<SandboxConfig>("SandboxConfig");
		if (config == null)
		{
			Debug.LogError("[Sandbox] Resources/SandboxConfig.asset is missing.");
			Destroy(gameObject);
			yield break;
		}

		active = true;

		LoadAllKnownCards();
		LoadEnabledCardsFromPrefs();
		EnsureAllCardsMaxLevel();
		ApplyEnabledDeck();
		LoadTuningPresets();
		LoadPalettePresets();
		BuildPanel();
	}

	// ── Cards (Part 3 + feature/hud-sandbox-ui step 4) ──────────────────────────────────────

	// feature/hud-sandbox-ui (step 4): every CardData asset in the project, not just
	// SandboxConfig.allCards -- that list is curated by hand and can silently miss a card (which
	// is exactly why Helicopter *looked* missing before: it IS in allCards, the card hand just
	// scrolls off-screen with no visual hint once SandboxCardHandScroll kicks in for 8+ cards).
	// AssetDatabase enumeration only exists in the Editor (where all iteration on this project
	// actually happens); a device/non-Editor build falls back to the curated list, which still
	// covers every regular card, just not LastTransport (deliberately excluded -- a free one-shot
	// Core Loop v2 card, not a sandbox deck toggle).
	private void LoadAllKnownCards()
	{
		var found = new List<CardData>();

#if UNITY_EDITOR
		string[] guids = UnityEditor.AssetDatabase.FindAssets("t:CardData");
		foreach (string guid in guids)
		{
			string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
			CardData card = UnityEditor.AssetDatabase.LoadAssetAtPath<CardData>(path);
			if (card != null) found.Add(card);
		}
#endif

		if (found.Count == 0 && config != null && config.allCards != null)
			found.AddRange(config.allCards.Where(c => c != null));

		found.Sort((a, b) => string.Compare(a.cardName, b.cardName, System.StringComparison.OrdinalIgnoreCase));
		allKnownCards = found;
	}

	private void LoadEnabledCardsFromPrefs()
	{
		enabledCardNames.Clear();
		string saved = PlayerPrefs.GetString(EnabledCardsPrefKey, "");

		if (string.IsNullOrEmpty(saved))
		{
			// First run ever: default to everything enabled (matches the old fixed "all cards
			// active" sandbox behavior).
			foreach (CardData c in allKnownCards)
				if (c != null) enabledCardNames.Add(c.name);
			return;
		}

		foreach (string n in saved.Split(','))
			if (!string.IsNullOrEmpty(n)) enabledCardNames.Add(n);
	}

	private void SaveEnabledCardsToPrefs()
	{
		PlayerPrefs.SetString(EnabledCardsPrefKey, string.Join(",", enabledCardNames));
	}

	// feature/hud-sandbox-ui (step 4) -- was ApplySandboxDeck's progress-bookkeeping half: every
	// KNOWN card gets bumped to max level (so a later toggle-on shows it fully upgraded), whether
	// or not it's currently enabled.
	private void EnsureAllCardsMaxLevel()
	{
		CardManager.SandboxAllCardsActive = true;

		if (PlayerProfile.Instance == null) return;

		foreach (CardData card in allKnownCards)
		{
			if (card == null) continue;

			CardProgress progress = PlayerProfile.Instance.ownedCardsProgress.Find(p => p.cardId == card.name);
			if (progress == null)
			{
				progress = new CardProgress(card.name) { currentLevel = card.maxLevel };
				PlayerProfile.Instance.ownedCardsProgress.Add(progress);
				injectedProgress.Add(progress);
			}
			else
			{
				originalLevels[progress] = progress.currentLevel;
				progress.currentLevel = card.maxLevel;
			}
		}
	}

	// feature/hud-sandbox-ui (step 4) -- was ApplySandboxDeck's deck-swap half, now repeatable
	// (called again every time a Cards-tab toggle changes), through the existing
	// CardManager.SandboxAllCards + RefreshDeck path.
	private void ApplyEnabledDeck()
	{
		CardManager.SandboxAllCards = allKnownCards.Where(c => c != null && enabledCardNames.Contains(c.name)).ToList();
		CardManager.Instance?.RefreshDeck();
	}

	private void ToggleCard(string cardKey)
	{
		if (enabledCardNames.Contains(cardKey)) enabledCardNames.Remove(cardKey);
		else enabledCardNames.Add(cardKey);

		if (cardToggleButtons.TryGetValue(cardKey, out Button b)) RefreshCardButtonVisual(cardKey, b);
		SaveEnabledCardsToPrefs();
		ApplyEnabledDeck();
	}

	private void SetAllCardsEnabled(bool enabled)
	{
		enabledCardNames.Clear();
		if (enabled)
			foreach (CardData c in allKnownCards)
				if (c != null) enabledCardNames.Add(c.name);

		foreach (var kv in cardToggleButtons) RefreshCardButtonVisual(kv.Key, kv.Value);
		SaveEnabledCardsToPrefs();
		ApplyEnabledDeck();
	}

	private void RefreshCardButtonVisual(string cardKey, Button b)
	{
		Image img = b.GetComponent<Image>();
		if (img != null) img.color = enabledCardNames.Contains(cardKey) ? CardOnColor : CardOffColor;
	}

	private void RestoreRealDeck()
	{
		CardManager.SandboxAllCardsActive = false;
		CardManager.SandboxAllCards = null;

		if (PlayerProfile.Instance == null) return;

		foreach (var kv in originalLevels) kv.Key.currentLevel = kv.Value;
		foreach (CardProgress injected in injectedProgress) PlayerProfile.Instance.ownedCardsProgress.Remove(injected);
	}

	private void OnDestroy()
	{
		if (active) RestoreRealDeck();
	}

	// ── CrowdTuning presets (Resources-discovered) ──────────────────────────────────────────

	private void LoadTuningPresets()
	{
		CrowdTuning[] found = Resources.LoadAll<CrowdTuning>("");
		tuningPresets = new List<CrowdTuning>(found);

		if (tuningPresets.Count == 0) return;

		CrowdTuning active2 = CrowdFieldManager.Instance != null ? CrowdFieldManager.Instance.ActiveTuning : null;
		tuningPresetIndex = active2 != null ? Mathf.Max(0, tuningPresets.IndexOf(active2)) : 0;
	}

	private void CycleTuningPreset()
	{
		if (tuningPresets == null || tuningPresets.Count == 0) return;

		tuningPresetIndex = (tuningPresetIndex + 1) % tuningPresets.Count;
		CrowdTuning picked = tuningPresets[tuningPresetIndex];

		CrowdFieldManager.Instance?.OverrideTuning(picked);

		foreach (Zombie z in Zombie.AllZombies) if (z != null) z.crowdTuning = picked;
		foreach (Human h in Human.AllHumans) if (h != null) h.crowdTuning = picked;
		foreach (Scientist s in Scientist.AllScientists) if (s != null) s.crowdTuning = picked;

		if (presetLabel != null) presetLabel.text = "Tuning: " + picked.name;
	}

	// ── LevelPalette presets (Resources-discovered, feature/readability-palette) ────────────

	private void LoadPalettePresets()
	{
		LevelPalette[] found = Resources.LoadAll<LevelPalette>("Palettes");
		palettePresets = new List<LevelPalette>(found);

		if (palettePresets.Count == 0) return;

		LevelPalette active = LevelPaletteService.Active;
		palettePresetIndex = active != null ? Mathf.Max(0, palettePresets.IndexOf(active)) : 0;
	}

	private void CyclePalette()
	{
		if (palettePresets == null || palettePresets.Count == 0) return;

		palettePresetIndex = (palettePresetIndex + 1) % palettePresets.Count;
		LevelPalette picked = palettePresets[palettePresetIndex];

		LevelPaletteApplier.Apply(picked);
		LevelPaletteService.RememberSandboxChoice(picked);

		if (paletteLabel != null) paletteLabel.text = "Palette: " + picked.name;
	}

	// ── Heli feel preset (feature/heli-rework) ──────────────────────────────────────────────

	private void CycleHeliPreset()
	{
		heliOldFeel = !heliOldFeel;

		HeliTuning tuning = HeliTuning.Active;
		if (tuning != null)
		{
			if (heliOldFeel)
			{
				tuning.inboundTime = 5.6f;
				tuning.enableLiveRescan = false;
				tuning.boardingSlots = 1;
				tuning.enableHeat = false;
			}
			else
			{
				tuning.inboundTime = 2.8f;
				tuning.enableLiveRescan = true;
				tuning.boardingSlots = 3;
				tuning.enableHeat = true;
			}
		}

		if (heliPresetLabel != null) heliPresetLabel.text = "Heli: " + (heliOldFeel ? "Old feel" : "New feel");
	}

	// ── Barricade feel preset (feature/barricade-feel) ──────────────────────────────────────

	private void CycleBarricadeFeelPreset()
	{
		barricadeOldFeel = !barricadeOldFeel;

		BarricadeFeelTuning tuning = BarricadeFeelTuning.Active;
		if (tuning != null) tuning.legacyFeelEnabled = barricadeOldFeel;

		if (barricadeFeelLabel != null) barricadeFeelLabel.text = "Barricade feel: " + (barricadeOldFeel ? "Old" : "New");
	}

	// ── Sniper feel preset (feature/sniper-feel) ────────────────────────────────────────────

	private void CycleSniperFeelPreset()
	{
		sniperOldFeel = !sniperOldFeel;

		SniperFeelTuning tuning = SniperFeelTuning.Active;
		if (tuning != null)
		{
			if (sniperOldFeel)
			{
				tuning.aimDuration = 1.5f;
				tuning.firstShotAimFactor = 0.4f;
				tuning.useTargetingV2 = false;
				tuning.lockOnRing = false;
				tuning.lifetimeArc = false;
			}
			else
			{
				tuning.aimDuration = 1.1f;
				tuning.firstShotAimFactor = 0.35f;
				tuning.useTargetingV2 = true;
				tuning.lockOnRing = true;
				tuning.lifetimeArc = true;
			}
		}

		if (sniperFeelLabel != null) sniperFeelLabel.text = "Sniper feel: " + (sniperOldFeel ? "Old feel" : "New feel");
	}

	// ── Bait feel preset (feature/bait-rework) ──────────────────────────────────────────────

	private void CycleBaitFeelPreset()
	{
		baitOldFeel = !baitOldFeel;

		BaitTuning tuning = BaitTuning.Active;
		if (tuning != null) tuning.legacyFeelEnabled = baitOldFeel;

		if (baitFeelLabel != null) baitFeelLabel.text = "Bait: " + (baitOldFeel ? "Old" : "New");
	}

	// ── Card scale preset (feature/card-scale) ──────────────────────────────────────────────

	private void CycleCardScalePreset()
	{
		cardScalePresetIndex++;
		if (cardScalePresetIndex >= cardScalePresets.Length) cardScalePresetIndex = -1;

		CardScaleTuning tuning = CardScaleTuning.Active;
		if (tuning != null)
		{
			if (cardScalePresetIndex < 0)
			{
				tuning.autoFromStreets = true;
			}
			else
			{
				tuning.autoFromStreets = false;
				tuning.manualMult = cardScalePresets[cardScalePresetIndex];
			}
		}

		LevelData data = LevelManager.Instance != null ? LevelManager.Instance.currentData : null;
		CardScale.OnLevelLoaded(data);

		string label = cardScalePresetIndex < 0 ? "Auto" : "x" + cardScalePresets[cardScalePresetIndex].ToString("0.##");
		if (cardScaleLabel != null) cardScaleLabel.text = "Card scale: " + label;
	}

	// ── Bomb feel preset (feature/bomb-combo) ───────────────────────────────────────────────

	private void CycleBombFeelPreset()
	{
		bombOldFeel = !bombOldFeel;

		ComboTuning tuning = ComboTuning.Active;
		if (tuning != null)
		{
			tuning.legacyFeelEnabled = bombOldFeel;
			tuning.useStreetBlast = !bombOldFeel;
			tuning.sparesOwnUnits = !bombOldFeel;
		}

		if (bombFeelLabel != null) bombFeelLabel.text = "Bomb: " + (bombOldFeel ? "Old" : "New");
	}

	// ── Civ AI preset (fix/civilian-flee) ───────────────────────────────────────────────────

	private void CycleCivAiPreset()
	{
		civAiOldFeel = !civAiOldFeel;

		CrowdTuning tuning = CrowdFieldManager.Instance != null ? CrowdFieldManager.Instance.ActiveTuning : null;
		if (tuning != null) tuning.civAiLegacyBehavior = civAiOldFeel;

		if (civAiFeelLabel != null) civAiFeelLabel.text = "Civ AI: " + (civAiOldFeel ? "Old" : "New");
	}

	// ── UI construction (RuntimeUiKit — no scene/prefab edits) ──────────────────────────────

	// feature/hud-sandbox-ui (step 3): closed by default behind one big "DEV" button (top-right,
	// clear of TopHudV2's 0.04-0.87 band); the expanded panel stops at anchor Y 0.13, well above
	// the card row (CardManager.cardsPanel is a fixed 200px-tall, bottom-pivoted rect -- 0.13 of
	// the 1920-tall reference canvas is ~250px, a safe margin above it).
	private void BuildPanel()
	{
		Canvas canvas = RuntimeUiKit.CreateOverlayCanvas("[SandboxDevPanelCanvas]", 5000, transform);

		RectTransform safe = RuntimeUiKit.CreateRect("SafeArea", canvas.transform);
		RuntimeUiKit.Stretch(safe);
		safe.gameObject.AddComponent<SafeArea>();

		Button devButton = RuntimeUiKit.CreateButton("DevToggleButton", safe, "DEV", 32f,
			new Color(0.85f, 0.25f, 0.2f, 0.95f), Color.white);
		RectTransform devRt = devButton.GetComponent<RectTransform>();
		devRt.anchorMin = devRt.anchorMax = devRt.pivot = new Vector2(1f, 1f);
		devRt.sizeDelta = new Vector2(140f, 96f);
		devRt.anchoredPosition = new Vector2(-16f, -16f);

		panelRoot = RuntimeUiKit.CreateRect("Panel", safe);
		panelRoot.anchorMin = new Vector2(0.03f, 0.13f);
		panelRoot.anchorMax = new Vector2(0.97f, 0.90f);
		panelRoot.offsetMin = Vector2.zero;
		panelRoot.offsetMax = Vector2.zero;

		Image panelBg = panelRoot.gameObject.AddComponent<Image>();
		panelBg.color = new Color(0.06f, 0.06f, 0.08f, 0.94f);

		VerticalLayoutGroup panelVlg = panelRoot.gameObject.AddComponent<VerticalLayoutGroup>();
		panelVlg.childControlHeight = true;
		panelVlg.childControlWidth = true;
		panelVlg.childForceExpandHeight = false;
		panelVlg.childForceExpandWidth = true;
		panelVlg.padding = new RectOffset(10, 10, 10, 10);
		panelVlg.spacing = 8f;

		countersText = RuntimeUiKit.CreateText("Counters", panelRoot, "", 26f, Color.white, TextAlignmentOptions.Left);
		LayoutElement countersLe = countersText.gameObject.AddComponent<LayoutElement>();
		countersLe.preferredHeight = 170f;

		RectTransform tabBar = RuntimeUiKit.CreateRect("TabBar", panelRoot);
		HorizontalLayoutGroup tabHlg = tabBar.gameObject.AddComponent<HorizontalLayoutGroup>();
		tabHlg.spacing = 6f;
		tabHlg.childControlWidth = true;
		tabHlg.childControlHeight = true;
		tabHlg.childForceExpandWidth = true;
		LayoutElement tabLe = tabBar.gameObject.AddComponent<LayoutElement>();
		tabLe.preferredHeight = 96f;

		string[] tabNames = { "General", "Cards", "AI / Tuning", "Visual" };
		tabPages = new RectTransform[tabNames.Length];
		var tabContents = new RectTransform[tabNames.Length];

		for (int i = 0; i < tabNames.Length; i++)
		{
			int captured = i;
			Button tb = RuntimeUiKit.CreateButton("Tab_" + i, tabBar, tabNames[i], 30f,
				new Color(0.2f, 0.2f, 0.26f, 0.9f), Color.white);
			tb.onClick.AddListener(() => SetActiveTab(captured));
		}

		RectTransform tabPagesHost = RuntimeUiKit.CreateRect("TabPages", panelRoot);
		LayoutElement pagesLe = tabPagesHost.gameObject.AddComponent<LayoutElement>();
		pagesLe.flexibleHeight = 1f;

		for (int i = 0; i < tabNames.Length; i++)
			tabContents[i] = BuildTabScrollContent(tabPagesHost, "Page_" + i, out tabPages[i]);

		BuildGeneralTab(tabContents[0]);
		BuildCardsTab(tabContents[1]);
		BuildAiTab(tabContents[2]);
		BuildVisualTab(tabContents[3]);

		panelRoot.gameObject.SetActive(false);
		devButton.onClick.AddListener(() =>
		{
			panelOpen = !panelOpen;
			panelRoot.gameObject.SetActive(panelOpen);
		});

		SetActiveTab(0);
	}

	private void SetActiveTab(int index)
	{
		activeTab = index;
		for (int i = 0; i < tabPages.Length; i++)
			tabPages[i].gameObject.SetActive(i == index);
	}

	// feature/hud-sandbox-ui (step 3): a clipped viewport + ScrollRect wrapping a 2-column
	// GridLayoutGroup content -- GridCellWidthFixer keeps the cell width matched to whatever this
	// panel's actual on-screen width turns out to be (different phone aspects/CanvasScaler match
	// modes give different UI-unit widths even at the same anchor fractions), cell HEIGHT stays
	// fixed at >=96px per the brief.
	private RectTransform BuildTabScrollContent(Transform parent, string name, out RectTransform pageRoot)
	{
		pageRoot = RuntimeUiKit.CreateRect(name, parent);
		RuntimeUiKit.Stretch(pageRoot);
		pageRoot.gameObject.AddComponent<RectMask2D>();
		Image clip = pageRoot.gameObject.AddComponent<Image>();
		clip.color = new Color(0f, 0f, 0f, 0f); // invisible; only here so the viewport is raycastable for drag

		RectTransform content = RuntimeUiKit.CreateRect("Content", pageRoot);
		content.anchorMin = new Vector2(0f, 1f);
		content.anchorMax = new Vector2(1f, 1f);
		content.pivot = new Vector2(0.5f, 1f);
		content.anchoredPosition = Vector2.zero;
		content.sizeDelta = Vector2.zero;

		GridLayoutGroup grid = content.gameObject.AddComponent<GridLayoutGroup>();
		grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
		grid.constraintCount = 2;
		grid.spacing = new Vector2(10f, 10f);
		grid.padding = new RectOffset(6, 6, 6, 6);
		grid.childAlignment = TextAnchor.UpperLeft;
		content.gameObject.AddComponent<GridCellWidthFixer>().Init(grid, 2, 110f);

		ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
		fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
		fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

		ScrollRect scroll = pageRoot.gameObject.AddComponent<ScrollRect>();
		scroll.horizontal = false;
		scroll.vertical = true;
		scroll.viewport = pageRoot;
		scroll.content = content;
		scroll.movementType = ScrollRect.MovementType.Clamped;

		return content;
	}

	// feature/hud-sandbox-ui (steps 3/4): keeps a 2-column GridLayoutGroup's cell WIDTH matched to
	// its own RectTransform's actual on-screen width every frame -- computed from real layout, not
	// guessed from a reference-resolution pixel count that would be wrong on a different aspect or
	// when AspectAwareCanvasScaler flips between Match Width/Height (see CLAUDE.md). Cell height is
	// fixed (>=96px at the 1080 reference per the brief) and never recomputed.
	private class GridCellWidthFixer : MonoBehaviour
	{
		private GridLayoutGroup grid;
		private int columns;
		private float cellHeight;
		private RectTransform rt;

		public void Init(GridLayoutGroup g, int cols, float height)
		{
			grid = g;
			columns = cols;
			cellHeight = height;
			rt = GetComponent<RectTransform>();
		}

		private void LateUpdate()
		{
			if (grid == null || rt == null) return;

			float totalSpacing = grid.spacing.x * (columns - 1) + grid.padding.left + grid.padding.right;
			float width = Mathf.Max(60f, (rt.rect.width - totalSpacing) / columns);
			grid.cellSize = new Vector2(width, cellHeight);
		}
	}

	private Button AddGridButton(Transform grid, string label, UnityEngine.Events.UnityAction onClick)
	{
		Button b = RuntimeUiKit.CreateButton("Btn_" + label, grid, label, 30f,
			new Color(0.2f, 0.2f, 0.26f, 0.9f), Color.white);
		if (onClick != null) b.onClick.AddListener(onClick);
		return b;
	}

	// ── General tab: timescale, pause/restart, dev toggles ──────────────────────────────────

	private void BuildGeneralTab(Transform grid)
	{
		foreach (float scale in config.timescalePresets)
		{
			float captured = scale;
			AddGridButton(grid, "x" + scale.ToString("0.#"), () => Time.timeScale = captured);
		}

		pauseButton = AddGridButton(grid, "Pause Waves", () =>
		{
			wavesPausedUi = !wavesPausedUi;
			if (LevelManager.Instance != null) LevelManager.Instance.wavesPaused = wavesPausedUi;
			SetButtonLabel(pauseButton, wavesPausedUi ? "Resume Waves" : "Pause Waves");
		});

		AddGridButton(grid, "Restart", () =>
		{
			LevelManager.PendingSandboxLevel = config.sandboxLevel;
			GameManager.Instance?.RestartLevel();
		});

		noCooldownsButton = AddGridButton(grid, "No Cooldowns: OFF", () =>
		{
			noCooldowns = !noCooldowns;
			SetButtonLabel(noCooldownsButton, "No Cooldowns: " + (noCooldowns ? "ON" : "OFF"));
		});

		infiniteSupplyButton = AddGridButton(grid, "Infinite Supply: OFF", () =>
		{
			infiniteSupply = !infiniteSupply;
			SetButtonLabel(infiniteSupplyButton, "Infinite Supply: " + (infiniteSupply ? "ON" : "OFF"));
		});

		spawnOnTapButton = AddGridButton(grid, "Spawn On Tap: OFF", () =>
		{
			spawnOnTapActive = !spawnOnTapActive;
			SetButtonLabel(spawnOnTapButton, "Spawn On Tap: " + (spawnOnTapActive ? "ON" : "OFF"));
		});
	}

	// ── Cards tab (feature/hud-sandbox-ui step 4) ────────────────────────────────────────────

	private void BuildCardsTab(Transform grid)
	{
		AddGridButton(grid, "ALL ON", () => SetAllCardsEnabled(true));
		AddGridButton(grid, "ALL OFF", () => SetAllCardsEnabled(false));

		foreach (CardData card in allKnownCards)
		{
			if (card == null) continue;

			string key = card.name;
			string label = !string.IsNullOrEmpty(card.cardName) ? card.cardName : card.name;
			Button b = AddGridButton(grid, label, null);
			b.onClick.AddListener(() => ToggleCard(key));
			cardToggleButtons[key] = b;
			RefreshCardButtonVisual(key, b);
		}
	}

	// ── AI / Tuning tab ──────────────────────────────────────────────────────────────────────

	private void BuildAiTab(Transform grid)
	{
		string initialTuningName = tuningPresets != null && tuningPresets.Count > 0 ? tuningPresets[tuningPresetIndex].name : "(none in Resources)";
		Button presetButton = AddGridButton(grid, "Tuning: " + initialTuningName, CycleTuningPreset);
		presetLabel = presetButton.GetComponentInChildren<TextMeshProUGUI>();

		gizmoButton = AddGridButton(grid, "Grid Gizmos: OFF", () =>
		{
			CrowdTuning t = CrowdFieldManager.Instance != null ? CrowdFieldManager.Instance.ActiveTuning : null;
			if (t == null) return;
			t.drawGridGizmos = !t.drawGridGizmos;
			SetButtonLabel(gizmoButton, "Grid Gizmos: " + (t.drawGridGizmos ? "ON" : "OFF"));
		});

		overlayButton = AddGridButton(grid, "Siege Overlay: OFF", () =>
		{
			CrowdTuning t = CrowdFieldManager.Instance != null ? CrowdFieldManager.Instance.ActiveTuning : null;
			if (t == null) return;
			t.showZombieSiegeOverlay = !t.showZombieSiegeOverlay;
			SetButtonLabel(overlayButton, "Siege Overlay: " + (t.showZombieSiegeOverlay ? "ON" : "OFF"));
		});

		Button civAiButton = AddGridButton(grid, "Civ AI: New", CycleCivAiPreset);
		civAiFeelLabel = civAiButton.GetComponentInChildren<TextMeshProUGUI>();
	}

	// ── Visual tab: palette + every card's feel preset ──────────────────────────────────────

	private void BuildVisualTab(Transform grid)
	{
		string initialPaletteName = palettePresets != null && palettePresets.Count > 0 ? palettePresets[palettePresetIndex].name : "(none in Resources)";
		Button paletteButton = AddGridButton(grid, "Palette: " + initialPaletteName, CyclePalette);
		paletteLabel = paletteButton.GetComponentInChildren<TextMeshProUGUI>();

		Button heliButton = AddGridButton(grid, "Heli: New feel", CycleHeliPreset);
		heliPresetLabel = heliButton.GetComponentInChildren<TextMeshProUGUI>();

		Button barricadeButton = AddGridButton(grid, "Barricade feel: New", CycleBarricadeFeelPreset);
		barricadeFeelLabel = barricadeButton.GetComponentInChildren<TextMeshProUGUI>();

		Button sniperButton = AddGridButton(grid, "Sniper feel: New feel", CycleSniperFeelPreset);
		sniperFeelLabel = sniperButton.GetComponentInChildren<TextMeshProUGUI>();

		Button baitButton = AddGridButton(grid, "Bait: New", CycleBaitFeelPreset);
		baitFeelLabel = baitButton.GetComponentInChildren<TextMeshProUGUI>();

		Button cardScaleButton = AddGridButton(grid, "Card scale: Auto", CycleCardScalePreset);
		cardScaleLabel = cardScaleButton.GetComponentInChildren<TextMeshProUGUI>();

		Button bombButton = AddGridButton(grid, "Bomb: New", CycleBombFeelPreset);
		bombFeelLabel = bombButton.GetComponentInChildren<TextMeshProUGUI>();
	}

	private static void SetButtonLabel(Button b, string text)
	{
		TextMeshProUGUI label = b.GetComponentInChildren<TextMeshProUGUI>();
		if (label != null) label.text = text;
	}

	// ── Per-frame dev behaviors ──────────────────────────────────────────────────────────────

	private void Update()
	{
		if (!active) return;

		fps = Mathf.Lerp(fps, 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime), 0.1f);

		counterRefreshTimer -= Time.unscaledDeltaTime;
		if (counterRefreshTimer <= 0f)
		{
			counterRefreshTimer = 0.25f;
			RefreshCounters();
		}

		if (noCooldowns) ApplyNoCooldowns();
		if (infiniteSupply) ApplyInfiniteSupply();
		if (spawnOnTapActive) HandleSpawnOnTap();
	}

	private void RefreshCounters()
	{
		if (countersText == null) return;

		int civilians = Human.AllHumans.Count + Scientist.AllScientists.Count;
		int zombies = Zombie.AllZombies.Count;

		int infected = 0;
		foreach (Human h in Human.AllHumans) if (h != null && h.IsInfected) infected++;
		foreach (Scientist s in Scientist.AllScientists) if (s != null && s.IsInfected) infected++;

		int stalled = 0;
		foreach (Zombie z in Zombie.AllZombies) if (z != null && z.IsStalled) stalled++;

		countersText.text =
			$"Civilians: {civilians}\n" +
			$"Zombies: {zombies}\n" +
			$"Infected: {infected}\n" +
			$"Stalled zombies: {stalled}\n" +
			// fix/civilian-flee (step 1): deaths/infections bucketed by the civilian's state at the
			// moment of the bite -- see CivilianDeathTelemetry.
			$"Deaths -- Calm: {CivilianDeathTelemetry.DeathsCalm}  Flee: {CivilianDeathTelemetry.DeathsPanicFlee}  Evac: {CivilianDeathTelemetry.DeathsPanicEvac}  Rescue: {CivilianDeathTelemetry.DeathsRescue}  Boarding: {CivilianDeathTelemetry.DeathsBoarding}\n" +
			$"FPS: {fps:F0}";
	}

	private void ApplyNoCooldowns()
	{
		if (CardManager.Instance == null || CardManager.Instance.cardsPanel == null) return;

		foreach (Transform child in CardManager.Instance.cardsPanel)
		{
			CardUI ui = child.GetComponent<CardUI>();
			if (ui != null) ui.ClearCooldown();
		}
	}

	private void ApplyInfiniteSupply()
	{
		if (SupplyManager.Instance == null) return;
		if (SupplyManager.Instance.Current < config.infiniteSupplyAmount)
		{
			SupplyManager.Instance.Gain(config.infiniteSupplyAmount - SupplyManager.Instance.Current);
		}
	}

	private void HandleSpawnOnTap()
	{
		if (!Input.GetMouseButtonDown(0)) return;
		if (CardManager.Instance != null && CardManager.Instance.IsScreenPointOverCardsPanel(Input.mousePosition)) return;

		Camera cam = Camera.main;
		if (cam == null) return;

		Plane ground = new Plane(Vector3.up, new Vector3(0f, config.spawnOnTapGroundY, 0f));
		Ray ray = cam.ScreenPointToRay(Input.mousePosition);
		if (!ground.Raycast(ray, out float enter)) return;

		Vector3 point = ray.GetPoint(enter);

		if (ZombiePool.Instance != null)
		{
			Zombie z = ZombiePool.Instance.Get(point, Quaternion.identity);
			if (z != null) z.spawnPointName = "(sandbox tap)";
		}
	}
}
