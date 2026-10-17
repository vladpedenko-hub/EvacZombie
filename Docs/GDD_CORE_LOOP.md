# EvacZombie — Core Loop GDD (simple → complex)
*Updated 23.08.2026. Goal of this document: a minimal description of what MUST work in MVP v1 — no narrative, no in-run roguelite. Full backlog with priorities and status → [Notion Dev Board](https://app.notion.com/p/a2dce6c7c84c41d794a674f42cc2fc2d).*

---

## Layer 0 — One level, bare-bones

- **Map:** static scene (roads, buildings, NavMesh)
- **Spawn points:** day spawn points + night spawn points (for sudden death)
- **Zombie:** spawns in waves on the level timeline → heads to the nearest Human/Scientist → attacks
- **Human / Scientist:** spawned at level start, wander, panic (ChaosSettings), run to the evac point once they enter the hero's attraction radius
- **Player:** Planning (place cards before the wave starts) → Playing (wave runs, evacuate) → timer expires → SuddenDeath (night, endless spawn) → Win/Lose

This core is already implemented and working (`GameManager`, `LevelManager`).

## Layer 1 — Units (full set, nothing more to add)

| Card | Role |
|---|---|
| Helicopter / Car | Evacuation (hero) |
| CombatHelicopter | Evacuation + damage |
| Sniper / Soldier | Precision damage |
| Bomb | AoE damage |
| Barricade | Path blocking |
| Bait | Zombie lure |

Each card: `cardPrefab` (logic in `Assets/Scripts/Bonus/`) + `CardData` (stats, level) + `CardUI` (drag&drop, cooldown).

**Resource for placement — card cooldown only.** The shared energy pool (`EnergyManager`) has been removed from the placement gate (see the Notion card "REMOVE EnergyManager mana-gate"). Reason: double-gating (cooldown + mana at the same time) is unnecessary real-time cognitive load; the genre originals (Atom Zombie Smasher, Rebel Inc.) use cooldown only.

## Layer 2 — Win / lose

- `requiredHumans` — threshold to win
- Stars 1–3: thresholds by number rescued + perfect clear (all zombies dead, someone still alive on the map)
- Level timer → SuddenDeath (night) → evacuate to the last person or fail

## Layer 3 — Between-level progression (meta, partly already in place)

Already implemented:
- `totalCurrency` for rescued people + a bonus for a level's first clear
- `totalScientistsCurrency` for rescued scientists (**currently has nowhere to be spent** — see Layer 4)
- Lootbox → new card for the deck (5 slots)
- Regional saga map (4 regions, 10 levels)
- Energy (lives, cap 20, 5-minute regen) — gates the attempt to play a level, does NOT gate card placement within a level (not to be confused with the Layer 1 EnergyManager)

## Layer 4 — What's missing to close the loop (new, priority)

Right now the only progression is cards via lootbox. `totalScientistsCurrency` accumulates with no use. We need:

**Talent Tree — a minimal meta-scene**
- A separate tab in the main menu (next to the region map)
- Nodes = flat % modifiers on existing `StatType`s (Damage, Health, FireRate, Speed, Radius), applied globally to all cards
- Currency: `totalCurrency` (soft) + `totalScientistsCurrency` (closes an existing hole in the economy — the currency already exists, there's just nothing to spend it on yet)
- Tree progress: linear-branching, each node unlocks the next (see Vlad's reference screenshot — flat % stats, locked/unlocked nodes)
- 8–12 nodes is enough for the MVP
- Technically: 80% of the infrastructure is already paid for by the roguelite work — card controllers already know how to read modifiers via the `Instance.GetModifier(key)` pattern (built for `RunSessionData`). The same pattern gets wired to a **persistent** source (`PlayerProfile`) instead of the run session.

Full tech note → Notion card "Meta: Talent Tree (persistent)".

---

## What's deliberately NOT in MVP v1

All of this is documented in Notion, phase **🚀 Post-Launch / v2**, not deleted — just out of scope:
- In-run roguelite progression (heroes, XP, per-wave upgrades) — 14 ScriptableObjects are ready, waiting for post-launch
- The entire narrative layer: intro/cutscene, 3-4 heroes with bios, briefing dialogues before levels, story boss regions, survivor diary, flavor text, tutorial narrative integration
- Region reputation system

## MVP v1 — content volume

10 existing levels ≈ 20-30 minutes of gameplay (curve: 1-3 tutorial, 4-7 ramp-up, 8-10 first real challenge) — this is the target volume, **new levels aren't needed right now**. Priority is balancing the existing ten, not expanding.
