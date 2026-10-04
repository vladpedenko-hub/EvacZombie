using System;
using System.Collections.Generic;
using UnityEngine;

// Meta progression save data. Currency balances are NOT stored here: they stay in PlayerProfile's save.
[Serializable]
public class MetaProgressionState
{
	public const int CurrentVersion = 1;

	public int version = CurrentVersion;
	public int highestClearedLevel;
	public List<string> clearedLevelIds = new List<string>();
	public List<string> restoredBuildings = new List<string>();
	public List<string> purchasedNodes = new List<string>();
	public List<string> unlockedCharacters = new List<string>();
	public List<string> unlockedTrees = new List<string>();

	// The one place that decides what "level progress" means for unlock conditions.
	// To count total levels cleared instead of the highest level, return clearedLevelIds.Count here.
	public int ClearedLevelMetric => highestClearedLevel;

	public MetaProgressionState Clone() => JsonUtility.FromJson<MetaProgressionState>(JsonUtility.ToJson(this));
}
