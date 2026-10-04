using System;
using System.Collections.Generic;
using UnityEngine;

public enum BuildingState { Locked, AvailableNotAffordable, Available, Restored }
public enum NodeState { Purchased, Available, NotAffordable, Locked }

public class BuildingStatus
{
	public BuildingState state;

	// Locked: the first unmet condition. AvailableNotAffordable: the People shortfall. Otherwise empty.
	public string reason = "";
}

// Central meta logic. Plain C# (no MonoBehaviour), so it can be tested without a scene.
// MetaRuntime wires it to the game (save, currency, tutorial/dialogue systems).
public class MetaService : IMetaRewardSink
{
	// Fires after every state change. UI refreshes and saves hook here.
	public event Action OnChanged;
	public event Action<BuildingDefinition> OnBuildingRestored;
	public event Action<SkillNodeDefinition> OnNodePurchased;
	public event Action<string> OnDialogueRequested;
	public event Action<string> OnTutorialRequested;

	private readonly MetaContentDatabase content;
	private readonly CurrencyService currency;

	public MetaProgressionState State { get; }

	public IReadOnlyList<BuildingDefinition> Buildings => content.buildings;

	public MetaService(MetaContentDatabase content, CurrencyService currency, MetaProgressionState state)
	{
		this.content = content;
		this.currency = currency;
		State = state;
	}

	// Every purchased node, for battle modifiers and the research preview.
	public IEnumerable<SkillNodeDefinition> PurchasedNodeDefinitions()
	{
		foreach (string nodeId in State.purchasedNodes)
		{
			if (content.FindNode(nodeId, out _, out SkillNodeDefinition node)) yield return node;
		}
	}

	// The tree a building owns (by owningBuildingId), or null if it has none.
	public SkillTreeDefinition FindTreeForBuilding(string buildingId) =>
		content.skillTrees.Find(t => t != null && t.owningBuildingId == buildingId);

	// --- Buildings ---

	public BuildingStatus GetBuildingState(string buildingId)
	{
		BuildingDefinition building = content.FindBuilding(buildingId);
		if (building == null)
			return new BuildingStatus { state = BuildingState.Locked, reason = $"Unknown building '{buildingId}'" };

		if (State.restoredBuildings.Contains(buildingId))
			return new BuildingStatus { state = BuildingState.Restored };

		foreach (UnlockCondition condition in building.conditions)
		{
			if (condition != null && !condition.IsMet(State))
				return new BuildingStatus { state = BuildingState.Locked, reason = condition.GetDescription() };
		}

		int have = currency.Get(CurrencyType.People);
		if (have < building.restorationCostPeople)
			return new BuildingStatus
			{
				state = BuildingState.AvailableNotAffordable,
				reason = $"Need {building.restorationCostPeople} People ({have}/{building.restorationCostPeople})"
			};

		return new BuildingStatus { state = BuildingState.Available };
	}

	public bool TryRestoreBuilding(string buildingId)
	{
		BuildingDefinition building = content.FindBuilding(buildingId);
		if (building == null)
		{
			Debug.LogWarning($"[Meta] TryRestoreBuilding: unknown building '{buildingId}'");
			return false;
		}

		if (GetBuildingState(buildingId).state != BuildingState.Available) return false;
		if (!currency.TrySpend(CurrencyType.People, building.restorationCostPeople)) return false;

		State.restoredBuildings.Add(buildingId);

		foreach (Reward reward in building.rewardsOnRestore)
			if (reward != null) reward.Apply(this);

		OnBuildingRestored?.Invoke(building);
		OnChanged?.Invoke();
		return true;
	}

	// Called after a level is cleared. Records progress and returns buildings that just became available.
	public List<BuildingDefinition> RecordLevelCleared(string levelId, int levelNumber)
	{
		int previousHighest = State.highestClearedLevel;

		if (!State.clearedLevelIds.Contains(levelId)) State.clearedLevelIds.Add(levelId);
		State.highestClearedLevel = Mathf.Max(State.highestClearedLevel, levelNumber);

		OnChanged?.Invoke();
		return GetNewlyAvailableBuildings(previousHighest, State.highestClearedLevel);
	}

	// Buildings whose conditions were not all met at previousClearedLevel but are met at newClearedLevel.
	public List<BuildingDefinition> GetNewlyAvailableBuildings(int previousClearedLevel, int newClearedLevel)
	{
		var result = new List<BuildingDefinition>();

		MetaProgressionState before = State.Clone();
		before.highestClearedLevel = previousClearedLevel;
		MetaProgressionState after = State.Clone();
		after.highestClearedLevel = newClearedLevel;

		foreach (BuildingDefinition building in content.buildings)
		{
			if (building == null || State.restoredBuildings.Contains(building.id)) continue;
			if (AllConditionsMet(building, before)) continue;
			if (AllConditionsMet(building, after)) result.Add(building);
		}

		return result;
	}

	private static bool AllConditionsMet(BuildingDefinition building, MetaProgressionState state)
	{
		foreach (UnlockCondition condition in building.conditions)
			if (condition != null && !condition.IsMet(state)) return false;
		return true;
	}

	// --- Skill trees ---

	public NodeState GetNodeState(string nodeId)
	{
		if (!content.FindNode(nodeId, out SkillTreeDefinition tree, out SkillNodeDefinition node))
			return NodeState.Locked;

		if (State.purchasedNodes.Contains(nodeId)) return NodeState.Purchased;
		if (!State.unlockedTrees.Contains(tree.id)) return NodeState.Locked;

		foreach (string prerequisite in node.prerequisites)
			if (!State.purchasedNodes.Contains(prerequisite)) return NodeState.Locked;

		if (!currency.CanAfford(CurrencyType.Scientists, node.cost)) return NodeState.NotAffordable;
		return NodeState.Available;
	}

	public bool TryPurchaseNode(string nodeId)
	{
		if (GetNodeState(nodeId) != NodeState.Available) return false;

		content.FindNode(nodeId, out _, out SkillNodeDefinition node);
		if (!currency.TrySpend(CurrencyType.Scientists, node.cost)) return false;

		State.purchasedNodes.Add(nodeId);
		OnNodePurchased?.Invoke(node);
		OnChanged?.Invoke();
		return true;
	}

	// --- Reward sink (called by Reward.Apply) ---

	void IMetaRewardSink.UnlockCharacter(string characterId)
	{
		if (content.FindCharacter(characterId) == null)
		{
			Debug.LogWarning($"[Meta] Reward references unknown character '{characterId}'");
			return;
		}
		if (!State.unlockedCharacters.Contains(characterId)) State.unlockedCharacters.Add(characterId);
	}

	void IMetaRewardSink.UnlockSkillTree(string treeId)
	{
		if (content.FindTree(treeId) == null)
		{
			Debug.LogWarning($"[Meta] Reward references unknown skill tree '{treeId}'");
			return;
		}
		if (!State.unlockedTrees.Contains(treeId)) State.unlockedTrees.Add(treeId);
	}

	void IMetaRewardSink.TriggerDialogue(string dialogueId) => OnDialogueRequested?.Invoke(dialogueId);

	void IMetaRewardSink.StartTutorial(string tutorialId) => OnTutorialRequested?.Invoke(tutorialId);
}
