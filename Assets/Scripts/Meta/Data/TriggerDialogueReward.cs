using UnityEngine;

// dialogueId = TutorialSequence.tutorialId of a sequence with DialogOnly steps (the existing dialogue format).
[CreateAssetMenu(fileName = "NewTriggerDialogueReward", menuName = "ZombieGame/Meta/Rewards/Trigger Dialogue")]
public class TriggerDialogueReward : Reward
{
	public string dialogueId;

	public override void Apply(IMetaRewardSink sink) => sink.TriggerDialogue(dialogueId);
}
