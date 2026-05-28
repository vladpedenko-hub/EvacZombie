using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Singleton — stores XP, upgrades, and modifiers for the current run.
/// Reset at the start of each level via ResetForNewLevel().
/// NOT persisted between levels.
/// </summary>
public class RunSessionData : MonoBehaviour
{
    public static RunSessionData Instance { get; private set; }

    // ── XP and levels ──────────────────────────────────────────────────────
    public int CurrentXP { get; private set; }
    public int CurrentRunLevel { get; private set; } = 1;

    // XP thresholds for each level (index = level-1, value = XP needed to advance)
    private static readonly int[] XpThresholds = { 100, 150, 200, 250, 300, 375, 450, 550, 700, 900 };

    // ── Upgrade stacks: upgradeId → how many times taken (0-3) ─────────────
    public Dictionary<string, int> UpgradeStacks { get; private set; } = new Dictionary<string, int>();

    // ── Numeric modifiers (additive on top of base stats) ─────────
    private Dictionary<string, float> _modifiers = new Dictionary<string, float>();

    // ── Flags for Ultimate effects ───────────────────────────────────────
    private HashSet<string> _flags = new HashSet<string>();

    // ── Level up event (UI subscribes) ──────────────────────────────
    public System.Action OnLevelUp;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        // NOT DontDestroyOnLoad — session data only lives within the level scene
    }

    // ── Public API ─────────────────────────────────────────────────────

    public void ResetForNewLevel()
    {
        CurrentXP = 0;
        CurrentRunLevel = 1;
        UpgradeStacks.Clear();
        _modifiers.Clear();
        _flags.Clear();
    }

    /// <summary>Grant XP. Call from XPManager.</summary>
    public void AddXP(int amount)
    {
        if (amount <= 0) return;

        // XP is only granted during active gameplay
        if (GameManager.Instance != null)
        {
            var st = GameManager.Instance.State;
            if (st != GameManager.GameState.Playing && st != GameManager.GameState.SuddenDeath)
            {
                Debug.Log($"[RunSessionData] AddXP({amount}) BLOCKED — state={st}");
                return;
            }
        }

        Debug.Log($"[RunSessionData] AddXP +{amount} → total={CurrentXP + amount}");
        CurrentXP += amount;
        CheckLevelUp();
    }

    /// <summary>Additive modifier. The same key can be added multiple times.</summary>
    public void AddModifier(string key, float value)
    {
        if (!_modifiers.ContainsKey(key)) _modifiers[key] = 0f;
        _modifiers[key] += value;
    }

    /// <summary>Read the total modifier. Returns defaultValue if not set.</summary>
    public float GetModifier(string key, float defaultValue = 0f)
    {
        return _modifiers.TryGetValue(key, out float val) ? val : defaultValue;
    }

    public void SetFlag(string flagId) => _flags.Add(flagId);
    public bool HasFlag(string flagId) => _flags.Contains(flagId);

    /// <summary>Apply an upgrade — increment the stack and trigger the effect.</summary>
    public void ApplyUpgrade(RunUpgradeDefinition upgrade)
    {
        if (!UpgradeStacks.ContainsKey(upgrade.upgradeId))
            UpgradeStacks[upgrade.upgradeId] = 0;

        UpgradeStacks[upgrade.upgradeId]++;
        int tier = UpgradeStacks[upgrade.upgradeId];

        upgrade.ApplyEffect(tier, this);
    }

    public int GetStack(string upgradeId)
    {
        return UpgradeStacks.TryGetValue(upgradeId, out int v) ? v : 0;
    }

    public bool IsMaxed(string upgradeId) => GetStack(upgradeId) >= 3;

    // ── Internal ───────────────────────────────────────────────────────

    private void CheckLevelUp()
    {
        int thresholdIndex = CurrentRunLevel - 1;
        if (thresholdIndex >= XpThresholds.Length) return; // max level reached

        if (CurrentXP >= XpThresholds[thresholdIndex])
        {
            CurrentXP -= XpThresholds[thresholdIndex];
            CurrentRunLevel++;
            Debug.Log($"[RunSessionData] *** LEVEL UP → level {CurrentRunLevel} ***");
            OnLevelUp?.Invoke();
        }
    }

    public int GetXpForNextLevel()
    {
        int idx = CurrentRunLevel - 1;
        return idx < XpThresholds.Length ? XpThresholds[idx] : 9999;
    }
}
