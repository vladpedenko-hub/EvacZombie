using UnityEngine;

// What the side dock shows for this shortcut. Conditions, badges and actions are picked from short enums,
// so a new shortcut of an existing kind is an asset edit. A new kind is one enum value and one case in SideDock.
[CreateAssetMenu(fileName = "NewDockShortcut", menuName = "ZombieGame/Meta/Dock Shortcut")]
public class DockShortcutDefinition : ScriptableObject
{
	public enum Visibility
	{
		Always,
		AfterAnyTreeUnlocked,
	}

	public enum Badge
	{
		None,
		PurchasableNode,
	}

	public enum Action
	{
		OpenResearch,
	}

	public string id;
	public string label = "R";
	public Visibility visibility = Visibility.AfterAnyTreeUnlocked;
	public Badge badge = Badge.None;
	public Action action = Action.OpenResearch;
}
