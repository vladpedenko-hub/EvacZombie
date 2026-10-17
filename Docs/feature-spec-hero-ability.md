# Feature Spec: Hero Ability — Civilian Speed Boost

**Status:** ✅ IMPLEMENTED (discovered during the 29.08.2026 audit — the feature was actually built back in May 2026, commits "Ultimate Ability"/"All in English"; this spec was written later and didn't know about the existing implementation). `AbilityManager.cs` (151 lines) and `AbilityButtonUI.cs` (164 lines) exist, are wired into `GameManager.cs`/`Human.cs`/`Scientist.cs`, the GameObject lives in `Assets/Scenes/Gameplay.unity`, and `Assets/Data/Tutorial/Tutorial_AbilityReady.asset` exists. The document below is the historical design spec — cross-check against the actual code, don't treat it as the source of truth for current state.  
**Priority:** N/A — already done.  
**Dependencies:** TutorialManager, GameManager, Human.cs, Scientist.cs

---

## 1. Feature summary

The player builds up a charge by rescuing civilians. When the charge is full, an active button appears on screen. Pressing it temporarily speeds up every civilian on the map.

**Core loop:** Rescue people → build up charge → activate → rescue even more.

---

## 2. Parameters (balance, tweakable)

| Parameter | Value | Description |
|---|---|---|
| `maxCharge` | 5 | Need to rescue 5 civilians for a full charge |
| `chargePerCivilian` | 1 | Each rescued civilian grants 1 charge |
| `abilityDuration` | 8f | Seconds the ability lasts |
| `speedMultiplier` | 2.0f | How much civilians speed up by |
| `tutorialId` | `"Tutorial_Ability"` | ID for PlayerPrefs (shown once) |

---

## 3. Components

### 3.1 `AbilityManager.cs` — new script

Singleton. Manages charge, state, and the speed boost.

**States:**
- `Idle` — charging up
- `Ready` — charge full, waiting for the player to press the button
- `Active` — ability active, draining

**Public methods:**
```
void AddCharge(int civiliansCount)
void ActivateAbility()
float GetSpeedMultiplier()   // 1f in the normal state, speedMultiplier when Active
```

**Events (C# events):**
```
event Action<float, float> OnChargeChanged   // currentCharge, maxCharge
event Action OnAbilityReady
event Action OnAbilityActivated
event Action<float> OnAbilityTick            // remainingNormalized (1→0)
event Action OnAbilityExpired
```

**Charge logic:**
- `AddCharge()` is called from `GameManager.AddRescuedFromTransport()`
- Charge doesn't accumulate while the ability is `Active`
- On transitioning to `Ready` — raises `OnAbilityReady` + triggers the tutorial (once per session)

**Speed boost logic:**
- While state == `Active`: returns `speedMultiplier`
- Otherwise: returns `1f`
- Civilians read this value themselves via `AbilityManager.Instance.GetSpeedMultiplier()`

**Tutorial trigger (fires once per entire game):**
```csharp
if (!PlayerPrefs.HasKey("ABILITY_TUTORIAL_SHOWN"))
{
    PlayerPrefs.SetInt("ABILITY_TUTORIAL_SHOWN", 1);
    TutorialManager.Instance.StartTutorial(abilityReadyTutorialSequence);
}
```
> The `abilityReadyTutorialSequence` field — a reference to a TutorialSequence SO, assigned in the Inspector.

---

### 3.2 `AbilityButtonUI.cs` — new script

Manages the button's visual states. Registers as a TutorialTarget.

**UI object hierarchy (in Canvas):**
```
AbilityButton (Button)
  ├── BackgroundImage         ← static icon/frame
  ├── FillImage (Image)       ← single Image with fillType Vertical
  ├── PulseGlow (Image)       ← semi-transparent glow, hidden by default
  └── AbilityButtonUI (.cs)
      └── TutorialTarget (.cs)  ← targetId = "ability_button"
```

**FillImage — two-state logic via one component:**

| State | `fillOrigin` | `fillAmount` | Direction |
|---|---|---|---|
| Charging | `0` (Bottom) | grows `0 → 1` | bottom to top |
| Active/Draining | `1` (Top) | shrinks `1 → 0` | top to bottom |

Flipping `fillOrigin` on activation gives a visual "flip" without a second Image.

**Button states:**

1. **Charging** (Idle/building up):
   - `Button.interactable = false`
   - `FillImage.fillOrigin = 0` (Bottom)
   - `FillImage.fillAmount` = `currentCharge / maxCharge`
   - Fill color: neutral (gray-white)
   - PulseGlow: `SetActive(false)`

2. **Ready** (charge full, waiting for a press):
   - `Button.interactable = true`
   - `FillImage.fillAmount = 1f`
   - Fill color: accent (yellow / bright green)
   - PulseGlow: `SetActive(true)` + DOTween pulse (alpha 0.3 → 0.8, loop Yoyo, 0.5s)
   - Device vibration: `Handheld.Vibrate()` once on the transition

3. **Active** (ability running, draining):
   - `Button.interactable = false`
   - `FillImage.fillOrigin = 1` (Top)  ← flipped on activation
   - `FillImage.fillAmount` = `remainingNormalized` (1 → 0)
   - Fill color: active (blue / cyan — distinct from ready)
   - PulseGlow: `SetActive(false)`

**Event subscriptions:**
```
AbilityManager.OnChargeChanged   → update fillAmount (Charging state)
AbilityManager.OnAbilityReady    → switch to Ready state
AbilityManager.OnAbilityActivated → switch to Active state, flip fillOrigin
AbilityManager.OnAbilityTick      → update fillAmount (drain)
AbilityManager.OnAbilityExpired   → reset to Charging state, fillAmount = 0
```

**Fill color animation (DOTween):**
```csharp
FillImage.DOColor(readyColor, 0.3f);    // on transitioning to Ready
FillImage.DOColor(activeColor, 0.2f);   // on activation
FillImage.DOColor(chargingColor, 0.4f); // on reset
```

---

### 3.3 Changes to `Human.cs` and `Scientist.cs`

Minimal edits — read the speed multiplier from AbilityManager.

In `Human.Update()`, everywhere `agent.speed` is set:

```csharp
// BEFORE:
agent.speed = runSpeed;

// AFTER:
float boost = AbilityManager.Instance != null ? AbilityManager.Instance.GetSpeedMultiplier() : 1f;
agent.speed = runSpeed * boost;
```

Same for `walkSpeed`:
```csharp
agent.speed = walkSpeed * boost;
```

Same in `Scientist.cs` (if it has similar speed logic).

> **Important:** `GetSpeedMultiplier()` returns 1f when AbilityManager isn't active — null-safe.

---

### 3.4 Changes to `GameManager.cs`

In the `AddRescuedFromTransport()` method, after the existing logic:

```csharp
// Existing code:
XPManager.Instance?.OnCivilianEvacuated(humans, scientists);

// Add this line:
AbilityManager.Instance?.AddCharge(total);
```

---

## 4. Tutorial Step — "Ability Ready"

### TutorialSequence ScriptableObject

Create: `Assets/Data/Tutorials/Tutorial_AbilityReady.asset`

```
tutorialId: "Tutorial_Ability"
steps:
  [0]:
    stepType: DialogAndClick
    targetId: "ability_button"
    useDarkMask: true
    dialogText: "You've rescued enough people! Press the ability — speed up everyone!"
    characterIcon: [character icon, same one as in the Level 1 tutorial]
    dialogPosition: Bottom
```

**How this works technically:**
1. `AbilityManager` calls `TutorialManager.Instance.StartTutorial(abilityReadyTutorialSequence)`
2. `TutorialManager.StartTutorial()` sets `Time.timeScale = 0f`
3. The dialog + finger pointer on the ability button appear
4. The button stays interactable (TutorialTarget "ability_button" = a "hole" in the mask)
5. The player presses the button → both listeners fire:
   - `AbilityManager.ActivateAbility()` (wired up in the Inspector via Button.onClick)
   - `TutorialManager.NextStep()` (added dynamically by TutorialManager)
6. `TutorialManager.FinishTutorial()` → `Time.timeScale = 1f`
7. The ability activates, civilians speed up

> **Order matters:** `FinishTutorial` restores timeScale BEFORE the drain coroutine in AbilityManager starts counting time. That's fine — Unity processes onClick listeners in the order they were added. `ActivateAbility()` runs after `NextStep()` since it was added earlier in the Inspector, or you can explicitly call `ActivateAbility()` from `NextStep` via an `OnAbilityTutorialCompleted` callback. **Recommendation:** call `ActivateAbility()` at the end of `FinishTutorial` via an event, not via Button.onClick, to avoid a race.

### Alternative clean implementation (recommended):

```csharp
// In AbilityManager:
private void OnAbilityButtonClicked()
{
    if (state == AbilityState.Ready)
    {
        ActivateAbility();
    }
}
```

The button only calls `AbilityManager.OnAbilityButtonClicked()`. TutorialManager adds `NextStep` as a second listener. Order doesn't matter — both methods are idempotent.

---

## 5. Edge Cases

| Scenario | Behavior |
|---|---|
| Ability charges while GameState = Planning | `AddCharge()` accepts the charge, but the tutorial doesn't start until Playing |
| Player dies while the ability is Active | `AbilityManager.OnGameOver()` → resets state to Idle, doesn't save the charge |
| No civilians on the map at activation time | The ability runs, there's just nobody to affect. Not critical. |
| Charge builds up again after use | Yes, the ability recharges. No cooldown between uses in the MVP. |
| Tutorial already shown (replaying the level) | `PlayerPrefs.HasKey("ABILITY_TUTORIAL_SHOWN")` = true → tutorial is skipped |
| AbilityManager = null (not in the gameplay scene) | All access goes through `?.` — null-safe |

---

## 6. VFX / Audio (scope at discretion)

- **On transitioning to Ready:** "charge complete" sound + vibration
- **On activation:** "whoosh/boost" sound + civilians get a visual effect (particles above the head or a ring on the ground)
- **On expiry:** quiet "fade out" sound

---

## 7. New files

| File | Type | Description |
|---|---|---|
| `Assets/Scripts/AbilityManager.cs` | MonoBehaviour (Singleton) | Charge and ability logic |
| `Assets/Scripts/UI/AbilityButtonUI.cs` | MonoBehaviour | Button visual states |
| `Assets/Data/Tutorials/Tutorial_AbilityReady.asset` | ScriptableObject | TutorialSequence for the ability |

## 8. Changes to existing files

| File | Change |
|---|---|
| `GameManager.cs` | +1 line in `AddRescuedFromTransport()` |
| `Human.cs` | Multiply `agent.speed` by `AbilityManager.GetSpeedMultiplier()` (3 places) |
| `Scientist.cs` | Same as Human.cs |
| Gameplay scene | Add AbilityManager GO, AbilityButtonUI in Canvas, TutorialTarget on the button |
| `Tutorial_AbilityReady.asset` | Create via the ScriptableObject menu |
