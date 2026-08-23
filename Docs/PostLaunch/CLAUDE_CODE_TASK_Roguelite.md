# TASK: Roguelite In-Run Progression System (MVP)
> Передай этот файл Claude Code как промпт. Он содержит всё необходимое для реализации.

---

## КОНТЕКСТ ПРОЕКТА

Unity-игра EvacZombie. Top-down тактика: игрок размещает карты-способности до волны зомби, эвакуирует гражданских.

### Ключевые существующие файлы (прочитай их в первую очередь):

```
Assets/Scripts/Data/CardData.cs          ← ScriptableObject карты, StatType enum
Assets/Scripts/CardManager.cs            ← CardType enum, рендер колоды
Assets/Scripts/PlayerProfile.cs          ← профиль игрока, currentDeck, ownedCardsProgress
Assets/Scripts/GameManager.cs            ← AddRescuedFromTransport(), AddRescuedHumans()
Assets/Scripts/Bonus/HelicopterController.cs  ← герой MVP, читай паттерн инициализации
Assets/Scripts/Bonus/Sniper.cs           ← паттерн чтения CardData + Start()
Assets/Scripts/Bonus/Bomb.cs             ← паттерн чтения CardData + Start()
Assets/Scripts/Bonus/Barricade.cs        ← паттерн чтения CardData + Start()
Assets/Scripts/Bonus/Soldier.cs          ← для справки
Assets/Scripts/Bonus/Bait.cs             ← для справки
Assets/Scripts/Bonus/CarController.cs    ← для справки
Assets/Scripts/Bonus/CombatHelicopter.cs ← для справки
```

### Существующие enum'ы (не менять):

```csharp
// CardData.cs
public enum StatType { None, Capacity, Speed, Damage, FireRate, Radius, Duration, Cooldown, Count, Health }
public enum CardCategory { Evacuation, Combat, Utility }
public enum CardRarity { Common, Rare, Epic, Legendary }

// CardManager.cs
public enum CardType { None, Helicopter, Soldier, Bait, Bomb, Car, Sniper, CombatHelicopter, Barricade }
```

### Паттерн инициализации карты (используется ВЕЗДЕ — не нарушать):

```csharp
private void Start()
{
    int currentLevel = 1;
    if (PlayerProfile.Instance != null && myCardData != null)
    {
        var progress = PlayerProfile.Instance.ownedCardsProgress.Find(p => p.cardId == myCardData.name);
        if (progress != null) currentLevel = progress.currentLevel;

        someValue = myCardData.GetCalculatedStat(StatType.Capacity, currentLevel);
        // ... остальные статы
    }
    // fallback defaults если CardData null
}
```

---

## ЗАДАЧА

Реализовать систему roguelite прогрессии **только внутри одного уровня**:
- Игрок получает XP за убийство зомби и эвакуацию гражданских
- При накоплении нужного XP — пауза, выбор из 3 улучшений
- Улучшения модифицируют статы карт **только в текущем забеге**
- При старте нового уровня — всё сбрасывается
- Стакинг: взял одно улучшение 2 раза → Enhanced-версия; 3 раза → Ultimate

**MVP-скоуп: Герой Вертолёт + Карты Sniper, Bomb, Barricade**

---

## ШАГ 1: Создать `Assets/Scripts/Roguelite/RunSessionData.cs`

Синглтон. Хранит состояние текущего забега. Сбрасывается при каждом старте уровня.

```csharp
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
    // Ключи: см. раздел "КЛЮЧИ МОДИФИКАТОРОВ" ниже
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

    /// <summary>Начислить XP. Вызывать из GameManager и контроллеров зомби.</summary>
    public void AddXP(int amount)
    {
        if (amount <= 0) return;
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
            OnLevelUp?.Invoke();
        }
    }

    public int GetXpForNextLevel()
    {
        int idx = CurrentRunLevel - 1;
        return idx < XpThresholds.Length ? XpThresholds[idx] : 9999;
    }
}
```

---

## ШАГ 2: Создать `Assets/Scripts/Roguelite/RunUpgradeDefinition.cs`

ScriptableObject для каждого улучшения. Логика эффекта — здесь, не в UpgradeManager.

```csharp
using UnityEngine;

public enum UpgradeTier { Tier1 = 1, Tier2_Enhanced = 2, Tier3_Ultimate = 3 }
public enum UpgradeEffectType
{
    // Вертолёт
    Helicopter_SpeedMult,       // verticalSpeed *= (1 + value)
    Helicopter_CapacityAdd,     // maxCapacity += value
    Helicopter_RadiusMult,      // attractRadius *= (1 + value)
    Helicopter_LoadTimeAdd,     // loadTime += value
    Helicopter_BoardingReduction, // boardingCooldown *= (1 - value)
    Helicopter_NoPanic,         // флаг: никогда не паникует
    Helicopter_InstantLand,     // флаг: мгновенная посадка
    Helicopter_UnlimitedCapacity, // флаг: безлимитная вместимость
    Helicopter_MegaphoneUlt,    // флаг: все гражданские бегут к вертолёту

    // Снайпер
    Sniper_RangeMult,           // attackRange *= (1 + value)
    Sniper_DamageMult,          // damage *= (1 + value)
    Sniper_CooldownReduction,   // cooldownDelay *= (1 - value)
    Sniper_DurationAdd,         // lifespan += value
    Sniper_PierceAdd,           // maxPierceTargets += value
    Sniper_Instakill,           // флаг: instakill non-boss
    Sniper_Permanent,           // флаг: lifespan = бесконечен
    Sniper_GlobalRange,         // флаг: бьёт по всей карте
    Sniper_TripleTarget,        // флаг: 3 цели одновременно

    // Бомба
    Bomb_RadiusMult,            // damageRadius *= (1 + value)
    Bomb_DamageMult,            // damage *= (1 + value)
    Bomb_ClusterCount,          // мини-бомбы: добавить value штук
    Bomb_Count,                 // количество бомб за активацию += value
    Bomb_DestroyBuildings,      // флаг: уничтожает здания
    Bomb_Stun,                  // флаг: оглушает выживших зомби
    Bomb_MegaRadius,            // флаг: radius = половина карты

    // Баррикада
    Barricade_HPMult,           // maxHealth *= (1 + value)
    Barricade_ReflectDamage,    // флаг + value: % отражения урона
    Barricade_StunDuration,     // длительность оглушения += value
    Barricade_WidthMult,        // NavMeshObstacle scale *= (1 + value)
    Barricade_CountAdd,         // количество баррикад за установку += value
    Barricade_Indestructible,   // флаг: нельзя уничтожить
    Barricade_DeathZone,        // флаг: зона мгновенной смерти
    Barricade_FullWidth,        // флаг: блокирует всю улицу

    // Общие
    General_XPMult,             // множитель входящего XP
    General_NoPanic,            // флаг: герой не паникует 5с после зомби рядом
}

[CreateAssetMenu(fileName = "NewUpgrade", menuName = "ZombieGame/Run Upgrade")]
public class RunUpgradeDefinition : ScriptableObject
{
    [Header("Идентификация")]
    public string upgradeId;              // напр. "helicopter_quicklanding"
    public CardManager.CardType targetCardType; // CardType.None = General

    [Header("Отображение (Тир 1)")]
    public string displayName;
    [TextArea(2, 4)] public string descriptionTier1;
    public Sprite iconTier1;
    public Color accentColor = Color.white;

    [Header("Отображение (Тир 2 — Enhanced)")]
    public string displayNameEnhanced;
    [TextArea(2, 4)] public string descriptionTier2;
    public Sprite iconTier2;

    [Header("Отображение (Тир 3 — ULTIMATE)")]
    public string displayNameUltimate;
    [TextArea(2, 4)] public string descriptionTier3;
    public Sprite iconTier3;
    public Color ultimateColor = new Color(1f, 0.8f, 0.2f); // золотой

    [Header("Эффекты")]
    public UpgradeEffectType effectType;
    public float valueT1;   // значение для тира 1
    public float valueT2;   // значение для тира 2 (суммарное с T1)
    public float valueT3;   // значение для тира 3 / флаг (суммарное)

    // ── Применение эффекта ────────────────────────────────────────────────

    /// <summary>Вызывается из RunSessionData.ApplyUpgrade(). tier = 1, 2 или 3.</summary>
    public void ApplyEffect(int tier, RunSessionData session)
    {
        // Определяем значение для текущего тира
        float val = tier == 1 ? valueT1 : tier == 2 ? valueT2 : valueT3;

        switch (effectType)
        {
            // ── Вертолёт ──────────────────────────────────────────────
            case UpgradeEffectType.Helicopter_SpeedMult:
                session.AddModifier("heli_speed_mult", val);
                break;
            case UpgradeEffectType.Helicopter_CapacityAdd:
                session.AddModifier("heli_capacity_add", val);
                break;
            case UpgradeEffectType.Helicopter_RadiusMult:
                session.AddModifier("heli_radius_mult", val);
                break;
            case UpgradeEffectType.Helicopter_LoadTimeAdd:
                session.AddModifier("heli_loadtime_add", val);
                break;
            case UpgradeEffectType.Helicopter_BoardingReduction:
                session.AddModifier("heli_boarding_reduction", val);
                break;
            case UpgradeEffectType.Helicopter_NoPanic:
                session.SetFlag("heli_no_panic");
                break;
            case UpgradeEffectType.Helicopter_InstantLand:
                session.SetFlag("heli_instant_land");
                break;
            case UpgradeEffectType.Helicopter_UnlimitedCapacity:
                session.SetFlag("heli_unlimited_capacity");
                break;
            case UpgradeEffectType.Helicopter_MegaphoneUlt:
                session.SetFlag("heli_global_attract");
                break;

            // ── Снайпер ──────────────────────────────────────────────
            case UpgradeEffectType.Sniper_RangeMult:
                session.AddModifier("sniper_range_mult", val);
                break;
            case UpgradeEffectType.Sniper_DamageMult:
                session.AddModifier("sniper_damage_mult", val);
                break;
            case UpgradeEffectType.Sniper_CooldownReduction:
                session.AddModifier("sniper_cooldown_red", val);
                break;
            case UpgradeEffectType.Sniper_DurationAdd:
                session.AddModifier("sniper_duration_add", val);
                break;
            case UpgradeEffectType.Sniper_PierceAdd:
                session.AddModifier("sniper_pierce_add", val);
                break;
            case UpgradeEffectType.Sniper_Instakill:
                session.SetFlag("sniper_instakill");
                break;
            case UpgradeEffectType.Sniper_Permanent:
                session.SetFlag("sniper_permanent");
                break;
            case UpgradeEffectType.Sniper_GlobalRange:
                session.SetFlag("sniper_global_range");
                break;
            case UpgradeEffectType.Sniper_TripleTarget:
                session.SetFlag("sniper_triple_target");
                break;

            // ── Бомба ────────────────────────────────────────────────
            case UpgradeEffectType.Bomb_RadiusMult:
                session.AddModifier("bomb_radius_mult", val);
                break;
            case UpgradeEffectType.Bomb_DamageMult:
                session.AddModifier("bomb_damage_mult", val);
                break;
            case UpgradeEffectType.Bomb_ClusterCount:
                session.AddModifier("bomb_cluster_count", val);
                break;
            case UpgradeEffectType.Bomb_Count:
                session.AddModifier("bomb_count_add", val);
                break;
            case UpgradeEffectType.Bomb_DestroyBuildings:
                session.SetFlag("bomb_destroy_buildings");
                break;
            case UpgradeEffectType.Bomb_Stun:
                session.SetFlag("bomb_stun");
                break;
            case UpgradeEffectType.Bomb_MegaRadius:
                session.SetFlag("bomb_mega_radius");
                break;

            // ── Баррикада ────────────────────────────────────────────
            case UpgradeEffectType.Barricade_HPMult:
                session.AddModifier("barricade_hp_mult", val);
                break;
            case UpgradeEffectType.Barricade_ReflectDamage:
                session.AddModifier("barricade_reflect_pct", val);
                break;
            case UpgradeEffectType.Barricade_StunDuration:
                session.AddModifier("barricade_stun_dur", val);
                break;
            case UpgradeEffectType.Barricade_WidthMult:
                session.AddModifier("barricade_width_mult", val);
                break;
            case UpgradeEffectType.Barricade_CountAdd:
                session.AddModifier("barricade_count_add", val);
                break;
            case UpgradeEffectType.Barricade_Indestructible:
                session.SetFlag("barricade_indestructible");
                break;
            case UpgradeEffectType.Barricade_DeathZone:
                session.SetFlag("barricade_death_zone");
                break;
            case UpgradeEffectType.Barricade_FullWidth:
                session.SetFlag("barricade_full_width");
                break;

            // ── Общие ────────────────────────────────────────────────
            case UpgradeEffectType.General_XPMult:
                session.AddModifier("general_xp_mult", val);
                break;
            case UpgradeEffectType.General_NoPanic:
                session.SetFlag("general_no_panic");
                break;
        }
    }

    // ── Хелперы для UI ────────────────────────────────────────────────────

    public string GetDisplayName(int tier) => tier switch
    {
        1 => displayName,
        2 => displayNameEnhanced,
        3 => displayNameUltimate,
        _ => displayName
    };

    public string GetDescription(int tier) => tier switch
    {
        1 => descriptionTier1,
        2 => descriptionTier2,
        3 => descriptionTier3,
        _ => descriptionTier1
    };

    public Sprite GetIcon(int tier) => tier switch
    {
        1 => iconTier1 != null ? iconTier1 : iconTier2,
        2 => iconTier2 != null ? iconTier2 : iconTier1,
        3 => iconTier3 != null ? iconTier3 : iconTier1,
        _ => iconTier1
    };

    public Color GetAccentColor(int tier) => tier == 3 ? ultimateColor : accentColor;
}
```

---

## ШАГ 3: Создать `Assets/Scripts/Roguelite/UpgradeManager.cs`

Формирует пул, выбирает 3 варианта, передаёт выбор в RunSessionData.

```csharp
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
            // Только для карт, которые есть в деке, + общие
            if (!deckCardTypes.Contains(upg.targetCardType)) continue;

            int currentStack = session.GetStack(upg.upgradeId);
            if (currentStack >= 3) continue; // максимум достигнут

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
```

---

## ШАГ 4: Создать `Assets/Scripts/Roguelite/XPManager.cs`

Централизованный класс начисления XP. Читает модификатор `general_xp_mult`.

```csharp
using UnityEngine;

public class XPManager : MonoBehaviour
{
    public static XPManager Instance { get; private set; }

    [Header("XP за события")]
    public int xpPerZombieKill     = 5;
    public int xpPerBossKill       = 50;
    public int xpPerCivilian       = 20;
    public int xpPerScientist      = 40;
    public int xpBonusFullTransport = 25;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void OnZombieKilled(bool isBoss = false)
    {
        int raw = isBoss ? xpPerBossKill : xpPerZombieKill;
        Grant(raw);
    }

    public void OnCivilianEvacuated(int humanCount, int scientistCount, bool wasFullTransport = false)
    {
        int raw = humanCount * xpPerCivilian + scientistCount * xpPerScientist;
        if (wasFullTransport) raw += xpBonusFullTransport;
        Grant(raw);
    }

    private void Grant(int rawAmount)
    {
        if (RunSessionData.Instance == null) return;

        float mult = 1f + RunSessionData.Instance.GetModifier("general_xp_mult");
        int final = Mathf.RoundToInt(rawAmount * mult);
        RunSessionData.Instance.AddXP(final);
    }
}
```

---

## ШАГ 5: Модификации в GameManager.cs

Найди метод `AddRescuedFromTransport` и добавь вызов XPManager.
Найди или создай точку вызова при смерти зомби.

```csharp
// В AddRescuedFromTransport — ДОБАВИТЬ в конце перед/после начисления очков:
public void AddRescuedFromTransport(int humanCount, int scientistCount, Vector3 pos)
{
    // ... существующий код начисления очков ...

    // НОВОЕ: начисление XP
    bool wasFull = (humanCount + scientistCount) >= /* maxCapacity транспорта */;
    XPManager.Instance?.OnCivilianEvacuated(humanCount, scientistCount, wasFull);
}

// В AddRescuedHumans (используется CombatHelicopter) — ДОБАВИТЬ:
public void AddRescuedHumans(int count, Vector3 pos)
{
    // ... существующий код ...
    XPManager.Instance?.OnCivilianEvacuated(count, 0);
}
```

Также найди в `Zombie.cs` метод смерти зомби (скорее всего `Die()` или `TakeDamage` когда HP <= 0) и добавь:
```csharp
XPManager.Instance?.OnZombieKilled(false); // true для босса
```

---

## ШАГ 6: Модификации в HelicopterController.cs

В методе `Start()`, ПОСЛЕ существующего кода чтения из CardData, добавить:

```csharp
// ── Run-модификаторы (roguelite) ──────────────────────────────────────
var run = RunSessionData.Instance;
if (run != null)
{
    // Числовые модификаторы
    maxCapacity += (int)run.GetModifier("heli_capacity_add");
    verticalSpeed *= (1f + run.GetModifier("heli_speed_mult"));
    attractRadius *= (1f + run.GetModifier("heli_radius_mult"));
    loadTime += run.GetModifier("heli_loadtime_add");
    boardingCooldown *= Mathf.Max(0.05f, 1f - run.GetModifier("heli_boarding_reduction"));

    // Флаги Ultimate
    if (run.HasFlag("heli_no_panic"))
        panicRadius = 0f;

    if (run.HasFlag("heli_instant_land"))
        verticalSpeed = 9999f;

    if (run.HasFlag("heli_unlimited_capacity"))
        maxCapacity = 999;
}
// ── конец run-модификаторов ───────────────────────────────────────────
```

Флаг `heli_global_attract` (Ultimate мегафона — все гражданские бегут к вертолёту):
В методе `StartLoading()`, после существующего цикла по Human.AllHumans, добавить:

```csharp
// Ultimate: Городская Тревога — привлекаем ВСЕХ гражданских независимо от радиуса
if (RunSessionData.Instance != null && RunSessionData.Instance.HasFlag("heli_global_attract"))
{
    foreach (var h in Human.AllHumans)
    {
        if (h == null || h.rescueTarget != null) continue;
        h.SetRescueTarget(transform);
    }
    foreach (var s in Scientist.AllScientists)
    {
        if (s == null || s.rescueTarget != null) continue;
        s.SetRescueTarget(transform);
    }
}
```

---

## ШАГ 7: Модификации в Sniper.cs

В `Start()`, ПОСЛЕ существующего блока чтения CardData:

```csharp
var run = RunSessionData.Instance;
if (run != null)
{
    attackRange *= (1f + run.GetModifier("sniper_range_mult"));
    damage = Mathf.RoundToInt(damage * (1f + run.GetModifier("sniper_damage_mult")));
    cooldownDelay *= Mathf.Max(0.1f, 1f - run.GetModifier("sniper_cooldown_red"));
    lifespan += run.GetModifier("sniper_duration_add");
    maxPierceTargets += (int)run.GetModifier("sniper_pierce_add");

    if (run.HasFlag("sniper_global_range"))
        attackRange = 9999f;

    if (run.HasFlag("sniper_permanent"))
        lifespan = 99999f;
}
```

Флаг `sniper_instakill` — в методе `ApplyPiercingDamage`:
```csharp
// Перед z.TakeDamage(damage):
int actualDamage = (RunSessionData.Instance != null && RunSessionData.Instance.HasFlag("sniper_instakill"))
    ? 99999
    : Mathf.RoundToInt(currentDamage);
z.TakeDamage(actualDamage);
```

Флаг `sniper_triple_target` — в `SniperRoutine()`, вместо одной цели ищем 3 и стреляем по каждой (только если флаг установлен). Реализуй упрощённо: если флаг есть — найди TopN=3 ближайших зомби и вызови `ApplyPiercingDamage` для каждого в цикле.

---

## ШАГ 8: Модификации в Bomb.cs

В `Start()`, ПОСЛЕ существующего блока:

```csharp
var run = RunSessionData.Instance;
if (run != null)
{
    damageRadius *= (1f + run.GetModifier("bomb_radius_mult"));
    damage = Mathf.RoundToInt(damage * (1f + run.GetModifier("bomb_damage_mult")));

    if (run.HasFlag("bomb_mega_radius"))
        damageRadius = 150f; // покрывает половину карты
}
```

В методе `Explode()`, ПОСЛЕ основного урона по зомби:

```csharp
var run = RunSessionData.Instance;
if (run != null)
{
    // Destroy buildings if flag set
    if (run.HasFlag("bomb_destroy_buildings"))
    {
        foreach (var h in hits)
        {
            if (h != null && h.CompareTag("Building"))
                Destroy(h.gameObject);
        }
    }

    // Stun survivors
    if (run.HasFlag("bomb_stun"))
    {
        Collider[] stunHits = Physics.OverlapSphere(targetPos, damageRadius);
        foreach (var h in stunHits)
        {
            Zombie z = h?.GetComponent<Zombie>();
            if (z != null && !z.IsDead)
                z.Stun(5f); // метод Stun нужно реализовать в Zombie.cs если отсутствует
        }
    }

    // Cluster bombs
    int clusterCount = (int)run.GetModifier("bomb_cluster_count");
    if (clusterCount > 0)
    {
        for (int i = 0; i < clusterCount; i++)
        {
            Vector2 rndOffset = Random.insideUnitCircle * damageRadius * 0.7f;
            Vector3 miniPos = targetPos + new Vector3(rndOffset.x, 0, rndOffset.y);
            // Нанести урон 150 в радиусе damageRadius * 0.3f в точке miniPos
            Collider[] miniHits = Physics.OverlapSphere(miniPos, damageRadius * 0.3f);
            foreach (var mh in miniHits)
            {
                mh?.GetComponent<Zombie>()?.TakeDamage(150);
            }
        }
    }
}
```

---

## ШАГ 9: Модификации в Barricade.cs

В `Start()`, ПОСЛЕ блока чтения из CardData:

```csharp
var run = RunSessionData.Instance;
if (run != null)
{
    float hpMult = 1f + run.GetModifier("barricade_hp_mult");
    maxHealth = Mathf.RoundToInt(maxHealth * hpMult);

    if (run.HasFlag("barricade_indestructible"))
        maxHealth = 999999;

    // Масштаб NavMeshObstacle
    float widthMult = run.GetModifier("barricade_width_mult");
    if (widthMult > 0f)
    {
        transform.localScale *= (1f + widthMult);
    }
}
currentHealth = maxHealth;
```

В `TakeDamage(int damage)`, перед `currentHealth -= damage`:

```csharp
// Reflect damage
var run = RunSessionData.Instance;
if (run != null)
{
    float reflectPct = run.GetModifier("barricade_reflect_pct");
    if (reflectPct > 0f)
    {
        // Ищем ближайшего атакующего зомби и наносим ему урон
        Collider[] nearby = Physics.OverlapSphere(transform.position, 3f);
        foreach (var c in nearby)
        {
            Zombie z = c?.GetComponent<Zombie>();
            if (z != null)
            {
                z.TakeDamage(Mathf.RoundToInt(damage * reflectPct));
                break;
            }
        }
    }

    // Death zone: убиваем всех зомби в 1.5м немедленно при атаке
    if (run.HasFlag("barricade_death_zone"))
    {
        Collider[] deathZone = Physics.OverlapSphere(transform.position, 1.5f);
        foreach (var c in deathZone)
            c?.GetComponent<Zombie>()?.TakeDamage(99999);
    }
}
```

Флаг `barricade_stun_dur` — найди место где зомби атакует баррикаду (в `Zombie.cs`, метод InfectTarget или аналогичный) и добавь оглушение после удара:
```csharp
// Если зомби ударил баррикаду и barricade_stun_dur > 0:
float stunDur = RunSessionData.Instance?.GetModifier("barricade_stun_dur") ?? 0f;
if (stunDur > 0f) zombie.Stun(stunDur);
```

---

## ШАГ 10: Создать UI — `Assets/Scripts/UI/LevelUpScreen.cs`

```csharp
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// Экран выбора апгрейда при Level Up.
/// Подписывается на RunSessionData.OnLevelUp.
/// Паузит Time.timeScale = 0 пока открыт.
/// </summary>
public class LevelUpScreen : MonoBehaviour
{
    [Header("UI References")]
    public GameObject screenRoot;         // корневой объект экрана (включаем/выключаем)
    public TextMeshProUGUI levelUpTitle;  // "LEVEL UP!" или "Уровень 3"
    public UpgradeCardUI[] upgradeCards; // ровно 3 карточки

    [Header("Настройки")]
    public CardManager.CardType heroType = CardManager.CardType.Helicopter;

    private List<(RunUpgradeDefinition, int)> _currentOptions;

    private void Awake()
    {
        screenRoot.SetActive(false);
    }

    private void OnEnable()
    {
        if (RunSessionData.Instance != null)
            RunSessionData.Instance.OnLevelUp += ShowScreen;
    }

    private void OnDisable()
    {
        if (RunSessionData.Instance != null)
            RunSessionData.Instance.OnLevelUp -= ShowScreen;
    }

    private void ShowScreen()
    {
        var deck = PlayerProfile.Instance?.currentDeck ?? new List<CardData>();
        _currentOptions = UpgradeManager.Instance.GetUpgradeOptions(deck, heroType);

        if (_currentOptions.Count == 0)
        {
            // Нет доступных апгрейдов — пропускаем
            return;
        }

        Time.timeScale = 0f;
        screenRoot.SetActive(true);

        if (levelUpTitle != null)
            levelUpTitle.text = $"УРОВЕНЬ {RunSessionData.Instance.CurrentRunLevel}";

        for (int i = 0; i < upgradeCards.Length; i++)
        {
            if (i < _currentOptions.Count)
            {
                var (upg, tier) = _currentOptions[i];
                upgradeCards[i].gameObject.SetActive(true);
                upgradeCards[i].Setup(upg, tier, OnCardSelected);
            }
            else
            {
                upgradeCards[i].gameObject.SetActive(false);
            }
        }
    }

    private void OnCardSelected(RunUpgradeDefinition upgrade)
    {
        UpgradeManager.Instance.ApplyUpgrade(upgrade);
        screenRoot.SetActive(false);
        Time.timeScale = 1f;
    }
}
```

---

## ШАГ 11: Создать `Assets/Scripts/UI/UpgradeCardUI.cs`

```csharp
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class UpgradeCardUI : MonoBehaviour
{
    [Header("UI Elements")]
    public Image cardBackground;
    public Image upgradeIcon;
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI descriptionText;
    public TextMeshProUGUI tierBadgeText;    // "★" / "★★" / "★★★"
    public Image tierGlow;                   // подсветка для Ultimate
    public Button selectButton;

    private RunUpgradeDefinition _upgrade;
    private Action<RunUpgradeDefinition> _onSelected;

    public void Setup(RunUpgradeDefinition upgrade, int tier, Action<RunUpgradeDefinition> onSelected)
    {
        _upgrade = upgrade;
        _onSelected = onSelected;

        titleText.text = upgrade.GetDisplayName(tier);
        descriptionText.text = upgrade.GetDescription(tier);

        var icon = upgrade.GetIcon(tier);
        if (icon != null) upgradeIcon.sprite = icon;

        Color accent = upgrade.GetAccentColor(tier);
        if (cardBackground != null) cardBackground.color = new Color(accent.r, accent.g, accent.b, 0.15f);

        tierBadgeText.text = tier switch { 1 => "★", 2 => "★★", 3 => "★★★ ULTIMATE", _ => "★" };

        if (tierGlow != null)
            tierGlow.gameObject.SetActive(tier == 3);

        selectButton.onClick.RemoveAllListeners();
        selectButton.onClick.AddListener(() => _onSelected?.Invoke(_upgrade));
    }
}
```

---

## ШАГ 12: Создать `Assets/Scripts/UI/XPBarUI.cs`

```csharp
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class XPBarUI : MonoBehaviour
{
    [Header("UI References")]
    public Slider xpSlider;
    public TextMeshProUGUI levelText;    // "Ур. 3"
    public TextMeshProUGUI xpText;      // "150 / 200"

    private void Update()
    {
        var session = RunSessionData.Instance;
        if (session == null) return;

        int current = session.CurrentXP;
        int max = session.GetXpForNextLevel();

        if (xpSlider != null)
        {
            xpSlider.minValue = 0;
            xpSlider.maxValue = max;
            xpSlider.value = current;
        }

        if (levelText != null)
            levelText.text = $"Ур. {session.CurrentRunLevel}";

        if (xpText != null)
            xpText.text = $"{current} / {max}";
    }
}
```

---

## ШАГ 13: Создать ScriptableObjects для MVP-апгрейдов

Создай папку `Assets/Resources/Upgrades/`.
Создай следующие ScriptableObject'ы через меню `ZombieGame/Run Upgrade`:

### ВЕРТОЛЁТ

**`upgrade_heli_speed.asset`**
- upgradeId: `helicopter_quicklanding`
- targetCardType: `Helicopter`
- effectType: `Helicopter_SpeedMult`
- displayName: "Быстрая Посадка"
- displayNameEnhanced: "Форсаж"
- displayNameUltimate: "VTOL-Протокол"
- descriptionTier1: "+35% скорость снижения"
- descriptionTier2: "Скорость x2.5, первые 3 сек. после посадки — зомби не вызывают панику"
- descriptionTier3: "Вертолёт телепортируется к цели мгновенно"
- valueT1: 0.35 | valueT2: 1.5 | valueT3: 9999
- (T3 использует флаг — измени effectType на `Helicopter_InstantLand` для T3, или обрабатывай в ApplyEffect по значению tier)

> **Важно:** для улучшений где T1/T2 = числовой бонус, а T3 = флаг — используй отдельный `case` по tier внутри `ApplyEffect`, или создай два отдельных asset'а и объедини в один через override-логику. Рекомендую: в `ApplyEffect` проверять `if (tier == 3)` и ставить флаг вместо числового модификатора.

**`upgrade_heli_capacity.asset`**
- upgradeId: `helicopter_capacity`
- effectType: `Helicopter_CapacityAdd`
- displayName: "Доп. Места" | Enhanced: "Тяжёлый Транспорт" | Ultimate: "Массовая Эвакуация"
- descriptionTier1: "+2 места" | T2: "+6 мест суммарно" | T3: "Вместимость — все гражданские в зоне"
- valueT1: 2 | valueT2: 4 | (T3 → флаг `heli_unlimited_capacity`)

**`upgrade_heli_attract.asset`**
- upgradeId: `helicopter_megaphone`
- effectType: `Helicopter_RadiusMult`
- displayName: "Мегафон" | Enhanced: "Экстренное вещание" | Ultimate: "Городская Тревога"
- descriptionTier1: "+40% радиус привлечения" | T2: "Радиус x2, гражданские бегут" | T3: "ВСЕ гражданские на карте идут к вертолёту"
- valueT1: 0.40 | valueT2: 1.0 | (T3 → флаг `heli_global_attract`)

### СНАЙПЕР

**`upgrade_sniper_range.asset`**
- upgradeId: `sniper_range`
- effectType: `Sniper_RangeMult`
- displayName: "Удлинённый Ствол" | Enhanced: "Дальнобойная Платформа" | Ultimate: "Всевидящее Око"
- valueT1: 0.35 | valueT2: 0.65 | (T3 → `sniper_global_range`)

**`upgrade_sniper_damage.asset`**
- upgradeId: `sniper_damage`
- effectType: `Sniper_DamageMult`
- displayName: "Бронебойные Патроны" | Enhanced: "Экзекутор" | Ultimate: "Шторм Свинца"
- valueT1: 0.5 | valueT2: 1.0 | (T3 → `sniper_instakill`)

**`upgrade_sniper_duration.asset`**
- upgradeId: `sniper_duration`
- effectType: `Sniper_DurationAdd`
- displayName: "Долгая Вахта" | Enhanced: "Оверватч" | Ultimate: "Вечный Страж"
- valueT1: 8 | valueT2: 9999 (флаг `sniper_permanent`) | T3: флаг `sniper_permanent` + respawn

### БОМБА

**`upgrade_bomb_radius.asset`**
- upgradeId: `bomb_radius`
- effectType: `Bomb_RadiusMult`
- displayName: "Увеличенная Боеголовка" | Enhanced: "Тактическое Ядро" | Ultimate: "Термобарика"
- valueT1: 0.4 | valueT2: 2.0 | (T3 → `bomb_mega_radius`)

**`upgrade_bomb_damage.asset`**
- upgradeId: `bomb_damage`
- effectType: `Bomb_DamageMult`
- displayName: "Ударная Волна" | Enhanced: "Избыточная Сила" | Ultimate: "ЭМИ-Взрыв"
- valueT1: 1.0 | valueT2: 9.0 | (T3 → `bomb_stun`)

**`upgrade_bomb_cluster.asset`**
- upgradeId: `bomb_cluster`
- effectType: `Bomb_ClusterCount`
- displayName: "Кассетная Начинка" | Enhanced: "Ковровая Бомбардировка" | Ultimate: "Роевой Удар"
- valueT1: 3 | valueT2: 5 | valueT3: 12

### БАРРИКАДА

**`upgrade_barricade_hp.asset`**
- upgradeId: `barricade_hp`
- effectType: `Barricade_HPMult`
- displayName: "Армированный Бетон" | Enhanced: "Крепостная Стена" | Ultimate: "Несокрушимый Бункер"
- valueT1: 0.5 | valueT2: 3.0 | (T3 → `barricade_indestructible`)

**`upgrade_barricade_spike.asset`**
- upgradeId: `barricade_spike`
- effectType: `Barricade_ReflectDamage`
- displayName: "Колючая Проволока" | Enhanced: "Бритвенный Провод" | Ultimate: "Зона Смерти"
- valueT1: 0.25 | valueT2: 0.75 | (T3 → `barricade_death_zone`)

**`upgrade_barricade_count.asset`**
- upgradeId: `barricade_count`
- effectType: `Barricade_CountAdd`
- displayName: "Быстрое Развёртывание" | Enhanced: "Минное Поле" | Ultimate: "Периметр"
- valueT1: 1 | valueT2: 1 | (T3 → логика авто-кольца из 6 баррикад — реализуй в отдельном месте)

### ОБЩИЕ (2 штуки для MVP)

**`upgrade_general_xp.asset`**
- upgradeId: `general_xp`
- targetCardType: `None`
- effectType: `General_XPMult`
- displayName: "Ускоренное Обучение"
- descriptionTier1: "+25% к получаемому XP"
- descriptionTier2: "+60% к получаемому XP"
- descriptionTier3: "+100% к получаемому XP"
- valueT1: 0.25 | valueT2: 0.35 | valueT3: 0.40

**`upgrade_general_nopanic.asset`**
- upgradeId: `general_nopanic`
- targetCardType: `None`
- effectType: `General_NoPanic`
- displayName: "Ветеранский Инстинкт"
- descriptionTier1-3: "Герой не уходит из-за паники от зомби"
- valueT1/T2/T3: 0 (только флаг)

---

## ШАГ 14: Инициализация и сцена

### GameManager или LevelManager — добавить в старте уровня:

```csharp
// Сброс сессии при каждом старте уровня
private void Start()
{
    RunSessionData.Instance?.ResetForNewLevel();
    // ... остальная инициализация ...
}
```

### В сцене уровня добавить объекты:

1. **RunSessionData** — пустой GameObject с компонентом `RunSessionData`
2. **XPManager** — пустой GameObject с компонентом `XPManager`
3. **UpgradeManager** — пустой GameObject с компонентом `UpgradeManager` (список апгрейдов — через Resources/Upgrades/ или вручную)
4. **LevelUpScreen** — Canvas-объект с компонентом `LevelUpScreen`, дочерние `UpgradeCardUI` x3
5. **XPBarUI** — HUD-элемент с компонентом `XPBarUI`, Slider + TextMeshPro

### Порядок Awake/Start не должен создавать проблем — все через `Instance?.` (null-safe).

---

## ОГРАНИЧЕНИЯ И ПРАВИЛА

1. **НЕ менять** `CardData.cs`, `CardManager.cs`, `PlayerProfile.cs` — только читать.
2. **НЕ менять** существующую логику начисления очков в `GameManager` — только добавлять вызовы XPManager.
3. **НЕ использовать** `DontDestroyOnLoad` для `RunSessionData` — он живёт только в сцене уровня.
4. Все вызовы к синглтонам через null-safe `?.` — сцены без roguelite должны работать без ошибок.
5. **Не добавлять** визуальные эффекты для Ultimate в MVP — только функциональность.
6. Класс `Zombie` — если нет метода `Stun(float duration)` — реализуй минимальную версию: установи флаг `isStunned`, в Update() пока флаг — не двигаться/атаковать, через `duration` секунд — снять флаг.

---

## ФИНАЛЬНЫЙ ЧЕКЛИСТ

- [ ] `RunSessionData.cs` создан и работает как синглтон
- [ ] `RunUpgradeDefinition.cs` создан, ScriptableObject
- [ ] `UpgradeManager.cs` создан, формирует пул из 3 вариантов
- [ ] `XPManager.cs` создан, вызывается из GameManager
- [ ] `GameManager.cs` — добавлены вызовы XPManager
- [ ] `Zombie.cs` — добавлен вызов `XPManager.OnZombieKilled()` при смерти
- [ ] `HelicopterController.cs` — добавлено чтение run-модификаторов
- [ ] `Sniper.cs` — добавлено чтение run-модификаторов
- [ ] `Bomb.cs` — добавлено чтение run-модификаторов + cluster + stun
- [ ] `Barricade.cs` — добавлено чтение run-модификаторов + reflect damage
- [ ] `LevelUpScreen.cs` + `UpgradeCardUI.cs` созданы
- [ ] `XPBarUI.cs` создан
- [ ] ScriptableObjects созданы в `Assets/Resources/Upgrades/` (минимум 11 штук)
- [ ] `RunSessionData.ResetForNewLevel()` вызывается при старте уровня
- [ ] Всё работает без ошибок если RunSessionData == null (старые сцены)

---

*GDD с полным дизайном: `Docs/GDD_Heroes_Roguelite_System.md`*
