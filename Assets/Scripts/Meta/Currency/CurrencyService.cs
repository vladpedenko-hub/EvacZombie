using System;

// Where balances physically live. Implemented by PlayerProfileCurrencyStore in the game.
public interface ICurrencyStore
{
	int Get(CurrencyType type);
	void Set(CurrencyType type, int value);
	void Persist();
}

// Single entry point for People and Scientists. Owns no balances itself.
// Future rewards: CurrencyService.Add(type, amount, CurrencySourceType.ThreeStars, levelId)
public class CurrencyService
{
	public event Action<CurrencyType, int> OnChanged;

	private readonly ICurrencyStore store;

	public CurrencyService(ICurrencyStore store)
	{
		this.store = store;
	}

	public int Get(CurrencyType type) => store.Get(type);

	public bool CanAfford(CurrencyType type, int amount) => store.Get(type) >= amount;

	// sourceType/sourceId are accepted now so every grant is tagged from day one (analytics, debugging).
	public void Add(CurrencyType type, int amount, CurrencySourceType sourceType, string sourceId = null)
	{
		if (amount <= 0) return;
		SetAndNotify(type, store.Get(type) + amount);
	}

	public void Add(CurrencyGrant grant, string sourceId = null)
	{
		if (grant == null) return;
		Add(grant.currencyType, grant.amount, grant.sourceType, sourceId);
	}

	public bool TrySpend(CurrencyType type, int amount)
	{
		if (amount <= 0) return true;

		int current = store.Get(type);
		if (current < amount) return false;

		SetAndNotify(type, current - amount);
		return true;
	}

	private void SetAndNotify(CurrencyType type, int value)
	{
		store.Set(type, value);
		store.Persist();
		OnChanged?.Invoke(type, value);
	}
}
