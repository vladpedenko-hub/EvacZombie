using UnityEngine;
using System;
using System.Collections;

public class AbilityManager : MonoBehaviour
{
    public static AbilityManager Instance { get; private set; }

    public enum AbilityState { Idle, Ready, Active }

    [Header("Balance")]
    public int maxCharge = 5;
    public float abilityDuration = 8f;
    public float speedMultiplier = 2.0f;

    [Header("Tutorial")]
    public TutorialSequence abilityReadyTutorialSequence;

    public bool IsInitialized { get; private set; } = false;
    public bool IsEnabled { get; private set; } = false;

    // Core Loop v2 Signal Flare (Docs/CORE_LOOP_V2.md Phase 6): read by SignalFlareDragHandler to
    // decide whether a drag may begin.
    public bool IsReady => state == AbilityState.Ready;

    public event Action<bool> OnInitialized;
    public event Action<float, float> OnChargeChanged;
    public event Action OnAbilityReady;
    public event Action OnAbilityActivated;
    public event Action<float> OnAbilityTick;
    public event Action OnAbilityExpired;
    public event Action OnButtonShouldHide;
    public event Action OnButtonShouldShow;

    private AbilityState state = AbilityState.Idle;
    private float currentCharge = 0;
    private Coroutine drainCoroutine;
    private bool _tutorialPending = false;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // Called from LevelManager.LoadLevel() — the sole initialization entry point
    public void Initialize(LevelData levelData)
    {
        IsEnabled = levelData != null && levelData.abilityEnabled;
        IsInitialized = true;
        OnInitialized?.Invoke(IsEnabled);

        // Core Loop v2 (Docs/CORE_LOOP_V2.md Phase 6): the ability button only becomes a drag
        // target (Signal Flare) on v2 levels with the ability enabled. Added/removed here so a
        // legacy level's button never gets the extra component at all -- no risk of it changing
        // Unity's click-vs-drag disambiguation for levels that must stay byte-identical.
        bool wantsFlareDrag = IsEnabled && levelData != null && levelData.useCoreLoopV2;
        SignalFlareDragHandler.SetEnabledOnButton(wantsFlareDrag);
    }

    public void AddCharge(int civiliansCount)
    {
        if (!IsEnabled) return;
        if (state == AbilityState.Active) return;

        currentCharge = Mathf.Min(currentCharge + civiliansCount, maxCharge);
        OnChargeChanged?.Invoke(currentCharge, maxCharge);

        if (currentCharge >= maxCharge && state == AbilityState.Idle)
        {
            state = AbilityState.Ready;
            OnAbilityReady?.Invoke();
            TriggerReadyTutorial();
        }
    }

    private void TriggerReadyTutorial()
    {
        if (TutorialManager.Instance == null || abilityReadyTutorialSequence == null) return;

        if (LevelUpScreen.IsShowing)
        {
            _tutorialPending = true;
            return;
        }

        _tutorialPending = false;
        TutorialManager.Instance.StartTutorial(abilityReadyTutorialSequence);
    }

    // Called from LevelUpScreen.ShowScreen() — hide the button while upgrade selection is open
    public void OnLevelUpScreenOpened()
    {
        OnButtonShouldHide?.Invoke();
    }

    // Called from LevelUpScreen.CloseScreen() after timeScale is restored
    public void OnLevelUpScreenClosed()
    {
        // Show the button first — TutorialTarget will register before
        // the tutorial tries to find a finger target
        OnButtonShouldShow?.Invoke();

        if (_tutorialPending && state == AbilityState.Ready)
            TriggerReadyTutorial();
    }

    public void OnAbilityButtonClicked()
    {
        if (!IsEnabled) return;

        // Core Loop v2 (Docs/CORE_LOOP_V2.md Phase 6): a plain click/tap no-ops in v2 -- Signal
        // Flare only fires via SignalFlareDragHandler's drag-to-target release.
        if (CoreLoopV2.IsActive) return;

        if (state == AbilityState.Ready)
            ActivateAbility();
    }

    // Core Loop v2 Signal Flare (Docs/CORE_LOOP_V2.md Phase 6): instant, targeted activation --
    // replaces the legacy speed-boost effect entirely for v2 levels. Resets charge to 0 so it can
    // charge again, same as the legacy DrainRoutine's end state.
    public void ActivateFlareAt(Vector3 worldPoint)
    {
        if (!IsEnabled || state != AbilityState.Ready) return;

        if (drainCoroutine != null) { StopCoroutine(drainCoroutine); drainCoroutine = null; }

        OnAbilityActivated?.Invoke();
        ApplyFlareEffect(worldPoint);

        currentCharge = 0;
        state = AbilityState.Idle;
        OnChargeChanged?.Invoke(0, maxCharge);
        OnAbilityExpired?.Invoke();
    }

    private void ApplyFlareEffect(Vector3 center)
    {
        CoreLoopV2Config cfg = CoreLoopV2Config.Active;
        float radius = cfg != null ? cfg.flareRadius : 12f;
        float stunDuration = cfg != null ? cfg.flareStunDuration : 4f;
        float radiusSqr = radius * radius;

        foreach (var z in Zombie.AllZombies.ToArray())
        {
            if (z != null && (z.transform.position - center).sqrMagnitude <= radiusSqr)
                z.Stun(stunDuration);
        }

        Transform nearestTransport = CoreLoopV2.FindNearestActiveTransport(center);

        if (nearestTransport != null)
        {
            foreach (var h in Human.AllHumans.ToArray())
            {
                if (h != null && (h.transform.position - center).sqrMagnitude <= radiusSqr)
                    h.SetRescueTarget(nearestTransport);
            }
            foreach (var s in Scientist.AllScientists.ToArray())
            {
                if (s != null && (s.transform.position - center).sqrMagnitude <= radiusSqr)
                    s.SetRescueTarget(nearestTransport);
            }
        }

        Feedback.Play(FeedbackEvent.SignalFlare, center);
    }

    public void ActivateAbility()
    {
        if (!IsEnabled) return;
        if (state != AbilityState.Ready) return;

        state = AbilityState.Active;
        OnAbilityActivated?.Invoke();

        if (drainCoroutine != null) StopCoroutine(drainCoroutine);
        drainCoroutine = StartCoroutine(DrainRoutine());
    }

    private IEnumerator DrainRoutine()
    {
        float elapsed = 0f;
        while (elapsed < abilityDuration)
        {
            elapsed += Time.deltaTime;
            float remaining = Mathf.Clamp01(1f - elapsed / abilityDuration);
            OnAbilityTick?.Invoke(remaining);
            yield return null;
        }

        state = AbilityState.Idle;
        currentCharge = 0;
        OnAbilityExpired?.Invoke();
        OnChargeChanged?.Invoke(0, maxCharge);
    }

    public float GetSpeedMultiplier()
    {
        if (!IsEnabled) return 1f;
        return state == AbilityState.Active ? speedMultiplier : 1f;
    }

    public void OnGameOver()
    {
        if (!IsEnabled) return;
        if (drainCoroutine != null) StopCoroutine(drainCoroutine);
        state = AbilityState.Idle;
        currentCharge = 0;
    }
}
