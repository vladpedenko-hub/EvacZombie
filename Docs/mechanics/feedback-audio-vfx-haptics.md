# Feedback: sound, vibration, effects, settings

Status: implemented as **placeholders** (04.10.2026). All the logic is ready, assets get swapped in without touching code.
Code: `Assets/Scripts/Feedback/`.

## How it's wired up (1 minute)

Game code calls **one line**:

```csharp
Feedback.Play(FeedbackEvent.ZombieDeath, position);   // with a position — spawns particles, off-screen events are culled
Feedback.Play(FeedbackEvent.UiToggle);                // no position — UI / global moments
```

The `Feedback.table` table in `Feedback.cs` is the **single place** where an event is assigned sound + vibration + particles + camera shake. Want to change "how it feels"? Edit one line there.

| File | Role |
|---|---|
| `Feedback.cs` | Facade + event table (tuning lives here) |
| `AudioManager.cs` | Source pool, frequency limits, per-scene music. Creates itself before the first scene |
| `PlaceholderSfx.cs` | Synthesizes placeholders in code if there's no real file |
| `Haptics.cs` | Android: JNI VibrationEffect; iOS: only strong events (see limitations) |
| `VfxManager.cs` | Pool of procedural particles (the `specs` table is where tuning lives) |
| `CameraShake.cs` | Camera shake only during render time (doesn't conflict with `CameraController`) |
| `UiClickFeedback.cs` | Automatic click sound on **any** Button/Toggle across every scene |
| `GameSettings.cs` | Sound / Music / Vibration / 60 FPS, PlayerPrefs |
| `SettingsPopup.cs`, `SettingsButtonInjector.cs`, `RuntimeUiKit.cs` | Settings window and gear icon in MainMenu (built in code, scenes untouched) |
| `GameLinks.cs` | Privacy / Terms URLs, support e-mail — **fill in before submission** |

All managers are created via `[RuntimeInitializeOnLoadMethod]` — nothing needs adding to any scene.

## Where it's hooked in (hooks in existing code)

Everywhere, it's one added line — no logic was changed.

| Moment | File | Event |
|---|---|---|
| Pick up a card / drop it in the play area with a rejection / cooldown finished | `CardUI` | `CardPickUp` / `CardPlaceInvalid` / `CardReady` |
| Card successfully played | `InputManager.EndDragging` | `CardPlaceOk` (+ particle ring) |
| Wave warning shown | `LevelManager.PrepareAndWarnWave` | `WaveWarning` |
| Night falls | `UIManager.ShowNightPopup` | `NightStart` (scream — existing AudioSource, now respects the Sound setting) |
| Zombie: hit / death / infected a human or scientist | `Zombie` | `ZombieHit` / `ZombieDeath` / `ZombieInfect` |
| Sniper / soldier / combat helicopter fires | `Sniper`, `Soldier`, `CombatHelicopter` | `SniperShot` / `SoldierShot` / `HeliGun` |
| Bomb: explosion / own-side casualty | `Bomb.Explode` | `BombExplosion` (+shake, heavy vibration) / `CivilianLost` |
| Barricade: hit / destroyed | `Barricade` | `BarricadeHit` / `BarricadeBreak` |
| Evac helicopter: landing / takeoff | `HelicopterController` | `HeliArrive` / `HeliTakeoff` |
| Car drove off | `CarController.Launch` | `CarArrive` |
| Civilians evacuated / +N reaches the counter / goal reached | `UIManager` | `RescueDeparted` / `RescueTick` / `GoalReached` (once per level) |
| Ultimate ready / activated | `AbilityButtonUI` | `AbilityReady` (replaced a bare `Handheld.Vibrate()`) / `AbilityActivate` |
| Boss enters rage | `ZombieBoss.RageLoop` | `BossRage` (+shake) |
| Win / perfect win / lose | `UIManager.ShowResultPopup` / `ShowLosePopup` | `Win` / `PerfectClear` / `Lose` |
| Stars on the result screen, reward card | `ResultPopupUI` | `UiStar` (per star) / `UiReward` |
| Level start / out of energy | `LevelMissionPopupUI` | `UiLevelStart` / `UiError` |
| Energy purchase / ad reward | `EnergyStorePopupUI` | `UiReward` |
| Equip / unequip / upgrade a card | `CardPopupManager`, `CardInfoPopup` | `UiEquip` / `UiUpgrade` |
| Region reward / region transition | `MapController` | `UiReward` |
| Any button click | `UiClickFeedback` (auto) | `UiClick` / `UiBack` (name contains close/back/cancel/nothanks) |

Anti-spam protection (zombie swarm): a minimum interval per sound (`AudioManager.minInterval`), a haptic cooldown per event (`hapticCooldown`), a global 50ms haptic limit, a cap of 60 simultaneous particles, and events fully off-screen are dropped entirely.

## How to swap a placeholder for a real sound

Drop a file **named after the enum value** into `Assets/Resources/Audio/Sfx/`, e.g. `Assets/Resources/Audio/Sfx/SniperShot.ogg` (wav/mp3/ogg). Nothing else needed.
Music: `Assets/Resources/Audio/Music/menu.ogg` and `gameplay.ogg` (loops on its own; a synthesized drone plays right now — turn it off with `UsePlaceholderMusic = false` in `AudioManager`).

Import recommendations (mobile): SFX — mono, Load Type "Decompress On Load", Compression "Vorbis" ~q50 (short, <1s clips — "ADPCM"); music — "Streaming".
The project already has the CC0 pack `ExternalAssets/kenney_ui-audio` — fits the UI sounds (click/switch/rollover) with barely any searching.

List of file names:

```
UiClick
UiBack
UiPopupOpen
UiToggle
UiError
UiStar
UiReward
UiLevelStart
UiUpgrade
UiEquip
CardPickUp
CardPlaceOk
CardPlaceInvalid
CardReady
WaveWarning
ZombieHit
ZombieDeath
ZombieInfect
CivilianLost
SniperShot
SoldierShot
HeliGun
BombExplosion
BarricadeHit
BarricadeBreak
HeliArrive
HeliTakeoff
CarArrive
RescueDeparted
RescueTick
GoalReached
AbilityReady
AbilityActivate
BossRage
SignalFlare
Win
PerfectClear
Lose
```

## How to add a new event

1. A value in `FeedbackEvent` (and, if a new sound is needed, in `SfxId`; particles go in `VfxId` + a line in `VfxManager.specs`).
2. A line in `Feedback.table`.
3. `Feedback.Play(...)` at the right spot.

## Settings (screen)

A gear icon in MainMenu's top-right corner (its own Canvas, sorting 4000, inside the safe area; position is the `Offset` constant in `SettingsButtonInjector`). Window: **Sound**, **Music**, **Vibration** (hidden on devices with no vibration motor), **Smooth 60 FPS** (off = 30 FPS, saves battery), **Privacy Policy**, **Terms of Service**, **Contact Support**, version.

Open it from anywhere: `SettingsPopup.Show()` — useful for a pause in Gameplay.

### What else settings usually need (not done yet — add as features land)

| Item | When it's needed |
|---|---|
| Privacy choices / Consent (reopen the consent dialog) | As soon as we add ads / analytics (GDPR: withdrawing consent must be as easy as giving it) |
| Language | Once localization lands |
| Notifications | Once we add "energy restored" pushes |
| Restore Purchases | As soon as IAP lands (App Store requirement) |
| Reset progress / Delete my data | Good to have before launch; mandatory if accounts ever appear (Google Play requires in-app account deletion) |
| Credits / Licenses | Synty, Kenney (CC0), DOTween — before launch |
| Rate us | After soft-launch |
| Cloud save / account | If we decide to |

## Privacy / GDPR — do we need a link

- **A Privacy Policy URL is always needed**: Google Play (store listing field + Data Safety form) and the App Store (URL + privacy labels) require one for any app, even if we collect nothing. An in-app link is mandatory for Google Play if the app handles personal data, and is standard practice otherwise.
- A separate "GDPR link" isn't needed — a policy with a GDPR section (what data, why, how to delete it, contacts) is enough.
- **A consent window isn't needed today**: no monetization or analytics yet, `Watch Ad` is a placeholder. As soon as we add ads/analytics: EEA/UK need a certified CMP (Google UMP for AdMob/MAX), iOS needs an ATT prompt, and a "Privacy choices" entry in settings.
- Not legal advice — review the policy wording before submission.

**Blocker before submission:** fill in `GameLinks.PrivacyPolicyUrl` (currently empty — the button logs a warning and plays an error sound).

## Known limitations / TODO

- **iOS haptics**: Unity has no light-tap API without a native plugin — on iOS only Heavy/Success/Failure vibrate, via `Handheld.Vibrate()` (a crude buzz). For proper Taptic feedback: Nice Vibrations / Lofelt, or a custom native plugin (replace the iOS branch in `Haptics.Play`).
- **Android**: the `VIBRATE` permission is added by Unity as long as `Handheld.Vibrate()` is in the code (kept as a fallback in `Haptics`). If it's removed, add the permission manually.
- **Settings UI is built in code** (plain rectangles, default TMP font) — once there's art, move it into a prefab while keeping the `SettingsPopup.Show()` API.
- **No settings entry point in Gameplay** — only the existing `MenuButton`. Needs a pause button → `SettingsPopup.Show()` + `TimeManager.PauseTime()/ResumeTime()` (account for `InputManager.IsPaused`).
- Audio doesn't duck when overlapping, and there are no looping sounds yet (helicopter rotor) — placeholders are one-shot.
- Camera shake isn't gated by a setting (could add a "Reduce motion" option).
- Only sounds routed through `Feedback`/`AudioManager` + the two existing AudioSources (`nightSound`, `Bait`) are muted by settings. New AudioSources in scenes/prefabs must check `AudioManager.SfxAllowed`.
- The code hasn't been run in the Unity Editor (no Unity in the build environment): every file's syntax was checked, logic was reviewed manually. First run → open MainMenu, press the gear icon, then play a level.
