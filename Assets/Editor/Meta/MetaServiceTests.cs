using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// EditMode tests for the meta rules. They use an in-memory currency store, so no PlayerProfile or scene is needed.
[TestFixture]
public class MetaServiceTests
{
	private class FakeCurrencyStore : ICurrencyStore
	{
		public readonly Dictionary<CurrencyType, int> balances = new Dictionary<CurrencyType, int>
		{
			{ CurrencyType.People, 0 },
			{ CurrencyType.Scientists, 0 }
		};

		public int Get(CurrencyType type) => balances[type];
		public void Set(CurrencyType type, int value) => balances[type] = value;
		public void Persist() { }
	}

	private MetaContentDatabase content;
	private FakeCurrencyStore store;
	private MetaService service;
	private BuildingDefinition lab;
	private SkillTreeDefinition labTree;

	[SetUp]
	public void SetUp()
	{
		content = ScriptableObject.CreateInstance<MetaContentDatabase>();
		store = new FakeCurrencyStore();

		var clear2 = ScriptableObject.CreateInstance<LevelsClearedCondition>();
		clear2.requiredLevel = 2;

		lab = ScriptableObject.CreateInstance<BuildingDefinition>();
		lab.id = "lab";
		lab.conditions = new UnlockCondition[] { clear2 };
		lab.restorationCostPeople = 20;
		var unlockTree = ScriptableObject.CreateInstance<UnlockSkillTreeReward>();
		unlockTree.treeId = "tree";
		lab.rewardsOnRestore = new Reward[] { unlockTree };

		var clear5 = ScriptableObject.CreateInstance<LevelsClearedCondition>();
		clear5.requiredLevel = 5;
		var house2 = ScriptableObject.CreateInstance<BuildingDefinition>();
		house2.id = "house2";
		house2.conditions = new UnlockCondition[] { clear5 };
		house2.restorationCostPeople = 60;

		labTree = ScriptableObject.CreateInstance<SkillTreeDefinition>();
		labTree.id = "tree";
		labTree.nodes = new List<SkillNodeDefinition>
		{
			new SkillNodeDefinition { id = "n1", cost = 1, prerequisites = new string[0] },
			new SkillNodeDefinition { id = "n2", cost = 2, prerequisites = new[] { "n1" } }
		};

		content.buildings = new List<BuildingDefinition> { lab, house2 };
		content.skillTrees = new List<SkillTreeDefinition> { labTree };

		service = new MetaService(content, new CurrencyService(store), new MetaProgressionState());
	}

	[Test]
	public void FreshSave_BuildingLocked_WithConditionText()
	{
		BuildingStatus status = service.GetBuildingState("lab");

		Assert.AreEqual(BuildingState.Locked, status.state);
		Assert.AreEqual("Clear level 2", status.reason);
	}

	[Test]
	public void Available_ButNotEnoughPeople_CannotRestore()
	{
		service.State.highestClearedLevel = 2;
		store.balances[CurrencyType.People] = 19;

		Assert.AreEqual(BuildingState.AvailableNotAffordable, service.GetBuildingState("lab").state);
		Assert.IsFalse(service.TryRestoreBuilding("lab"));
		Assert.AreEqual(19, store.balances[CurrencyType.People], "No People should be spent on a failed restore");
	}

	[Test]
	public void Restore_SpendsPeople_MarksRestored_UnlocksTree()
	{
		service.State.highestClearedLevel = 2;
		store.balances[CurrencyType.People] = 25;

		Assert.IsTrue(service.TryRestoreBuilding("lab"));
		Assert.AreEqual(5, store.balances[CurrencyType.People]);
		Assert.AreEqual(BuildingState.Restored, service.GetBuildingState("lab").state);
		Assert.Contains("tree", service.State.unlockedTrees);
	}

	[Test]
	public void Nodes_LockedUntilTreeUnlocked_ThenBoughtInOrder()
	{
		store.balances[CurrencyType.Scientists] = 10;
		Assert.AreEqual(NodeState.Locked, service.GetNodeState("n1"), "Tree not unlocked yet");

		service.State.unlockedTrees.Add("tree");
		Assert.AreEqual(NodeState.Locked, service.GetNodeState("n2"), "n2 needs n1 first");

		Assert.IsTrue(service.TryPurchaseNode("n1"));
		Assert.AreEqual(9, store.balances[CurrencyType.Scientists]);
		Assert.IsTrue(service.TryPurchaseNode("n2"));
		Assert.AreEqual(7, store.balances[CurrencyType.Scientists]);
		Assert.AreEqual(NodeState.Purchased, service.GetNodeState("n2"));
	}

	[Test]
	public void Node_NotAffordable_CannotPurchase()
	{
		service.State.unlockedTrees.Add("tree");
		store.balances[CurrencyType.Scientists] = 0;

		Assert.AreEqual(NodeState.NotAffordable, service.GetNodeState("n1"));
		Assert.IsFalse(service.TryPurchaseNode("n1"));
	}

	[Test]
	public void NewlyAvailable_ReportedOnlyWhenConditionsCrossed()
	{
		List<BuildingDefinition> afterLevel2 = service.GetNewlyAvailableBuildings(1, 2);
		Assert.AreEqual(1, afterLevel2.Count);
		Assert.AreSame(lab, afterLevel2[0]);

		Assert.AreEqual(0, service.GetNewlyAvailableBuildings(2, 3).Count, "Lab was already available at level 2");
	}

	[Test]
	public void RecordLevelCleared_ReturnsNewlyAvailable_AndKeepsHighest()
	{
		List<BuildingDefinition> newly = service.RecordLevelCleared("Level_2_Data", 2);
		Assert.AreEqual(1, newly.Count);
		Assert.AreEqual(2, service.State.highestClearedLevel);

		service.RecordLevelCleared("Level_1_Data", 1);
		Assert.AreEqual(2, service.State.highestClearedLevel, "Replaying an earlier level must not lower progress");
	}

	[Test]
	public void PendingFocus_SetByUnlockingClear_ConsumedOnce()
	{
		service.RecordLevelCleared("Level_2_Data", 2);
		Assert.AreEqual("lab", service.ConsumePendingFocus());
		Assert.IsNull(service.ConsumePendingFocus(), "Focus is consumed once");
	}

	[Test]
	public void PendingFocus_NotSetByClearThatUnlocksNothing()
	{
		service.RecordLevelCleared("Level_1_Data", 1);
		Assert.IsNull(service.ConsumePendingFocus());
	}

	[Test]
	public void AttentionItems_TrueWhenRestorable_FalseWhenNothingActionable()
	{
		Assert.IsFalse(service.HasAttentionItems(), "Nothing unlocked on a fresh save");

		service.State.highestClearedLevel = 2;
		Assert.IsFalse(service.HasAttentionItems(), "Available but not affordable is not an attention item");

		store.balances[CurrencyType.People] = 20;
		Assert.IsTrue(service.HasAttentionItems());

		service.TryRestoreBuilding("lab");
		store.balances[CurrencyType.Scientists] = 1;
		Assert.IsTrue(service.HasAttentionItems(), "A purchasable node is an attention item");

		service.TryPurchaseNode("n1");
		Assert.IsFalse(service.HasAttentionItems());
	}

	[Test]
	public void MetaIntro_MarkedOnce()
	{
		Assert.IsFalse(service.State.metaIntroSeen);
		service.MarkMetaIntroSeen();
		Assert.IsTrue(service.State.metaIntroSeen);
	}

	[Test]
	public void NewFields_SurviveSaveRoundTrip()
	{
		service.RecordLevelCleared("Level_2_Data", 2);
		service.MarkMetaIntroSeen();

		MetaProgressionState loaded = JsonUtility.FromJson<MetaProgressionState>(JsonUtility.ToJson(service.State));
		Assert.AreEqual("lab", loaded.pendingFocusBuildingId);
		Assert.IsTrue(loaded.metaIntroSeen);
	}
}
