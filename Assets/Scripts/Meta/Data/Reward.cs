using UnityEngine;

// Implemented by MetaService. Rewards only call this interface, never game systems directly.
public interface IMetaRewardSink
{
	void UnlockCharacter(string characterId);
	void UnlockSkillTree(string treeId);
	void TriggerDialogue(string dialogueId);
	void StartTutorial(string tutorialId);
}

// Base for things that happen when a building is restored. To add one: subclass and create an asset.
public abstract class Reward : ScriptableObject
{
	public abstract void Apply(IMetaRewardSink sink);
}
