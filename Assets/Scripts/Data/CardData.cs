using UnityEngine;
using System.Collections.Generic;

public enum CardCategory { Evacuation, Combat, Utility }
public enum CardRarity { Common, Rare, Epic, Legendary }

// НОВОЕ: Технические типы статов для связи с кодом
public enum StatType
{
	None,
	Capacity,
	Speed,
	Damage,
	FireRate,
	Radius,
	Duration,
	Cooldown,
	Count           
}

[System.Serializable]
public class CardStat
{
	[Tooltip("Для кода (выбери тип стата)")]
	public StatType statType;

	[Tooltip("Для UI (например: 'Вместимость')")]
	public string statName;

	public float baseValue;
	public float valuePerLevel;
	public string unitSuffix;

	public string GetStringValue(int currentLevel)
	{
		float val = GetFloatValue(currentLevel);
		return (val % 1 == 0 ? val.ToString("F0") : val.ToString("F1")) + unitSuffix;
	}

	public float GetFloatValue(int currentLevel)
	{
		return baseValue + (valuePerLevel * (currentLevel - 1));
	}
}

[System.Serializable]
public class UpgradeRequirements
{
	public int duplicateCardsNeeded;
	public int currencyCost;
}

[CreateAssetMenu(fileName = "NewCardData", menuName = "ZombieGame/Card Data")]
public class CardData : ScriptableObject
{
	[Header("Базовые данные")]
	public CardManager.CardType cardType;
	public string cardName;
	[TextArea] public string description;
	public Sprite icon;
	public GameObject cardPrefab;

	[Header("Мета и UI")]
	public CardCategory category;
	public CardRarity rarity;
	public GameObject uiButtonPrefab;

	[Header("Гача и Прокачка")]
	public int maxLevel = 5;
	public List<UpgradeRequirements> upgradeCosts;

	[Header("Характеристики")]
	public List<CardStat> stats;

	// НОВЫЙ МЕТОД: Код машины вызывает его, чтобы получить готовую цифру
	public float GetCalculatedStat(StatType type, int currentLevel)
	{
		CardStat foundStat = stats.Find(s => s.statType == type);
		if (foundStat != null)
		{
			return foundStat.GetFloatValue(currentLevel);
		}

		Debug.LogWarning($"Стат {type} не найден в карточке {cardName}!");
		return 0f;
	}
}