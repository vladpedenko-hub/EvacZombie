using UnityEngine;

[System.Serializable]
public class RegionRewardData
{
	public TrophyRewardType rewardType = TrophyRewardType.SoftCurrency;

	[Header("Unlock Threshold")]
	[Range(0f, 1f)]
	[Tooltip("At what progress percentage this reward becomes available. 0.25 = quarter, 0.5 = halfway, 1 = all done.")]
	public float progressThreshold = 0.25f;

	[Header("Soft Currency")]
	public int softCurrencyAmount = 0;

	[Header("Lootbox")]
	public LootboxData lootboxReward;

	[Header("Specific Reward")]
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