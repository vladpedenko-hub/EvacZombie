# Meta layer (Base city) — progress

Branch: `feature/meta-city`. Spec: the Meta Layer brief (Base city, stages a–f).

## Status

| Stage | Scope | Status |
|---|---|---|
| a | Data, save, currency, services | Done (commit A + stage-a follow-up) |
| b | BaseView in MainMenu (houses, fog, info sheet, tab badge, top bar) | Not started. Blocked: `MainMenu.unity` has uncommitted edits |
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

## Open items

- `MainMenu.unity` and the SDF fallback font asset have uncommitted edits from before this work. Stage (b) waits until they are committed or stashed.
- `MetaCity.unity` is still in Build Settings. It gets removed after stage (b) is verified, with the owner's confirmation.
- `TutorialManager` soft-lock risk: if a step's target never appears, the blocker stays up. The fix (skip, timeout, abandon on pause) belongs in stage (e).
- `CurrencyService.Add` accepts `sourceType` and `sourceId` but does not store them yet. Analytics hookup is later.

## To test in editor (stage a)

- Run EditMode tests: 12 should pass.
- Nothing visible changes yet. The MainMenu and MetaCity scenes behave as before.
