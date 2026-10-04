using System.Collections.Generic;
using UnityEngine;

// Composition root for the meta layer. Static because PlayerProfile is a DontDestroyOnLoad singleton
// and GameManager, ResultPopupUI and MetaCity all need the same instances.
public static class MetaRuntime
{
	private const string ContentResourcePath = "Meta/MetaContentDatabase";

	public static CurrencyService Currency { get; private set; }
	public static MetaService Service { get; private set; }
	private static MetaModifiers modifiers;

	// Battle reads research bonuses through here. Null when the meta data is unavailable (no bonuses).
	public static MetaModifiers Modifiers
	{
		get
		{
			EnsureInitialized();
			return modifiers;
		}
	}

	// Buildings unlocked by the most recent level clear. Read by the post-level routing.
	public static List<BuildingDefinition> PendingNewlyAvailable { get; private set; } = new List<BuildingDefinition>();

	// Returns false until PlayerProfile exists. The content database is required only by the meta service.
	public static bool EnsureInitialized()
	{
		if (Currency != null) return true;
		if (PlayerProfile.Instance == null) return false;

		Currency = new CurrencyService(new PlayerProfileCurrencyStore());

		var content = Resources.Load<MetaContentDatabase>(ContentResourcePath);
		if (content == null)
		{
			Debug.LogError($"[Meta] MetaContentDatabase not found at Resources/{ContentResourcePath}");
			return true;
		}

		Service = new MetaService(content, Currency, MetaSaveService.Load());
		Service.OnChanged += () => MetaSaveService.Save(Service.State);
		modifiers = new MetaModifiers(Service);
		WarnStatsNoCardHas(content);
		return true;
	}

	// A node effect on a stat that no card has does nothing. Warn once so the designer sees it.
	private static void WarnStatsNoCardHas(MetaContentDatabase content)
	{
		foreach (SkillTreeDefinition tree in content.skillTrees)
		{
			if (tree == null || tree.nodes == null) continue;
			foreach (SkillNodeDefinition node in tree.nodes)
			{
				foreach (StatModifier effect in node.effects)
				{
					if (!AnyCardHasStat(effect.stat))
						Debug.LogWarning($"[Meta] Node '{node.id}' modifies {effect.stat}, which no card has. Ignored.");
				}
			}
		}
	}

	private static bool AnyCardHasStat(StatType stat) =>
		PlayerProfile.Instance.allAvailableCards.Exists(card =>
			card != null && card.stats != null && card.stats.Exists(s => s.statType == stat));

	// Called from GameManager.EndLevel on a win.
	public static void OnLevelCleared(LevelData level)
	{
		PendingNewlyAvailable = new List<BuildingDefinition>();
		if (!EnsureInitialized() || Service == null || level == null) return;

		int number = GetGlobalLevelNumber(level);
		if (number <= 0)
		{
			Debug.LogWarning($"[Meta] Level '{level.name}' is not in PlayerProfile.allRegions; progress not recorded.");
			return;
		}

		PendingNewlyAvailable = Service.RecordLevelCleared(level.name, number);
	}

	// 1-based position across all regions in PlayerProfile order (Level_1 = 1, ..., Level_10 = 10).
	public static int GetGlobalLevelNumber(LevelData level)
	{
		int number = 0;
		foreach (RegionConfig region in PlayerProfile.Instance.allRegions)
		{
			if (region == null || region.levels == null) continue;
			foreach (LevelData candidate in region.levels)
			{
				number++;
				if (candidate == level) return number;
			}
		}
		return 0;
	}

	// Domain-reload-disabled play mode would otherwise keep stale instances.
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ResetStatics()
	{
		Currency = null;
		Service = null;
		modifiers = null;
		PendingNewlyAvailable = new List<BuildingDefinition>();
	}
}
