using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class UpgradeManager : MonoBehaviour
{
    public static UpgradeManager Instance { get; private set; }

    [Header("All upgrades in the game (fill in Inspector or via Resources.LoadAll)")]
    public List<RunUpgradeDefinition> allUpgrades;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Auto-load from Resources/Upgrades/ if the list is empty
        if (allUpgrades == null || allUpgrades.Count == 0)
        {
            allUpgrades = Resources.LoadAll<RunUpgradeDefinition>("Upgrades").ToList();
        }
    }

    /// <summary>
    /// Return 3 random upgrades to show the player.
    /// Excludes fully maxed upgrades (stack = 3).
    /// </summary>
    public List<(RunUpgradeDefinition upgrade, int nextTier)> GetUpgradeOptions(
        List<CardData> currentDeck,
        CardManager.CardType heroType)
    {
        var session = RunSessionData.Instance;
        var pool = new List<(RunUpgradeDefinition, int nextTier)>();

        // Collect card types from the current deck
        var deckCardTypes = new HashSet<CardManager.CardType>(
            currentDeck.Select(c => c.cardType)
        );
        deckCardTypes.Add(heroType);
        deckCardTypes.Add(CardManager.CardType.None); // General upgrades

        foreach (var upg in allUpgrades)
        {
            if (!deckCardTypes.Contains(upg.targetCardType)) continue;

            int currentStack = session.GetStack(upg.upgradeId);
            if (currentStack >= 3) continue;

            int nextTier = currentStack + 1;
            pool.Add((upg, nextTier));
        }

        // Shuffle and take 3 unique upgradeIds
        pool = pool.OrderBy(_ => Random.value).ToList();

        var result = new List<(RunUpgradeDefinition, int)>();
        var usedIds = new HashSet<string>();

        foreach (var item in pool)
        {
            if (usedIds.Contains(item.Item1.upgradeId)) continue;
            usedIds.Add(item.Item1.upgradeId);
            result.Add(item);
            if (result.Count >= 3) break;
        }

        return result;
    }

    /// <summary>Apply the selected upgrade.</summary>
    public void ApplyUpgrade(RunUpgradeDefinition upgrade)
    {
        RunSessionData.Instance.ApplyUpgrade(upgrade);
    }
}
