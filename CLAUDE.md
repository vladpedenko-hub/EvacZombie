# CLAUDE.md — EvacZombie

Mobile (portrait, iOS/Android) zombie-evacuation tactics game, Unity 6000.6.4f1. Solo dev: Vlad (design) + Claude Code (code). Code is the source of truth; if this file disagrees with code, fix this file.
Full history of past passes (refactor, responsive, balance, camera): `Docs/HISTORY.md` — read only when a task touches that area.

## Working rules
- One task per session. Keep reports short (≤15 lines): what changed, commit hashes, what is unverified.
- Verify compilation via the Unity CLI after every script change. Verify behavior in Play Mode. If Play Mode can't be driven (CLI unstable), say so in the FIRST line of the report — never present unplayed code as done.
- Don't read whole large files when a grep will do. No screenshots unless the task asks for them.
- Never: delete assets, change save formats (`PlayerProfile`, `MetaProgressionState`), reactivate frozen roguelite objects, add SDKs — without an explicit request.
- Stop and ask instead of working around: prefab-instance reparenting, save-format changes, unclear root cause after 2 attempts.

## Current focus: Core Loop v2 (branch `feature/core-loop-v2`)
Supply economy, Planning + GO, sequential waves with breathers, time-limited Night "Last Call" + Last Transport, rescue progress bar with stars, Signal Flare ultimate. Gated by `LevelData.useCoreLoopV2` + per-level `v2*` flags. Tunables: `Assets/Resources/CoreLoopV2Config.asset`. Spec/status: `Docs/CORE_LOOP_V2.md`.

## Key systems (where things live)
- Flow: `GameManager` (states Planning/Playing/SuddenDeath/GameOver/Lose, win/lose, stars, `EndLevel`), `LevelManager` (level load, waves, night spawns).
- Input/cards: `InputManager` (drag pipeline, placement rules, slow-mo), `UI/CardUI`, `CardManager` (hand; `IsScreenPointOverCardsPanel` is the only "is this UI" check — no magic screen %).
- Units: `Assets/Scripts/Bonus/*`. Civilians: `EvacueeBase` (Human/Scientist are thin subclasses). Zombies: `Zombie`, `ZombieBoss`, `ZombiePool`.
- Card stats: `CardData` (`baseValue + valuePerLevel*(lvl-1)`), level read from `PlayerProfile.ownedCardsProgress`.
- Meta: `Assets/Scripts/Meta/*` (Base city, Research tree → `MetaRuntime.Modifiers`), own save `MetaProgressionState`. Currency only via `MetaRuntime.Currency`.
- Feedback (SFX/haptics/VFX): `Feedback.Play(FeedbackEvent.X, pos)`; new event = one table row in `Feedback.cs`.
- Tutorial: `TutorialManager` / `TutorialSequence` / `TutorialTarget`.
- Dev tools (editor/debug only): `Dev/BalanceProbe`, `Dev/PlaytestLog`, `CheatManager`.
- Frozen (post-launch): `Scripts/Roguelite/*` (`RunSessionData` etc., inactive in scene; code reads via `?.`), `Docs/PostLaunch/*`, narrative.

## UI rules (portrait, ref 1080x1920)
- Zones: TOP = read-only level state; CENTER = gameplay + transient banners only; BOTTOM = everything touchable.
- Everything under `Gameplay/Canvas/SafeAreaContainer`. New Canvas with CanvasScaler → add `AspectAwareCanvasScaler`.
- New top HUD element → add to `LevelManager.topReservedUI` (camera fit). Bottom UI must be covered by the cards-panel hit-test.
- Can't reparent inside prefab instances — solve in code (see `TutorialManager` safe-area inset).

## Intentional mechanics (not bugs)
- Bomb kills civilians/own Soldier/Sniper in radius (risk mechanic).
- Transports abort loading ("too hot") when a zombie is within panic radius.

## Docs
`Docs/CORE_LOOP_V2.md` (current), `Docs/LEVEL_BALANCE.md` (level numbers), `Docs/CAMERA_READABILITY.md` (camera tunables), `Docs/mechanics/*` (system specs), `Docs/HISTORY.md` (past passes). Backlog lives in Notion (not accessible from Claude Code).

## Tools / git
- Unity CLI: `unity status` must be `ready`; `unity command eval --code '<C#>'`. Ask before installing anything.
- If `.git/*.lock` errors appear: rename the lock file aside (`mv`), don't delete. Vlad merges branches himself via GitHub Desktop; never merge to main.
