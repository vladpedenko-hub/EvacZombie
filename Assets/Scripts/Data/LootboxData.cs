using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class DropRate
{
	public CardData card;
	[Range(1, 100)] public int weight; // "Вес" карточки для рулетки
}

[CreateAssetMenu(fileName = "NewLootbox", menuName = "ZombieGame/Lootbox Data")]
public class LootboxData : ScriptableObject
{
	public string boxName = "Обычный Ящик";
	public Sprite boxIcon;

	[Header("Туториал / Сюжет")]
	public bool isTutorialBox = false;     // Если true - рандом отключается
	public CardData guaranteedCard;        // Карта, которая выпадет 100%

	[Header("Рулетка (для обычных)")]
	public List<DropRate> possibleDrops;   // Список карт и шанс их выпадения

	// Метод, который сама игра будет вызывать, чтобы узнать, что выпало
	public CardData OpenBox()
	{
		if (isTutorialBox && guaranteedCard != null) return guaranteedCard;

		if (possibleDrops == null || possibleDrops.Count == 0) return null;

		// Считаем общую сумму всех "весов"
		int totalWeight = 0;
		foreach (var drop in possibleDrops) totalWeight += drop.weight;

		// Кидаем кубик
		int randomValue = Random.Range(0, totalWeight);
		int currentWeight = 0;

		foreach (var drop in possibleDrops)
		{
			currentWeight += drop.weight;
			if (randomValue < currentWeight)
			{
				return drop.card; // Выпала эта карта!
			}
		}

		return possibleDrops[0].card; // Заглушка на всякий случай
	}
}