// Balances stay in PlayerProfile's existing save (totalCurrency = People, totalScientistsCurrency = Scientists).
// This adapter only routes reads and writes, so there is no second copy of the numbers.
public class PlayerProfileCurrencyStore : ICurrencyStore
{
	public int Get(CurrencyType type) => type == CurrencyType.People
		? PlayerProfile.Instance.totalCurrency
		: PlayerProfile.Instance.totalScientistsCurrency;

	public void Set(CurrencyType type, int value)
	{
		if (type == CurrencyType.People) PlayerProfile.Instance.totalCurrency = value;
		else PlayerProfile.Instance.totalScientistsCurrency = value;
	}

	public void Persist() => PlayerProfile.Instance.SaveProfile();
}
