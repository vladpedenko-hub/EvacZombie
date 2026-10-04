// What battle code reads from meta progression. Kept small: one call per stat and mode.
public interface IMetaModifiers
{
	float GetFlat(StatType stat, CardCategory category);
	float GetPercent(StatType stat, CardCategory category);
}
