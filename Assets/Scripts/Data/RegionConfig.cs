using UnityEngine;
using System.Collections.Generic;

// [ГДЕ ИСПОЛЬЗОВАТЬ]: В папке Data (наши ScriptableObjects)
[CreateAssetMenu(fileName = "NewRegion", menuName = "ZombieGame/RegionData")]
public class RegionConfig : ScriptableObject
{
	[Header("Базовая информация")]
	public string regionName = "Nevada";

	[Tooltip("Префаб с визуалом карты (со скриптом RegionMapVisual)")]
	public GameObject regionUIPrefab;

	[Header("Старый региональный приз (можно оставить для совместимости)")]
	public LootboxData regionRewardLootbox;

	[Header("Уровни региона")]
	public List<LevelData> levels = new List<LevelData>();

	[Header("Trophy Road")]
	[Tooltip("Награды за прогресс региона. Обычно 3 штуки: 25%, 50%, 100%")]
	public RegionRewardData[] trophyRoadRewards = new RegionRewardData[3];
}