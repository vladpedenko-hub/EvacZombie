using UnityEngine;

[CreateAssetMenu(fileName = "NewLevelsClearedCondition", menuName = "ZombieGame/Meta/Conditions/Levels Cleared")]
public class LevelsClearedCondition : UnlockCondition
{
	[Min(1)] public int requiredLevel = 1;

	public override bool IsMet(MetaProgressionState state) => state.ClearedLevelMetric >= requiredLevel;

	public override string GetDescription() => $"Clear level {requiredLevel}";
}
