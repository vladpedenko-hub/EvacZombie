# Meta layer (Base city) — progress

Branch: `feature/meta-city`. Spec: the Meta Layer brief (Base city, stages a–f).

## Status

| Stage | Scope | Status |
|---|---|---|
| a | Data, save, currency, services | Done (commit A + stage-a follow-up) |
| b | BaseView in MainMenu (houses, fog, info sheet, tab badge, top bar) | Done. Verified in Play: tab switch, house tap, sheet above tab bar, badge |
| c | Research overlay in MainMenu, battle hook | Tree screen exists as a scene-bound screen; port pending. Battle hook done |
| d | Post-level routing (pending focus, intro) | Data done (`ConsumePendingFocus`, `MarkMetaIntroSeen`). Routing pending |
| e | Tutorial and dialogue content, tutorial extension | Not started |
| f | Debug panel | Not started |

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

## Open items

- `MetaCity.unity` is still in Build Settings and references deleted scripts. Remove it with the owner's confirmation.
- Swipe across the city pans the city; it does not change tabs. Tab buttons work.
- Research overlay sits under the bottom tab bar; fix in stage (c).
- `TutorialManager` soft-lock risk: if a step's target never appears, the blocker stays up. The fix (skip, timeout, abandon on pause) belongs in stage (e).
- `CurrencyService.Add` accepts `sourceType` and `sourceId` but does not store them yet. Analytics hookup is later.

## To test in editor (stage a)

- Run EditMode tests: 12 should pass.
- Nothing visible changes yet. The MainMenu and MetaCity scenes behave as before.
