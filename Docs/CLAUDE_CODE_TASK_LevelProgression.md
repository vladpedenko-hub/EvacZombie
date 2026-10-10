# CLAUDE CODE TASK — Level Progression & Balance Pass (MVP, 10 levels)

Owner: Vlad (producer/design) · Executor: Claude Code (Unity MCP) · Written: 2026-10-10
Branch: `feature/level-progression` (from `main`)
Companion output you will create: `Docs/LEVEL_BALANCE.md` (live balance sheet) + playtest logger.

## 0. TL;DR

Today all 10 levels are copies of one placeholder: same two wave assets, same 30 s timer, multipliers = 1, L9–L10 have zero waves, two cards (Car, Bomb) are impossible to obtain, card upgrades need shards that no source produces, and the research tree costs 3× the Scientists the game can ever pay out.

Your job: turn the 10 levels into a 30–40 minute progression with a real difficulty curve, using Level 1 as the calibration anchor. You design and set the absolute numbers yourself — this doc gives you the beats, the target ratios vs Level 1, the hard rules, and the method. Measure, tune, document.

Scope rule: content + data + the minimum code listed in §6. No new mechanics, no new zombie types, no new UI screens. If something outside scope seems needed, write it in `Docs/LEVEL_BALANCE.md` → "Proposals" and keep going.

## 1. Current state (verified in `main`, 2026-10-10)

| Area | Finding | Consequence |
|---|---|---|
| Waves | Only 2 WaveData assets exist (`Wave_Boss_North`, `Wave_Boss_All`). L1–L8 all use both. L9–L10: `waves: []`. | No difficulty curve; L9–L10 likely auto-resolve or break. |
| Boss | `Wave_Boss_All` spawns a `ZombieBoss` at t≈30 s on every level including L1 (tutorial). | Boss has no reveal moment. |
| Multipliers | `LevelData.zombieSpeedMultiplier` / `zombieHealthMultiplier` are not read anywhere in code. | Dead fields — must be wired (§6.1). |
| Timer | `levelTimer = 30` on all levels. It is the day phase; at 0 the game enters Sudden Death (night spawns every `suddenDeathSpawnRate` s until the map is empty). | Main tension knob is unused. |
| Objectives | `requiredRescuedHumans = 10` everywhere; `star2RequiredHumans = 20` on L1–L5, 0 (→ fallback required+5) on L6–L10. `humanCount` 50 (L2 = 100). | Flat. |
| Scientists | `scientistCount = 0` on all levels. Scientist currency only from `clearGrants` (+1 per first clear → max 10). | Research tree `Lab_Tree_01` (total cost 31) is ~⅓ affordable. |
| Maps | L8, L9, L10 all reuse `Level_03`. Unused prefabs exist: `Level_01 First Big`, `Level_01 3`, `Level_01 BoxHouse`, `Level_01 First try All work`, `Level_02`, `Level_03`. | Map variety possible, each must be validated (spawn markers, NavMesh, camera). |
| Cards | Starter: Helicopter, Barricade. First-clear lootboxes: L1 Bait, L2 Bait, L3 Soldier, L4 Bait, L5 Sniper, L6–L10 none. Region reward box: CombatHelicopter. Car, Bomb: no source. Deck = 5 slots. | 2 of 8 cards unreachable; deckbuilding choice never happens. |
| Card upgrades | Shards per level-up: 5/10/15/20/25 dupes. Currency: 100 each for most cards; Bait & Barricade 100/500/1000/5000/10000. Only shard source = 3 Bait duplicates. | Card progression is dead in practice. |
| Meta buildings | Lab @ Clear_L2 (20 People), House2 @ L5 (60), House3 @ L10 (80), House4 @ L15 (100), House5 @ L20 (120). | House4/5 unreachable in a 10-level game. |
| Regions | Region1 "Nevada" = L1–5, Region2 "Texas" = L6–10. Region3 & Region4 assets duplicate Region1's level list. | If reachable, players replay L1–5 as "new" content. |
| People economy | Per clear (repeatable): +1 People per rescued human. First clear: +currencyReward (50; L2 = 100) +10 People +1 Scientist. | Replays farm People; Scientists can't be farmed. |

## 2. Design framework (apply, don't debate)

1. Teach → Test → Twist. Every level introduces or tests exactly one thing.
2. Sawtooth difficulty, not a ramp. Pressure rises 3–4 levels, peaks at a wall, then drops for one relief level.
3. Before each wall the player must already have a reason and the currency to upgrade/build. The wall converts that into an action.
4. First-attempt failure rate is the KPI. A wall at 40–55% first-attempt fail is healthy if a retry after one meta action wins at ≥75%.
5. Session length: L1 ≈ 1.5 min, mid ≈ 2–3 min, walls ≈ 3–4 min (incl. night). Never above ~4 min.
6. ★1 = win. ★2 ≈ +50–60% rescue over ★1. ★3 = perfect clear (existing logic). ★2 reachable first try by an attentive player on non-wall levels.
7. Every level ends with something new or earned. No dead clears.
8. The 6th owned card must arrive before the first wall, so the player benches a card for the first time where it matters.

## 3. Progression sheet (producer decisions)

PI = Pressure Index, relative to Level 1 = 1.00 (definition §4). PI is a target; you choose the levers. Fail-rate is a playtest target — log it (§7).

| # | Region | Beat | Teaches / tests | Card in focus | Target PI | Day timer | Twist levers | 1st-try fail target | First-clear reward |
|---|---|---|---|---|---|---|---|---|---|
| 1 | Nevada | First Contact — anchor | Placement + heli evac | Helicopter, Barricade | 1.00 | keep (re-measure) | Remove boss (§4.3) | <5% | Soldier |
| 2 | Nevada | Hold the Line | First combat card; single direction | Soldier | 1.25–1.35 | +10–20% vs L1 | One spawn group (North) | ~10% | Bait |
| 3 | Nevada | Distraction | Lure to buy evac time; 2 directions | Bait | 1.50–1.65 | +20–30% | 2 groups, staggered | ~15% | Sniper |
| 4 | Nevada | Overwatch | Long-range defense; dense bursts; deck full 5/5 | Sniper | 1.85–2.05 | +30–40% | Burst patterns, higher humanCount | 20–25% | Car (6th card) |
| 5 | Nevada | Nevada Falls — WALL #1 | Boss reveal; all directions; deck choice | Car + full deck | 2.50–2.70 | +40–50% | ZombieBoss first time, all groups, HP mult ≤1.15 | 40–55% | Bomb + region reward |
| 6 | Texas | Brain Drain — RELIEF | Scientists appear on map | Bomb | 1.80–2.00 | +40–50% | scientistCount 3–5; lower density | ~15% | CombatHelicopter |
| 7 | Texas | Rush Hour | Fast zombies | CombatHelicopter | 2.30–2.50 | +40% | speed mult 1.15–1.25 | 25–30% | shard bundle |
| 8 | Texas | Hard Targets | Tanky zombies; focus fire | Sniper/Soldier upgrades | 2.60–2.85 | +50% | HP mult 1.3–1.5; new map if valid | 30–40% | shard bundle |
| 9 | Texas | Nightfall | Evacuate before night | Car / Helicopter | 2.90–3.15 | shortest of L5–L10 | Low day timer, lower suddenDeathSpawnRate, higher humanCount | 35–45% | shard bundle |
| 10 | Texas | Texas Falls — WALL #2 | Everything combined; double boss | Full deck, upgrades | 3.40–3.70 | +60% | 2 bosses staggered, all groups, speed ≤1.2, HP ≤1.4 | 50–60% | region reward + big People/Scientist grant |

Meta pacing:
- After L2: Lab unlocks (exists) → first research node (cost 1) affordable.
- Before L5: player can afford either a card upgrade or House2 — not both on first-clear income alone.
- After L6: Scientists flow → research tree is the main sink.
- Before L10: ~60–70% of the tree (~19–22 of 31 Scientists) affordable for a player who replays 2–3 levels. Never 100% in MVP.

Building unlock retarget: Lab @ L2 (keep) · House2 @ L4 · House3 @ L6 · House4 @ L8 · House5 @ L10. Create new `Clear_Level_4/6/8` condition assets; don't edit the L15/L20 assets, just stop referencing them. Keep restoration costs unless §5 math says otherwise.

## 4. Calibration method

### 4.1 BalanceProbe (dev-only)
Create `Assets/Scripts/Dev/BalanceProbe.cs` (editor/dev-build only, never active in release; same pattern as the dev debug panel / CheatManager). Runs a level with zero player input at accelerated Time.timeScale and logs per run:
- t25, t50, t75 — seconds from StartGame() until 25/50/75% of initial residents are infected.
- daySpawns — zombies spawned by the wave timeline before Sudden Death.
- peakZombies — max Zombie.AllZombies.Count.
- dayLength, totalLength (until level empty).
- bossSpawnTime (if any).
Run each level ≥10 seeds. CSV to `Application.persistentDataPath/balance_probe.csv`; median + p10/p90 into LEVEL_BALANCE.md.

### 4.2 Pressure Index
```
PI(level) = 0.5 * ( t50(L1) / t50(level) )
          + 0.3 * ( daySpawnsWeighted(level) / daySpawnsWeighted(L1) )
          + 0.2 * ( humanCount(level) * required%(level) ) / ( humanCount(L1) * required%(L1) )

daySpawnsWeighted = Σ zombies × speedMult × healthMult
(boss counted as N regular zombies, N = boss HP / regular HP — read from prefabs, document N)
```
Measure L1 as-is first (raw anchor), then §4.3.

### 4.3 Re-anchor Level 1
Remove the boss from L1. Replace its threat with regular zombies so L1's t50 stays within ±10% of the as-is measurement. Re-anchored L1 = PI 1.00. Don't touch L1's map, tutorial (TutorialSequenceGame01), camera or humanCount.

### 4.4 Tuning loop per level
1. Pick map + levers from §3. 2. Author waves (§5.1). 3. Probe → PI → adjust until in band. 4. Flag any level whose probe totalLength median exceeds 3 min. 5. Record in LEVEL_BALANCE.md.

## 5. Content & economy specs

### 5.1 Waves
- New per-level wave assets in `Assets/Data/Waves/` named `W_L{NN}_{A|B|C}`. Do not edit `Wave_Boss_North` / `Wave_Boss_All`; levels stop using them.
- 2–4 waves per level. First wave 3–6 s after StartGame(), warningDuration ≥ 3 s.
- Linear = steady pressure (early), Burst = spikes (L4+), RandomInterval = chaos (L7+).
- Before assigning spawnGroup, read which SpawnPointMarker groups exist on that map. Never reference a missing group. Document groups per map.
- Boss: first in L5, ≥10 s warning, never in first 15 s. L10: two bosses ≥15 s apart, different groups.

### 5.2 Objectives & stars (% of initial residents = humans + scientists)
| Levels | required | ★2 |
|---|---|---|
| L1–L2 | 20% | 40% |
| L3–L4 | 25% | 45–50% |
| L5 | 30% | 55% |
| L6 | 25% | 50% |
| L7–L9 | 30–35% | 55–60% |
| L10 | 35–40% | 60–65% |
Set star2RequiredHumans explicitly on every level. Account for scientists on L6+. Write a one-line English missionDescription per level (the beat, e.g. "Lure them away. The chopper needs time.") and star3Description.

### 5.3 Cards & rewards
- Unlock order (first-clear levelRewardLootbox, guaranteed): L1 Soldier · L2 Bait · L3 Sniper · L4 Car · L5 Bomb · L6 CombatHelicopter. Create lootbox assets as needed (`LB_L{NN}_{Card}`). If CombatHelicopter is also Region1's regionRewardLootbox, replace that region reward with a shards/People bundle.
- L7–L10 first-clear: shard bundle (lootbox with possibleDrops = owned cards). If LootboxData only gives one card, give one random owned card per box and note it in Proposals.
- Upgrade curve, all 8 cards, uniform:
  | Level-up | 1→2 | 2→3 | 3→4 | 4→5 |
  | Duplicates | 2 | 3 | 4 | 6 |
  | People | 60 | 120 | 220 | 400 |
  Verify how CardInfoPopup indexes upgradeCosts before removing/keeping the 5th entry; don't change that code. Target: 2–3 favourite cards at level 3 by L10; nobody hits level 5 in MVP.
- abilityEnabled: false L1–L2, true from L3.

### 5.4 Currency budget (player first-clears L1–L10 + replays ~3 levels)
| | Income by end of L10 | Sinks |
|---|---|---|
| People | ~1,400–1,800 | Buildings 380 + upgrades ~1,000–1,400 |
| Scientists | ~19–22 | Tree 31 |
Levers: currencyReward, clearGrants, scientistCount (L6+: 3–5), humanCount. Build the per-level income table (first-clear vs replay, cumulative) and prove the L5 "upgrade OR build" choice holds.

### 5.5 Regions
Check whether Region3/Region4 are reachable from the world map. If yes, remove them from the list feeding the map (don't delete assets).

## 6. Code changes allowed (only these)
1. Wire multipliers: apply zombieSpeedMultiplier / zombieHealthMultiplier from LevelManager.Instance.currentData to every zombie when it becomes active — wave spawns, Sudden Death spawns, infection-created zombies, pooled zombies on reuse (reset to base, then multiply — no compounding), ZombieBoss. One small reviewable commit.
2. BalanceProbe (§4.1), dev-only.
3. PlaytestLog (§7), dev-only, local file, no SDK.
4. Data-only changes elsewhere.
Do not touch GameManager/LevelManager win/lose/flow logic, save formats, PlayerProfile schema, or UI layout.

## 7. Playtest logger
Dev-only PlaytestLog appending one JSON line per level attempt to `Application.persistentDataPath/playtest_log.jsonl`:
timestamp, sessionId, level, attemptNumber, result (win/lose/quit), stars, rescued, required, initialResidents, durationDay, durationTotal, cardsPlaced[{id,count}], deck[5], cardLevels{}, peopleBefore/After, scientistsBefore/After, metaActionsSinceLastAttempt (upgrades/buildings/research bought).
Add an Export button to the existing dev debug panel (copy file to shareable location / clipboard). Togglable, off in release builds.

## 8. Execution plan

| Phase | Work | Done when |
|---|---|---|
| P1 Audit | Map prefab inventory (spawn groups, NavMesh, playable y/n). Boss HP ratio N. LootboxData capabilities. Region3/4 reachability. upgradeCosts indexing. | Inventory table in LEVEL_BALANCE.md |
| P2 Instruments | Wire multipliers. BalanceProbe. PlaytestLog. | Probe CSV for L1 as-is |
| P3 Anchor | Measure L1 as-is → re-anchor without boss. | L1 t50 within ±10%, PI=1.00 recorded |
| P4 Region 1 | L2–L5: maps, waves, objectives, rewards, tuned to PI. | All 5 in band |
| P5 Region 2 | L6–L10. | All 10 in band |
| P6 Economy | Rewards, clearGrants, scientistCount, upgrade curve, building conditions. Income table. | §5.4 met; L5 choice holds |
| P7 Verify | Probe all 10; no missing spawn groups; every level ends; every card obtainable; old saves (LevelPassed_* keys) don't crash. | Checklist green |

Stop and ask Vlad if: a PI band can't be reached without a new mechanic; a map needs prefab surgery (NavMesh rebake, new spawn markers); a change would alter save data shape; the boss can't be removed from L1 without editing the tutorial.

## 9. Docs/LEVEL_BALANCE.md structure
1. Summary table per level: map, wave assets, timer, humanCount, scientistCount, required, ★2, speed/HP mult, suddenDeathSpawnRate, PI target, PI measured, t50 median, reward.
2. Probe results (median, p10/p90).
3. Economy table per level (first-clear / replay / cumulative / unlocks available).
4. Map inventory.
5. Proposals.
6. Playtest hooks (which log field answers which question).
Single source of truth for numbers. CLAUDE.md gets a 2–3 line pointer only.

## 10. Acceptance criteria
- [ ] Every level has its own wave assets; none reference Wave_Boss_*.
- [ ] L9 and L10 have waves and end correctly.
- [ ] Measured PI inside its band for every level; sawtooth visible (L5 > L6 < L7, L10 max).
- [ ] Boss first in L5; none in L1; two in L10.
- [ ] Multipliers affect all zombie sources incl. infected & pooled, no compounding.
- [ ] All 8 cards obtainable by first-clearing L1–L6; 6th card before L5.
- [ ] Upgrade curve rescaled; level 3 on 2–3 cards reachable by L10.
- [ ] No meta condition references a level > 10.
- [ ] Region3/4 unreachable (or confirmed already).
- [ ] star2RequiredHumans explicit on all levels; mission descriptions written.
- [ ] BalanceProbe + PlaytestLog dev-only, off in release.
- [ ] LEVEL_BALANCE.md complete; CLAUDE.md pointer added.
