using UnityEngine;

[CreateAssetMenu(fileName = "NewCharacter", menuName = "ZombieGame/Meta/Character")]
public class CharacterDefinition : ScriptableObject
{
	public string id;
	public string displayName;
	public Sprite portrait;
}
