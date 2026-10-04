using UnityEngine;

// Base for building unlock conditions. To add one: subclass, then create an asset in the editor.
public abstract class UnlockCondition : ScriptableObject
{
	public abstract bool IsMet(MetaProgressionState state);

	// Shown to the player when the condition is not met yet.
	public abstract string GetDescription();
}
