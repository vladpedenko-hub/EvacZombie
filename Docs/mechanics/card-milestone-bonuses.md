# Mechanic: card milestone bonuses (level 5)

*Designed 2026-08-23, cross-checked against the code 2026-08-29 (see "VERIFIED"/"CORRECTION" notes on each card). **UPDATED 2026-08-29 (again)**: a persistent modifier store is NOT a prerequisite for this mechanic — see `Docs/CARD_SYSTEM_DESIGN.md` §5 and the "Technical dependency" section below. The store (`Docs/mechanics/persistent-modifier-store.md`) is only still needed for the future Talent Tree.*

## Concept

Each of the 8 cards gets ONE qualitative (not just numeric) bonus, unlocked at the current max upgrade level (5). Reuses ideas from the frozen roguelite effect catalog (`RunUpgradeDefinition.cs`, T1 tier), but retuned for a persistent (not one-run) context. Gives the player a mid-term reason to upgrade cards beyond flat stat growth.

**Locked-in scope:**
- One bonus per card, at the current max level (5). We are NOT extending maxLevel to multiple tiers right now — that would mean recalculating the entire shard-currency upgrade cost curve.
- All 8 cards get a bonus for symmetry (not just the 4 cards that already have ready-made code).

## Table (8/8)

| Card | Bonus at level 5 | Implementation |
|---|---|---|
| **Barricade** | Reflects 25% of incoming damage back at the attacking zombie | VERIFIED: the code already exists and works — `Barricade.cs` (~121-134) reads `RunSessionData.Instance.GetModifier("barricade_reflect_pct")`. Currently inactive only because `RunSessionData` is frozen (null). Implementation (see `Docs/CLAUDE_CODE_TASK_Cards_Bonuses_A.md`): leave the `RunSessionData` code path alone (kept for the future Talent Tree); the bonus is a separate `currentLevel >= 5` check nearby, with the level read directly from `PlayerProfile.Instance.ownedCardsProgress`, same as everywhere else in the card controllers. No new storage. |
| **Sniper** | The piercing shot (which already pierces 3 targets by default) deals FULL damage to all 3 instead of falling off (100%→50%→25%) | VERIFIED: `Sniper.cs` — `maxPierceTargets=3`, `pierceDamageFalloff=0.5` (default), `ApplyPiercingDamage()` (~329-367) multiplies damage by `pierceDamageFalloff` per pierced target. Bonus = a flag overriding the falloff to 1.0. Almost no new code. **Note**: the code already has a separate hook, unrelated to this bonus, `RunSessionData.Instance.HasFlag("sniper_instakill")` (line 354) — a stub left over from an earlier instakill idea. An instakill for Sniper was initially proposed and rejected as a no-op: the base damage of 100 at level 1 already one-shots a 100 HP zombie (`Zombie.cs`, `maxHealth=100`, VERIFIED), so an instakill would change nothing. This flag can be ignored/removed later; it has nothing to do with the new bonus. |
| **Bomb** | Deals no damage to allied units (Human/Soldier/Sniper) caught in the blast radius | CONFIRMED by Vlad (2026-08-29) as an intentional mechanic, NOT a bug: `Bomb.cs` (~120-148) **destroys** (`Destroy(human.gameObject)` for `Human`, `Destroy(target)` for `Soldier`/`Sniper` tags) anything in the blast radius — the bomb is the only card that can accidentally kill your own rescued people or your own unit if placed carelessly; that risk/reward is part of the card's tactics (in the spirit of the original Atom Zombie Smasher), not a bug (see `CLAUDE.md`, "Intentional risk mechanics" section). The level-5 bonus removes this risk as an upgrade reward, not a bug fix. Implementation is unchanged: wrap the three branches (`Human`/`Soldier`/`Sniper`) in a flag check, and skip the `Destroy()` call instead of calling it when the bonus is active. |
| **Helicopter** | While loading, a machine gun appears nearby and shoots at close zombies | A new mechanic, but an almost identical pattern already exists in `CombatHelicopter.cs`: the private method `SniperShoot()` (~113-121) shoots zombies in radius during the flight/loading phase — reuse/scale down that pattern instead of writing it from scratch. `HelicopterController.cs` (on the Helicopter unit itself) doesn't have this method — it needs to be ported over from there. "Unlimited capacity" was initially proposed and rejected by Vlad as overpowered/odd, replaced with this. |
| **Bait** | Zombies pulled in by the bait slow down on their way to it | VERIFIED: a new mechanic — `Bait.cs` currently has nothing resembling a slowdown (just an attraction radius via NavMesh sampling and a visual ring). "Attracts from the whole map" was initially proposed and rejected as overpowered, replaced with this (a local, controlled effect). |
| **Soldier** | Each soldier fires at 2 targets at once instead of 1 | VERIFIED: a new mechanic — `Soldier.cs` currently finds and fires at a single target (`FindTarget()` → `DoShot(target)`). Confirmed by Vlad, not elaborated on/contested further. |
| **Car** | Increased passenger capacity (a bigger jump than the usual per-level growth) | VERIFIED: a plain stat bump, deliberately not a new mechanic — `CarController.cs` reads `maxCapacity` from `CardData` (`StatType.Capacity`), there's no logic beyond that. Vlad deliberately deferred something more interesting for Car for later ("we'll think of something later"). "Unlimited capacity" (mirroring Helicopter) was initially proposed — self-withdrawn as the same overpowered pattern Vlad had just rejected for Helicopter. |
| **CombatHelicopter** | Finishes off (instakill) zombies below 20% HP | VERIFIED and NOT a no-op unlike Sniper: `CombatHelicopterCardData.asset` — `baseValue: 15`, `valuePerLevel: 5` → at level 5 = `15 + 5*(5-1) = 35` damage (`CardStat.GetFloatValue` formula, `Assets/Scripts/Data/CardData.cs`), which is less than a zombie's 100 HP (`Zombie.cs`) — finishing off below 20% genuinely saves a nearby shot on a kill, without trivializing the card. |

## Technical dependency — UPDATED 2026-08-29

Initially (2026-08-23) it was assumed all 8 bonuses depended on a persistent modifier/flag store — see `Docs/mechanics/persistent-modifier-store.md`. Checking the code of all 8 controllers (`Docs/CARD_SYSTEM_DESIGN.md` §5) showed this isn't the case: every controller already reads the card's current level directly from `PlayerProfile.Instance.ownedCardsProgress` at start. The level-5 bonus is an independent `if (currentLevel >= 5)` check next to the existing code, with no new infrastructure. Ready-to-execute briefs: `Docs/CLAUDE_CODE_TASK_Cards_Bonuses_A.md` (Barricade/Sniper/Bomb/Car) and `_B.md` (CombatHelicopter/Soldier/Bait/Helicopter).

The persistent store (`persistent-modifier-store.md`) is still needed ONLY for the future Talent Tree — not for this mechanic.

## Still not done (as of 2026-08-29, updated)

1. Implementing all 8 bonuses in the card controller code (per the table above) — the briefs are ready (`CLAUDE_CODE_TASK_Cards_Bonuses_A.md`/`_B.md`), execution has NOT started. There is no longer any prerequisite/blocker — this can be handed to Claude Code as-is.
2. Persistent modifier store — not needed for this mechanic (see above). Remains a separate task, tied to the Talent Tree.
