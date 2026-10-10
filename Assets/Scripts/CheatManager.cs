using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

// The one cheat and debug system. [WHERE IT LIVES]: the Cheats object in MainMenu and Gameplay.
// [BEHAVIOR]: persistent singleton. Reveal: tap the top-right corner 5 times, at most 1.5 s apart.
// A Dev button then appears under the settings gear and opens the full list: General, Battle, Meta, Tutorial.
// Raw input only, so the corner never blocks the settings button or any other UI.
// [RELEASE]: the whole body is compiled out unless UNITY_EDITOR or DEBUG (development builds) is defined.
// [RULE]: meta cheats change state only through MetaService and CurrencyService, so events fire and the UI refreshes.
public class CheatManager : MonoBehaviour
{
#if UNITY_EDITOR || DEBUG
	public static CheatManager Instance;

	[Header("Spawn Prefabs")]
	public GameObject humanPrefab;
	public GameObject zombiePrefab;

	private const string SnapshotKey = "MetaDebugSnapshot";
	private const int UnlockTapCount = 5;
	private const float UnlockTapWindow = 1.5f;

	private Camera mainCam;
	private bool isDevMenuUnlocked;
	private bool devMenuOpen;
	private Vector2 listScroll;
	private float buttonHeight = 60f;
	private enum SpawnMode { None, Human, Zombie }
	private SpawnMode currentMode = SpawnMode.None;

	private int tapCount;
	private float tapTimer;

	[System.Serializable]
	private class Snapshot
	{
		public string meta;
		public int people;
		public int scientists;
	}

	private void Awake()
	{
		// --- SINGLETON LOGIC (PERSISTENCE) ---
		if (Instance == null)
		{
			Instance = this;
			DontDestroyOnLoad(gameObject); // Do not destroy on scene change
		}
		else
		{
			Destroy(gameObject); // Destroy duplicate if it accidentally appeared
			return;
		}
	}

	private void Update()
	{
		// Update the camera reference on each new scene
		if (mainCam == null) mainCam = Camera.main;

		// Unlock taps: the counter resets once 1.5 s pass without a tap.
		if (tapTimer > 0f) tapTimer -= Time.unscaledDeltaTime;
		else tapCount = 0;
		if (!isDevMenuUnlocked && Input.GetMouseButtonDown(0)) RegisterUnlockTap(Input.mousePosition);

		// PC HOTKEYS (WORK EVERYWHERE)
		if (Input.GetKeyDown(KeyCode.F1)) GiveMaxResources();
		if (Input.GetKeyDown(KeyCode.F7)) ResetSaves();
		if (Input.GetKeyDown(KeyCode.F8)) GiveCardsCheat();
		if (Input.GetKeyDown(KeyCode.F9)) AddHardCurrency(); // Scientists (hard currency)

		// Other cheats only work when GameManager is found (i.e. we are in a battle)
		if (GameManager.Instance != null)
		{
			if (Input.GetKeyDown(KeyCode.F2)) currentMode = SpawnMode.Human;
			if (Input.GetKeyDown(KeyCode.F3)) currentMode = SpawnMode.Zombie;
			if (Input.GetKeyDown(KeyCode.F5)) CheatWin();
		}
		if (Input.GetKeyDown(KeyCode.F6)) AddPeople(); // People (soft currency), or rescued humans in a battle

		// Spawning in a battle. Taps while the list is open belong to the list, not the map.
		if (GameManager.Instance != null && currentMode != SpawnMode.None && !devMenuOpen && Input.GetMouseButtonDown(0))
			SpawnAtTap(Input.mousePosition);
	}

	private void RegisterUnlockTap(Vector2 screenPos)
	{
		bool inCorner = screenPos.x > Screen.width * 0.85f && screenPos.y > Screen.height * 0.88f;
		if (!inCorner) return;

		tapCount++;
		tapTimer = UnlockTapWindow;
		if (tapCount >= UnlockTapCount)
		{
			isDevMenuUnlocked = true;
			tapCount = 0;
		}
	}

	private void OnGUI()
	{
		if (!isDevMenuUnlocked) return;

		int font = Mathf.Clamp(Screen.height / 55, 14, 30);
		GUI.skin.button.fontSize = font;
		GUI.skin.label.fontSize = font;
		buttonHeight = Mathf.Max(Screen.height * 0.055f, 44f);

		// Under the settings gear (which sits in the top-right corner).
		float width = Screen.width * 0.42f;
		if (GUI.Button(new Rect(Screen.width - width - 10f, Screen.height * 0.075f, width, buttonHeight),
			devMenuOpen ? "Close Dev" : "Dev"))
		{
			devMenuOpen = !devMenuOpen;
			currentMode = SpawnMode.None;
		}

		if (devMenuOpen) DrawList(new Rect(0f, 0f, Screen.width, Screen.height));
	}

	private void DrawList(Rect area)
	{
		// Opaque backdrop, so the game behind the list does not reduce its readability.
		Color previous = GUI.color;
		GUI.color = new Color(0.04f, 0.05f, 0.07f, 0.97f);
		GUI.DrawTexture(area, Texture2D.whiteTexture);
		GUI.color = previous;
		GUILayout.BeginArea(new Rect(area.x + 16f, area.y + 16f, area.width - 32f, area.height - 32f));
		if (GUILayout.Button("Close", GUILayout.Height(buttonHeight))) devMenuOpen = false;

		listScroll = GUILayout.BeginScrollView(listScroll);

		Section("GENERAL");
		Button("Unlock All (F1)", GiveMaxResources);
		Button("+100 Shards + Cash + Energy (F8)", GiveCardsCheat);
		if (GameManager.Instance == null) Button("+100 People (F6)", AddPeople);
		Button("+100 Scientists (F9)", AddHardCurrency);
		Button("RESET ALL (F7)", ResetSaves);

		Section("PLAYTEST LOG");
		Button("Export playtest_log.jsonl to clipboard", () =>
		{
			string path = PlaytestLog.ExportToClipboard();
			Debug.Log("[Cheats] Playtest log copied to clipboard (source: " + path + ")");
		});

		if (GameManager.Instance != null)
		{
			Section("BATTLE");
			Button(currentMode == SpawnMode.Human ? "> TAP MAP <" : "Spawn Human (F2)", () => currentMode = SpawnMode.Human);
			Button(currentMode == SpawnMode.Zombie ? "> TAP MAP <" : "Spawn Zombie (F3)", () => currentMode = SpawnMode.Zombie);
			Button("Win Level (F5)", CheatWin);
			Button("Rescue +10 humans (F6)", AddPeople);
		}

		DrawMeta();
		DrawTutorial();

		GUILayout.EndScrollView();
		GUILayout.EndArea();
	}

	private void DrawMeta()
	{
		Section("META");
		if (!MetaReady())
		{
			GUILayout.Label("Meta data is unavailable. Start from MainMenu.");
			return;
		}

		MetaService service = MetaRuntime.Service;
		GUILayout.Label(MetaStatus(service));

		Button("+100 People", () => AddMetaCurrency(CurrencyType.People, 100));
		Button("+10 Scientists", () => AddMetaCurrency(CurrencyType.Scientists, 10));
		Button("Simulate clear level 2", () => service.RecordLevelCleared("debug_level_2", 2));
		Button("Simulate clear level 5", () => service.RecordLevelCleared("debug_level_5", 5));

		GUILayout.Label("Set highest cleared level:");
		GUILayout.BeginHorizontal();
		foreach (int level in new[] { 0, 2, 5, 10 })
		{
			if (GUILayout.Button("= " + level, GUILayout.Height(buttonHeight))) service.DebugSetHighestCleared(level);
		}
		GUILayout.EndHorizontal();

		GUILayout.Label("Buildings:");
		foreach (BuildingDefinition building in service.Buildings)
		{
			if (building == null) continue;
			BuildingStatus status = service.GetBuildingState(building.id);
			GUILayout.Label($"{building.id}: {status.state}{(string.IsNullOrEmpty(status.reason) ? "" : " (" + status.reason + ")")}");

			GUILayout.BeginHorizontal();
			if (GUILayout.Button("Restore (normal rules)", GUILayout.Height(buttonHeight)))
				Log("Restore " + building.id, service.TryRestoreBuilding(building.id));
			if (GUILayout.Button("Reset", GUILayout.Height(buttonHeight))) service.DebugResetBuilding(building.id);
			GUILayout.EndHorizontal();
		}

		Button("Fast-forward: Laboratory restored", () => FastForwardRestore(service, "lab"));
		Button("Clear pending focus + intro flag", service.DebugClearFlags);
		Button("Reset ALL meta progress", () =>
		{
			service.DebugResetAll();
			ResetMetaTutorials();
		});
		Button("Save meta snapshot", SaveSnapshot);
		Button("Restore meta snapshot", RestoreSnapshot);
	}

	private void DrawTutorial()
	{
		Section("TUTORIAL");
		TutorialManager tutorial = TutorialManager.Instance;
		GUILayout.Label(tutorial != null && tutorial.IsActive
			? "Active tutorial step target: " + (string.IsNullOrEmpty(tutorial.CurrentTargetId) ? "(dialogue)" : tutorial.CurrentTargetId)
			: "No tutorial is running.");

		Button("Reset meta tutorials (dialogue + tour)", ResetMetaTutorials);
		Button("Clear dialogue-seen flags", ClearDialogueSeen);
		Button("Skip active tutorial (if allowed)", () => { if (tutorial != null) tutorial.SkipTutorial(); });
		Button("Abandon active tutorial (resumes later)", () => { if (tutorial != null) tutorial.AbandonTutorial(); });
		Button("Reload MainMenu (run tutorials now)", () => SceneManager.LoadScene("MainMenu"));
	}

	private void Section(string title)
	{
		GUILayout.Space(12f);
		GUILayout.Label("--- " + title + " ---");
	}

	private void Button(string label, System.Action run)
	{
		if (GUILayout.Button(label, GUILayout.Height(buttonHeight))) run();
	}

	// --- Meta helpers. Every change goes through MetaService or CurrencyService. ---

	private static bool MetaReady() => MetaRuntime.EnsureInitialized() && MetaRuntime.Service != null;

	private static string MetaStatus(MetaService service)
	{
		var sb = new System.Text.StringBuilder();
		sb.AppendLine($"People {MetaRuntime.Currency.Get(CurrencyType.People)}   Scientists {MetaRuntime.Currency.Get(CurrencyType.Scientists)}");
		sb.AppendLine($"Highest cleared {service.State.highestClearedLevel}   Pending focus '{service.State.pendingFocusBuildingId}'   Intro seen {service.State.metaIntroSeen}");
		sb.AppendLine($"Trees unlocked: {string.Join(",", service.State.unlockedTrees)}   Nodes bought: {service.State.purchasedNodes.Count}");
		sb.Append($"Attention badge: {service.HasAttentionItems()}   Dock badge: {service.HasPurchasableNode()}");
		return sb.ToString();
	}

	private static void AddMetaCurrency(CurrencyType type, int amount) =>
		MetaRuntime.Currency.Add(type, amount, CurrencySourceType.Debug);

	// Walks the building's unlock conditions and raises the highest cleared level to meet them, then pays the cost.
	private static void FastForwardRestore(MetaService service, string buildingId)
	{
		BuildingDefinition building = null;
		foreach (BuildingDefinition candidate in service.Buildings)
		{
			if (candidate != null && candidate.id == buildingId) building = candidate;
		}
		if (building == null) return;

		foreach (UnlockCondition condition in building.conditions)
		{
			if (condition is LevelsClearedCondition levels)
				service.DebugSetHighestCleared(Mathf.Max(service.State.highestClearedLevel, levels.requiredLevel));
		}

		int shortfall = building.restorationCostPeople - MetaRuntime.Currency.Get(CurrencyType.People);
		if (shortfall > 0) AddMetaCurrency(CurrencyType.People, shortfall);
		Log("Fast-forward " + buildingId, service.TryRestoreBuilding(buildingId));
	}

	// Resets the meta tutorials so they run again. Both are keyed by the Laboratory tutorial director.
	private static void ResetMetaTutorials()
	{
		TutorialProgress.Reset(MetaTutorialDirector.LabFreedId);
		TutorialProgress.Reset(MetaTutorialDirector.ResearchTourId);
		Debug.Log("[Cheats] Meta tutorials reset.");
	}

	// Dialogues are sequences with only DialogOnly steps. Clearing their flags lets them play again.
	private static void ClearDialogueSeen()
	{
		if (!MetaReady()) return;

		foreach (TutorialSequence sequence in MetaRuntime.Service.Sequences)
		{
			if (sequence == null || sequence.steps.Count == 0) continue;

			bool dialogueOnly = true;
			foreach (TutorialStep step in sequence.steps)
			{
				if (step.stepType != TutorialStepType.DialogOnly) dialogueOnly = false;
			}
			if (dialogueOnly) TutorialProgress.Reset(sequence.tutorialId);
		}
		Debug.Log("[Cheats] Dialogue-seen flags cleared.");
	}

	private static void SaveSnapshot()
	{
		if (!MetaReady()) return;

		var snapshot = new Snapshot
		{
			meta = JsonUtility.ToJson(MetaRuntime.Service.State),
			people = MetaRuntime.Currency.Get(CurrencyType.People),
			scientists = MetaRuntime.Currency.Get(CurrencyType.Scientists),
		};
		PlayerPrefs.SetString(SnapshotKey, JsonUtility.ToJson(snapshot));
		PlayerPrefs.Save();
		Debug.Log($"[Cheats] Meta snapshot saved (People {snapshot.people}, Scientists {snapshot.scientists}).");
	}

	private static void RestoreSnapshot()
	{
		if (!MetaReady()) return;
		if (!PlayerPrefs.HasKey(SnapshotKey))
		{
			Debug.LogWarning("[Cheats] No meta snapshot saved yet.");
			return;
		}

		Snapshot snapshot = JsonUtility.FromJson<Snapshot>(PlayerPrefs.GetString(SnapshotKey));
		MetaRuntime.Service.DebugReplaceState(JsonUtility.FromJson<MetaProgressionState>(snapshot.meta));
		MetaRuntime.Currency.DebugSetBalance(CurrencyType.People, snapshot.people);
		MetaRuntime.Currency.DebugSetBalance(CurrencyType.Scientists, snapshot.scientists);
		Debug.Log($"[Cheats] Meta snapshot restored (People {snapshot.people}, Scientists {snapshot.scientists}).");
	}

	private static void Log(string action, bool ok) =>
		Debug.Log($"[Cheats] {action}: {(ok ? "done" : "refused (conditions or cost not met)")}");

	// --- General and battle cheats (original behaviour, currency now through CurrencyService) ---

	private void ResetSaves()
	{
		// Meta state lives in memory for the whole session, so it is reset here too, before the scene reload.
		if (MetaReady())
		{
			MetaRuntime.Service.DebugResetAll();
			ResetMetaTutorials();
		}

		if (PlayerProfile.Instance != null) PlayerProfile.Instance.ResetProfile();
		Debug.Log("<color=red>RESET: All data erased. Restarting...</color>");
		SceneManager.LoadScene(0);
	}

	private void GiveMaxResources()
	{
		if (PlayerProfile.Instance == null) return;

		MetaRuntime.Currency.Add(CurrencyType.People, 10000, CurrencySourceType.Debug);
		MetaRuntime.Currency.Add(CurrencyType.Scientists, 1000, CurrencySourceType.Debug);

		// Energy cheat
		PlayerProfile.Instance.currentEnergy = PlayerProfile.Instance.maxEnergy;

		foreach (CardData card in PlayerProfile.Instance.allAvailableCards)
		{
			if (PlayerProfile.Instance.ownedCardsProgress.Find(p => p.cardId == card.name) == null)
				PlayerProfile.Instance.ownedCardsProgress.Add(new CardProgress(card.name));
		}

		PlayerProfile.Instance.SaveProfile();
		RefreshDeckMenu();
	}

	private void GiveCardsCheat()
	{
		if (PlayerProfile.Instance == null) return;

		MetaRuntime.Currency.Add(CurrencyType.People, 5000, CurrencySourceType.Debug);

		// Energy cheat
		PlayerProfile.Instance.currentEnergy = PlayerProfile.Instance.maxEnergy;

		foreach (CardData card in PlayerProfile.Instance.allAvailableCards)
		{
			CardProgress progress = PlayerProfile.Instance.ownedCardsProgress.Find(p => p.cardId == card.name);

			if (progress == null)
			{
				progress = new CardProgress(card.name);
				PlayerProfile.Instance.ownedCardsProgress.Add(progress);
			}

			progress.collectedShards += 100;
		}

		PlayerProfile.Instance.SaveProfile();
		RefreshDeckMenu();
		Debug.Log("<color=green>CHEAT: Added +100 card duplicates, 5000 people, and full energy!</color>");
	}

	private void RefreshDeckMenu()
	{
		var menu = FindFirstObjectByType<DeckMenuManager>();
		if (menu != null) menu.RefreshUI();
	}

	private void CheatWin() => GameManager.Instance?.EndLevel();

	// F6: rescues humans in a battle, adds People in the menu.
	private void AddPeople()
	{
		if (GameManager.Instance != null) GameManager.Instance.AddRescuedHumans(10, Vector3.zero);
		else AddMetaCurrency(CurrencyType.People, 100);
	}

	private void AddHardCurrency() => AddMetaCurrency(CurrencyType.Scientists, 100);

	private void SpawnAtTap(Vector2 screenPos)
	{
		GameObject prefab = currentMode == SpawnMode.Human ? humanPrefab : zombiePrefab;
		if (prefab == null || mainCam == null) return;

		Ray ray = mainCam.ScreenPointToRay(screenPos);
		if (Physics.Raycast(ray, out RaycastHit hit))
		{
			if (NavMesh.SamplePosition(hit.point, out NavMeshHit navHit, 5f, NavMesh.AllAreas))
				Instantiate(prefab, navHit.position, Quaternion.identity);
		}
	}
#endif
}
