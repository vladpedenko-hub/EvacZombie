using UnityEngine;
using System.Collections.Generic;

// [File location]: in the Data folder (alongside ScriptableObjects)
[CreateAssetMenu(fileName = "NewRegion", menuName = "ZombieGame/RegionData")]
public class RegionConfig : ScriptableObject
{
	[Header("Region Settings")]
	public string regionName = "Nevada";

	[Tooltip("Prefab with visual map elements (used by RegionMapVisual)")]
	public GameObject regionUIPrefab;

	[Header("Region Completion Reward (shown after all levels are cleared)")]
	public LootboxData regionRewardLootbox;

	[Header("Level List")]
	public List<LevelData> levels = new List<LevelData>();

	[Header("Trophy Road")]
	[Tooltip("Rewards on the trophy road. Expects 3 entries: 25%, 50%, 100%")]
	public RegionRewardData[] trophyRoadRewards = new RegionRewardData[3];
}