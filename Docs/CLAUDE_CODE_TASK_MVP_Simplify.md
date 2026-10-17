# TASK: Simplify to MVP v1 (remove the double resource gate + freeze roguelite)
> Hand this file to Claude Code as a prompt. Two independent, low-risk changes — they don't touch the core loop.

---

## TASK 1 — Remove double-gating on card placement

**File:** `Assets/Scripts/UI/CardUI.cs`

Right now `OnBeginDrag` checks BOTH conditions:
```csharp
if (isOnCooldown || EnergyManager.Instance.CurrentEnergy < cost || myCardData == null) return;
```

Remove the energy check, keep only the cooldown:
```csharp
if (isOnCooldown || myCardData == null) return;
```

In `OnEndDrag`, remove the energy-spend call:
```csharp
if (success)
{
    // EnergyManager.Instance.TrySpendEnergy(cost); ← remove this line
    StartCooldown();
}
```

**Don't touch:** leave `EnergyManager.cs` in the project as-is (it just stops being called) — don't delete the file, don't remove the component from the scene. The `cost` field on `CardUI` can stay unused or be hidden from the Inspector later — not critical for the MVP.

**Check:** after the change, cards should be gated only by their own cooldown, with no reference to the shared energy pool.

---

## TASK 2 — Freeze the roguelite in-run progression

**Don't change code.** The system is built null-safe (`RunSessionData.Instance?.…`, `XPManager.Instance?.…`, etc. — see `Docs/CLAUDE_CODE_TASK_Roguelite.md`, "CONSTRAINTS" section, rule 4), so it's enough to remove the objects that drive it from the scene.

**Scene file:** `Assets/Scenes/Gameplay.unity`

Find and **disable (SetActive false) or remove from the scene** the following GameObjects:
1. `RunSessionData`
2. `XPManager`
3. `UpgradeManager`
4. `LevelUpScreen` (the Canvas object with the upgrade-choice UI)

After that, every `Instance` of these classes stays `null`, and all the code in `HelicopterController`, `Sniper`, `Bomb`, `Barricade`, `GameManager`, `Zombie` that polls these singletons via `?.` will safely skip them.

**Don't delete:**
- Scripts under `Assets/Scripts/Roguelite/` and `Assets/Scripts/UI/LevelUpScreen.cs`, `UpgradeCardUI.cs`, `XPBarUI.cs`
- ScriptableObjects under `Assets/Resources/Upgrades/` (14 of them)
- `AbilityManager` / `AbilityButtonUI` (the ultimate ability from rescued civilians) — this is a SEPARATE system, don't touch it, it stays in the game

**Check:** after loading a level, the upgrade-choice screen should NOT appear, and the XP bar should not be displayed (or should be hidden if it depends on the now-nonexistent `RunSessionData`).

---

## Decision context

Both items are part of the 23.08.2026 decision tracked on the Notion Dev Board (cards "REMOVE EnergyManager mana-gate" and "FREEZE Roguelite objects", phase 🚨 Phase 0 — Critical Fix). Full rationale is there and in `Docs/GDD_CORE_LOOP.md`.
