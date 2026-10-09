# EvacZombie — Refactor Audit & Plan

*Phase 0 (audit) + Phase 1 (plan). No code changed to produce this document — every claim below was checked against the actual code on `main` (2026, commit before this branch), not against the docs. Where a doc turned out stale, that's called out explicitly.*

## How to read this

- File:line citations are real, checked locations, not estimates.
- "LOC" = `wc -l` on the file(s) at audit time.
- Priority: **P0** blocks the performance targets or is actively misleading (stale doc, wrong assumption) · **P1** real but contained · **P2** cosmetic/optional.

---

## 0. Corrections to the task brief itself

Two assumptions in the task brief don't hold, checked directly:

- **"CheatManager not in build scenes"** — false. `Assets/Scripts/CheatManager.cs` (430 lines) *is* attached to a `Cheats` GameObject in both `MainMenu.unity` and `Gameplay.unity` (verified via the script's GUID, not a text search — Unity scenes reference scripts by GUID, so grepping for the class name finds nothing). It is correctly **not dead code**: its entire body is wrapped in `#if UNITY_EDITOR || DEBUG` (`CheatManager.cs:14,429`), so it compiles to an empty component in release builds. The mechanism that keeps cheats out of production is a compile guard, not scene exclusion — same goal, different (already-correct) mechanism.
- **CLAUDE.md's stale "known bugs" entry and the hero-ability doc** — confirmed stale exactly as the brief said. Also found: `Docs/mechanics/persistent-modifier-store.md` frames "where do Talent Tree modifiers live" as an **open, unresolved** architecture question — it is not. See §5.

---

## 1. System map

Grouped by responsibility. 113 `.cs` files, ~15,160 lines total under `Assets/Scripts/` (+1 misplaced at `Assets/TimeManager.cs`, see §4).

| System | Key files (LOC) | Responsibility | Depends on | Called by |
|---|---|---|---|---|
| **Core game loop** | `GameManager.cs`(311), `LevelManager.cs`(451), `TimeManager.cs`(64, at `Assets/` root) | Win/lose state machine, wave timeline, rescued/required counters, slow-mo | `UIManager`, `PlayerProfile`, `MetaRuntime`, `AbilityManager`, `XPManager`, `RunSessionData` (frozen) | `InputManager`, `Human`/`Scientist`/`Zombie`, `TutorialManager`, `CheatManager`, UI popups |
| **Input / card placement** | `InputManager.cs`(416) | Drag-to-place cards, raycast validation, card-specific spawn switch | `TimeManager`, `GameManager`, `TutorialManager`, all 8 card prefabs | `CardUI` (drag events) |
| **Civilians** | `Human.cs`(314), `Scientist.cs`(313) | Wander/flee/panic/rescue AI via NavMeshAgent | `Zombie.AllZombies` (per-frame scan), `AbilityManager`, `ChaosSettings` | `Zombie` (infect), card controllers (rescue) |
| **Zombies** | `Zombie.cs`(790), `ZombieBoss.cs`(183), `ZombiePool.cs`(95) | Target-seek/attack/infect AI, pooling | `Human`/`Scientist`/`Bait`/`Barricade` static lists, `RunSessionData` (frozen) | `LevelManager` (spawns via pool), card controllers (damage) |
| **Cards / units** | `Assets/Scripts/Bonus/*.cs`: `Sniper`(386), `HelicopterController`(384), `CarController`(322), `Soldier`(315), `Barricade`(245), `Bait`(244), `Bomb`(218), `CombatHelicopter`(164). Plus `CardManager.cs`(47), `CardData.cs`(92), `CardProgress.cs`(11) | Per-card gameplay behavior, each independently finds targets and fires | `Zombie`/`Human`/`Scientist` static lists, `PlayerProfile` (level), `RunSessionData` (frozen modifiers) | `InputManager` (spawn) |
| **Progression / save** | `PlayerProfile.cs`(449) | Currency (People/Scientists via `MetaRuntime.Currency`), energy, world-map position, owned cards, deck, level stars, region rewards — **the save file** | PlayerPrefs + JsonUtility | Nearly everything meta-related |
| **Meta currency bridge** | `Meta/Currency/*.cs`: `CurrencyService`(64), `CurrencyTypes.cs`(15), `PlayerProfileCurrencyStore.cs`(16) | Thin typed wrapper so currency reads/writes go through one API instead of touching `PlayerProfile.totalCurrency` directly | `PlayerProfile` (storage) | Card upgrade UI, `MetaService`, `CheatManager` |
| **Meta city/talent-tree layer** | `Meta/MetaService.cs`(314), `Meta/Base/BaseView.cs`(278)+`BaseHouseView.cs`(168), `Meta/City/MetaBuildingPanel.cs`(138), `Meta/Research/MetaSkillTreeScreen.cs`(307), `Meta/Dock/SideDock.cs`(174)+`SideDockInjector.cs`(36), `Meta/Tutorial/MetaTutorialDirector.cs`(184), `Meta/Modifiers/MetaModifiers.cs`(51), `Meta/Save/*.cs`(85), `Meta/MetaRuntime.cs`(111), `Meta/UI/MetaUI.cs`(83), `Meta/Data/*.cs`(~250 across 10 small SO classes) | **A whole second progression system**: restorable "houses", a researchable skill tree (flat/percent stat modifiers applied globally to cards), its own save blob, its own tutorial chain. Entirely code-built UI (no scene prefabs — see §3 positive patterns) | `MetaRuntime` (composition point), `PlayerProfile` only for currency | `BaseView` built once from `MetaPanel` in MainMenu; `CheatManager` for debug |
| **Roguelite (frozen)** | `Roguelite/RunSessionData.cs`(127), `RunUpgradeDefinition.cs`(245), `UpgradeManager.cs`(75), `XPManager.cs`(41), `UI/LevelUpScreen.cs`(284) | In-run XP/level-up/temporary-modifier system from an earlier design direction | `GameManager`, `AbilityManager` (see §3) | Would be called by combat if re-enabled |
| **Hero ability** | `AbilityManager.cs`(151), `UI/AbilityButtonUI.cs`(165) | Charge-on-rescue → temporary civilian speed boost. **Fully implemented and live**, despite `feature-spec-hero-ability.md` being written as if it weren't | `Human`/`Scientist` (speed read), `LevelUpScreen.IsShowing` (see §3) | `GameManager.AddRescuedFromTransport` |
| **Energy (two unrelated systems, same name)** | `EnergyManager.cs`(69) — in-battle resource, regen-per-second, gated nothing (its one consumer is commented out). `PlayerProfile.currentEnergy`/`maxEnergy` — meta "attempts to play a level" gate, regen-per-5-minutes | — | — | See §4 for why this is flagged |
| **Tutorial** | `TutorialManager.cs`(539), `TutorialSequence.cs`(69), `TutorialStep` (inline), `TutorialTarget.cs`(23), `TutorialProgress.cs`(43) | Data-driven step sequencer: masks, finger pointer, dialog, resumability | Target `RectTransform`s registered by any UI | `AbilityManager`, `MetaTutorialDirector`, `InputManager`, level-start hooks |
| **Feedback (audio/vfx/haptics)** | `Feedback/Feedback.cs`(172), `AudioManager.cs`(212), `VfxManager.cs`(245), `Haptics.cs`(126), `CameraShake.cs`(99), `SfxId.cs`(61), `PlaceholderSfx.cs`(152), `GameSettings.cs`(88), `SettingsPopup.cs`(230), `RuntimeUiKit.cs`(158), `UiClickFeedback.cs`(71), `SettingsButtonInjector.cs`(53), `GameLinks.cs`(12) | One table-driven facade (`Feedback.Play(event[, pos])`) fans out to pooled SFX, pooled VFX, haptics, camera shake. Self-bootstrapping, no scene setup | Nothing upstream | Called from ~every gameplay script |
| **World map / regions** | `UI/MapController.cs`(557), `UI/RegionMapVisual.cs`(14), `UI/LevelNodeStarsView.cs`(65), `UI/RegionRewardMilestoneButton.cs`(33), `UI/LevelMissionPopupUI.cs`(156), `Data/RegionConfig.cs`(22), `Data/RegionRewardData.cs`(31) | Region map rendering, node unlock state, trophy-road rewards, mission popup | `PlayerProfile` (heavily — reads/writes map position directly) | `MainMenuSwipeController` |
| **Deck / card UI** | `UI/CardUI.cs`(158, in-battle hand), `UI/MetaCardUI.cs`(94, collection tile), `UI/DeckMenuManager.cs`(89), `CardInfoPopup.cs`(167), `CardPopupManager.cs`(178), `Data/CardVisuals.cs`(16) | Hand display, collection grid, upgrade popups | `PlayerProfile.ownedCardsProgress`/`currentDeck` directly | `CardManager`, `MainMenuSwipeController` |
| **Result/lose popups** | `UIManager.cs`(201), `UI/ResultPopupUI.cs`(268), `UI/FlyingText.cs`(34) | Win/lose screen, flying-rescue-counter animation | `GameManager` fields read directly, calls back into `GameManager` (see §2) | `GameManager.EndLevel`/`CheckWinLoseCondition` |
| **Camera / misc world** | `CameraController.cs`(38), `CameraRaycaster.cs`(27), `CameraSwitcher.cs`(53), `BuildingFader.cs`(43), `RoadZone.cs`(42), `SpawnPointMarker.cs`(27), `SpawnIndicator.cs`(15), `SirenEffect.cs`(35), `Billboard.cs`(6), `ChaosSettings.cs`(33, SO), `IDamageable.cs`(3) | Small, self-contained, one-shot responsibilities | Mostly none | Scene objects directly |

---

## 2. Dependency problems

### Confirmed circular / bidirectional type references

| Pair | Evidence | Problem |
|---|---|---|
| `GameManager` ↔ `UIManager`/`ResultPopupUI` | `GameManager.cs` calls `UIManager.Instance.ShowResultPopup/ShowLosePopup/UpdateRescuedCount/SpawnFlyingText`; `UIManager.SpawnFlyingText` (`UIManager.cs:112-141`) eventually results in `GameManager.Instance.OnFlyingTextReached(amount)` being called back (`GameManager.cs:77`); `ResultPopupUI.Show` reads `GameManager.Instance.totalScientists` directly | Presentation and core-state layers reference each other's concrete types both ways. Neither can be tested or reused without the other. |
| `AbilityManager` ↔ `LevelUpScreen` | `AbilityManager.cs:75` (`TriggerReadyTutorial`) checks `LevelUpScreen.IsShowing`; `LevelUpScreen.cs:120,203` calls `AbilityManager.Instance.OnLevelUpScreenOpened()/Closed()` | An **active MVP system** (hero ability) has a compile-time dependency on a **frozen roguelite UI** (`LevelUpScreen`, `m_IsActive: 0` in the scene) just to read one bool. If `LevelUpScreen.cs` is ever deleted, `AbilityManager.cs` fails to compile even though the feature it's coupled to is switched off. |

### God objects (large + many responsibilities, not necessarily broken)

- `Zombie.cs` (790 lines) — AI brain, combat, infection, pooling lifecycle, hit-flash VFX, crowd-around-bait shake, line-of-sight, barricade interaction all in one class. Works, but any change here risks all of it.
- `MapController.cs` (557 lines) — region rendering, node state, star animation, trophy-road rewards, region-transition sequencing, milestone claiming. No bugs found, but it's the natural next file to grow unmanageably.
- `LevelManager.cs` (451), `TutorialManager.cs` (539), `PlayerProfile.cs` (449) — large but each is cohesive (one clear job done thoroughly), not god objects in the "does unrelated things" sense. Leaving these alone per the "don't fix what isn't broken" rule.

### UI-holds-logic / logic-pokes-UI

- `InputManager.cs:143-149,175-182` — after placing a card, input code directly calls `GameManager.Instance.StartGame()` and `TutorialManager.Instance.NextStep()`. Not circular, but input → game-state → tutorial is three layers reached from one method, inline.
- `GameManager` → `UIManager` call-outs listed above are the clearest instance of "logic pokes UI directly" (`UIManager.Instance.ShowResultPopup(result)` etc. — though note this pattern is already being improved; see §6 risk register).

### Hidden coupling via statics (not FindObjectOfType/tags — this codebase mostly avoids those)

- Every entity type keeps its own `public static List<T> AllX` (`Zombie.AllZombies`, `Human.AllHumans`, `Scientist.AllScientists`, `Bait.AllBaits`, `Barricade.AllBarricades`) populated via `OnEnable`/`OnDisable`. This avoids `FindObjectOfType` (good) but means **every** card controller and **every** civilian independently iterates these lists with its own copy-pasted nearest-neighbor scan — see §3, this is the single biggest performance finding, not just a style issue.
- `FindObjectsOfType`/`FindAnyObjectByType` usage is otherwise rare and all one-shot (not per-frame): `LevelManager.cs:95` (once per level load), `UI/LevelUpScreen.cs:221,235,244` (frozen feature, not called today), `Meta/Base/BaseView.cs:54`, `Meta/Dock/SideDockInjector.cs:25` (both once at Start). **Not a hotspot.**

### Positive patterns worth protecting, not "fixing"

Two subsystems are already built the way the task's target architecture describes, and should be the model for new code rather than being refactored themselves:
- **`Meta/*`**: `MetaService` is plain C# (no `MonoBehaviour`), event-driven (`OnChanged`, `OnBuildingRestored`, `OnNodePurchased`, `OnDialogueRequested`), disciplined `Subscribe()`/`Unsubscribe()` pairs in every consumer (`BaseView`, `SideDock`, `MetaTutorialDirector`), UI built entirely in code via one small `MetaUI` factory (no prefab wiring to go stale).
- **`TutorialManager`** and **`Feedback/*`** (`Feedback.cs` facade + `AudioManager`/`VfxManager`/`Haptics`): both are data/table-driven, self-contained, and already pool what needs pooling (`AudioManager`'s `AudioSource` round-robin pool, `VfxManager`'s per-`VfxId` `Stack<ParticleSystem>` pool, with a single lazily-created shared `Material`).

---

## 3. Performance hotspots

The dominant, cross-cutting finding: **no spatial index or shared targeting service exists — every entity type re-implements its own linear "scan the whole static list, compute `Vector3.Distance` to each, keep the best" loop**, and several run every frame, not on a tick.

| # | File:line | What | Runs | Category |
|---|---|---|---|---|
| 1 | `Human.cs:111-121` | `foreach (Zombie.AllZombies) Vector3.Distance(...)` to find nearest threat | **`Update()`, every frame, per human** | O(H×Z) per frame — biggest single cost candidate |
| 2 | `Scientist.cs:111-121` | Identical to #1 (copy-pasted) | `Update()`, every frame, per scientist | Same |
| 3 | `Soldier.cs:297-315` (`FindTarget`) | `foreach (Zombie.AllZombies) Vector3.Distance` | `Update()`, every frame, per soldier | O(S×Z) per frame |
| 4 | `HelicopterController.cs:165,184,206,211,249,265,290,342,345,356,361` | Eleven separate full scans of `Human.AllHumans`/`Scientist.AllScientists`/`Zombie.AllZombies` across boarding/targeting/capacity checks | Several per `Update()` tick, per helicopter | Compounds with everything else |
| 5 | `CarController.cs:174,192,220,292,294` | Same pattern for boarding/crush logic | Per `Update()`, per car | Same |
| 6 | `Sniper.cs:223-256,262-275,371-385` (`FindTarget`, `CollectCivilians`, `FindTopNTargets`) | Three separate full scans (zombies, humans, scientists) **per aim cycle**, plus a second full zombie scan for the triple-target upgrade | Every ~aimDuration/cooldown seconds, per sniper | Lower frequency than Update-driven ones, still O(n) × instances |
| 7 | `Zombie.cs:240-330` (`Brain`), `359-397`, `399-439`, `477-490` | Own full scans of `Bait.AllBaits`, `Human.AllHumans`+`Scientist.AllScientists`, `Barricade.AllBarricades` | Every 0.15s (coroutine tick, not every frame — already better than #1-5) | O(Z×(baits+humans+scientists+barricades)) every tick |

**Fix direction that respects "zero behavior change":** don't change *when* or *how often* these run, or which target gets picked — just stop duplicating the scan code eight times, so a future spatial-index optimization (if ever approved) has one call site per query type instead of eight. See Phase plan §7, Phase C. A real Big-O fix (grid buckets, reduced tick rate for Human/Scientist) is possible but **changes observable timing** (how fast a human reacts to an approaching zombie) — flagged in the risk register as needing your explicit sign-off, since it sits right on the line of the "game must play identically" rule.

### GC allocations in combat-frequency code (not per-frame, but per-shot/per-hit — still shows up as spikes during peak waves)

| File:line | Allocation | Frequency |
|---|---|---|
| `Zombie.cs:501,520,677` | `new WaitForSeconds(...)` per attack/hit-flash | Every zombie attack, every hit taken |
| `Sniper.cs:190,193,198,204` | `new WaitForSeconds(...)` per aim cycle | Every sniper shot/retarget |
| `Soldier.cs:147` | `new WaitForSeconds(currentFireRate)` | Every shot (note: the file *also* shows the fix, one line up at `:138`, cached as `idleWait` — just not applied to the hot branch) |
| `CombatHelicopter.cs:162`, `CarController.cs:289`, `Barricade.cs:207`, `Bait.cs:207,213` | Same pattern | Per-shot / per-hit / per-ping |
| `Sniper.cs:337` (`ApplyPiercingDamage`) | `Physics.RaycastAll` (array alloc) + `System.Array.Sort` with a lambda comparator (delegate alloc) | Every successful sniper shot |
| `Bomb.cs:109,171,186` | Three separate `Physics.OverlapSphere` calls (array alloc each) in one explosion | Every bomb detonation (×3) |
| `CarController.cs:145` | `Physics.OverlapSphere` **inside `Update()`** | **Every frame while the car is driving — the only confirmed true per-frame physics allocation** |
| `CombatHelicopter.cs:84`, `CarController.cs:235`, `Barricade.cs:128,142` | `Physics.OverlapSphere` | Per pickup-check / per periodic check, not per-frame |
| `Sniper.cs:46`, `Soldier.cs:48`, `HelicopterController.cs:130`, `CombatHelicopter.cs:153`, `Bomb.cs:72`, `Bait.cs:149`, `InputManager.cs:37,46` | `new Material(Shader.Find("Sprites/Default"))` — one unique Material instance + one `Shader.Find` string lookup per placed unit | Once per unit placed (not per-frame, but during a busy wave many units get placed in a short window) |

All of the above have a zero-behavior-change fix: cache the `WaitForSeconds`, cache one shared `Material` (there is already a correct example of this exact pattern in `VfxManager.cs:219-244`'s `EnsureMaterial()`), replace `OverlapSphere`/`RaycastAll` with the `NonAlloc` overloads + a reusable buffer sized once.

### What's *not* a problem (checked, not assumed)

- No `Debug.Log` found in any hot gameplay path (`Zombie.cs`, `Human.cs`, `Scientist.cs`, `Bonus/*.cs`, `GameManager.cs`, `LevelManager.cs` — zero hits). The 16 project-wide `Debug.Log` calls are all in one-shot Meta/Tutorial/Cheat code.
- No `FindObjectOfType` family call inside any `Update`/`FixedUpdate`/coroutine tick — every use is Start/Awake-time, once.
- `AudioManager` and `VfxManager` already pool correctly and already rate-limit (`AudioManager.cs:37-49`, anti-spam `minInterval` table specifically tuned for "zombie swarms").
- Zombies are correctly pooled (`ZombiePool.cs`).

### Pooling candidates not yet pooled

- Humans/Scientists: `Instantiate`d once per level (`LevelManager.cs` `SpawnHumans`/`SpawnScientists`), `Destroy`ed on infection (`Zombie.cs:562,583`). Lower frequency than zombie churn, but the only entity types with zero pooling.
- All 8 card prefabs: `Instantiate`d in `InputManager.ExecuteCardLogic`/`ExecuteSniperLogic`, self-`Destroy()` on expiry (`Sniper.cs:122`, `Soldier.cs:133`, etc.). Lower frequency (player-paced placement), not a peak-wave concern.

---

## 4. Dead code & duplication

| Finding | Evidence | Note |
|---|---|---|
| **`Human.cs` and `Scientist.cs` are ~95% identical** | Side-by-side: same fields, same `Update()` (even the same comments), same `ShouldPanic`/`StartPanicMove`/`SetRescueTarget`×2/`CancelRescue`/`PlayBoardingAnimation`/`SetRandomDest`. ~290 of ~314 lines duplicated verbatim. | Single highest-value, lowest-risk dedup target in the codebase. |
| **Roguelite system is fully wired but inert** | `Roguelite/RunSessionData.cs`, `RunUpgradeDefinition.cs`, `UpgradeManager.cs`, `XPManager.cs`, `UI/LevelUpScreen.cs` — `m_IsActive: 0` confirmed in `Gameplay.unity` for all four GameObjects. Card controllers still read `RunSessionData.Instance?.GetModifier(...)` defensively (always null today). | Not dead in the sense of "delete freely" — it's a frozen *feature*, not abandoned code, and several active systems (`AbilityManager`) couple to its UI (`LevelUpScreen.IsShowing`). Per the task's "do not delete" rule, this goes on the candidate list, not removed. |
| **`EnergyManager`'s one real consumer is commented out** | `CardUI.cs:139` — `// EnergyManager.Instance.TrySpendEnergy(cost); ← убрано`. `EnergyManager.Update()` (`EnergyManager.cs:29-43`) still runs every frame regenerating a value nothing gameplay-relevant reads anymore. | Confirm with you whether the in-battle energy HUD bar is still meant to be visible; if not, this is a full-removal candidate, not just a dead call site. |
| **Two systems named "Energy"** | `EnergyManager` (in-battle, per-second regen, no active consumer) vs `PlayerProfile.currentEnergy` (meta, gates "play a level" from the world map, 5-min regen). CLAUDE.md itself has to add "не путать" (don't confuse them) — a naming collision that already needed a warning label. | Cheap, safe rename candidate later (class rename only, `.meta` GUID stays with the file, so no serialization risk per rule 2) — not urgent. |
| **`Assets/TimeManager.cs`** | The only script outside `Assets/Scripts/` entirely. | Cosmetic. Safe to move with `git mv` (keeps `.meta`/GUID) whenever convenient; not worth its own phase. |
| **Stale doc, not stale code** | `Docs/mechanics/persistent-modifier-store.md` presents "where should Talent Tree modifiers live" as an open question needing your decision. The Talent Tree is already shipped (`MetaSkillTreeScreen` + `MetaModifiers` + `MetaProgressionState`) — see §5. | Recommend updating that doc; not a code task. |

No unused/orphaned scripts were found beyond the roguelite cluster above — everything else under `Assets/Scripts/` is reachable from a scene or referenced by something reachable.

---

## 5. Data layer

Two independent, intentionally-separate persistence paths, both via `PlayerPrefs`:

- **`PlayerProfile.cs`** — one key per field/list (`TotalCurrency`, `CurrentEnergy`, `CardsProgress`, `LevelStarsProgress`, `RegionRewardClaims`, `CurrentDeck`, …), each list wrapped in `SerializationWrapper<T>` + `JsonUtility`. This is the save file for currency, energy, world-map position, owned/deck cards, level stars.
- **`MetaProgressionState`** (`Meta/Save/MetaProgressionState.cs`) — one JSON blob under a single key (`"MetaProgression"`), versioned (`CurrentVersion`, with a `Migrate()` seam already in place), holding building/tree/node unlock state. Explicitly **not** storing currency (comment at `MetaProgressionState.cs:5`): "Currency balances are NOT stored here: they stay in PlayerProfile's save."

Both get written from the same event (`GameManager.EndLevel` calls `PlayerProfile.Instance.SaveProfile()` and, separately, `MetaRuntime.OnLevelCleared(level)` → `MetaService.RecordLevelCleared` → `MetaSaveService.Save`), as two separate `PlayerPrefs.Save()` calls. Low risk in practice (both are fast, synchronous, same frame), but it is two sources of truth that must be kept in sync by convention rather than by one atomic write. **Flagging per rule 6 — not proposing a change**, just documenting that this split exists and is intentional-but-fragile.

### The open question this audit was asked to answer: Talent Tree storage (PlayerProfile vs. a separate store)

**This is already decided and built** — `Docs/mechanics/persistent-modifier-store.md`'s "Option A vs Option B" is stale. What actually shipped is effectively a third option:

- **Option C (what exists today):** a dedicated, non-`MonoBehaviour` save-state class (`MetaProgressionState`) with its own save service (`MetaSaveService`), separate from `PlayerProfile`, read through one service (`MetaService`) and one read-only adapter (`MetaModifiers`) that card stat lookups already call (`CardData.GetCalculatedStat` → `MetaRuntime.Modifiers.GetFlat/GetPercent`).

**Recommendation: keep it, don't touch it.** It avoids both downsides the doc worried about — it doesn't bloat `PlayerProfile` further (Option A's concern), and unlike Option B (`RunSessionData`-style raw singleton polled ad hoc) it's accessed through one typed, event-raising service rather than scattered `Instance?.GetModifier("magic_string")` calls. The only action item is updating the doc to say "done," not "pending."

---

## 6. Risk register

| Risk | Likelihood | Mitigation / how we verify |
|---|---|---|
| Human/Scientist base-class extraction accidentally changes one subtle behavior difference between them | Low (they are byte-for-byte identical in every method body checked) | Diff the two files line-by-line before extracting (already done for this audit); smoke-test panic/flee/rescue for both after. |
| GC-allocation fixes (cached `WaitForSeconds`, shared `Material`, `NonAlloc` physics) regress visuals or timing | Very low — these are pure implementation substitutions with identical outputs | Visual check of laser/tracer/outline lines and explosion radii after each change; confirm hit counts unchanged in a scripted test wave. |
| **Tension between Hard Rule 1 ("game plays identically") and the stated performance targets ("stable 60 FPS during peak waves")** | The dedup-only fixes (Phase C) carry no risk here. A *real* algorithmic fix (spatial grid, reduced AI tick rate) for the O(n×m) scan problem in §3 **would** change exact-frame reaction timing for civilians/turrets, which is observable | Flagged, not scheduled. Needs your explicit go-ahead before anyone touches tick rate or introduces spatial partitioning — see Phase plan, "Phase 5 (gated)". |
| Deduplicating the nearest-target scan across 8 files touches every combat-relevant script at once | Medium — large blast radius even though each change is mechanical | One file per commit, run the existing manual smoke test after each, not after the whole batch. |
| `AbilityManager` ↔ `LevelUpScreen` decoupling breaks the (currently working) ability-vs-levelup interaction | Low if we only invert the dependency (event instead of static read), not if we remove the frozen system | Not in scope for this plan — noted as a candidate, not scheduled, since `LevelUpScreen` is frozen and "isolated and working" per rule 7. |
| Two-save-system (PlayerProfile + MetaProgressionState) desync on crash mid-save | Low, already existing, not introduced by this refactor | No change proposed (rule 6). Documented only. |

---

# Phase 1 — Refactor plan

Ordered by value ÷ risk, safest first. Each phase is one branch of commits, one logical step per commit, stop-and-verify between phases per your instructions.

| Phase | Goal | Files touched | Expected effect | How to verify | Size |
|---|---|---|---|---|---|
| **A** | Extract shared base class for `Human`/`Scientist` (identical Update/panic/rescue/boarding logic) | `Human.cs`, `Scientist.cs`, new base class file | ~290 duplicated lines → one copy; zero behavior change | Manual smoke test: spawn both types, trigger panic (zombie nearby), rescue via car and via helicopter, confirm identical feel | **S** |
| **B** | Cache `WaitForSeconds` objects in combat-frequency coroutines; share one `Material` per shader instead of `new Material(Shader.Find(...))` per unit placed | `Zombie.cs`, `Sniper.cs`, `Soldier.cs`, `CombatHelicopter.cs`, `CarController.cs`, `Barricade.cs`, `Bait.cs`, `Bomb.cs`, `HelicopterController.cs`, `InputManager.cs` | Removes per-shot/per-hit/per-placement GC allocations; no visual change | Visual check of all laser/tracer/outline/circle rendering after; Unity Profiler GC alloc count before/after a scripted wave | **S** |
| **C** | Replace `Physics.OverlapSphere`/`RaycastAll` with `NonAlloc` + reused buffers; fix `CarController.cs:145`'s per-frame `OverlapSphere` specifically | `Bomb.cs`, `CarController.cs`, `Barricade.cs`, `CombatHelicopter.cs`, `Sniper.cs` | Removes the one confirmed true per-frame physics allocation, plus per-explosion/per-shot array allocs | Confirm identical hit sets (same zombies take damage) via a scripted test level; Profiler GC count | **S** |
| **D** | Deduplicate the "scan `AllX`, keep nearest" loop pattern into shared static helper(s), called at the **exact same frequency and call sites** as today (no tick-rate or algorithm change) | `Human.cs`, `Scientist.cs`, `Zombie.cs`, `Sniper.cs`, `Soldier.cs`, `HelicopterController.cs`, `CarController.cs` | One implementation of "nearest zombie to point X" etc. instead of 8; sets up (but does not perform) a future spatial-index swap | Side-by-side target selection on a fixed test scene before/after — same unit gets targeted every time | **M** |
| **E** | Fix the two-sided `EnergyManager` question: confirm with you whether the in-battle energy bar is still used; if not, remove `EnergyManager` and its UI entirely (it's currently dead-but-running) | `EnergyManager.cs`, any HUD binding to `OnEnergyChanged` | Removes one per-frame Update doing pointless work | Confirm the energy HUD widget (if any) still renders correctly, or confirm it's already gone from the UI | **S** (pending your confirmation) |
| **F** | Documentation only: update `Docs/mechanics/persistent-modifier-store.md` and `CLAUDE.md`'s stale sections to reflect that the Talent Tree is shipped, not pending | Docs only | Removes a misleading "open question" | N/A (docs) | **S** |

**Deliberately not scheduled in this plan** (would need your explicit sign-off first, since they risk the "plays identically" rule or touch persistence):
- A real algorithmic fix for the O(n×m) targeting scan (spatial grid, or reduced AI tick rate for `Human`/`Scientist`) — changes observable reaction timing.
- Pooling Humans/Scientists/card prefabs the way `ZombiePool` already does — the main risk is subtle residual-state bugs in the reset path, same category as rule 1 cares about, worth its own careful phase later if you want it.
- Any `PlayerProfile`/`MetaProgressionState` restructuring — rule 6 requires stopping first regardless of how safe it looks.
- Splitting `MapController.cs`/`Zombie.cs` for size alone — both are cohesive and working; not touching per rule 7 unless a future task already has a reason to be in that file.

---

**Stopping here per your instructions — waiting for approval before starting Phase 2 (execution).**
