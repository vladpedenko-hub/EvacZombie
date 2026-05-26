using UnityEngine;
using System.Collections.Generic;

public enum CardCategory { Evacuation, Combat, Utility }
public enum CardRarity { Common, Rare, Epic, Legendary }

// �����: ����������� ���� ������ ��� ����� � �����
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
	Count,
	Health
}

[System.Serializable]
public class CardStat
{
	[Tooltip("��� ���� (������ ��� �����)")]
	public StatType statType;

	[Tooltip("��� UI (��������: '�����������')")]
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
	[Header("������� ������")]
	public CardManager.CardType cardType;
	public string cardName;
	[TextArea] public string description;
	public Sprite icon;
	public GameObject cardPrefab;

	[Header("���� � UI")]
	public CardCategory category;
	public CardRarity rarity;
	public GameObject uiButtonPrefab;

	[Header("���� � ��������")]
	public int maxLevel = 5;
	public List<UpgradeRequirements> upgradeCosts;

	[Header("��������������")]
	public List<CardStat> stats;

	// ����� �����: ��� ������ �������� ���, ����� �������� ������� �����
	public float GetCalculatedStat(StatType type, int currentLevel)
	{
		CardStat foundStat = stats.Find(s => s.statType == type);
		if (foundStat != null)
		{
			return foundStat.GetFloatValue(currentLevel);
		}

		Debug.LogWarning($"���� {type} �� ������ � �������� {cardName}!");
		return 0f;
	}
}