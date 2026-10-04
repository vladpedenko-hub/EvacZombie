using UnityEngine;

[CreateAssetMenu(fileName = "NewUnlockSkillTreeReward", menuName = "ZombieGame/Meta/Rewards/Unlock Skill Tree")]
public class UnlockSkillTreeReward : Reward
{
	public string treeId;

	public override void Apply(IMetaRewardSink sink) => sink.UnlockSkillTree(treeId);
}
