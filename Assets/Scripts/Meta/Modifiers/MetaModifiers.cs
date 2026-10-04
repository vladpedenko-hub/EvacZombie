// Read-only view of purchased skill nodes. Used by card stat lookups in battle and by the research preview.
public class MetaModifiers : IMetaModifiers
{
	private readonly MetaService service;

	public MetaModifiers(MetaService service)
	{
		this.service = service;
	}

	public float GetFlat(StatType stat, CardCategory category) => Sum(stat, category, ModifierMode.Flat);

	// Percent effects are authored as whole numbers (3 = +3%); the hook takes a fraction.
	public float GetPercent(StatType stat, CardCategory category) => Sum(stat, category, ModifierMode.Percent) / 100f;

	// Total for one exact filter (not "applies to"). Used to show "current -> new" in the research screen.
	public float SumForFilter(StatType stat, CardCategoryFilter filter, ModifierMode mode)
	{
		float total = 0f;
		foreach (SkillNodeDefinition node in service.PurchasedNodeDefinitions())
		{
			foreach (StatModifier effect in node.effects)
			{
				if (effect.stat == stat && effect.mode == mode && effect.category == filter) total += effect.value;
			}
		}
		return total;
	}

	private float Sum(StatType stat, CardCategory category, ModifierMode mode)
	{
		float total = 0f;
		foreach (SkillNodeDefinition node in service.PurchasedNodeDefinitions())
		{
			foreach (StatModifier effect in node.effects)
			{
				if (effect.stat == stat && effect.mode == mode && Applies(effect.category, category)) total += effect.value;
			}
		}
		return total;
	}

	private static bool Applies(CardCategoryFilter filter, CardCategory category) => filter switch
	{
		CardCategoryFilter.All => true,
		CardCategoryFilter.Evacuation => category == CardCategory.Evacuation,
		CardCategoryFilter.Combat => category == CardCategory.Combat,
		CardCategoryFilter.Utility => category == CardCategory.Utility,
		_ => false
	};
}
