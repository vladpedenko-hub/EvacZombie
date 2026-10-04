#if UNITY_EDITOR || DEBUG
using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Dev-only panel for the meta layer, so pacing and every flow can be tested without playing 25 levels.
// Compiled out of release builds. Opened from a small DEV button under the settings gear in MainMenu.
// Snapshot/Restore keeps your real save safe: take a snapshot before testing, restore it afterwards.
public static class MetaDebugInjector
{
	private const string MenuSceneName = "MainMenu";

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Init()
	{
		SceneManager.sceneLoaded -= OnSceneLoaded;
		SceneManager.sceneLoaded += OnSceneLoaded;
	}

	private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		if (scene.name != MenuSceneName) return;

		Canvas canvas = MetaUI.CreateOverlayCanvas("[MetaDebug]", 4500);
		SceneManager.MoveGameObjectToScene(canvas.gameObject, scene);

		var panel = new MetaDebugPanel();
		panel.Build(canvas.transform);

		// Top-right, under the settings gear (96 px, 24 px inset).
		Button open = MetaUI.Button("DevButton", canvas.transform, "DEV", MetaUI.ButtonSecondary,
			new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-132f, -180f), new Vector2(-24f, -128f), out _);
		open.onClick.AddListener(panel.Show);
	}
}

public class MetaDebugPanel
{
	private const string SnapshotKey = "MetaDebugSnapshot";

	[Serializable]
	private class Snapshot
	{
		public string meta;
		public int people;
		public int scientists;
	}

	private GameObject root;
	private TextMeshProUGUI status;

	private static MetaService Service => MetaRuntime.EnsureInitialized() ? MetaRuntime.Service : null;

	public void Build(Transform parent)
	{
		root = MetaUI.ImageBox("MetaDebugRoot", parent, new Color(0.03f, 0.03f, 0.05f, 0.97f),
			Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject;

		status = MetaUI.Text("Status", root.transform, "", 34, new Vector2(0.05f, 0.62f), new Vector2(0.95f, 0.96f),
			Vector2.zero, Vector2.zero, TextAlignmentOptions.TopLeft);

		var actions = new (string label, Action run)[]
		{
			("+100 People", () => Currency(CurrencyType.People, 100)),
			("+10 Scientists", () => Currency(CurrencyType.Scientists, 10)),
			("Simulate clear level 2", () => Clear(2)),
			("Simulate clear level 5", () => Clear(5)),
			("Set highest cleared = 0", () => Service?.DebugSetHighestCleared(0)),
			("Restore Laboratory (normal rules)", () => Log("Restore Laboratory", Service?.TryRestoreBuilding("lab") ?? false)),
			("Fast-forward: Laboratory restored", FastForwardLab),
			("Reset Laboratory (undo restore)", () => Service?.DebugResetBuilding("lab")),
			("Clear pending focus + intro flag", () => Service?.DebugClearFlags()),
			("Reset ALL meta progress", () => Service?.DebugResetAll()),
			("Save snapshot", SaveSnapshot),
			("Restore snapshot", RestoreSnapshot),
		};

		const float rowHeight = 0.044f;
		float top = 0.58f;
		for (int i = 0; i < actions.Length; i++)
		{
			Action run = actions[i].run;
			float y = top - i * rowHeight;
			Button button = MetaUI.Button("Action_" + i, root.transform, actions[i].label, MetaUI.ButtonSecondary,
				new Vector2(0.05f, y - rowHeight + 0.006f), new Vector2(0.95f, y), Vector2.zero, Vector2.zero, out _);
			button.onClick.AddListener(() =>
			{
				run();
				Refresh();
			});
		}

		Button close = MetaUI.Button("Close", root.transform, "CLOSE", MetaUI.ButtonPrimary,
			new Vector2(0.05f, 0.02f), new Vector2(0.95f, 0.06f), Vector2.zero, Vector2.zero, out _);
		close.onClick.AddListener(Hide);

		root.SetActive(false);
	}

	public void Show()
	{
		root.SetActive(true);
		if (Service != null)
		{
			Service.OnChanged -= Refresh; // never subscribe twice
			Service.OnChanged += Refresh;
		}
		Refresh();
	}

	public void Hide()
	{
		if (Service != null) Service.OnChanged -= Refresh;
		root.SetActive(false);
	}

	private void Refresh()
	{
		MetaService service = Service;

		// The panel dies with its scene. A handler left on the shared service would throw on every change
		// and stop the event from reaching the other subscribers, so it unsubscribes itself here.
		if (root == null)
		{
			if (service != null) service.OnChanged -= Refresh;
			return;
		}
		if (service == null || !root.activeSelf) return;

		var sb = new StringBuilder();
		sb.AppendLine($"People {MetaRuntime.Currency.Get(CurrencyType.People)}   Scientists {MetaRuntime.Currency.Get(CurrencyType.Scientists)}");
		sb.AppendLine($"Highest cleared: {service.State.highestClearedLevel}   Pending focus: {Or(service.State.pendingFocusBuildingId)}   Intro seen: {service.State.metaIntroSeen}");
		foreach (BuildingDefinition b in service.Buildings)
		{
			BuildingStatus s = service.GetBuildingState(b.id);
			sb.AppendLine($"  {b.id}: {s.state}{(string.IsNullOrEmpty(s.reason) ? "" : " (" + s.reason + ")")}");
		}
		sb.AppendLine($"Trees unlocked: {string.Join(",", service.State.unlockedTrees)}   Nodes bought: {service.State.purchasedNodes.Count}");
		sb.AppendLine($"Attention badge: {service.HasAttentionItems()}");
		status.text = sb.ToString();
	}

	private static string Or(string value) => string.IsNullOrEmpty(value) ? "-" : value;

	private static void Currency(CurrencyType type, int amount)
	{
		if (!MetaRuntime.EnsureInitialized()) return;
		MetaRuntime.Currency.Add(type, amount, CurrencySourceType.Debug);
	}

	private static void Clear(int level)
	{
		if (Service == null) return;
		Service.RecordLevelCleared("debug_level_" + level, level);
	}

	private static void FastForwardLab()
	{
		MetaService service = Service;
		if (service == null) return;

		BuildingDefinition lab = null;
		foreach (BuildingDefinition b in service.Buildings)
		{
			if (b != null && b.id == "lab") lab = b;
		}
		if (lab == null) return;

		service.DebugSetHighestCleared(Math.Max(service.State.highestClearedLevel, 2));
		int shortfall = lab.restorationCostPeople - MetaRuntime.Currency.Get(CurrencyType.People);
		if (shortfall > 0) MetaRuntime.Currency.Add(CurrencyType.People, shortfall, CurrencySourceType.Debug);
		Log("Fast-forward Laboratory", service.TryRestoreBuilding("lab"));
	}

	private static void SaveSnapshot()
	{
		MetaService service = Service;
		if (service == null) return;

		var snapshot = new Snapshot
		{
			meta = JsonUtility.ToJson(service.State),
			people = MetaRuntime.Currency.Get(CurrencyType.People),
			scientists = MetaRuntime.Currency.Get(CurrencyType.Scientists),
		};
		PlayerPrefs.SetString(SnapshotKey, JsonUtility.ToJson(snapshot));
		PlayerPrefs.Save();
		Debug.Log($"[MetaDebug] Snapshot saved (People {snapshot.people}, Scientists {snapshot.scientists}).");
	}

	private static void RestoreSnapshot()
	{
		MetaService service = Service;
		if (service == null) return;
		if (!PlayerPrefs.HasKey(SnapshotKey))
		{
			Debug.LogWarning("[MetaDebug] No snapshot saved yet.");
			return;
		}

		Snapshot snapshot = JsonUtility.FromJson<Snapshot>(PlayerPrefs.GetString(SnapshotKey));
		service.DebugReplaceState(JsonUtility.FromJson<MetaProgressionState>(snapshot.meta));

		PlayerProfile profile = PlayerProfile.Instance;
		if (profile != null)
		{
			profile.totalCurrency = snapshot.people;
			profile.totalScientistsCurrency = snapshot.scientists;
			profile.SaveProfile();
		}
		Debug.Log($"[MetaDebug] Snapshot restored (People {snapshot.people}, Scientists {snapshot.scientists}).");
	}

	private static void Log(string action, bool ok) =>
		Debug.Log($"[MetaDebug] {action}: {(ok ? "done" : "refused (conditions or cost not met)")}");
}
#endif
