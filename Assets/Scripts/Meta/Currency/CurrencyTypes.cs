using System;

public enum CurrencyType { People, Scientists }

// Where a currency change came from. Used for audit/analytics and to pick grant rules.
public enum CurrencySourceType { LevelClear, ThreeStars, Quest, Debug }

// One currency payout, configured in data (LevelData.clearGrants, future quest/star configs).
[Serializable]
public class CurrencyGrant
{
	public CurrencyType currencyType;
	public int amount;
	public CurrencySourceType sourceType = CurrencySourceType.LevelClear;
}
