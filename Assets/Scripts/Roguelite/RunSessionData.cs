using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Синглтон — хранит XP, апгрейды и модификаторы текущего забега.
/// Сбрасывается при старте каждого уровня через ResetForNewLevel().
/// НЕ сохраняется между уровнями.
/// </summary>
public class RunSessionData : MonoBehaviour
{
    public static RunSessionData Instance { get; private set; }

    // ── XP и уровни ──────────────────────────────────────────────────────
    public int CurrentXP { get; private set; }
    public int CurrentRunLevel { get; private set; } = 1;

    // Пороги XP для каждого уровня (индекс = уровень-1, значение = нужный XP для перехода)
    private static readonly int[] XpThresholds = { 100, 150, 200, 250, 300, 375, 450, 550, 700, 900 };

    // ── Стаки апгрейдов: upgradeId → сколько раз взято (0-3) ─────────────
    public Dictionary<string, int> UpgradeStacks { get; private set; } = new Dictionary<string, int>();

    // ── Числовые модификаторы (аддитивные поверх базовых статов) ─────────
    private Dictionary<string, float> _modifiers = new Dictionary<string, float>();

    // ── Флаги для Ultimate-эффектов ───────────────────────────────────────
    private HashSet<string> _flags = new HashSet<string>();

    // ── Событие левел апа (UI подписывается) ──────────────────────────────
    public System.Action OnLevelUp;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        // НЕ DontDestroyOnLoad — сессионные данные живут только в сцене уровня
    }

    // ── Публичный API ─────────────────────────────────────────────────────

    public void ResetForNewLevel()
    {
        CurrentXP = 0;
        CurrentRunLevel = 1;
        UpgradeStacks.Clear();
        _modifiers.Clear();
        _flags.Clear();
    }

    /// <summary>Начислить XP. Вызывать из XPManager.</summary>
    public void AddXP(int amount)
    {
        if (amount <= 0) return;

        // XP начисляется только во время активного геймплея
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

    /// <summary>Аддитивный модификатор. Один и тот же ключ может быть добавлен несколько раз.</summary>
    public void AddModifier(string key, float value)
    {
        if (!_modifiers.ContainsKey(key)) _modifiers[key] = 0f;
        _modifiers[key] += value;
    }

    /// <summary>Прочитать суммарный модификатор. Возвращает defaultValue если не задан.</summary>
    public float GetModifier(string key, float defaultValue = 0f)
    {
        return _modifiers.TryGetValue(key, out float val) ? val : defaultValue;
    }

    public void SetFlag(string flagId) => _flags.Add(flagId);
    public bool HasFlag(string flagId) => _flags.Contains(flagId);

    /// <summary>Применить апгрейд — увеличить стак и запустить эффект.</summary>
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

    // ── Внутреннее ───────────────────────────────────────────────────────

    private void CheckLevelUp()
    {
        int thresholdIndex = CurrentRunLevel - 1;
        if (thresholdIndex >= XpThresholds.Length) return; // достигнут максимум

        if (CurrentXP >= XpThresholds[thresholdIndex])
        {
            CurrentXP -= XpThresholds[thresholdIndex];
            CurrentRunLevel++;
            Debug.Log($"[RunSessionData] *** LEVEL UP → уровень {CurrentRunLevel} ***");
            OnLevelUp?.Invoke();
        }
    }

    public int GetXpForNextLevel()
    {
        int idx = CurrentRunLevel - 1;
        return idx < XpThresholds.Length ? XpThresholds[idx] : 9999;
    }
}
