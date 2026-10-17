# Mechanic: persistent modifier/flag store (ARCHIVE — the question was closed differently)

*Status: CLOSED 09.10.2026, but not the way this document designed it. The Talent Tree is implemented and shipped — but not through a separate persistent key-value store as described below (neither Option A nor Option B from the "Where to store it" section was chosen). Instead, Talent Tree modifiers live in `MetaRuntime.Modifiers` (`Assets/Scripts/Meta/Modifiers/MetaModifiers.cs`), apply globally to all cards, and are saved through a layer separate from `PlayerProfile` — `MetaProgressionState` + `MetaSaveService` (`Assets/Scripts/Meta/Save/*`, a single JSON blob under the key `"MetaProgression"`). See `CLAUDE.md` → "Meta layer 'city' + Talent Tree" and `Docs/REFACTOR_AUDIT.md` §1 for the full system map.*

*The document below is kept as an archive of the design rationale (23.08–29.08.2026) — the requirements and the two storage options that were considered, in case the decision history is ever needed. Don't read it as an active spec: the Talent Tree is already implemented, and this file doesn't describe it.*

## Why

**UPDATED 29.08.2026**: originally (23.08.2026) two features landed here — the Talent Tree and card milestone bonuses. Card milestone bonuses are no longer on the list: a code check (`Docs/CARD_SYSTEM_DESIGN.md` §5) showed that every card controller already reads the current level directly from `PlayerProfile.Instance.ownedCardsProgress` — a separate modifier store isn't needed for that, the bonus is implemented as a plain `currentLevel >= 5` check.

The only consumer of this store now is:

1. **Talent Tree** (see `Docs/GDD_CORE_LOOP.md`, Layer 4) — 8-12 nodes, flat % modifiers on `StatType` (Damage, Health, FireRate, Speed, Radius), applied globally to all cards. Not tied to a specific card's level (unlike card bonuses) — which is exactly why it needs a real persistent key-value store instead of reading a field directly.

The project already has a ready-made interface pattern (`RunSessionData`), it's just not persistent.

## Reference: an existing (but NOT persistent) analog

`RunSessionData.cs` (`Assets/Scripts/Roguelite/`) — a per-level singleton, reset via `ResetForNewLevel()`. Public interface:

```
AddModifier(string key, float value)      // additive, a repeated call with the same key sums up
GetModifier(string key, float default=0)  // returns default if the key isn't set
SetFlag(string flagId)
HasFlag(string flagId)
```

Card controllers already read it through a null-safe call: `RunSessionData.Instance?.GetModifier("barricade_reflect_pct")` (example from `Barricade.cs`). Right now `Instance` is null because the `RunSessionData` object is disabled in the scene (see `CLAUDE.md`) — that does NOT get in the way of the new persistent store; it's a read model for a different object.

## What's newly needed

A second source of modifiers/flags with the **same interface** (`GetModifier`/`HasFlag`), but:
- Not tied to the level scene — lives across levels and across game sessions.
- Saved to disk and survives an app restart.

## Where to store it: open architecture question (CLOSED — see the status note at the top of the file; neither option below was chosen)

The project already has exactly one persistent layer — `PlayerProfile.cs` (singleton, `DontDestroyOnLoad`, saved via `PlayerPrefs` + `JsonUtility`, lists like `ownedCardsProgress` go through the `SerializationWrapper<T>` pattern). Two options:

**Option A (recommended)** — modifiers/flags as new fields directly on `PlayerProfile` (`Dictionary<string,float>` + `HashSet<string>`, serialized with the same `SerializationWrapper` pattern as the other lists). Pro: one persistent object in the project, not two. Con: `PlayerProfile.cs` is already 449 lines and keeps growing.

**Option B** — a separate `PersistentModifierStore` singleton (modeled on `RunSessionData`, but saved via `PlayerPrefs`), polled by card controllers alongside `RunSessionData.Instance`. Pro: clean separation of responsibility, easier to find the code. Con: two similar singletons in the project, duplicated save/load plumbing.

This is an architectural decision, not something to dictate unilaterally — needs confirmation before turning it into a `Docs/tasks/CLAUDE_CODE_TASK_*.md` brief for Claude Code.

## Consumers (who will read this store)

- Talent Tree UI (not built yet) — writes modifiers when a node is unlocked, card controllers read them the same way they currently read `RunSessionData`.

*(Card milestone bonuses no longer belong here — see "Why" above. They read the card's level directly from `PlayerProfile`, without this store.)*

## What's NOT part of this task

- The talent tree UI (a separate task, depends on this store).
- Retuning the specific modifier values for milestone bonuses (partly already done, see the table in `card-milestone-bonuses.md`) — the store is only the transport, not the source of balance.
