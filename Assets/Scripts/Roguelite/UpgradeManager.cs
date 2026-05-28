using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class UpgradeManager : MonoBehaviour
{
    public static UpgradeManager Instance { get; private set; }

    [Header("Все апгрейды в игре (заполни в инспекторе или через Resources.LoadAll)")]
    public List<RunUpgradeDefinition> allUpgrades;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Автозагрузка из Resources/Upgrades/ если список пуст
        if (allUpgrades == null || allUpgrades.Count == 0)
        {
            allUpgrades = Resources.LoadAll<RunUpgradeDefinition>("Upgrades").ToList();
        }
    }

    /// <summary>
    /// Вернуть 3 случайных апгрейда для показа игроку.
    /// Исключает максимально прокачанные (стак = 3).
    /// </summary>
    public List<(RunUpgradeDefinition upgrade, int nextTier)> GetUpgradeOptions(
        List<CardData> currentDeck,
        CardManager.CardType heroType)
    {
        var session = RunSessionData.Instance;
        var pool = new List<(RunUpgradeDefinition, int nextTier)>();

        // Собираем типы карт в деке
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

        // Перемешать и взять 3 уникальных upgradeId
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

    /// <summary>Применить выбранный апгрейд.</summary>
    public void ApplyUpgrade(RunUpgradeDefinition upgrade)
    {
        RunSessionData.Instance.ApplyUpgrade(upgrade);
    }
}
