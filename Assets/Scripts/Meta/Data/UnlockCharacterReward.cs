using UnityEngine;

[CreateAssetMenu(fileName = "NewUnlockCharacterReward", menuName = "ZombieGame/Meta/Rewards/Unlock Character")]
public class UnlockCharacterReward : Reward
{
	public string characterId;

	public override void Apply(IMetaRewardSink sink) => sink.UnlockCharacter(characterId);
}
