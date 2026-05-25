using UnityEngine;

[System.Serializable]
public class RegionRewardData
{
	public TrophyRewardType rewardType = TrophyRewardType.SoftCurrency;

	[Header("Порог прогресса")]
	[Range(0f, 1f)]
	[Tooltip("На каком проценте прогресса региона открывается награда. 0.25 = четверть, 0.5 = половина, 1 = полный бар.")]
	public float progressThreshold = 0.25f;

	[Header("Софт валюта")]
	public int softCurrencyAmount = 0;

	[Header("Лутбокс")]
	public LootboxData lootboxReward;

	[Header("Будущая гибкость")]
	public CardData cardReward;
	public Sprite skinRewardIcon;
	public string customRewardId;
}

public enum TrophyRewardType
{
	SoftCurrency = 0,
	Lootbox = 1,
	Card = 2,
	Skin = 3,
	Custom = 4
}