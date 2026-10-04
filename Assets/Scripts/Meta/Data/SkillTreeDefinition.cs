using System;
using System.Collections.Generic;
using UnityEngine;

public enum ModifierMode { Flat, Percent }

// Restricts a stat modifier to cards of one category. "All" applies to every card with that stat.
public enum CardCategoryFilter { All, Evacuation, Combat, Utility }

[Serializable]
public class StatModifier
{
	public StatType stat;
	public CardCategoryFilter category = CardCategoryFilter.All;
	public ModifierMode mode = ModifierMode.Flat;
	public float value;
}

// Nodes are plain data inside a tree asset, so one tree = one asset to edit.
[Serializable]
public class SkillNodeDefinition
{
	public string id;
	public string displayName;
	public Sprite icon;
	[Min(0)] public int cost = 1;
	[Tooltip("Node ids that must be purchased first. Linear tree = each node lists the previous one.")]
	public string[] prerequisites = Array.Empty<string>();
	[Tooltip("Rendered as a diamond in the tree UI.")]
	public bool isMilestone;
	public StatModifier[] effects = Array.Empty<StatModifier>();
}

[CreateAssetMenu(fileName = "NewSkillTree", menuName = "ZombieGame/Meta/Skill Tree")]
public class SkillTreeDefinition : ScriptableObject
{
	public string id;
	public string displayName;
	[Tooltip("Building that owns this tree (informational; unlock is driven by UnlockSkillTreeReward).")]
	public string owningBuildingId;
	[Tooltip("Index 0 is the bottom of the chain (first node).")]
	public List<SkillNodeDefinition> nodes = new List<SkillNodeDefinition>();
}
