using UnityEngine;

[CreateAssetMenu(fileName = "NewStartTutorialReward", menuName = "ZombieGame/Meta/Rewards/Start Tutorial")]
public class StartTutorialReward : Reward
{
	public string tutorialId;

	public override void Apply(IMetaRewardSink sink) => sink.StartTutorial(tutorialId);
}
