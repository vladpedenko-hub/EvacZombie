# Core Loop v2 — System Map & Plan

Branch: `feature/core-loop-v2`, started 12.10.2026. Brief: ad-hoc task prompt (not a `Docs/tasks/CLAUDE_CODE_TASK_*.md` file this time — kept here instead). Scope: Levels 1, 3, 5 only (`Level_1_Data`/`Level_3_Data`/`Level_5_Data.useCoreLoopV2 = true`). All other levels keep today's behavior byte-for-byte.

## Toggle strategy

Every new system checks `LevelManager.Instance.currentData.useCoreLoopV2` before doing anything. A new static helper, `CoreLoopV2.IsActive` (`Assets/Scripts/CoreLoopV2/CoreLoopV2.cs`), centralizes that null-safe check so it isn't re-derived at every call site. Legacy branches in the six touched files (`GameManager`, `LevelManager`, `InputManager`, `CardUI`, `AbilityManager`, `HelicopterController`) are left as literal, unmoved code paths guarded by `if (!CoreLoopV2.IsActive) { ...unchanged... }` — never refactored into a shared helper that could subtly change legacy timing/order.

All v2-only systems are new files that self-bootstrap at runtime (same pattern as `Feedback`/`AudioManager`/`CameraZoomPanController`) and build their own UI via `RuntimeUiKit`, parented into `Gameplay.unity/Canvas/SafeAreaContainer` (found at runtime via `GameObject.Find`/scene search, not scene editing). No new file touches a scene or prefab asset directly.

## One tunables asset

`Assets/Scripts/CoreLoopV2/CoreLoopV2Config.cs` (SO) → `Assets/Resources/CoreLoopV2Config.asset`, same `Resources.Load`-cached-static-`.Active` pattern as `CameraReadabilityConfig`. Holds every number in the brief: supply start/cap/income/trickle/wave-cleared-bonus, v2 card cooldown, planning duration + ad grants, wave max duration + breather, last-call duration + last-transport boarding interval + auto-place delay, flare radius + stun duration.

## File-by-file map

| File | What changes |
|---|---|
| `LevelData.cs` | + `useCoreLoopV2`, `startingSupply` (0 = config default), `offerSupplyAd` |
| `CardData.cs` | + `supplyCost`, `isLastTransport` |
| `GameManager.cs` | `Update()` branches v2 (no timer countdown/old night trigger; still runs the early-lose check via `CheckWinLoseCondition(false)`, skips the "map empty → win" legacy branch). `CheckPerfectClear()` branches v2 → own Perfect-Evacuation rule (`EndLevel(true)` immediately, skip legacy auto-evac-survivors coroutine). New `StartV2Night()`, `OnV2LastTransportDeparted()` + its wait-for-pending-flying-text coroutine. `OnFlyingTextReached` credits `SupplyManager` on the humans/scientists split it already computes. |
| `LevelManager.cs` | `StartInitialSpawns()` branches to new `WaveSequenceRoutineV2()` (sequential waves, breather, forced-vs-cleared) instead of the legacy timer-driven `WaveTimelineRoutine()`. New `StopV2WaveSequencer()` for the Perfect-Evacuation early-out. Reuses `PrepareAndWarnWave`/`ExecuteWave`/`ExecuteWaveAction` unchanged — only the *scheduling* around them is new. |
| `InputManager.cs` | The two `StartGame()` auto-trigger call sites in `EndDragging` are skipped when `CoreLoopV2.IsActive` (Planning no longer ends on first placement). |
| `CardUI.cs` | Cost badge, dim+non-draggable when unaffordable, affordability check in `OnBeginDrag`, supply spend on successful placement in `OnEndDrag`, v2 cooldown override (`CoreLoopV2Config.Active.v2CardCooldown` instead of the prefab's `cooldownTime`), lock overlay for `CardCategory.Evacuation` during v2 Planning. |
| `AbilityManager.cs` | `IsReady` getter. `OnAbilityButtonClicked` no-ops under v2 (drag replaces click). New `ActivateFlareAt(Vector3)` (stun + redirect-to-nearest-transport + VFX, instant, resets charge) alongside the untouched legacy `ActivateAbility()`/`DrainRoutine()`. |
| `HelicopterController.cs` | `ConfigureAsLastTransport(float departAtTime)`: unlimited capacity, `panicRadius = 0` (no too-hot abort — reuses the existing panic-radius-driven check, no new branch), fast landing, boarding interval from config, global civilian attract on landing, stays until `departAtTime` regardless of load/timer, then departs and calls `GameManager.OnV2LastTransportDeparted()` + `NightLastCallController.OnTransportDeparted()`. |
| `CardManager.cs` *(not on the protected list — free to touch)* | `SpawnTemporaryCard(CardData, GameObject uiPrefab)` to inject the free Last Transport card without going through `currentDeck`. |
| `Feedback.cs` / `FeedbackEvent` | + `SignalFlare` row (heavy haptic, shake, flash VFX). Brief's global rules pre-authorize new feedback rows. |

## New v2-only files (`Assets/Scripts/CoreLoopV2/`)

- `CoreLoopV2.cs` — the `IsActive` helper + `FindNearestActiveTransport` (queries `HelicopterController`/`CarController`/`CombatHelicopter` via `FindObjectsByType` — reads those types without editing their files, since only `HelicopterController` is on the protected-touch list).
- `CoreLoopV2Config.cs` — tunables SO.
- `SupplyManager.cs` — runtime-created singleton, active only in v2 levels. Start/cap/income/trickle/spend, `OnSupplyChanged` event.
- `SupplyHud.cs` — counter above the card hand, "+N" fly-in, cap-pulse, shake-on-unaffordable.
- `PlanningControllerV2.cs` — GO button, countdown, auto-start, fake-door ad button (Planning) + retry-with-bonus one-shot flag (Lose popup).
- `WaveHudV2.cs` — "WAVE n/total" + breather countdown, cleared/forced banners, next-wave size-hint preview.
- `NightLastCallController.cs` — Last Call countdown UI, spawns/auto-places the Last Transport card, notifies `GameManager`/itself on departure.
- `GoalBarUI.cs` — rescued/total progress bar with ★1/★2 markers, "alive N", ★1-crossed banner. Finds and hides the legacy `ingameRescuedText`/`checkmarkIcon` GameObjects at runtime (by scene search, not by editing `UIManager.cs`, which is not on the protected-touch list) rather than leaving them stacked on top.
- `SignalFlareDragHandler.cs` — added to the ability button GameObject only when a v2 level with `abilityEnabled` loads (added/removed by `AbilityManager.Initialize`), so legacy levels' button never gets a drag-capable component (avoids any click-vs-drag disambiguation risk on levels that must stay byte-identical).

## Risks / stop-condition checks (brief §11)

- No save-format or asset-deletion changes anywhere in this plan.
- No reparenting inside a prefab instance is needed — all new v2 UI is built fresh in code and parented under `SafeAreaContainer` (a plain scene GameObject, not a prefab instance boundary) or into its own overlay `Canvas` (same pattern `SettingsButtonInjector`/`CameraZoomPanController` already use for safe-area-correct runtime UI).
- `BalanceProbe` calls `GameManager.Instance.StartGame()` directly after `LoadLevel()`, bypassing all UI (Planning button, countdown, etc.) entirely — this already works unmodified for v2 since `StartGame()`'s own guard (`if (State != Planning) return`) is untouched. Confirmed empirically during Phase 7 verification before assuming it; flagged here in case it turns out not to hold.

## Tunables table (defaults, `CoreLoopV2Config.asset`)

| Field | Default |
|---|---|
| `startingSupplyDefault` | 6 |
| `supplyCap` | 20 |
| `supplyIncomeHuman` / `supplyIncomeScientist` | 1 / 3 |
| `supplyTrickleAmount` / `supplyTrickleInterval` | 1 / 5s |
| `waveClearedBonus` | 2 |
| `v2CardCooldown` | 0.5s |
| `planningMaxDuration` | 15s |
| `fakeDoorAdSupplyGrant` | 3 |
| `loseRetryAdSupplyGrant` | 5 |
| `waveMaxDuration` | 25s |
| `breatherDuration` | 6s |
| `lastCallDuration` | 18s |
| `lastTransportBoardingInterval` | 0.1s |
| `lastTransportAutoPlaceDelay` | 6s |
| `flareRadius` | 12 |
| `flareStunDuration` | 4s |

Card supply costs (`CardData.supplyCost`): Barricade 1, Bait 1, Car 2, Soldier 2, Sniper 3, Bomb 3, Helicopter 4, CombatHelicopter 6.

## Status

- [ ] Phase 1 — Supply economy
- [ ] Phase 2 — Planning + fake-door ads
- [ ] Phase 3 — Sequential waves + breathers
- [ ] Phase 4 — Night "Last Call" + Perfect Evacuation
- [ ] Phase 5 — Goal bar + stars
- [ ] Phase 6 — Signal Flare
- [ ] Phase 7 — Instrumentation, verification, docs

This section is updated as each phase lands, with the commit hash.
