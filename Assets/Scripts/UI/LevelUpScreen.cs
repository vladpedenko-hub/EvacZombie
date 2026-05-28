using UnityEngine;
using UnityEngine.AI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// Upgrade selection screen shown on Level Up.
/// Full pause: Time.timeScale=0 + explicit stop of NavMeshAgent/Animator/Helicopter.
/// </summary>
public class LevelUpScreen : MonoBehaviour
{
    [Header("UI References")]
    public GameObject screenRoot;
    public TextMeshProUGUI levelUpTitle;
    public UpgradeCardUI[] upgradeCards;

    [Header("Settings")]
    public CardManager.CardType heroType = CardManager.CardType.Helicopter;

    public static bool IsShowing { get; private set; } = false;

    // ── internal state ──────────────────────────────────────────────
    private bool _isShowing = false;
    private GameManager.GameState _stateBeforePause;
    private bool _subscribedToEvent = false;

    // All agents stopped by us (so we only unfreeze those we froze)
    private readonly List<NavMeshAgent> _frozenAgents     = new List<NavMeshAgent>();
    private readonly List<Animator>     _frozenAnimators  = new List<Animator>();
    private readonly List<HelicopterController> _frozenHelis = new List<HelicopterController>();

    // ── lifecycle ────────────────────────────────────────────────────

    private void Start()
    {
        if (screenRoot != null)
            screenRoot.SetActive(false);

        TrySubscribe();
    }

    private void Update()
    {
        if (!_subscribedToEvent)
            TrySubscribe();
    }

    private void OnDestroy()
    {
        if (RunSessionData.Instance != null)
            RunSessionData.Instance.OnLevelUp -= ShowScreen;

        TutorialManager.OnTutorialStarted -= HandleTutorialStarted;
        TutorialManager.OnTutorialFinished -= HandleTutorialFinished;
    }

    private void OnEnable()
    {
        TutorialManager.OnTutorialStarted += HandleTutorialStarted;
        TutorialManager.OnTutorialFinished += HandleTutorialFinished;
    }

    private void OnDisable()
    {
        TutorialManager.OnTutorialStarted -= HandleTutorialStarted;
        TutorialManager.OnTutorialFinished -= HandleTutorialFinished;
    }

    // Tutorial pauses the world via PauseTime() (timeScale=0).
    // FreezeWorld() is NOT needed here — Human/HelicopterController check timeScale themselves,
    // and freezing animators breaks UI cards.
    private void HandleTutorialStarted()
    {
        // LevelUp already froze everything — nothing to do here
    }

    private void HandleTutorialFinished()
    {
        // TimeScale is restored via ResumeTime() inside TutorialManager.FinishTutorial()
    }

    private void TrySubscribe()
    {
        if (_subscribedToEvent) return;
        if (RunSessionData.Instance == null) return;
        RunSessionData.Instance.OnLevelUp += ShowScreen;
        _subscribedToEvent = true;
    }

    // ── show screen ──────────────────────────────────────────────────────

    private void ShowScreen()
    {
        if (_isShowing) return;
        if (UpgradeManager.Instance == null) return;

        if (GameManager.Instance == null)
        {
            Debug.LogWarning("[LevelUpScreen] ShowScreen: GameManager is null — skipping");
            return;
        }

        var curState = GameManager.Instance.State;
        if (curState != GameManager.GameState.Playing && curState != GameManager.GameState.SuddenDeath)
        {
            Debug.Log($"[LevelUpScreen] ShowScreen BLOCKED — state={curState}");
            return;
        }

        var deck = new List<CardData>();
        if (PlayerProfile.Instance?.currentDeck != null)
            foreach (var c in PlayerProfile.Instance.currentDeck)
                if (c != null) deck.Add(c);

        var options = UpgradeManager.Instance.GetUpgradeOptions(deck, heroType);
        if (options.Count == 0) return;

        _isShowing = true;
        IsShowing = true;
        AbilityManager.Instance?.OnLevelUpScreenOpened();

        // ── 1. Full time stop (via TimeManager so its Update() doesn't restore timeScale) ──
        if (TimeManager.Instance != null)
            TimeManager.Instance.PauseTime();
        else
            Time.timeScale = 0f;

        // ── 2. Explicitly freeze the entire world (NavMesh, Animator, Helicopter) ──
        FreezeWorld();

        // ── 3. Block UI cards ─────────────────────────────────────────
        if (InputManager.Instance != null)
            InputManager.Instance.IsPaused = true;

        if (CardManager.Instance != null && CardManager.Instance.cardsPanel != null)
            CardManager.Instance.cardsPanel.gameObject.SetActive(false);

        // ── 4. Save GameManager state ───────────────────────────────
        _stateBeforePause = GameManager.Instance.State;
        GameManager.Instance.State = GameManager.GameState.GameOver;

        // ── 5. Show screen ───────────────────────────────────────────
        if (screenRoot != null)
            screenRoot.SetActive(true);

        if (levelUpTitle != null && RunSessionData.Instance != null)
            levelUpTitle.text = $"LEVEL {RunSessionData.Instance.CurrentRunLevel}";

        for (int i = 0; i < upgradeCards.Length; i++)
        {
            if (i < options.Count)
            {
                var (upg, tier) = options[i];
                upgradeCards[i].gameObject.SetActive(true);
                upgradeCards[i].Setup(upg, tier, OnCardSelected);
            }
            else
            {
                upgradeCards[i].gameObject.SetActive(false);
            }
        }
    }

    // ── card selection ────────────────────────────────────────────────────

    private void OnCardSelected(RunUpgradeDefinition upgrade)
    {
        if (UpgradeManager.Instance != null)
            UpgradeManager.Instance.ApplyUpgrade(upgrade);

        CloseScreen();
    }

    // ── close and unpause ───────────────────────────────────────────────

    private void CloseScreen()
    {
        if (!_isShowing) return;
        _isShowing = false;
        IsShowing = false;

        if (screenRoot != null)
            screenRoot.SetActive(false);

        // Restore in reverse order
        if (GameManager.Instance != null)
            GameManager.Instance.State = _stateBeforePause;

        if (CardManager.Instance != null && CardManager.Instance.cardsPanel != null)
            CardManager.Instance.cardsPanel.gameObject.SetActive(true);

        if (InputManager.Instance != null)
            InputManager.Instance.IsPaused = false;

        // Unfreeze the world BEFORE restoring time
        UnfreezeWorld();

        if (TimeManager.Instance != null)
            TimeManager.Instance.ResumeTime();
        else
            Time.timeScale = 1f;

        AbilityManager.Instance?.OnLevelUpScreenClosed();
    }

    // ── freeze / unfreeze world ───────────────────────────────────────

    /// <summary>
    /// Explicitly stops all NavMeshAgents, Animators, and Helicopters.
    /// Call AFTER setting timeScale = 0.
    /// </summary>
    private void FreezeWorld()
    {
        _frozenAgents.Clear();
        _frozenAnimators.Clear();
        _frozenHelis.Clear();

        // ── NavMeshAgent: updatePosition=false GUARANTEES the transform does not move ──
        // This is the official Unity API for pausing agent movement.
        // isStopped only decelerates; updatePosition=false actually blocks movement.
        foreach (var agent in FindObjectsOfType<NavMeshAgent>())
        {
            if (agent == null || !agent.isActiveAndEnabled) continue;
            agent.updatePosition = false;   // ← key flag: agent does NOT move the transform
            agent.updateRotation = false;
            if (agent.isOnNavMesh)
            {
                agent.velocity  = Vector3.zero;
                agent.isStopped = true;
            }
            _frozenAgents.Add(agent);
        }

        // ── Animator: speed=0 freezes animation ──────────────────────
        foreach (var anim in FindObjectsOfType<Animator>())
        {
            if (anim == null || !anim.isActiveAndEnabled) continue;
            anim.updateMode = AnimatorUpdateMode.Normal; // switch to mode that respects timeScale
            anim.speed      = 0f;
            _frozenAnimators.Add(anim);
        }

        // ── Helicopter: disable Update() ───────────────────────────────
        foreach (var heli in FindObjectsOfType<HelicopterController>())
        {
            if (heli == null || !heli.enabled) continue;
            heli.enabled = false;
            _frozenHelis.Add(heli);
        }
    }

    private void UnfreezeWorld()
    {
        foreach (var agent in _frozenAgents)
        {
            if (agent == null) continue;
            agent.updatePosition = true;
            agent.updateRotation = true;
            // Warp syncs the agent's internal position with the transform
            // (in case they drifted apart during the pause)
            if (agent.isActiveAndEnabled && agent.isOnNavMesh)
            {
                agent.Warp(agent.transform.position);
                agent.isStopped = false;
            }
        }

        foreach (var anim in _frozenAnimators)
        {
            if (anim == null) continue;
            anim.speed = 1f;
        }

        foreach (var heli in _frozenHelis)
        {
            if (heli == null) continue;
            heli.enabled = true;
        }

        _frozenAgents.Clear();
        _frozenAnimators.Clear();
        _frozenHelis.Clear();
    }
}
