using System.Collections.Generic;
using UnityEngine;

// All meta content in one place. Lives at Assets/Resources/Meta/MetaContentDatabase.asset.
// Add a building, tree, character or dialogue by adding it to a list here. No code changes.
[CreateAssetMenu(fileName = "MetaContentDatabase", menuName = "ZombieGame/Meta/Content Database")]
public class MetaContentDatabase : ScriptableObject
{
	public List<BuildingDefinition> buildings = new List<BuildingDefinition>();
	public List<SkillTreeDefinition> skillTrees = new List<SkillTreeDefinition>();
	public List<CharacterDefinition> characters = new List<CharacterDefinition>();

	[Tooltip("Sequences used as dialogues (DialogOnly steps) and as tutorials. Looked up by tutorialId.")]
	public List<TutorialSequence> sequences = new List<TutorialSequence>();

	public BuildingDefinition FindBuilding(string id) => buildings.Find(b => b != null && b.id == id);

	public SkillTreeDefinition FindTree(string id) => skillTrees.Find(t => t != null && t.id == id);

	public CharacterDefinition FindCharacter(string id) => characters.Find(c => c != null && c.id == id);

	public TutorialSequence FindSequence(string id) => sequences.Find(s => s != null && s.tutorialId == id);

	public bool FindNode(string nodeId, out SkillTreeDefinition tree, out SkillNodeDefinition node)
	{
		foreach (SkillTreeDefinition t in skillTrees)
		{
			if (t == null || t.nodes == null) continue;
			foreach (SkillNodeDefinition n in t.nodes)
			{
				if (n != null && n.id == nodeId)
				{
					tree = t;
					node = n;
					return true;
				}
			}
		}

		tree = null;
		node = null;
		return false;
	}

	private void OnValidate()
	{
		WarnDuplicateIds("building", buildings, b => b != null ? b.id : null);
		WarnDuplicateIds("skill tree", skillTrees, t => t != null ? t.id : null);
		WarnDuplicateIds("character", characters, c => c != null ? c.id : null);
		WarnDuplicateIds("sequence", sequences, s => s != null ? s.tutorialId : null);
	}

	private static void WarnDuplicateIds<T>(string kind, List<T> items, System.Func<T, string> idOf) where T : Object
	{
		var seen = new HashSet<string>();
		foreach (T item in items)
		{
			string id = idOf(item);
			if (string.IsNullOrEmpty(id)) continue;
			if (!seen.Add(id)) Debug.LogWarning($"[Meta] Duplicate {kind} id '{id}' in MetaContentDatabase.");
		}
	}
}
