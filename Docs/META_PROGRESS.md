# Meta layer (Base city) — progress

Branch: `feature/meta-city`. Spec: the Meta Layer brief (Base city, stages a–f).

## Status

| Stage | Scope | Status |
|---|---|---|
| a | Data, save, currency, services | Done (commit A + stage-a follow-up) |
| b | BaseView in MainMenu (houses, fog, info sheet, tab badge, top bar) | Done. Verified in Play: tab switch, house tap, sheet above tab bar, badge |
| c | Research overlay in MainMenu, battle hook | Overlay in MainMenu above the tab bar, Android Back closes it. Battle hook done (stage C). Not yet verified in Play (needs a restored Laboratory) |
| d | Post-level routing (pending focus, intro) | Done. Routes on MainMenu entry, verified in Play |
| e | Tutorial and dialogue content, tutorial extension | Done for the Laboratory chain (dialogue + research tour). Intro steps from the first brief not done (see open items) |
| f | Debug panel | Done. Verified in Play (see verification notes below) |

## Done in stage (a)

- `CurrencyService` over PlayerProfile balances. People = `totalCurrency`, Scientists = `totalScientistsCurrency`.
- Per-level grants in `LevelData.clearGrants` (all 10 levels configured), routed through `GameManager.EndLevel`.
- `MetaProgressionState` (versioned): buildings, nodes, trees, characters, `pendingFocusBuildingId`, `metaIntroSeen`.
- `MetaService`: building and node state, restore, purchase, newly-available routing, `HasAttentionItems()`, `ConsumePendingFocus()`, `MarkMetaIntroSeen()`.
- EditMode tests: 12, all passing (run via `unity command run_tests`).

## Decisions

- **Pending focus:** each clear that unlocks a house replaces the previous pending focus. The Base badge still covers any house left unrestored.
- **Attention badge** counts only houses that are affordable right now, not merely unlocked. A house that is unlocked but short on People does not raise the badge.
- **Sources:** `CurrencyService.Add(type, amount, CurrencySourceType.X, sourceId)` is the only entry for grants. ThreeStars and Quest are reserved enum values. Nothing calls them yet.
- **Dialogues** are `TutorialSequence` assets with dialog-only steps, looked up by id in `MetaContentDatabase.sequences`. Completion persists through the existing `TUTORIAL_DONE_<id>` keys.
- **Save:** PlayerPrefs JSON under key `MetaProgression`. New fields are defaulted, so no migration is needed for version 1.

## Done in stage (b)

- `Assets/Prefabs/Meta/BaseView.prefab`: one instance in MainMenu under `MetaPanel`. Scene references set on the instance.
- uGUI city: ScrollRect (clamped, no zoom), viewport stops above the tab bar, houses from data.
- `BaseHouseView`: ruined and restored primitives, locked dim + LOCKED tag, available pulse + !, fog that fades on live restore.
- Info sheet (`MetaBuildingPanel`) sits above the tab bar (bottom inset 224 canvas px).
- Badge ! on the Base tab button when `HasAttentionItems()`.
- Animations pause while the Base tab is hidden (`MainMenuSwipeController.OnTabChanged`).
- Deleted the 3D city scripts and `SafeAreaFitter`. `MetaCity.unity` is still present and is now broken (missing scripts). Delete it after confirmation.
- Building data moved to canvas pixels: `cityPosition`, `cityLayoutSize`, `fogRadius`.

## Done in stages (c) and (d)

- Research overlay parented under BaseView, bottom inset above the tab bar.
- Android Back (Escape): closes research, then the info sheet. Only while the Base tab is visible.
- Routing on MainMenu entry, one frame after the tab controller starts: pending focus switches to Base, centers the house, and consumes the pending value. Intro switches to Base once (`metaIntroSeen`).
- The result popup lives in the Gameplay scene and loads MainMenu on continue, so routing runs after the rewards are done.

## Done in stage (f): debug panel (dev builds only)

- Opened from a DEV button under the settings gear in MainMenu. Compiled out of release builds (`UNITY_EDITOR || DEBUG`).
- Actions: +100 People, +10 Scientists, simulate clear level 2 / 5, set highest cleared = 0, restore Laboratory (normal rules), fast-forward Laboratory restored, reset Laboratory, clear pending focus and intro flag, reset all meta progress, save snapshot, restore snapshot.
- Snapshot stores the meta save plus People and Scientists in PlayerPrefs (`MetaDebugSnapshot`). Take one before testing; restore it afterwards.
- Fixed during testing: the panel stayed subscribed to MetaService.OnChanged after its scene unloaded. A stale handler threw, which stopped the event before the other subscribers ran. The panel now unsubscribes itself.

### Verified in Play (debug panel, real button handlers)

- Restore refused without enough People: the Restore button is disabled with "Need 20 People (5/20)".
- Restore with enough People: People 105 -> 85, Laboratory Restored, Lab_Tree_01 unlocked, fog deactivates after the fade, sheet switches to "Open Research".
- Rewards: UnlockSkillTree applied. No character or dialogue reward exists yet (none authored).
- Research overlay: opened from the sheet, buy enabled at 10 Scientists, first node bought (Scientists 10 -> 9, node state Purchased).
- Research BACK button: closes the overlay and returns to the Base tab.
- Routing: simulate clear 2, reload MainMenu -> Base tab, pending focus consumed, intro flag set.
- Snapshot restore: returned the save to its state from before the test session.
- Not verified: the Android Back key itself. The CLI cannot inject key presses, so this needs a manual check.

## Done in stage (B): side research icon (SideDock)

- `SideDock` (+ `SideDockInjector`, same runtime pattern as the settings gear): right edge, vertically at 36% of the screen, 96 px icon, on its own overlay canvas. Visible on Deck, Map and Base.
- Shortcuts are data: `DockShortcutDefinition` (id, label, visibility, badge, action) listed in `MetaContentDatabase.dockShortcuts`. Current list: `Assets/Data/Meta/Dock/ResearchShortcut.asset`.
- Visibility: after any tree is unlocked. Badge: a node is purchasable right now (`MetaService.HasPurchasableNode`).
- One open path: `BaseView.OpenResearch(tree)`. The house button and the dock both call it. Opening does not change tabs, so Back returns to the tab the player was on.
- Research overlay moved to its own overlay canvas (sort 2000) so it works from any tab. Bottom inset keeps the tab bar visible.
- Dock hides while the research overlay is open (`BaseView.ResearchStateChanged`).
- Events: subscribed in OnEnable, unsubscribed in OnDisable. No Update loop, no tweens.
- Base city: viewport leaves a 124 px right margin for the dock, so houses are never hidden under it.
- Fixed: Base and dock did not refresh on currency changes (People and Scientists do not raise MetaService.OnChanged). Both now also listen to `CurrencyService.OnChanged`.
- Tutorial highlight: `SideDock.IconRect("research")` returns the icon's RectTransform (used by stage E).

### Verified in Play

- Dock hidden before the Laboratory is restored; visible after.
- Badge off with no purchasable node, on when Scientists cover one, off again when Scientists are spent (through `CurrencyService`).
- Tapped from Deck: research opens, dock hides, tab stays Deck. BACK closes, dock returns, tab still Deck.
- Screenshots on Deck, Map and Base at 16:9 (1280x720): no overlap with cards, nodes, or houses. A corner of the island on Map is touched.
- Not verified: 9:16 and 9:21 layouts (the CLI captures 16:9 only), and the Android Back key itself.

## Done in stage (E): Laboratory dialogue and research tour

- Extension to TutorialManager (separate commit, all opt-in): Skip, missing-target timeout, release on app pause / scene change, step resume. Details in that commit message.
- `MetaTutorialDirector` (runtime, MainMenu): starts, resumes, or skips the chain from the save.
  - `meta_lab_freed`: Mad Scientist dialogue, started by the Laboratory's TriggerDialogueReward. Skippable, resumable.
  - `meta_research_tour`: dock icon -> first node -> Buy -> closing line. Skippable, resumable, every target step has an 8 s timeout.
  - Players who already bought a node skip the tour. A tour step that points into research resumes at the dock step if the overlay is closed.
- The dock hides during blocking steps and stays up when the step targets the icon.
- Research overlay and dock moved to sort 90, under the tutorial canvas (100), so tutorial masks cover them.
- Buy step is skipped when the first node is not affordable (the Buy button is disabled, and its tap would never advance).
- Wide dialog text for meta steps (the original text box is 200 px and wraps badly).
- Debug panel: "Reset meta tutorials" action.

### Verified in Play

- Freed dialogue starts on the Laboratory restore.
- Pause mid-dialogue: screen released, dock back, step saved (1). Relaunch resumes at step 1.
- Freed dialogue finished -> tour starts at the dock step.
- Full tour with Scientists: dock -> node -> Buy -> closing. Node bought (Scientists 10 -> 9), tour done.
- Tour with no Scientists: Buy step skipped automatically, tour completes.
- Back during the node step: screen released, dock back, step saved (1). Relaunch resumes at the dock step.
- Missing target (inactive): step advances after 8 s ("Target missing for 8s; skipping step.").
- Pause during the closing step: saved (3). Relaunch resumes at step 3.
- Skip on the freed dialogue: done, tour starts. Skip on the tour: done, screen released.
- Not verified: a real OS backgrounding (simulated with OnApplicationPause), real Android Back, and battle tutorials in a real battle (defaults are guarded by EditMode tests only).

## Open items

- `MetaCity.unity` is still in Build Settings and references deleted scripts. Remove it with the owner's confirmation.
- Swipe across the city pans the city; it does not change tabs. Tab buttons work.
- Research overlay sits under the bottom tab bar; fix in stage (c).
- `TutorialManager` soft-lock risk: if a step's target never appears, the blocker stays up. The fix (skip, timeout, abandon on pause) belongs in stage (e).
- Intro steps from the first brief (radio dialogue after level 1, highlight house 1) are not implemented. Ask before building them.
- No character unlock: Lab rewards are the tree and the dialogue only. The Mad Scientist CharacterDefinition does not exist yet.
- Existing bug, not part of this work: PlayerProfile.Update throws ArgumentOutOfRangeException at PlayerProfile.cs:71 (AddSeconds on the energy timestamp). It appears in the console while energy is below max.
- `CurrencyService.Add` accepts `sourceType` and `sourceId` but does not store them yet. Analytics hookup is later.

## To test in editor (stage a)

- Run EditMode tests: 12 should pass.
- Nothing visible changes yet. The MainMenu and MetaCity scenes behave as before.
