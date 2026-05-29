using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class DropRate
{
	public CardData card;
	[Range(1, 100)] public int weight; // "weight" relative to other drops
}

[CreateAssetMenu(fileName = "NewLootbox", menuName = "ZombieGame/Lootbox Data")]
public class LootboxData : ScriptableObject
{
	public string boxName = "Standard Box";
	public Sprite boxIcon;

	[Header("Tutorial / Guaranteed")]
	public bool isTutorialBox = false;     // If true, always drops the guaranteed card
	public CardData guaranteedCard;        // Card dropped 100% of the time

	[Header("Drop Pool (weighted)")]
	public List<DropRate> possibleDrops;   // List of cards with weighted drop chances

	// Call this to open the box and receive a card
	public CardData OpenBox()
	{
		if (isTutorialBox && guaranteedCard != null) return guaranteedCard;

		if (possibleDrops == null || possibleDrops.Count == 0) return null;

		// Sum all "weights" in the pool
		int totalWeight = 0;
		foreach (var drop in possibleDrops) totalWeight += drop.weight;

		// Roll a random value
		int randomValue = Random.Range(0, totalWeight);
		int currentWeight = 0;

		foreach (var drop in possibleDrops)
		{
			currentWeight += drop.weight;
			if (randomValue < currentWeight)
			{
				return drop.card; // This card wins!
			}
		}

		return possibleDrops[0].card; // Fallback to first entry
	}
}