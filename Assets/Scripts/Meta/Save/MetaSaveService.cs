using System;
using UnityEngine;

// Stores MetaProgressionState as JSON in PlayerPrefs, the same storage PlayerProfile uses.
public static class MetaSaveService
{
	private const string Key = "MetaProgression";

	public static MetaProgressionState Load()
	{
		if (!PlayerPrefs.HasKey(Key)) return new MetaProgressionState();

		try
		{
			MetaProgressionState state = JsonUtility.FromJson<MetaProgressionState>(PlayerPrefs.GetString(Key));
			return state != null ? Migrate(state) : new MetaProgressionState();
		}
		catch (Exception e)
		{
			Debug.LogError($"[Meta] Save data unreadable, starting fresh: {e.Message}");
			return new MetaProgressionState();
		}
	}

	public static void Save(MetaProgressionState state)
	{
		state.version = MetaProgressionState.CurrentVersion;
		PlayerPrefs.SetString(Key, JsonUtility.ToJson(state));
		PlayerPrefs.Save();
	}

	public static void Delete() => PlayerPrefs.DeleteKey(Key);

	// Add one step per schema version change, e.g. if (state.version < 2) { ... }
	private static MetaProgressionState Migrate(MetaProgressionState state) => state;
}
