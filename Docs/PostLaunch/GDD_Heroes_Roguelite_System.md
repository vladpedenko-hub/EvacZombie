# EvacZombie — Hero System & Roguelite In-Run Progression
**GDD v1.0 — MVP Design Document**

> This document describes two related systems:
> 1. **Hero system** — evacuation cards become characters with a unique identity
> 2. **Roguelite progression** — XP from kills and evacuations → level up → choose 1 of 3 upgrades

---

## 1. HERO SYSTEM

### 1.1 Concept

Three evacuation cards (`Helicopter`, `Car`, `CombatHelicopter`) become **heroes**. A hero is the player's persistent character for the run. Regular ability cards (Sniper, Soldier, Bomb, Barricade, Bait) form the deck tied to that hero.

### 1.2 Hero list

| Hero | CardType | Starting | Unlock condition |
|---|---|---|---|
| 🚁 Helicopter "Phoenix" | `Helicopter` | ✅ Yes | — |
| 🚗 Armored Truck "Locus" | `Car` | ❌ | Clear 5 levels |
| 🚁🔫 Combat Helicopter "Ghost" | `CombatHelicopter` | ❌ | Clear 15 levels |

### 1.3 Hero mechanics in a run

- The player enters a level with **1 active hero** and **their own deck** (3–5 ability cards).
- The hero is the only evacuation card. No other evacuation card can be in the deck.
- Ability cards are used the same way as now (placed on the field).
- The hero is activated via a UI button (a separate zone) — not part of the regular deck.

### 1.4 Data structure (C#)

```csharp
// New ScriptableObject for a hero
[CreateAssetMenu(fileName = "NewHeroData", menuName = "ZombieGame/Hero Data")]
public class HeroData : ScriptableObject
{
    public string heroName;
    public string heroDescription;
    [TextArea] public string loreText;
    public Sprite heroIcon;
    public CardType heroCardType; // Helicopter / Car / CombatHelicopter
    public CardData linkedCardData; // Reference to the existing CardData

    // Unlock condition
    public int levelsRequiredToUnlock; // 0 = starting hero
}

// Add to PlayerProfile:
public string selectedHeroId;         // id of the currently active hero
public List<string> unlockedHeroIds;  // list of unlocked heroes
```

---

## 2. ROGUELITE PROGRESSION (WITHIN A SINGLE LEVEL)

### 2.1 XP sources

| Event | XP |
|---|---|
| Killing a regular zombie | +5 |
| Killing a ZombieBoss | +50 |
| Evacuating a regular civilian | +20 |
| Evacuating a Scientist | +40 |
| Full evacuation-transport load | +25 (bonus) |

### 2.2 XP level scale (within a single run)

```
Level 1 → 2:   100 XP
Level 2 → 3:   150 XP
Level 3 → 4:   200 XP
Level 4 → 5:   250 XP
Level 5 → 6:   300 XP
Level 6 → 7:   375 XP
Level 7 → 8:   450 XP
Level 8 → 9:   550 XP
Level 9 → 10:  700 XP
Level 10+:     +200 XP each subsequent level
```

Target pacing: **8–12 level-ups** per average level.

### 2.3 Level Up logic

1. When XP reaches the threshold — the game **pauses** (Time.timeScale = 0).
2. A UI appears with **3 upgrade options** to choose from.
3. The player picks 1 → the effect applies immediately → the game resumes.

### 2.4 Building the upgrade pool

The pool is built using these rules:
1. For each card in the player's deck — add **all non-maxed upgrades** for that card to the pool.
2. For the hero — add **all non-maxed hero upgrades** to the pool.
3. Add **all general upgrades** that are applicable.
4. From the pool, randomly pick **3 unique options** (no duplicates in a single offer).
5. If there are several upgrades of the same type (due to stacking) — show the same card, but at the increased tier.

**Weighting system (MVP):**
- Upgrades for cards the player has never picked yet: weight 1.5x (encourages variety)
- Upgrades taken once (tier 2 available): weight 1.2x
- General upgrades: weight 1.0x

---

## 3. STACKING AND ULTIMATE UPGRADE SYSTEM

### 3.1 Principle

Each upgrade has 3 tiers:
- **Tier 1** (first pick): base upgrade.
- **Tier 2** (second pick of the same card): **Enhanced** — a significant boost that changes behavior.
- **Tier 3 / Ultimate** (third pick): **ULTIMATE** — a fundamental change to the mechanic.

After picking tier 3, the upgrade is removed from the pool (maxed out).

### 3.2 Data structure

```csharp
public enum UpgradeTier { Tier1, Tier2_Enhanced, Tier3_Ultimate }

[System.Serializable]
public class RunUpgradeDefinition
{
    public string upgradeId;           // unique ID, e.g. "helicopter_quicklanding"
    public CardType targetCardType;    // which card this belongs to (or None for General)
    public string displayName;         // "Quick Landing"
    public string description;         // tier 1 description
    public string descriptionEnhanced; // tier 2 description
    public string descriptionUltimate; // tier 3 description
    public Sprite icon;
}

// Stored in RunSessionData (reset after each level)
[System.Serializable]
public class RunSessionData
{
    public Dictionary<string, int> upgradeStacks; // upgradeId → number of times taken (0-3)
    public int currentXP;
    public int currentRunLevel;
    // Applied bonuses are stored as multipliers/additives:
    public Dictionary<string, float> runModifiers; // "helicopter_capacity" → 8f
}
```

### 3.3 Applying effects

Effects are applied **at the moment a card is placed** (via RunSessionData.runModifiers). Each controller (HelicopterController, Sniper, etc.) queries not only `PlayerProfile` but also `RunSessionData` in Start():

```csharp
// Example for HelicopterController
float capacityMod = RunSession.GetModifier("helicopter_capacity"); // 0 if not taken
maxCapacity = (int)(baseCapacity + capacityMod);
```

---

## 4. UPGRADE TREES

### Notation used in the tables
- **[Parameter]** — a specific StatType from CardData (Capacity, Speed, Damage, FireRate, Radius, Duration, Cooldown, Count, Health)
- ✨ Enhanced — tier 2 (taken twice)
- 💥 ULTIMATE — tier 3 (taken three times)

---

## 4.1 HERO: 🚁 HELICOPTER "PHOENIX"
*Parameters: Capacity, Speed (vertical speed), Radius (attractRadius), Duration (loadTime), Cooldown (boardingCooldown)*

### Upgrade A: Quick Landing
- **Tier 1** `quicklanding`: `verticalSpeed` +35% → the helicopter reaches the landing point faster
- ✨ **Tier 2** `quicklanding x2` — **"Afterburner"**: `verticalSpeed` x2.5 + for the first 3 seconds after landing, zombies don't trigger panic
- 💥 **ULTIMATE** `quicklanding x3` — **"VTOL Protocol"**: the helicopter teleports to the point instantly (no flight animation), `verticalSpeed` x10

### Upgrade B: Megaphone
- **Tier 1** `megaphone`: `attractRadius` +40% → attracts civilians from a greater distance
- ✨ **Tier 2** `megaphone x2` — **"Emergency Broadcast"**: `attractRadius` x2 + all civilians in the zone start running (speed x2)
- 💥 **ULTIMATE** `megaphone x3` — **"City-Wide Alert"**: EVERY civilian on the map starts moving toward the helicopter regardless of distance

### Upgrade C: Extra Seats
- **Tier 1** `extraseats`: `maxCapacity` +2 → carries more passengers per trip
- ✨ **Tier 2** `extraseats x2` — **"Heavy Transport"**: `maxCapacity` +6 total, scientists board with priority
- 💥 **ULTIMATE** `extraseats x3` — **"Mass Evacuation"**: `maxCapacity` becomes unlimited (picks up everyone who makes it in time for loadTime)

### Upgrade D: Fast Boarding
- **Tier 1** `fastboarding`: `boardingCooldown` -30% → the next civilian boards faster
- ✨ **Tier 2** `fastboarding x2` — **"Express"**: `boardingCooldown` -60%, 2 civilians per tick instead of 1
- 💥 **ULTIMATE** `fastboarding x3` — **"Magnet"**: every civilian within `boardingRadius` teleports into the helicopter instantly

### Upgrade E: Extended Dwell Time
- **Tier 1** `longstay`: `loadTime` +6s → the helicopter waits longer at the landing site
- ✨ **Tier 2** `longstay x2` — **"Fortified LZ"**: `panicRadius` halved (zombies must get right up close to trigger panic)
- 💥 **ULTIMATE** `longstay x3` — **"Unshakeable"**: the helicopter becomes completely immune to panic — it never leaves because of zombies

---

## 4.2 HERO: 🚗 ARMORED TRUCK "LOCUS"
*Parameters: Capacity, Duration (loadTime), Radius (sirenRadius), Cooldown (boardingCooldown), Speed (moveSpeed)*

### Upgrade A: Turbo Engine
- **Tier 1** `turboengine`: `moveSpeed` +35%
- ✨ **Tier 2** `turboengine x2` — **"Rally Driver"**: `moveSpeed` x2, the truck makes a second trip after leaving (drives back in again)
- 💥 **ULTIMATE** `turboengine x3` — **"Speedrun"**: the truck completes its entire route in 40% of the normal time and ignores `dangerRadius`

### Upgrade B: Bigger Trunk
- **Tier 1** `biggertrunk`: `maxCapacity` +3
- ✨ **Tier 2** `biggertrunk x2` — **"Bus"**: `maxCapacity` becomes 12
- 💥 **ULTIMATE** `biggertrunk x3` — **"Convoy"**: on activation, a second Armored Truck automatically spawns heading to the same point

### Upgrade C: Loud Siren
- **Tier 1** `loudsiren`: `sirenRadius` +50%
- ✨ **Tier 2** `loudsiren x2` — **"Panic"**: civilians in the zone start running to the truck (speed x2)
- 💥 **ULTIMATE** `loudsiren x3` — **"Regional Alert"**: EVERY civilian on the map starts moving toward the nearest exit

### Upgrade D: Ram Bar
- **Tier 1** `crashbar`: `crushRadius` +50% → the truck crushes zombies in a wider radius while moving
- ✨ **Tier 2** `crashbar x2` — **"Berserk"**: the truck crushes zombies even while standing still (while loading)
- 💥 **ULTIMATE** `crashbar x3` — **"Tank"**: the truck gets an HP bar (200 HP), zombies attack the truck instead of blocking loading; the truck doesn't leave as long as it's alive

### Upgrade E: Fast Doors
- **Tier 1** `fastdoors`: `boardingCooldown` -35%
- ✨ **Tier 2** `fastdoors x2` — **"Open Bed"**: the boarding animation is skipped entirely
- 💥 **ULTIMATE** `fastdoors x3` — **"On the Move"**: civilians jump into the truck while it's still driving (loading starts while it's still en route to the point)

---

## 4.3 HERO: 🚁🔫 COMBAT HELICOPTER "GHOST"
*Parameters: Capacity, Speed (flySpeed), Duration (loadTime), Radius (shootRadius), FireRate, Damage*

### Upgrade A: Heavy Armor
- **Tier 1** `heavyarmor`: `loadTime` +4s → stays in the zone longer
- ✨ **Tier 2** `heavyarmor x2` — **"Unbreakable"**: stays in the zone until it reaches full capacity or picks up every remaining civilian
- 💥 **ULTIMATE** `heavyarmor x3` — **"Eternal"**: the helicopter becomes a permanent combat unit for the rest of the level

### Upgrade B: Marksman AI
- **Tier 1** `marksman`: `damage` x1.5
- ✨ **Tier 2** `marksman x2` — **"One-Shot Protocol"**: damage x3, kills any regular zombie in 1 shot
- 💥 **ULTIMATE** `marksman x3` — **"Railgun"**: shots pierce through up to 10 zombies (piercing along the line of fire)

### Upgrade C: Rate of Fire
- **Tier 1** `rapidfire`: `fireRate` improved by 40% (interval between shots reduced)
- ✨ **Tier 2** `rapidfire x2` — **"Minigun"**: `fireRate` = 0.08s, the visual tracer becomes continuous
- 💥 **ULTIMATE** `rapidfire x3` — **"Firestorm"**: every shot becomes a mini-explosion (AoE damage), `fireRate` = 0.2s

### Upgrade D: Wide Coverage
- **Tier 1** `widenet`: `shootRadius` +40%
- ✨ **Tier 2** `widenet x2` — **"Exclusion Zone"**: `shootRadius` covers half the map
- 💥 **ULTIMATE** `widenet x3` — **"Carpet Fire"**: fires on EVERY zombie in the zone simultaneously each tick

### Upgrade E: Overload
- **Tier 1** `overloaded`: `maxCapacity` +5
- ✨ **Tier 2** `overloaded x2` — **"Transporter"**: `maxCapacity` = 25
- 💥 **ULTIMATE** `overloaded x3` — **"Carrier"**: every civilian on the map teleports aboard on landing

---

## 4.4 CARD: 🎯 SNIPER
*Parameters (from CardData): Cooldown, Radius (attackRange), Damage, Duration (lifespan)*

### Upgrade A: Extended Barrel
- **Tier 1** `sniper_range`: `attackRange` +35%
- ✨ **Tier 2**: **"Long-Range Platform"** — range effectively covers the whole map
- 💥 **ULTIMATE**: **"All-Seeing Eye"** — the sniper sees and hits ANY zombie on the map regardless of position

### Upgrade B: Armor-Piercing Rounds
- **Tier 1** `sniper_damage`: `damage` x1.5
- ✨ **Tier 2**: **"Executioner"** — instakills any non-boss zombie
- 💥 **ULTIMATE**: **"Anti-Tank Rifle"** — now also damages ZombieBoss (30 HP per shot)

### Upgrade C: Quick Reload
- **Tier 1** `sniper_cooldown`: `cooldownDelay` -35%
- ✨ **Tier 2**: **"Suppressing Fire"** — `cooldownDelay` = 0.3s, near-continuous fire
- 💥 **ULTIMATE**: **"Storm of Lead"** — tracks 3 different targets at once, firing on all of them simultaneously

### Upgrade D: Long Watch
- **Tier 1** `sniper_duration`: `lifespan` +8s
- ✨ **Tier 2**: **"Overwatch"** — `lifespan` = the whole level (a permanent unit)
- 💥 **ULTIMATE**: **"Eternal Guardian"** — a permanent unit + respawns 10s after being destroyed

### Upgrade E: Increased Pressure
- **Tier 1** `sniper_pierce`: `maxPierceTargets` +2 → pierces more zombies with a single shot
- ✨ **Tier 2**: **"Infinite Piercing"** — pierces EVERY zombie along the line of fire
- 💥 **ULTIMATE**: **"Chainsaw Round"** — the shot ricochets between zombies up to 6 times after piercing

---

## 4.5 CARD: 💣 BOMB
*Parameters: Radius (damageRadius), Damage*

### Upgrade A: Bigger Warhead
- **Tier 1** `bomb_radius`: `damageRadius` +40%
- ✨ **Tier 2**: **"Tactical Core"** — `damageRadius` x3
- 💥 **ULTIMATE**: **"Thermobaric"** — instantly kills EVERY zombie across half the map

### Upgrade B: Shockwave
- **Tier 1** `bomb_damage`: `damage` x2
- ✨ **Tier 2**: **"Overkill"** — guaranteed kill on any zombie (any HP)
- 💥 **ULTIMATE**: **"EMP Blast"** — surviving zombies in the radius are paralyzed for 5 seconds

### Upgrade C: Cluster Payload
- **Tier 1** `bomb_cluster`: on explosion, 3 mini-bombs spawn at random points within the radius
- ✨ **Tier 2**: **"Carpet Bombing"** — 8 mini-bombs, covering the whole zone
- 💥 **ULTIMATE**: **"Swarm Strike"** — 20 mini-bombs, full map coverage

### Upgrade D: Bunker Buster
- **Tier 1** `bomb_buildings`: the explosion now destroys buildings in the zone
- ✨ **Tier 2**: **"Scorched Earth"** — destroys all buildings + fences + barricades in the zone
- 💥 **ULTIMATE**: **"Flatten"** — everything in the zone is destroyed, the NavMesh temporarily rebuilds

### Upgrade E: Split Warheads
- **Tier 1** `bomb_mirv`: on activation, 2 bombs drop instead of 1 (slightly offset)
- ✨ **Tier 2**: **"Volley"** — 5 bombs in a wide spread around the point
- 💥 **ULTIMATE**: **"Orbital Strike"** — a laser beam from orbit sweeps a line across the entire map

---

## 4.6 CARD: 🧱 BARRICADE
*Parameters: Health*

### Upgrade A: Reinforced Concrete
- **Tier 1** `barricade_hp`: `maxHealth` x1.5
- ✨ **Tier 2**: **"Fortress Wall"** — `maxHealth` x4 (withstands a horde's attacks)
- 💥 **ULTIMATE**: **"Unbreakable Bunker"** — the barricade cannot be destroyed by zombies (lasts until the end of the level)

### Upgrade B: Barbed Wire
- **Tier 1** `barricade_spike`: attacking zombies take reflected damage (25% reflection)
- ✨ **Tier 2**: **"Razor Wire"** — 100% damage reflection + a small AoE around the barricade
- 💥 **ULTIMATE**: **"Death Zone"** — any zombie entering within 1.5m of the barricade dies instantly

### Upgrade C: Electrification
- **Tier 1** `barricade_stun`: attacking zombies are stunned for 1.5s
- ✨ **Tier 2**: **"Electric Grid"** — the stun zone extends 2m around the barricade
- 💥 **ULTIMATE**: **"Arc Reactor"** — a continuous electric pulse kills weak zombies nearby and stuns strong ones

### Upgrade D: Wide Span
- **Tier 1** `barricade_width`: NavMeshObstacle scale +50% (blocks more of the path)
- ✨ **Tier 2**: **"Great Wall"** — the barricade blocks the road edge-to-edge
- 💥 **ULTIMATE**: **"Iron Curtain"** — creates a full wall across the entire street (every NavMesh path is blocked)

### Upgrade E: Rapid Deployment
- **Tier 1** `barricade_count`: 2 barricades instead of 1 per activation
- ✨ **Tier 2**: **"Minefield"** — 3 barricades in a configurable pattern
- 💥 **ULTIMATE**: **"Perimeter"** — automatically generates a ring of 6 barricades around the chosen point

---

## 4.7 CARD: 🪤 BAIT
*Parameters: Radius (attractRadius), Duration (lifeTime)*

### Upgrade A: Boosted Signal
- **Tier 1** `bait_radius`: `attractRadius` +50%
- ✨ **Tier 2**: **"Irresistible"** — EVERY zombie on the map is attracted to the bait
- 💥 **ULTIMATE**: **"World Signal"** — every zombie instantly teleports to the bait's point when it's placed

### Upgrade B: Extended Charge
- **Tier 1** `bait_duration`: `lifeTime` +6s
- ✨ **Tier 2**: **"Permanent Decoy"** — `lifeTime` = 60s (the whole level)
- 💥 **ULTIMATE**: **"Eternal Trap"** — becomes a permanent attraction point for the rest of the level, refreshing with every wave

### Upgrade C: Lethal Trap
- **Tier 1** `bait_lethal`: on expiry — an explosion within `attractRadius` (damage = 150)
- ✨ **Tier 2**: **"Grenade Crater"** — explosion damage = a full bomb (1000+)
- 💥 **ULTIMATE**: **"Tactical Core Bait"** — kills EVERY zombie within double the attractRadius

### Upgrade D: Mass Hysteria
- **Tier 1** `bait_priority`: zombies within the radius ignore civilians and focus ONLY on the bait
- ✨ **Tier 2**: **"Zombie Magnet"** — zombies run to the bait at x2 speed
- 💥 **ULTIMATE**: **"Mass Grave"** — 5 baits spawn simultaneously at random points

### Upgrade E: Delayed Fuse
- **Tier 1** `bait_mine`: the bait explodes when a zombie enters its radius (proximity mine)
- ✨ **Tier 2**: **"Directional Blast"** — the explosion is aimed toward the zombie cluster
- 💥 **ULTIMATE**: **"Nuclear Pit"** — the explosion covers 50% of the map's area

---

## 4.8 CARD: 🪖 SOLDIER
*Parameters: FireRate, Radius (attackRange), Damage, Duration (lifespan)*

### Upgrade A: Combat Training
- **Tier 1** `soldier_damage`: `damage` x1.5
- ✨ **Tier 2**: **"Elite Operative"** — `damage` x3
- 💥 **ULTIMATE**: **"One-Man Army"** — kills a ZombieBoss in 5 hits

### Upgrade B: Extended Deployment
- **Tier 1** `soldier_duration`: `lifespan` +6s
- ✨ **Tier 2**: **"Mercenary"** — `lifespan` = the whole level (a permanent unit)
- 💥 **ULTIMATE**: **"Immortal"** — respawns 10s after dying (until the end of the level)

### Upgrade C: Suppressing Fire
- **Tier 1** `soldier_firerate`: `fireRate` x1.5 (interval decreases)
- ✨ **Tier 2**: **"Machine Gun"** — `fireRate` = 0.08s, continuous fire
- 💥 **ULTIMATE**: **"Gunner"** — fires on every zombie in the zone simultaneously

### Upgrade D: Eagle Eye
- **Tier 1** `soldier_range`: `attackRange` +35%
- ✨ **Tier 2**: **"Long-Range Shooter"** — range covers half the map
- 💥 **ULTIMATE**: **"Global Coverage"** — the soldier attacks any zombie on the map

### Upgrade E: Squad Tactics
- **Tier 1** `soldier_count`: 2 soldiers instead of 1
- ✨ **Tier 2**: **"Fire Team"** — 4 soldiers form a defensive perimeter
- 💥 **ULTIMATE**: **"Platoon"** — 8 soldiers form a full perimeter around the chosen point

---

## 5. GENERAL UPGRADES

Always available in the pool regardless of deck. MVP: implement the first 6.

| ID | Name | Effect |
|---|---|---|
| `general_adrenaline` | **Adrenaline Rush** | The next evacuation completes 50% faster (one-time use per wave) |
| `general_triage` | **Medical Protocol** | Every successful evacuation restores 25 HP to all barricades |
| `general_combatbonus` | **Combat Bonus** | Killing 10 zombies in a row grants +50% XP for the next 30 seconds |
| `general_cascade` | **Cascade** | After a full transport load, every card's cooldown is reduced by 1 |
| `general_veteran` | **Veteran Instinct** | The hero is immune to panic for 5 seconds after spotting a zombie |
| `general_precisionstrike` | **Precision Strike** | The next bomb deals x2 damage and doesn't harm civilians (one-time use) |
| `general_fieldmedic` | **Field Medic** | 30% chance that an evacuated civilian grants double XP |
| `general_overcharge` | **Overcharge** | All active cards: +30% duration for the current wave |
| `general_desperatehour` | **Desperate Hour** | If ≤5 civilians remain — the hero's capacity becomes unlimited |
| `general_doublexp` | **Accelerated Learning** | +25% XP gained for the next 2 waves |

---

## 6. DATA STRUCTURE FOR CLAUDE CODE

### 6.1 New files to create

```
Assets/Scripts/
  Roguelite/
    RunSessionData.cs        ← current run's data (XP, stacks, modifiers)
    RunUpgradeDefinition.cs  ← ScriptableObject for each upgrade
    UpgradeManager.cs        ← pool logic, selection, application
    XPManager.cs             ← XP awarding, level-up trigger

  Heroes/
    HeroData.cs              ← hero ScriptableObject
    HeroManager.cs           ← hero selection/switching (meta level)

  UI/
    LevelUpScreen.cs         ← upgrade-choice screen UI (3 cards)
    UpgradeCardUI.cs         ← a single upgrade card in the UI
    XPBarUI.cs               ← XP bar in the HUD
```

### 6.2 RunSessionData (singleton, reset before each level)

```csharp
public class RunSessionData : MonoBehaviour
{
    public static RunSessionData Instance;

    // XP and progression
    public int currentXP;
    public int currentRunLevel;
    public int[] xpThresholds; // {100, 150, 200, 250, 300, ...}

    // Upgrade stacks: upgradeId → number of times taken (0-3)
    public Dictionary<string, int> upgradeStacks = new Dictionary<string, int>();

    // Numeric modifiers: key → value
    // Card controllers read these at Init
    public Dictionary<string, float> modifiers = new Dictionary<string, float>();

    // Special flags (for Ultimate effects)
    public HashSet<string> activeFlags = new HashSet<string>();

    public float GetModifier(string key, float defaultValue = 0f);
    public void AddModifier(string key, float value); // additive
    public void SetFlag(string flagId);
    public bool HasFlag(string flagId);

    public void AddXP(int amount);
    private void CheckLevelUp();
    public void ResetForNewLevel();
}
```

### 6.3 UpgradeManager

```csharp
public class UpgradeManager : MonoBehaviour
{
    public static UpgradeManager Instance;

    // All upgrade definitions (fill in via the inspector)
    public List<RunUpgradeDefinition> allUpgrades;

    // Return 3 random upgrades to show the player
    public List<RunUpgradeDefinition> GetUpgradeOptions(List<CardData> currentDeck, CardType heroType);

    // Apply the chosen upgrade
    public void ApplyUpgrade(string upgradeId);

    // Get the current tier of an upgrade (for UI display)
    public UpgradeTier GetCurrentTier(string upgradeId);

    private float GetUpgradeWeight(RunUpgradeDefinition upgrade);
    private bool IsUpgradeMaxed(string upgradeId);
}
```

### 6.4 RunUpgradeDefinition (ScriptableObject)

```csharp
[CreateAssetMenu(fileName = "NewUpgrade", menuName = "ZombieGame/Run Upgrade")]
public class RunUpgradeDefinition : ScriptableObject
{
    public string upgradeId;
    public CardType targetCardType; // CardType.None = General upgrade
    public string displayName;
    public string displayNameEnhanced;
    public string displayNameUltimate;
    [TextArea] public string descriptionTier1;
    [TextArea] public string descriptionTier2;
    [TextArea] public string descriptionTier3;
    public Sprite iconTier1;
    public Sprite iconTier2;
    public Sprite iconTier3;
    public Color accentColor; // card border color in the UI
}
```

### 6.5 Changes to existing controllers

Every card controller needs to be changed: after reading from `PlayerProfile`, apply `RunSessionData` modifiers.

**Example for HelicopterController.cs:**
```csharp
private void Start()
{
    // Existing CardData-reading code...
    maxCapacity = (int)myCardData.GetCalculatedStat(StatType.Capacity, currentLevel);

    // NEW: apply run modifiers
    if (RunSessionData.Instance != null)
    {
        maxCapacity += (int)RunSessionData.Instance.GetModifier("helicopter_capacity");
        verticalSpeed *= (1f + RunSessionData.Instance.GetModifier("helicopter_speed_mult"));
        attractRadius *= (1f + RunSessionData.Instance.GetModifier("helicopter_radius_mult"));
        loadTime += RunSessionData.Instance.GetModifier("helicopter_loadtime");
        boardingCooldown *= (1f - RunSessionData.Instance.GetModifier("helicopter_boarding_reduction"));

        // Ultimate flags
        if (RunSessionData.Instance.HasFlag("helicopter_no_panic"))
            panicRadius = 0f; // never panics
        if (RunSessionData.Instance.HasFlag("helicopter_instant_landing"))
            verticalSpeed = 9999f; // instant landing
    }
}
```

**Equivalent keys for the other cards:**
- `car_speed_mult`, `car_capacity`, `car_siren_radius_mult`, `car_boarding_reduction`, `car_crush_radius_mult`
- `sniper_range_mult`, `sniper_damage_mult`, `sniper_cooldown_reduction`, `sniper_duration`, `sniper_pierce_count`
- `bomb_radius_mult`, `bomb_damage_mult`, `bomb_cluster_count`, `bomb_count`
- `barricade_hp_mult`, `barricade_stun_duration`, `barricade_reflect_pct`, `barricade_count`
- `bait_radius_mult`, `bait_duration`, `bait_explosion_damage`, `bait_count`
- `soldier_damage_mult`, `soldier_duration`, `soldier_firerate_mult`, `soldier_range_mult`, `soldier_count`

### 6.6 XP awarding (GameManager + XPManager)

In `GameManager.cs`, add calls on:
- `AddRescuedFromTransport()` → `XPManager.AddXP(humanCount * 20 + scientistCount * 40)`
- `Zombie.Die()` → `XPManager.AddXP(isBoss ? 50 : 5)`
- A full transport load → `XPManager.AddXP(25)` (bonus)

---

## 7. UI REQUIREMENTS (MVP)

### 7.1 HUD — XP Bar
- An XP bar at the top of the screen (or built into the hero card)
- Shows: current XP / threshold for the next level
- A light animation when XP is awarded

### 7.2 Level Up screen
- `Time.timeScale = 0` while shown
- 3 upgrade cards, laid out horizontally
- Each card contains:
  - The card's icon (with a tier badge: ★ ★★ ★★★)
  - The upgrade's name (reflecting its tier: regular / Enhanced / ULTIMATE)
  - A description of the current tier's effect
  - An accent color for ULTIMATE upgrades (gold/purple)
- Tapping a card → apply it → hide the screen → `Time.timeScale = 1`
- For ULTIMATE — a special appearance animation for the card (flash/effect)

### 7.3 Hero Selection (Meta-UI)
- Add a "Hero" section to the level-select screen / main menu
- Show the current hero + locked heroes with their unlock condition
- A hero-switch button (if at least 1 additional hero is unlocked)

---

## 8. MVP SCOPE

### What to implement in the MVP

**Required:**
- [ ] `RunSessionData.cs` — XP, stacks, modifiers
- [ ] `XPManager.cs` — XP awarding from GameManager
- [ ] `UpgradeManager.cs` — pool and application (no weighting, random is enough)
- [ ] Level Up screen (3 cards, pause)
- [ ] XP bar in the HUD
- [ ] Changes to every card controller (reading modifiers)
- [ ] 3 upgrades for each card in the deck (Tiers 1–3)
- [ ] 3–4 general upgrades
- [ ] HeroData + HeroManager (basic, Helicopter only)
- [ ] `RunUpgradeDefinition` ScriptableObjects for all MVP upgrades

**Not in MVP (Post-MVP):**
- Weighting system in the pool
- Unlocking Car and CombatHelicopter (hero progression)
- The full hero-selection screen in the meta-UI
- Special animations for Ultimate
- Saving run stats (best level, max stack, etc.)
- Synergies between upgrades from different cards

### Implementation priority order

1. `RunSessionData` → the basic XP and modifiers system
2. `XPManager` → calls from `GameManager`
3. XP bar in the HUD
4. Level Up screen (UI, no effects)
5. `UpgradeManager` — a random pool from the available upgrades
6. Applying modifiers in `HelicopterController` (start with one hero)
7. `RunUpgradeDefinition` ScriptableObjects for the Helicopter (5 upgrades x 3 tiers)
8. Applying modifiers in the other controllers
9. ScriptableObjects for the other cards
10. Balance testing: average level = 8–12 level-ups

---

*Document created for handoff to Claude Code. When implementing, follow the order in section 8.*
