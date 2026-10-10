# Level Progression & Balance — Live Sheet

Source task: `Docs/CLAUDE_CODE_TASK_LevelProgression.md`. This file is the single source of truth for the numbers; `CLAUDE.md` only points here.

Status: **P1 (Audit) complete.** P2+ in progress — this file is updated after every phase.

---

## 1. P1 Audit

### 1.1 Map prefab inventory

Checked live in the Editor (instantiate → count `SpawnPointMarker`s by group → count `Building`-tagged objects → real `NavMeshSurface.BuildNavMesh()` → `NavMesh.CalculateTriangulation()` → destroy). Not a text-search guess.

| Prefab | Used by | Spawn markers | Groups present | Night spawns | Buildings | NavMesh verts | Valid? |
|---|---|---|---|---|---|---|---|
| `Level_01 BoxHouse 2 New Build` | L1 | 5 | North=1, All=1, Any=3 | 3 | 33 | 260 | ✅ |
| `Level_02 BoxHouse` | L2 | 7 | North=1, Any=6 | 4 | 65 | 634 | ✅ |
| `Level_03 BoxHouse` | L3 | 7 | Any=7 | 4 | 65 | 355 | ✅ |
| `Level_04 BoxHouse` | L4 | 8 | Any=8 | 5 | 108 | 580 | ✅ |
| `Level_05 BoxHouse` | L5 | 8 | Any=8 | 5 | 141 | 551 | ✅ |
| `Level_06 BoxHouse` | L6 | 8 | Any=8 | 5 | 154 | 411 | ✅ |
| `Level_07 BoxHouse` | L7 | 8 | Any=8 | 5 | 154 | 349 | ✅ |
| `Level_03` (bare) | **L8, L9, L10 (current, broken)** | **0** | — | 0 | 19 | 454 | ❌ no spawn points at all |
| `Level_01 3` | unused | 0 | — | 0 | 18 | 181 | ❌ |
| `Level_01 BoxHouse` | unused | 6 | North=1, All=1, Any=4 | 4 | 33 | 305 | ✅ (valid, just not wired to any level) |
| `Level_01 First Big` | unused | 0 | — | 0 | 98 | 949 | ❌ |
| `Level_01 First try All work` | unused | 0 | — | 0 | 18 | 181 | ❌ |
| `Level_02` (bare) | unused | 0 | — | 0 | 30 | 693 | ❌ |

**Finding (not in the brief): L8 is already broken today, not just L9/L10.** `Level_03` (bare) has zero `SpawnPointMarker`s. L8's current waves (`Wave_Boss_North`+`Wave_Boss_All`) reference `North` and `All` groups that resolve to an empty point list on this map — `LevelManager.ExecuteWaveAction` logs `"No spawn points found for group X. Spawn skipped"` and nothing ever spawns. So today L1–L7 are playable, **L8, L9, L10 are all non-functional** (L8 silently spawns nothing; L9/L10 have no waves at all).

**Finding: only one named compass group exists anywhere — `North`** (on `Level_01 BoxHouse 2 New Build`, `Level_02 BoxHouse`, and the unused `Level_01 BoxHouse`). No map has `South`/`East`/`West`/`Special_1`/`Special_2` markers. `SpawnGroup.All` means "every non-night point on the map, simultaneously" (`LevelManager.cs:329-331`); `SpawnGroup.Any` means "one random point, chosen once for that wave action, all of that action's count spawns from it" (`LevelManager.cs:321-327` + `:393`). So "N directions" from §3 is implemented as: `All` for "all directions" (literal, exact), multiple independent `Any` actions for "2+ directions" (probabilistic — each action rolls its own point, not guaranteed distinct, but with 7-8 Any points per map the odds of two actions coinciding are low and the result reads as multi-directional). This avoids needing new spawn markers (which would be prefab surgery — one of the brief's explicit stop conditions) for every beat in §3.

**Decision: L8/L9/L10 reuse already-validated BoxHouse maps instead of the broken bare prefabs**, since none of the unused prefabs (except `Level_01 BoxHouse`, a near-duplicate of L1's own map) have spawn points, and adding markers to them is prefab surgery (stop condition). Not maximal map variety, but doesn't require an editor-manual pass or a stop. Flagged in Proposals for Vlad to decide whether to invest in real new maps for L8-10 later.
- L8 → reuses `Level_04 BoxHouse` (Any=8, 108 buildings — distinct building count from L4's own usage reads differently in a wave context, not ideal but functional)
- L9 → reuses `Level_06 BoxHouse` (Any=8, 154 buildings)
- L10 → reuses `Level_07 BoxHouse` (Any=8, 154 buildings, biggest footprint — fits a wall level)

### 1.2 Boss HP ratio (N)

`Assets/Prefabs/Zombie.prefab` `Zombie.maxHealth = 60`. `Assets/Prefabs/ZombieBoss.prefab` `Zombie.maxHealth = 200` (inherited field, overridden on the prefab). **N = 200/60 = 3.33** — used as-is in the Pressure Index formula (§4.2 of the brief): a boss counts as 3.33 regular zombies in `daySpawnsWeighted`.

### 1.3 LootboxData capabilities

`Assets/Scripts/Data/LootboxData.cs` already supports both modes needed:
- `isTutorialBox=true` + `guaranteedCard` → 100% guaranteed single card (used for all per-level unlock boxes).
- `possibleDrops: List<DropRate>` (card + weight) → weighted pool, `OpenBox()` already implements weighted random selection. Used for the L7-L10 "shard bundle" boxes (`possibleDrops` = the player's already-unlocked cards at that point, equal weight — "opening a box for a card you already own" **is** the duplicate/shard mechanic: `CardProgress.collectedShards` only goes up when `OpenBox()` returns a `CardData` the player already owns).

No code change needed here — data-only.

### 1.4 Region reachability + a dead field found along the way

`PlayerProfile.allRegions` (live, Inspector-wired in `MainMenu.unity`) contains exactly `NewRegion1` (Nevada, L1-5) and `NewRegion2` (Texas, L6-10). `NewRegion3`/`NewRegion4` assets exist on disk but are **not referenced by `allRegions` or anything else reachable** — confirmed unreachable already, no code change needed (per the brief's §5.5, "don't delete assets").

**Finding (not in the brief): `RegionConfig.regionRewardLootbox` is dead data — never read by any script** (grep across `Assets/Scripts` turns up only the field declaration). The actual live "region completion reward" mechanism is `RegionConfig.trophyRoadRewards[2]` (the 100%-threshold entry), consumed by `MapController.cs`. Checked both regions' trophy roads directly:
- **Region1 (Nevada)**: 25%→100 soft People, 50%→500 soft People, **100%→`Lootbox1Bait`** (guaranteed Bait) — this is the live "L5 region reward" the brief refers to, and it's currently a duplicate of L1's own first-clear reward (also guaranteed Bait).
- **Region2 (Texas)**: all three trophy entries are unconfigured placeholder stubs (`threshold=0.25` ×3, `soft=0`, no lootbox) — broken, not just suboptimal.
- `regionRewardLootbox` on both regions is set to `LootboxRegion1` (guaranteed CombatHelicopter) but, being dead/unread, has zero actual effect on play. Left as-is (harmless dead data, out of scope to clean up further per "data-only changes" not touching code that reads it — there is none).

Fix (P6): Region1's 100% trophy reward → a People/Scientist currency bundle (not a duplicate card) via `TrophyRewardType.SoftCurrency`, sized into the §5.4 budget. Region2's three trophy thresholds get real values (25/50/100%) with real rewards, also budgeted.

### 1.5 `CardInfoPopup` upgradeCosts indexing

`CardInfoPopup.cs:100`: `costIndex = Mathf.Clamp(lvl - 1, 0, upgradeCosts.Count - 1)`, gated by `isMaxLevel = lvl >= data.maxLevel || upgradeCosts.Count == 0` (`:72`). All 8 `CardData` assets currently have `maxLevel=5` and **5** `upgradeCosts` entries. Given the clamp and the `isMaxLevel` gate, `lvl` can only be 1-4 when the upgrade button is active, so `costIndex` only ever reaches 0-3 — **the 5th entry (index 4) is provably unreachable dead data today**, confirmed by the indexing logic, not just the brief's suspicion. Safe to drop to 4 entries matching the brief's §5.3 curve (1→2, 2→3, 3→4, 4→5). `CardInfoPopup.cs` itself needs no code change — behavior is identical, just reads a shorter (correctly-sized) list.

### 1.6 Win-condition / rescued-count mechanics (needed for §5.2 math)

`GameManager.SetTotalHumans()`: `totalHumans` = spawned Human count, `totalScientists` = `Scientist.AllScientists.Count` (both captured once at level start). `requiredHumans` = `LevelData.requiredRescuedHumans` directly (an absolute int, not a percentage — so §5.2's percentage table must be converted to absolute counts per level: `round(pct * (humanCount + scientistCount))`, stored as the actual `requiredRescuedHumans`/`star2RequiredHumans` values). `GetStar1Target()`/`GetStar2Target()` fall back to `requiredRescuedHumans`/`requiredRescuedHumans+5` only when `star1RequiredHumans`/`star2RequiredHumans` are left at 0 — per the brief, both get set explicitly everywhere.

---

## 2. Map groups reference (for wave authoring, P4/P5)

| Map | Groups available |
|---|---|
| L1 (`Level_01 BoxHouse 2 New Build`) | North, All, Any |
| L2 (`Level_02 BoxHouse`) | North, Any |
| L3 (`Level_03 BoxHouse`) | Any (+All, implicit) |
| L4 (`Level_04 BoxHouse`) | Any (+All) |
| L5 (`Level_05 BoxHouse`) | Any (+All) |
| L6 (`Level_06 BoxHouse`) | Any (+All) |
| L7 (`Level_07 BoxHouse`) | Any (+All) |
| L8 (reusing `Level_04 BoxHouse`) | Any (+All) |
| L9 (reusing `Level_06 BoxHouse`) | Any (+All) |
| L10 (reusing `Level_07 BoxHouse`) | Any (+All) |

`All` is always implicitly available (it's "every non-night point," not a per-point label) on every map that has any day spawn points.

---

## 3. Summary table (filled in as each phase lands)

*Columns per brief §9.1. Empty until P3+.*

| # | Map | Waves | Timer | humanCount | sciCount | required | ★2 | spd/HP mult | suddenRate | PI target | PI measured | t50 median | Reward |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | | | | | | | | | | 1.00 | | | |
| 2 | | | | | | | | | | 1.25-1.35 | | | |
| 3 | | | | | | | | | | 1.50-1.65 | | | |
| 4 | | | | | | | | | | 1.85-2.05 | | | |
| 5 | | | | | | | | | | 2.50-2.70 | | | |
| 6 | | | | | | | | | | 1.80-2.00 | | | |
| 7 | | | | | | | | | | 2.30-2.50 | | | |
| 8 | | | | | | | | | | 2.60-2.85 | | | |
| 9 | | | | | | | | | | 2.90-3.15 | | | |
| 10 | | | | | | | | | | 3.40-3.70 | | | |

## 4. Probe results

*Pending P2 (instrument) + P3 (anchor).*

## 5. Economy table

*Pending P6.*

## 6. Proposals

1. **Real map variety for L8-L10** needs either new spawn markers added to the currently-marker-less unused prefabs (`Level_01 3`, `Level_01 First Big`, `Level_01 First try All work`, `Level_02` bare, `Level_03` bare) — prefab surgery, one of the brief's explicit stop conditions — or new maps built outside this task's scope. For now L8/L9/L10 reuse L4/L6/L7's own maps (§1.1).
2. **`EnergyManager`/`RegionConfig.regionRewardLootbox`-style dead fields**: found one more (`regionRewardLootbox` itself, §1.4). Not touched (out of scope — no code reads it, so it's inert, not harmful), but worth a cleanup pass sometime.
3. *(more added as later phases surface them)*

## 7. Playtest hooks

*Pending P2 (PlaytestLog implementation).*
