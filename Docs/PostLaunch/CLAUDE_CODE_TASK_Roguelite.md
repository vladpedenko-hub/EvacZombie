# TASK: Roguelite In-Run Progression System (MVP)
> Hand this file to Claude Code as a prompt. It contains everything needed for implementation.

---

## PROJECT CONTEXT

Unity game EvacZombie. Top-down tactics: the player places ability cards ahead of a zombie wave, evacuating civilians.

### Key existing files (read these first):

```
Assets/Scripts/Data/CardData.cs          ← card ScriptableObject, StatType enum
Assets/Scripts/CardManager.cs            ← CardType enum, deck rendering
Assets/Scripts/PlayerProfile.cs          ← player profile, currentDeck, ownedCardsProgress
Assets/Scripts/GameManager.cs            ← AddRescuedFromTransport(), AddRescuedHumans()
Assets/Scripts/Bonus/HelicopterController.cs  ← MVP hero, read its init pattern
Assets/Scripts/Bonus/Sniper.cs           ← CardData-reading + Start() pattern
Assets/Scripts/Bonus/Bomb.cs             ← CardData-reading + Start() pattern
Assets/Scripts/Bonus/Barricade.cs        ← CardData-reading + Start() pattern
Assets/Scripts/Bonus/Soldier.cs          ← for reference
Assets/Scripts/Bonus/Bait.cs             ← for reference
Assets/Scripts/Bonus/CarController.cs    ← for reference
Assets/Scripts/Bonus/CombatHelicopter.cs ← for reference
```

### Existing enums (do not change):

```csharp
// CardData.cs
public enum StatType { None, Capacity, Speed, Damage, FireRate, Radius, Duration, Cooldown, Count, Health }
public enum CardCategory { Evacuation, Combat, Utility }
public enum CardRarity { Common, Rare, Epic, Legendary }

// CardManager.cs
public enum CardType { None, Helicopter, Soldier, Bait, Bomb, Car, Sniper, CombatHelicopter, Barricade }
```

### Card initialization pattern (used EVERYWHERE — do not break it):

```csharp
private void Start()
{
    int currentLevel = 1;
    if (PlayerProfile.Instance != null && myCardData != null)
    {
        var progress = PlayerProfile.Instance.ownedCardsProgress.Find(p => p.cardId == myCardData.name);
        if (progress != null) currentLevel = progress.currentLevel;

        someValue = myCardData.GetCalculatedStat(StatType.Capacity, currentLevel);
        // ... other stats
    }
    // fallback defaults if CardData is null
}
```

---

## TASK

Implement a roguelite progression system **scoped to a single level only**:
- The player gains XP from killing zombies and evacuating civilians
- Once enough XP accumulates — pause, choose 1 of 3 upgrades
- Upgrades modify card stats **only for the current run**
- Everything resets when a new level starts
- Stacking: pick the same upgrade twice → Enhanced version; three times → Ultimate

**MVP scope: Helicopter Hero + Sniper, Bomb, Barricade cards**

---

## STEP 1: Create `Assets/Scripts/Roguelite/RunSessionData.cs`

A singleton. Holds the current run's state. Resets at the start of every level.

```csharp
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Singleton — holds the current run's XP, upgrades, and modifiers.
/// Reset at the start of every level via ResetForNewLevel().
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

    // ── Upgrade stacks: upgradeId → number of times taken (0-3) ─────────
    public Dictionary<string, int> UpgradeStacks { get; private set; } = new Dictionary<string, int>();

    // ── Numeric modifiers (additive on top of base stats) ─────────
    // Keys: see the "MODIFIER KEYS" section below
    private Dictionary<string, float> _modifiers = new Dictionary<string, float>();

    // ── Flags for Ultimate effects ───────────────────────────────────────
    private HashSet<string> _flags = new HashSet<string>();

    // ── Level-up event (UI subscribes to this) ──────────────────────────────
    public System.Action OnLevelUp;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        // NOT DontDestroyOnLoad — session data only lives for the level's scene
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

    /// <summary>Award XP. Call from GameManager and zombie controllers.</summary>
    public void AddXP(int amount)
    {
        if (amount <= 0) return;
        CurrentXP += amount;
        CheckLevelUp();
    }

    /// <summary>Additive modifier. The same key can be added to multiple times.</summary>
    public void AddModifier(string key, float value)
    {
        if (!_modifiers.ContainsKey(key)) _modifiers[key] = 0f;
        _modifiers[key] += value;
    }

    /// <summary>Read the accumulated modifier. Returns defaultValue if not set.</summary>
    public float GetModifier(string key, float defaultValue = 0f)
    {
        return _modifiers.TryGetValue(key, out float val) ? val : defaultValue;
    }

    public void SetFlag(string flagId) => _flags.Add(flagId);
    public bool HasFlag(string flagId) => _flags.Contains(flagId);

    /// <summary>Apply an upgrade — increase its stack and run its effect.</summary>
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
        if (thresholdIndex >= XpThresholds.Length) return; // max reached

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

## STEP 2: Create `Assets/Scripts/Roguelite/RunUpgradeDefinition.cs`

A ScriptableObject for each upgrade. The effect logic lives here, not in UpgradeManager.

```csharp
using UnityEngine;

public enum UpgradeTier { Tier1 = 1, Tier2_Enhanced = 2, Tier3_Ultimate = 3 }
public enum UpgradeEffectType
{
    // Helicopter
    Helicopter_SpeedMult,       // verticalSpeed *= (1 + value)
    Helicopter_CapacityAdd,     // maxCapacity += value
    Helicopter_RadiusMult,      // attractRadius *= (1 + value)
    Helicopter_LoadTimeAdd,     // loadTime += value
    Helicopter_BoardingReduction, // boardingCooldown *= (1 - value)
    Helicopter_NoPanic,         // flag: never panics
    Helicopter_InstantLand,     // flag: instant landing
    Helicopter_UnlimitedCapacity, // flag: unlimited capacity
    Helicopter_MegaphoneUlt,    // flag: every civilian runs to the helicopter

    // Sniper
    Sniper_RangeMult,           // attackRange *= (1 + value)
    Sniper_DamageMult,          // damage *= (1 + value)
    Sniper_CooldownReduction,   // cooldownDelay *= (1 - value)
    Sniper_DurationAdd,         // lifespan += value
    Sniper_PierceAdd,           // maxPierceTargets += value
    Sniper_Instakill,           // flag: instakill non-boss
    Sniper_Permanent,           // flag: lifespan = infinite
    Sniper_GlobalRange,         // flag: hits anywhere on the map
    Sniper_TripleTarget,        // flag: 3 targets at once

    // Bomb
    Bomb_RadiusMult,            // damageRadius *= (1 + value)
    Bomb_DamageMult,            // damage *= (1 + value)
    Bomb_ClusterCount,          // mini-bombs: add value of them
    Bomb_Count,                 // number of bombs per activation += value
    Bomb_DestroyBuildings,      // flag: destroys buildings
    Bomb_Stun,                  // flag: stuns surviving zombies
    Bomb_MegaRadius,            // flag: radius = half the map

    // Barricade
    Barricade_HPMult,           // maxHealth *= (1 + value)
    Barricade_ReflectDamage,    // flag + value: % damage reflected
    Barricade_StunDuration,     // stun duration += value
    Barricade_WidthMult,        // NavMeshObstacle scale *= (1 + value)
    Barricade_CountAdd,         // number of barricades placed += value
    Barricade_Indestructible,   // flag: cannot be destroyed
    Barricade_DeathZone,        // flag: instant-death zone
    Barricade_FullWidth,        // flag: blocks the entire street

    // General
    General_XPMult,             // multiplier on incoming XP
    General_NoPanic,            // flag: the hero doesn't panic for 5s after a zombie gets close
}

[CreateAssetMenu(fileName = "NewUpgrade", menuName = "ZombieGame/Run Upgrade")]
public class RunUpgradeDefinition : ScriptableObject
{
    [Header("Identity")]
    public string upgradeId;              // e.g. "helicopter_quicklanding"
    public CardManager.CardType targetCardType; // CardType.None = General

    [Header("Display (Tier 1)")]
    public string displayName;
    [TextArea(2, 4)] public string descriptionTier1;
    public Sprite iconTier1;
    public Color accentColor = Color.white;

    [Header("Display (Tier 2 — Enhanced)")]
    public string displayNameEnhanced;
    [TextArea(2, 4)] public string descriptionTier2;
    public Sprite iconTier2;

    [Header("Display (Tier 3 — ULTIMATE)")]
    public string displayNameUltimate;
    [TextArea(2, 4)] public string descriptionTier3;
    public Sprite iconTier3;
    public Color ultimateColor = new Color(1f, 0.8f, 0.2f); // gold

    [Header("Effects")]
    public UpgradeEffectType effectType;
    public float valueT1;   // value for tier 1
    public float valueT2;   // value for tier 2 (cumulative with T1)
    public float valueT3;   // value for tier 3 / flag (cumulative)

    // ── Applying the effect ────────────────────────────────────────────────

    /// <summary>Called from RunSessionData.ApplyUpgrade(). tier = 1, 2, or 3.</summary>
    public void ApplyEffect(int tier, RunSessionData session)
    {
        // Determine the value for the current tier
        float val = tier == 1 ? valueT1 : tier == 2 ? valueT2 : valueT3;

        switch (effectType)
        {
            // ── Helicopter ──────────────────────────────────────────────
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

            // ── Sniper ──────────────────────────────────────────────
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

            // ── Bomb ────────────────────────────────────────────────
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

            // ── Barricade ────────────────────────────────────────────
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

            // ── General ────────────────────────────────────────────────
            case UpgradeEffectType.General_XPMult:
                session.AddModifier("general_xp_mult", val);
                break;
            case UpgradeEffectType.General_NoPanic:
                session.SetFlag("general_no_panic");
                break;
        }
    }

    // ── UI helpers ────────────────────────────────────────────────────

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

## STEP 3: Create `Assets/Scripts/Roguelite/UpgradeManager.cs`

Builds the pool, picks 3 options, hands the choice off to RunSessionData.

```csharp
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class UpgradeManager : MonoBehaviour
{
    public static UpgradeManager Instance { get; private set; }

    [Header("All upgrades in the game (fill in via inspector or Resources.LoadAll)")]
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
    /// Excludes fully-maxed ones (stack = 3).
    /// </summary>
    public List<(RunUpgradeDefinition upgrade, int nextTier)> GetUpgradeOptions(
        List<CardData> currentDeck,
        CardManager.CardType heroType)
    {
        var session = RunSessionData.Instance;
        var pool = new List<(RunUpgradeDefinition, int nextTier)>();

        // Collect the card types present in the deck
        var deckCardTypes = new HashSet<CardManager.CardType>(
            currentDeck.Select(c => c.cardType)
        );
        deckCardTypes.Add(heroType);
        deckCardTypes.Add(CardManager.CardType.None); // General upgrades

        foreach (var upg in allUpgrades)
        {
            // Only for cards that are in the deck, plus general ones
            if (!deckCardTypes.Contains(upg.targetCardType)) continue;

            int currentStack = session.GetStack(upg.upgradeId);
            if (currentStack >= 3) continue; // max reached

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

    /// <summary>Apply the chosen upgrade.</summary>
    public void ApplyUpgrade(RunUpgradeDefinition upgrade)
    {
        RunSessionData.Instance.ApplyUpgrade(upgrade);
    }
}
```

---

## STEP 4: Create `Assets/Scripts/Roguelite/XPManager.cs`

A centralized XP-awarding class. Reads the `general_xp_mult` modifier.

```csharp
using UnityEngine;

public class XPManager : MonoBehaviour
{
    public static XPManager Instance { get; private set; }

    [Header("XP per event")]
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

## STEP 5: Changes to GameManager.cs

Find the `AddRescuedFromTransport` method and add a call to XPManager.
Find or create a call site at zombie death.

```csharp
// In AddRescuedFromTransport — ADD at the end, before/after the existing score award:
public void AddRescuedFromTransport(int humanCount, int scientistCount, Vector3 pos)
{
    // ... existing score-awarding code ...

    // NEW: award XP
    bool wasFull = (humanCount + scientistCount) >= /* transport's maxCapacity */;
    XPManager.Instance?.OnCivilianEvacuated(humanCount, scientistCount, wasFull);
}

// In AddRescuedHumans (used by CombatHelicopter) — ADD:
public void AddRescuedHumans(int count, Vector3 pos)
{
    // ... existing code ...
    XPManager.Instance?.OnCivilianEvacuated(count, 0);
}
```

Also find the zombie-death method in `Zombie.cs` (most likely `Die()` or `TakeDamage` when HP <= 0) and add:
```csharp
XPManager.Instance?.OnZombieKilled(false); // true for a boss
```

---

## STEP 6: Changes to HelicopterController.cs

In `Start()`, AFTER the existing CardData-reading code, add:

```csharp
// ── Run modifiers (roguelite) ──────────────────────────────────────
var run = RunSessionData.Instance;
if (run != null)
{
    // Numeric modifiers
    maxCapacity += (int)run.GetModifier("heli_capacity_add");
    verticalSpeed *= (1f + run.GetModifier("heli_speed_mult"));
    attractRadius *= (1f + run.GetModifier("heli_radius_mult"));
    loadTime += run.GetModifier("heli_loadtime_add");
    boardingCooldown *= Mathf.Max(0.05f, 1f - run.GetModifier("heli_boarding_reduction"));

    // Ultimate flags
    if (run.HasFlag("heli_no_panic"))
        panicRadius = 0f;

    if (run.HasFlag("heli_instant_land"))
        verticalSpeed = 9999f;

    if (run.HasFlag("heli_unlimited_capacity"))
        maxCapacity = 999;
}
// ── end of run modifiers ───────────────────────────────────────────
```

The `heli_global_attract` flag (Megaphone Ultimate — every civilian runs to the helicopter):
In the `StartLoading()` method, after the existing loop over Human.AllHumans, add:

```csharp
// Ultimate: City-Wide Alert — attract EVERY civilian regardless of radius
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

## STEP 7: Changes to Sniper.cs

In `Start()`, AFTER the existing CardData-reading block:

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

The `sniper_instakill` flag — in the `ApplyPiercingDamage` method:
```csharp
// Before z.TakeDamage(damage):
int actualDamage = (RunSessionData.Instance != null && RunSessionData.Instance.HasFlag("sniper_instakill"))
    ? 99999
    : Mathf.RoundToInt(currentDamage);
z.TakeDamage(actualDamage);
```

The `sniper_triple_target` flag — in `SniperRoutine()`, instead of one target, look for 3 and fire on each (only if the flag is set). Keep the implementation simple: if the flag is set, find the top 3 nearest zombies and call `ApplyPiercingDamage` for each in a loop.

---

## STEP 8: Changes to Bomb.cs

In `Start()`, AFTER the existing block:

```csharp
var run = RunSessionData.Instance;
if (run != null)
{
    damageRadius *= (1f + run.GetModifier("bomb_radius_mult"));
    damage = Mathf.RoundToInt(damage * (1f + run.GetModifier("bomb_damage_mult")));

    if (run.HasFlag("bomb_mega_radius"))
        damageRadius = 150f; // covers half the map
}
```

In the `Explode()` method, AFTER the main damage pass against zombies:

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
                z.Stun(5f); // the Stun method needs implementing in Zombie.cs if it doesn't exist
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
            // Deal 150 damage within a damageRadius * 0.3f radius at miniPos
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

## STEP 9: Changes to Barricade.cs

In `Start()`, AFTER the CardData-reading block:

```csharp
var run = RunSessionData.Instance;
if (run != null)
{
    float hpMult = 1f + run.GetModifier("barricade_hp_mult");
    maxHealth = Mathf.RoundToInt(maxHealth * hpMult);

    if (run.HasFlag("barricade_indestructible"))
        maxHealth = 999999;

    // NavMeshObstacle scale
    float widthMult = run.GetModifier("barricade_width_mult");
    if (widthMult > 0f)
    {
        transform.localScale *= (1f + widthMult);
    }
}
currentHealth = maxHealth;
```

In `TakeDamage(int damage)`, before `currentHealth -= damage`:

```csharp
// Reflect damage
var run = RunSessionData.Instance;
if (run != null)
{
    float reflectPct = run.GetModifier("barricade_reflect_pct");
    if (reflectPct > 0f)
    {
        // Find the nearest attacking zombie and deal it damage
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

    // Death zone: kill every zombie within 1.5m immediately on attack
    if (run.HasFlag("barricade_death_zone"))
    {
        Collider[] deathZone = Physics.OverlapSphere(transform.position, 1.5f);
        foreach (var c in deathZone)
            c?.GetComponent<Zombie>()?.TakeDamage(99999);
    }
}
```

The `barricade_stun_dur` flag — find where a zombie attacks a barricade (in `Zombie.cs`, the `InfectTarget` method or similar) and add a stun after the hit:
```csharp
// If the zombie hit a barricade and barricade_stun_dur > 0:
float stunDur = RunSessionData.Instance?.GetModifier("barricade_stun_dur") ?? 0f;
if (stunDur > 0f) zombie.Stun(stunDur);
```

---

## STEP 10: Create the UI — `Assets/Scripts/UI/LevelUpScreen.cs`

```csharp
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// The upgrade-choice screen shown on Level Up.
/// Subscribes to RunSessionData.OnLevelUp.
/// Pauses via Time.timeScale = 0 while open.
/// </summary>
public class LevelUpScreen : MonoBehaviour
{
    [Header("UI References")]
    public GameObject screenRoot;         // the screen's root object (toggled on/off)
    public TextMeshProUGUI levelUpTitle;  // "LEVEL UP!" or "Level 3"
    public UpgradeCardUI[] upgradeCards; // exactly 3 cards

    [Header("Settings")]
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
            // No upgrades available — skip
            return;
        }

        Time.timeScale = 0f;
        screenRoot.SetActive(true);

        if (levelUpTitle != null)
            levelUpTitle.text = $"LEVEL {RunSessionData.Instance.CurrentRunLevel}";

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

## STEP 11: Create `Assets/Scripts/UI/UpgradeCardUI.cs`

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
    public Image tierGlow;                   // highlight for Ultimate
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

## STEP 12: Create `Assets/Scripts/UI/XPBarUI.cs`

```csharp
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class XPBarUI : MonoBehaviour
{
    [Header("UI References")]
    public Slider xpSlider;
    public TextMeshProUGUI levelText;    // "Lvl 3"
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
            levelText.text = $"Lvl {session.CurrentRunLevel}";

        if (xpText != null)
            xpText.text = $"{current} / {max}";
    }
}
```

---

## STEP 13: Create ScriptableObjects for the MVP upgrades

Create the folder `Assets/Resources/Upgrades/`.
Create the following ScriptableObjects via the `ZombieGame/Run Upgrade` menu:

### HELICOPTER

**`upgrade_heli_speed.asset`**
- upgradeId: `helicopter_quicklanding`
- targetCardType: `Helicopter`
- effectType: `Helicopter_SpeedMult`
- displayName: "Quick Landing"
- displayNameEnhanced: "Afterburner"
- displayNameUltimate: "VTOL Protocol"
- descriptionTier1: "+35% descent speed"
- descriptionTier2: "Speed x2.5, no zombie panic for the first 3 sec. after landing"
- descriptionTier3: "The helicopter teleports to the target instantly"
- valueT1: 0.35 | valueT2: 1.5 | valueT3: 9999
- (T3 uses a flag — change effectType to `Helicopter_InstantLand` for T3, or handle it in ApplyEffect based on the tier value)

> **Important:** for upgrades where T1/T2 are a numeric bonus and T3 is a flag — use a separate `case` on tier inside `ApplyEffect`, or create two separate assets and merge them via override logic. Recommended: in `ApplyEffect`, check `if (tier == 3)` and set a flag instead of a numeric modifier.

**`upgrade_heli_capacity.asset`**
- upgradeId: `helicopter_capacity`
- effectType: `Helicopter_CapacityAdd`
- displayName: "Extra Seats" | Enhanced: "Heavy Transport" | Ultimate: "Mass Evacuation"
- descriptionTier1: "+2 seats" | T2: "+6 seats total" | T3: "Capacity — every civilian in the zone"
- valueT1: 2 | valueT2: 4 | (T3 → flag `heli_unlimited_capacity`)

**`upgrade_heli_attract.asset`**
- upgradeId: `helicopter_megaphone`
- effectType: `Helicopter_RadiusMult`
- displayName: "Megaphone" | Enhanced: "Emergency Broadcast" | Ultimate: "City-Wide Alert"
- descriptionTier1: "+40% attract radius" | T2: "Radius x2, civilians run" | T3: "EVERY civilian on the map heads to the helicopter"
- valueT1: 0.40 | valueT2: 1.0 | (T3 → flag `heli_global_attract`)

### SNIPER

**`upgrade_sniper_range.asset`**
- upgradeId: `sniper_range`
- effectType: `Sniper_RangeMult`
- displayName: "Extended Barrel" | Enhanced: "Long-Range Platform" | Ultimate: "All-Seeing Eye"
- valueT1: 0.35 | valueT2: 0.65 | (T3 → `sniper_global_range`)

**`upgrade_sniper_damage.asset`**
- upgradeId: `sniper_damage`
- effectType: `Sniper_DamageMult`
- displayName: "Armor-Piercing Rounds" | Enhanced: "Executioner" | Ultimate: "Storm of Lead"
- valueT1: 0.5 | valueT2: 1.0 | (T3 → `sniper_instakill`)

**`upgrade_sniper_duration.asset`**
- upgradeId: `sniper_duration`
- effectType: `Sniper_DurationAdd`
- displayName: "Long Watch" | Enhanced: "Overwatch" | Ultimate: "Eternal Guardian"
- valueT1: 8 | valueT2: 9999 (flag `sniper_permanent`) | T3: flag `sniper_permanent` + respawn

### BOMB

**`upgrade_bomb_radius.asset`**
- upgradeId: `bomb_radius`
- effectType: `Bomb_RadiusMult`
- displayName: "Bigger Warhead" | Enhanced: "Tactical Core" | Ultimate: "Thermobaric"
- valueT1: 0.4 | valueT2: 2.0 | (T3 → `bomb_mega_radius`)

**`upgrade_bomb_damage.asset`**
- upgradeId: `bomb_damage`
- effectType: `Bomb_DamageMult`
- displayName: "Shockwave" | Enhanced: "Overkill" | Ultimate: "EMP Blast"
- valueT1: 1.0 | valueT2: 9.0 | (T3 → `bomb_stun`)

**`upgrade_bomb_cluster.asset`**
- upgradeId: `bomb_cluster`
- effectType: `Bomb_ClusterCount`
- displayName: "Cluster Payload" | Enhanced: "Carpet Bombing" | Ultimate: "Swarm Strike"
- valueT1: 3 | valueT2: 5 | valueT3: 12

### BARRICADE

**`upgrade_barricade_hp.asset`**
- upgradeId: `barricade_hp`
- effectType: `Barricade_HPMult`
- displayName: "Reinforced Concrete" | Enhanced: "Fortress Wall" | Ultimate: "Unbreakable Bunker"
- valueT1: 0.5 | valueT2: 3.0 | (T3 → `barricade_indestructible`)

**`upgrade_barricade_spike.asset`**
- upgradeId: `barricade_spike`
- effectType: `Barricade_ReflectDamage`
- displayName: "Barbed Wire" | Enhanced: "Razor Wire" | Ultimate: "Death Zone"
- valueT1: 0.25 | valueT2: 0.75 | (T3 → `barricade_death_zone`)

**`upgrade_barricade_count.asset`**
- upgradeId: `barricade_count`
- effectType: `Barricade_CountAdd`
- displayName: "Rapid Deployment" | Enhanced: "Minefield" | Ultimate: "Perimeter"
- valueT1: 1 | valueT2: 1 | (T3 → auto-ring-of-6-barricades logic — implement separately)

### GENERAL (2 for the MVP)

**`upgrade_general_xp.asset`**
- upgradeId: `general_xp`
- targetCardType: `None`
- effectType: `General_XPMult`
- displayName: "Accelerated Learning"
- descriptionTier1: "+25% XP gained"
- descriptionTier2: "+60% XP gained"
- descriptionTier3: "+100% XP gained"
- valueT1: 0.25 | valueT2: 0.35 | valueT3: 0.40

**`upgrade_general_nopanic.asset`**
- upgradeId: `general_nopanic`
- targetCardType: `None`
- effectType: `General_NoPanic`
- displayName: "Veteran Instinct"
- descriptionTier1-3: "The hero doesn't leave due to zombie-triggered panic"
- valueT1/T2/T3: 0 (flag only)

---

## STEP 14: Initialization and scene setup

### GameManager or LevelManager — add at the start of a level:

```csharp
// Reset the session at the start of every level
private void Start()
{
    RunSessionData.Instance?.ResetForNewLevel();
    // ... the rest of initialization ...
}
```

### Add these objects to the level scene:

1. **RunSessionData** — an empty GameObject with the `RunSessionData` component
2. **XPManager** — an empty GameObject with the `XPManager` component
3. **UpgradeManager** — an empty GameObject with the `UpgradeManager` component (the upgrade list — via Resources/Upgrades/ or manually)
4. **LevelUpScreen** — a Canvas object with the `LevelUpScreen` component, with 3 child `UpgradeCardUI`
5. **XPBarUI** — a HUD element with the `XPBarUI` component, Slider + TextMeshPro

### Awake/Start ordering shouldn't cause problems — everything goes through `Instance?.` (null-safe).

---

## CONSTRAINTS AND RULES

1. **DO NOT change** `CardData.cs`, `CardManager.cs`, `PlayerProfile.cs` — read-only.
2. **DO NOT change** the existing score-awarding logic in `GameManager` — only add XPManager calls.
3. **DO NOT use** `DontDestroyOnLoad` for `RunSessionData` — it only lives for the level's scene.
4. Every call to a singleton goes through null-safe `?.` — scenes without roguelite must work without errors.
5. **Do not add** visual effects for Ultimate in the MVP — functionality only.
6. The `Zombie` class — if there's no `Stun(float duration)` method, implement a minimal version: set an `isStunned` flag, in Update() don't move/attack while the flag is set, and clear the flag after `duration` seconds.

---

## FINAL CHECKLIST

- [ ] `RunSessionData.cs` created and works as a singleton
- [ ] `RunUpgradeDefinition.cs` created, a ScriptableObject
- [ ] `UpgradeManager.cs` created, builds a pool of 3 options
- [ ] `XPManager.cs` created, called from GameManager
- [ ] `GameManager.cs` — XPManager calls added
- [ ] `Zombie.cs` — `XPManager.OnZombieKilled()` call added on death
- [ ] `HelicopterController.cs` — run-modifier reading added
- [ ] `Sniper.cs` — run-modifier reading added
- [ ] `Bomb.cs` — run-modifier reading added + cluster + stun
- [ ] `Barricade.cs` — run-modifier reading added + reflect damage
- [ ] `LevelUpScreen.cs` + `UpgradeCardUI.cs` created
- [ ] `XPBarUI.cs` created
- [ ] ScriptableObjects created in `Assets/Resources/Upgrades/` (at least 11 of them)
- [ ] `RunSessionData.ResetForNewLevel()` called at the start of a level
- [ ] Everything works without errors if RunSessionData == null (old scenes)

---
