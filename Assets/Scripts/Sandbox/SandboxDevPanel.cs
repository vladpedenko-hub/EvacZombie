using System.Collections;
using System.Collections.Generic;
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

	private RectTransform bodyRoot;
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

		ApplySandboxDeck();
		LoadTuningPresets();
		LoadPalettePresets();
		BuildPanel();
	}

	// ── Part 3: all 8 cards at max level, sandbox only ──────────────────────────────────────

	private void ApplySandboxDeck()
	{
		CardManager.SandboxAllCardsActive = true;
		CardManager.SandboxAllCards = config.allCards;
		CardManager.Instance?.RefreshDeck();

		if (PlayerProfile.Instance == null) return;

		foreach (CardData card in config.allCards)
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

	// ── UI construction (RuntimeUiKit — no scene/prefab edits) ──────────────────────────────

	private void BuildPanel()
	{
		Canvas canvas = RuntimeUiKit.CreateOverlayCanvas("[SandboxDevPanelCanvas]", 5000, transform);

		RectTransform safe = RuntimeUiKit.CreateRect("SafeArea", canvas.transform);
		RuntimeUiKit.Stretch(safe);
		safe.gameObject.AddComponent<SafeArea>();

		Image panelBg = RuntimeUiKit.CreateImage("Panel", safe, new Color(0.06f, 0.06f, 0.08f, 0.88f));
		RectTransform panelRt = panelBg.rectTransform;
		panelRt.anchorMin = panelRt.anchorMax = panelRt.pivot = new Vector2(0f, 1f);
		panelRt.sizeDelta = new Vector2(config.panelWidth, 36f); // header-only height; body adds its own
		panelRt.anchoredPosition = new Vector2(config.panelAnchoredPosition.x, config.panelAnchoredPosition.y);

		VerticalLayoutGroup vlg = panelBg.gameObject.AddComponent<VerticalLayoutGroup>();
		vlg.childControlHeight = true;
		vlg.childControlWidth = true;
		vlg.childForceExpandHeight = false;
		vlg.childForceExpandWidth = true;
		vlg.padding = new RectOffset(8, 8, 6, 8);
		vlg.spacing = 4f;
		ContentSizeFitter fitter = panelBg.gameObject.AddComponent<ContentSizeFitter>();
		fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

		// Header: title + collapse toggle
		RectTransform header = RuntimeUiKit.CreateRect("Header", panelRt);
		LayoutElement headerLe = header.gameObject.AddComponent<LayoutElement>();
		headerLe.preferredHeight = 28f;
		Button collapseBtn = RuntimeUiKit.CreateButton("CollapseButton", header, "SANDBOX ▾", 22f,
			new Color(0.85f, 0.25f, 0.2f, 0.9f), Color.white);
		RuntimeUiKit.Stretch(collapseBtn.GetComponent<RectTransform>());

		bodyRoot = RuntimeUiKit.CreateRect("Body", panelRt);
		VerticalLayoutGroup bodyVlg = bodyRoot.gameObject.AddComponent<VerticalLayoutGroup>();
		bodyVlg.childControlHeight = true;
		bodyVlg.childControlWidth = true;
		bodyVlg.childForceExpandHeight = false;
		bodyVlg.childForceExpandWidth = true;
		bodyVlg.spacing = 4f;
		bodyRoot.gameObject.AddComponent<LayoutElement>();

		bool collapsed = false;
		collapseBtn.onClick.AddListener(() =>
		{
			collapsed = !collapsed;
			bodyRoot.gameObject.SetActive(!collapsed);
			TextMeshProUGUI label = collapseBtn.GetComponentInChildren<TextMeshProUGUI>();
			if (label != null) label.text = collapsed ? "SANDBOX ▸" : "SANDBOX ▾";
		});

		BuildTimescaleRow(bodyRoot);
		BuildTogglesRow(bodyRoot);
		BuildGizmoRow(bodyRoot);
		BuildPresetRow(bodyRoot);
		BuildPaletteRow(bodyRoot);
		BuildHeliPresetRow(bodyRoot);
		BuildBarricadeFeelPresetRow(bodyRoot);
		BuildSniperFeelPresetRow(bodyRoot);
		BuildBaitFeelPresetRow(bodyRoot);
		BuildCountersLabel(bodyRoot);
	}

	private RectTransform BuildRow(Transform parent, float height)
	{
		RectTransform row = RuntimeUiKit.CreateRect("Row", parent);
		HorizontalLayoutGroup hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
		hlg.spacing = 4f;
		hlg.childControlWidth = true;
		hlg.childControlHeight = true;
		hlg.childForceExpandWidth = true;
		LayoutElement le = row.gameObject.AddComponent<LayoutElement>();
		le.preferredHeight = height;
		return row;
	}

	private void BuildTimescaleRow(Transform parent)
	{
		RectTransform row = BuildRow(parent, 44f);

		foreach (float scale in config.timescalePresets)
		{
			float captured = scale;
			Button b = RuntimeUiKit.CreateButton("Timescale_" + scale, row, "x" + scale.ToString("0.#"), 20f,
				new Color(0.2f, 0.2f, 0.26f, 0.9f), Color.white);
			b.onClick.AddListener(() => Time.timeScale = captured);
		}
	}

	private void BuildTogglesRow(Transform parent)
	{
		RectTransform row1 = BuildRow(parent, 44f);
		pauseButton = RuntimeUiKit.CreateButton("PauseWaves", row1, "Pause Waves", 18f, new Color(0.2f, 0.2f, 0.26f, 0.9f), Color.white);
		pauseButton.onClick.AddListener(() =>
		{
			wavesPausedUi = !wavesPausedUi;
			if (LevelManager.Instance != null) LevelManager.Instance.wavesPaused = wavesPausedUi;
			SetButtonLabel(pauseButton, wavesPausedUi ? "Resume Waves" : "Pause Waves");
		});

		Button restartButton = RuntimeUiKit.CreateButton("Restart", row1, "Restart", 18f, new Color(0.2f, 0.2f, 0.26f, 0.9f), Color.white);
		restartButton.onClick.AddListener(() =>
		{
			LevelManager.PendingSandboxLevel = config.sandboxLevel;
			GameManager.Instance?.RestartLevel();
		});

		RectTransform row2 = BuildRow(parent, 44f);
		noCooldownsButton = RuntimeUiKit.CreateButton("NoCooldowns", row2, "No Cooldowns: OFF", 16f, new Color(0.2f, 0.2f, 0.26f, 0.9f), Color.white);
		noCooldownsButton.onClick.AddListener(() =>
		{
			noCooldowns = !noCooldowns;
			SetButtonLabel(noCooldownsButton, "No Cooldowns: " + (noCooldowns ? "ON" : "OFF"));
		});

		infiniteSupplyButton = RuntimeUiKit.CreateButton("InfiniteSupply", row2, "Infinite Supply: OFF", 16f, new Color(0.2f, 0.2f, 0.26f, 0.9f), Color.white);
		infiniteSupplyButton.onClick.AddListener(() =>
		{
			infiniteSupply = !infiniteSupply;
			SetButtonLabel(infiniteSupplyButton, "Infinite Supply: " + (infiniteSupply ? "ON" : "OFF"));
		});

		RectTransform row3 = BuildRow(parent, 44f);
		spawnOnTapButton = RuntimeUiKit.CreateButton("SpawnOnTap", row3, "Spawn On Tap: OFF", 16f, new Color(0.2f, 0.2f, 0.26f, 0.9f), Color.white);
		spawnOnTapButton.onClick.AddListener(() =>
		{
			spawnOnTapActive = !spawnOnTapActive;
			SetButtonLabel(spawnOnTapButton, "Spawn On Tap: " + (spawnOnTapActive ? "ON" : "OFF"));
		});
	}

	private void BuildGizmoRow(Transform parent)
	{
		RectTransform row = BuildRow(parent, 44f);

		gizmoButton = RuntimeUiKit.CreateButton("GridGizmos", row, "Grid Gizmos: OFF", 16f, new Color(0.2f, 0.2f, 0.26f, 0.9f), Color.white);
		gizmoButton.onClick.AddListener(() =>
		{
			CrowdTuning t = CrowdFieldManager.Instance != null ? CrowdFieldManager.Instance.ActiveTuning : null;
			if (t == null) return;
			t.drawGridGizmos = !t.drawGridGizmos;
			SetButtonLabel(gizmoButton, "Grid Gizmos: " + (t.drawGridGizmos ? "ON" : "OFF"));
		});

		overlayButton = RuntimeUiKit.CreateButton("SiegeOverlay", row, "Siege Overlay: OFF", 16f, new Color(0.2f, 0.2f, 0.26f, 0.9f), Color.white);
		overlayButton.onClick.AddListener(() =>
		{
			CrowdTuning t = CrowdFieldManager.Instance != null ? CrowdFieldManager.Instance.ActiveTuning : null;
			if (t == null) return;
			t.showZombieSiegeOverlay = !t.showZombieSiegeOverlay;
			SetButtonLabel(overlayButton, "Siege Overlay: " + (t.showZombieSiegeOverlay ? "ON" : "OFF"));
		});
	}

	private void BuildPresetRow(Transform parent)
	{
		RectTransform row = BuildRow(parent, 44f);

		string initialName = tuningPresets != null && tuningPresets.Count > 0 ? tuningPresets[tuningPresetIndex].name : "(none in Resources)";
		Button presetButton = RuntimeUiKit.CreateButton("TuningPreset", row, "Tuning: " + initialName, 16f, new Color(0.2f, 0.2f, 0.26f, 0.9f), Color.white);
		presetLabel = presetButton.GetComponentInChildren<TextMeshProUGUI>();
		presetButton.onClick.AddListener(CycleTuningPreset);
	}

	private void BuildPaletteRow(Transform parent)
	{
		RectTransform row = BuildRow(parent, 44f);

		string initialName = palettePresets != null && palettePresets.Count > 0 ? palettePresets[palettePresetIndex].name : "(none in Resources)";
		Button paletteButton = RuntimeUiKit.CreateButton("LevelPalette", row, "Palette: " + initialName, 16f, new Color(0.2f, 0.2f, 0.26f, 0.9f), Color.white);
		paletteLabel = paletteButton.GetComponentInChildren<TextMeshProUGUI>();
		paletteButton.onClick.AddListener(CyclePalette);
	}

	private void BuildHeliPresetRow(Transform parent)
	{
		RectTransform row = BuildRow(parent, 44f);

		Button heliButton = RuntimeUiKit.CreateButton("HeliPreset", row, "Heli: New feel", 16f, new Color(0.2f, 0.2f, 0.26f, 0.9f), Color.white);
		heliPresetLabel = heliButton.GetComponentInChildren<TextMeshProUGUI>();
		heliButton.onClick.AddListener(CycleHeliPreset);
	}

	private void BuildBarricadeFeelPresetRow(Transform parent)
	{
		RectTransform row = BuildRow(parent, 44f);

		Button barricadeButton = RuntimeUiKit.CreateButton("BarricadeFeelPreset", row, "Barricade feel: New", 16f, new Color(0.2f, 0.2f, 0.26f, 0.9f), Color.white);
		barricadeFeelLabel = barricadeButton.GetComponentInChildren<TextMeshProUGUI>();
		barricadeButton.onClick.AddListener(CycleBarricadeFeelPreset);
	}

	private void BuildSniperFeelPresetRow(Transform parent)
	{
		RectTransform row = BuildRow(parent, 44f);

		Button sniperButton = RuntimeUiKit.CreateButton("SniperFeelPreset", row, "Sniper feel: New feel", 16f, new Color(0.2f, 0.2f, 0.26f, 0.9f), Color.white);
		sniperFeelLabel = sniperButton.GetComponentInChildren<TextMeshProUGUI>();
		sniperButton.onClick.AddListener(CycleSniperFeelPreset);
	}

	private void BuildBaitFeelPresetRow(Transform parent)
	{
		RectTransform row = BuildRow(parent, 44f);

		Button baitButton = RuntimeUiKit.CreateButton("BaitFeelPreset", row, "Bait: New", 16f, new Color(0.2f, 0.2f, 0.26f, 0.9f), Color.white);
		baitFeelLabel = baitButton.GetComponentInChildren<TextMeshProUGUI>();
		baitButton.onClick.AddListener(CycleBaitFeelPreset);
	}

	private void BuildCountersLabel(Transform parent)
	{
		countersText = RuntimeUiKit.CreateText("Counters", parent, "", 16f, Color.white, TextAlignmentOptions.Left);
		LayoutElement le = countersText.gameObject.AddComponent<LayoutElement>();
		le.preferredHeight = 90f;
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
