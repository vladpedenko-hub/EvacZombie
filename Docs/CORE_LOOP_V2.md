# Core Loop v2 — System Map & Plan

Branch: `feature/core-loop-v2`, started 12.10.2026. Brief: ad-hoc task prompt (not a `Docs/tasks/CLAUDE_CODE_TASK_*.md` file this time — kept here instead). Scope: Levels 1, 3, 5 only (`Level_1_Data`/`Level_3_Data`/`Level_5_Data.useCoreLoopV2 = true`). All other levels keep today's behavior byte-for-byte.

## Changelog — UX-pass bugfix session (13.10.2026, tag `pre-ux-fix`)

- Fixed WAVE CLEARED firing before any zombie spawned (per-wave spawned-zombie counter gate).
- Fixed the Night tutorial soft-lock on L1 (removed it; raycast-block and missing-target timeout now apply globally).
- Fixed the L1 tutorial finger pointing at the left edge instead of the Helicopter card (tutorial now starts one frame after the hand is spawned); dialog panel is now a small dark pill, not a full-width yellow bar.
- Fixed zombies walking into buildings and getting stuck (removed a bait-crowd `DOShakePosition` tween that fought the NavMeshAgent for `transform.position`).
- HUD cleanup: no more empty panel/placeholder slots, wave indicator on L1, top zone rebuilt to two rows (wave+rescue count / rescue bar with star ticks), all top-zone text ≥36px.

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

## Status (12.10.2026, all code-complete; see "Verification" below for what's actually confirmed)

- [x] Phase 0 — Audit + system map (`1421ece3`)
- [x] Phase 1 — Supply economy (`04b6d129`)
- [x] Phase 2 — Planning + fake-door ads (`2bf1b3c9`)
- [x] Phase 3 — Sequential waves + breathers (`94aac57b`)
- [x] Phase 4 — Night Last Call + Perfect Evacuation (`dcf4cce3`)
- [x] Phase 5 — Goal bar + stars (`13a1ecd4`)
- [x] Phase 6 — Signal Flare (`34d18458`)
- [x] Phase 7 — Instrumentation + docs (this commit). Live Play-mode verification is **blocked**, see below.

## Verification (what's confirmed vs. not)

**Confirmed, via the Unity CLI, after every phase:** clean compile (`unity command console_status` → `compilationFailed: false`, `consoleErrors: 0`). Scene hierarchy (`Canvas/SafeAreaContainer/{TimerText, RescuedCounter, LosePopup, CardsPanel, AbilityButton, ...}`) was read directly from `Gameplay.unity` to anchor every runtime-built panel correctly, rather than guessed. All CardData/LevelData asset edits (`supplyCost`, `useCoreLoopV2`, `offerSupplyAd`, the new `LastTransportCardData` clone, `CoreLoopV2Config.lastTransportCard` wiring) were applied and saved via `unity command eval`/`eval_file`, then read back to confirm.

**Not confirmed — blocked, not skipped:** Play Mode in this Editor session will not advance past frame 1 (`Time.frameCount` stuck at `1`, `Time.time` stuck at `0`) regardless of `set_autotick`, `editor_focus`, or a fresh Play Mode entry from `MainMenu.unity`. This matches CLAUDE.md's own caveat that this CLI/Editor link is beta and can misbehave, with the instruction to report instability rather than route around it silently — flagging it here rather than claiming untested things work. Nothing in this branch could be exercised live: GO/countdown, affordability dimming, wave sequencing timing, the breather preview, Night/Last Call, auto-place, Signal Flare drag, the goal bar, or `PlaytestLog` output. **Needs a real Play Mode session on your end** (this may just need the Editor window actually focused/active on your machine, or a fresh Editor restart) before trusting any of this beyond "it compiles and the logic reads correctly."

## Known limitations / things to eyeball once Play Mode works

- **HUD crowding on L1/L3/L5's v2 levels.** `WaveHudV2` and `GoalBarUI` both stack under `TimerText` near the top of the screen (registered into `LevelManager.topReservedUI` at runtime so the camera still reserves space for them), and `PlanningControllerV2`'s GO panel sits center-screen. None of this was seen rendered — check for overlap/crowding on a real device aspect ratio, not just logic correctness.
- **`PlanningControllerV2`'s injected Lose-popup ad button** is added as a plain child of the existing `LosePopup` GameObject at a guessed anchor band (bottom 4-14%) rather than a position read from the popup's actual existing button layout (`RestartButton`/`MenuButton`) — check it doesn't overlap them.
- **`BalanceProbe` was reasoned about, not run.** It calls `GameManager.Instance.StartGame()` directly after `LoadLevel()`, bypassing `PlanningControllerV2` entirely (`StartGame()`'s own `State != Planning` guard is unchanged), so it should "auto-press GO" for free without any probe-side code change. This could not be confirmed live.
- **Last Transport visual distinction** is just a Legendary (gold) card border via the existing rarity system — no new art, per the brief's global rules.

## No manual Unity Editor steps are needed for this pass

Every new v2 system self-bootstraps (same pattern as `CameraZoomPanController`/`SettingsButtonInjector`) and builds its own UI in code at runtime, parented into `SafeAreaContainer` (found by name) or alongside `TimerText`/`RescuedCounter` (also found by name) — nothing was wired by hand in the Editor, and nothing needs to be.

---

## HUD rebuild + progressive onboarding (12.10.2026 follow-up)

A second pass, same branch. Driven by a playtest that found the v2 HUD chaotic (ad-hoc elements each claiming their own spot) and L1 introducing Planning/Supply/waves all at once before the player ever flies a helicopter. Two changes: (1) a strict HUD zone system, (2) v2 enabled on **all 10 levels** with per-level feature gating so the UI/tutorial introduces one mechanic at a time instead of dumping the whole system on L1.

### Final per-level feature matrix

| Level | `v2PlanningEnabled` | `v2ShowSupplyHud` | `v2ShowWaveIndicator` | `v2ShowAdOffer` | `startingSupply` | `abilityEnabled` | Tutorial |
|---|---|---|---|---|---|---|---|
| L1 | false | false | false | false | 20 (= cap, never blocks) | false | `onStartTutorial`: drag Helicopter. `onNightTutorial`: drag Last Transport. |
| L2 | true | true | true | false | 0 (default 6) | false | `onStartTutorial`: spawn point -> 2 Barricades -> GO. |
| L3 | true | true | true | false | 0 (default 6) | true | Ability-ready-triggered: drag Signal Flare, completes on use. |
| L4-L10 | true | true | true | true | 0 (default 6) | true | none |

Everywhere: the rescue progress bar + stars, Night/Last Call, and `useCoreLoopV2=true` are on. Numbers (costs, waves, durations) were **not** rebalanced — the only deliberate value changes are L1's `startingSupply=20` (so a hidden, logic-only Supply never blocks a level that doesn't show it) and L2 keeping the **default** `startingSupply` (6, from `CoreLoopV2Config`) rather than a level-specific override, since two Barricades (cost 1 each = 2 Supply) fit comfortably inside it with margin for the forced first wave.

### Zone layout (`Docs/CORE_LOOP_V2.md` HUD rebuild, section 3 of the brief)

- **TOP** (`TopHudV2`, replaces the old `WaveHudV2`+`GoalBarUI`): wave pills + "WAVE n/total" on the left (or, during Night, a big "LAST CALL m:ss" countdown that turns red in the last 5s — shows regardless of `v2ShowWaveIndicator`); rescued/total + a labeled-star progress bar + "ALIVE n" in the center; the pre-existing settings gear is left alone on the right. A thin "PLANNING — set up defenses" strip appears underneath while Planning is active.
- **CENTER** (`CenterBannerV2`): a single queued transient banner (WAVE CLEARED [+2 if Supply HUD shown] / NEXT WAVE INCOMING / GOAL REACHED ★1 / NIGHT FALLS / PERFECT EVACUATION), each on screen ≤2s, never overlapping, non-interactive.
- **BOTTOM** (`BottomZoneV2` + `SupplyHud` + `PlanningControllerV2` + the repositioned ability button + `CardManager`'s 5-slot hand): a control row above the hand holds the Supply pill (left), the ad-offer + GO buttons (center-right, GO's countdown is now inside its own label: "GO · 12"), and the pre-existing ability button (repositioned into the row's right slot rather than rebuilt). The hand itself always renders 5 equal-width slots (dimmed placeholders for unowned cards).

### Tutorial trigger vocabulary (`TutorialCompletionTrigger`, `TutorialSequence.cs`/`TutorialManager.cs`)

`CardPlaced` (+ `requiredCardType`/`requiredCount`/`requireLastTransportCard`), `WaveStarted`, `FirstRescueCredited`, `NightStarted`, `UltimateReady`, `UltimateUsed`. `Default` keeps every pre-existing behavior (Button.onClick for Click steps, an external `NextStep()` call for Drag steps, a blocker tap for DialogOnly) — used for the GO step, since GO is a real `Button` with a `TutorialTarget` and the click mechanism already is the generic, data-driven answer for "complete when this specific button is pressed." Only L1-L3 author steps; `WaveStarted`/`FirstRescueCredited`/`UltimateReady` aren't exercised by any authored step but exist as available vocabulary, per the brief's "add the missing triggers generically" framing.

### Known simplifications (flagged, not hidden)

- **L2 step 1** ("highlight the wave-1 spawn indicator") is authored as a plain `DialogOnly` tap-to-continue step, without an actual mask/finger pointing at the spawn indicator. Indicators are anonymous, regenerated-per-wave GameObjects with no stable `TutorialTarget` id today; wiring one up would mean touching `LevelManager`'s indicator-spawning code for a single cosmetic step. The dialog text ("Zombies come here.") still gives the right context.
- **L3's "highlight the largest zombie cluster on screen"** is not implemented as a dynamic world-space highlight — the tutorial system's mask/finger only knows how to point at a registered, static UI `RectTransform`. The step points at the ability button itself instead (the same target the pre-existing ability-ready sequence already used), which still teaches "drag this to use it."
- **The Last Transport card is a 6th hand slot at Night**, not a replacement of one of the 5 regular slots — `CardManager.SpawnTemporaryCard` appends it. "Always 5 equal-width slots" holds everywhere except that one window.
- **The ability button's reposition** only sets `anchorMin/Max`+zeroed offsets (so it exactly fills its new control-row slot) — it was not re-verified against its own internal children's (`BackgroundImage`/`FillImage`/`PulseGlow`) aspect ratio, since that slot's width:height ratio depends on screen aspect and couldn't be rendered to check.

### Verification (unchanged limitation from the first pass)

Play Mode still would not tick frames in this session (`Time.frameCount` stuck even after the intro-camera-shot wait coroutine would have resolved), confirmed again across multiple fresh attempts (including one `editor_play` that advanced exactly one frame in ~8s then fully stalled for the next 30s of polling) — this is the same CLI/Editor instability flagged in the first pass, not a regression from this work. Every change in this pass is confirmed by clean compiles (`consoleErrors: 0` after every edit) and by reading `Gameplay.unity`'s actual hierarchy (`TimerText`, `RescuedCounter`, `LosePopup`, `CardsPanel`, `AbilityButton`'s anchors, `CardsPanel`'s `HorizontalLayoutGroup` settings) and the 10 levels' real wave-list data directly, not by playing any of it. No screenshots could be captured (`capture_game_view` needs a running Game view). **This needs a real Play Mode / device session before trusting layout, tutorial pacing, or feel beyond "it compiles and the data/logic reads correctly."**
