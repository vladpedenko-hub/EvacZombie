using UnityEngine;
using UnityEngine.AI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// Экран выбора апгрейда при Level Up.
/// Полная пауза: Time.timeScale=0 + явная остановка NavMeshAgent/Animator/Helicopter.
/// </summary>
public class LevelUpScreen : MonoBehaviour
{
    [Header("UI References")]
    public GameObject screenRoot;
    public TextMeshProUGUI levelUpTitle;
    public UpgradeCardUI[] upgradeCards;

    [Header("Настройки")]
    public CardManager.CardType heroType = CardManager.CardType.Helicopter;

    public static bool IsShowing { get; private set; } = false;

    // ── внутреннее состояние ──────────────────────────────────────────────
    private bool _isShowing = false;
    private GameManager.GameState _stateBeforePause;
    private bool _subscribedToEvent = false;

    // Все агенты, которые были остановлены нами (чтобы потом разморозить только их)
    private readonly List<NavMeshAgent> _frozenAgents     = new List<NavMeshAgent>();
    private readonly List<Animator>     _frozenAnimators  = new List<Animator>();
    private readonly List<HelicopterController> _frozenHelis = new List<HelicopterController>();

    // ── жизненный цикл ────────────────────────────────────────────────────

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

    // Туториал паузит мир через PauseTime() (timeScale=0).
    // FreezeWorld() здесь НЕ нужен — Human/HelicopterController проверяют timeScale самостоятельно,
    // а заморозка аниматоров ломает UI-карточки.
    private void HandleTutorialStarted()
    {
        // LevelUp уже заморозил всё — нам ничего делать не нужно
    }

    private void HandleTutorialFinished()
    {
        // TimeScale восстановлен через ResumeTime() внутри TutorialManager.FinishTutorial()
    }

    private void TrySubscribe()
    {
        if (_subscribedToEvent) return;
        if (RunSessionData.Instance == null) return;
        RunSessionData.Instance.OnLevelUp += ShowScreen;
        _subscribedToEvent = true;
    }

    // ── показ экрана ──────────────────────────────────────────────────────

    private void ShowScreen()
    {
        if (_isShowing) return;
        if (UpgradeManager.Instance == null) return;

        if (GameManager.Instance == null)
        {
            Debug.LogWarning("[LevelUpScreen] ShowScreen: GameManager is null — пропуск");
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

        // ── 1. Полный тайм-стоп (через TimeManager, чтобы его Update() не восстанавливал timeScale) ──
        if (TimeManager.Instance != null)
            TimeManager.Instance.PauseTime();
        else
            Time.timeScale = 0f;

        // ── 2. Явно замораживаем весь мир (NavMesh, Animator, Helicopter) ──
        FreezeWorld();

        // ── 3. Блокируем UI-карты ─────────────────────────────────────────
        if (InputManager.Instance != null)
            InputManager.Instance.IsPaused = true;

        if (CardManager.Instance != null && CardManager.Instance.cardsPanel != null)
            CardManager.Instance.cardsPanel.gameObject.SetActive(false);

        // ── 4. Фиксируем стейт GameManager ───────────────────────────────
        _stateBeforePause = GameManager.Instance.State;
        GameManager.Instance.State = GameManager.GameState.GameOver;

        // ── 5. Показываем экран ───────────────────────────────────────────
        if (screenRoot != null)
            screenRoot.SetActive(true);

        if (levelUpTitle != null && RunSessionData.Instance != null)
            levelUpTitle.text = $"УРОВЕНЬ {RunSessionData.Instance.CurrentRunLevel}";

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

    // ── выбор карточки ────────────────────────────────────────────────────

    private void OnCardSelected(RunUpgradeDefinition upgrade)
    {
        if (UpgradeManager.Instance != null)
            UpgradeManager.Instance.ApplyUpgrade(upgrade);

        CloseScreen();
    }

    // ── закрытие и снятие паузы ───────────────────────────────────────────

    private void CloseScreen()
    {
        if (!_isShowing) return;
        _isShowing = false;
        IsShowing = false;

        if (screenRoot != null)
            screenRoot.SetActive(false);

        // Восстанавливаем в обратном порядке
        if (GameManager.Instance != null)
            GameManager.Instance.State = _stateBeforePause;

        if (CardManager.Instance != null && CardManager.Instance.cardsPanel != null)
            CardManager.Instance.cardsPanel.gameObject.SetActive(true);

        if (InputManager.Instance != null)
            InputManager.Instance.IsPaused = false;

        // Размораживаем мир ПЕРЕД возвратом времени
        UnfreezeWorld();

        if (TimeManager.Instance != null)
            TimeManager.Instance.ResumeTime();
        else
            Time.timeScale = 1f;

        AbilityManager.Instance?.OnLevelUpScreenClosed();
    }

    // ── заморозка / разморозка мира ───────────────────────────────────────

    /// <summary>
    /// Явно останавливает всех NavMeshAgent-ов, Animator-ы и Helicopter-ы.
    /// Вызывать ПОСЛЕ установки timeScale = 0.
    /// </summary>
    private void FreezeWorld()
    {
        _frozenAgents.Clear();
        _frozenAnimators.Clear();
        _frozenHelis.Clear();

        // ── NavMeshAgent: updatePosition=false ГАРАНТИРУЕТ что transform не двигается ──
        // Это официальный Unity API для паузы движения агента.
        // isStopped только замедляет, updatePosition=false — реально блокирует.
        foreach (var agent in FindObjectsOfType<NavMeshAgent>())
        {
            if (agent == null || !agent.isActiveAndEnabled) continue;
            agent.updatePosition = false;   // ← главный флаг: агент НЕ двигает transform
            agent.updateRotation = false;
            if (agent.isOnNavMesh)
            {
                agent.velocity  = Vector3.zero;
                agent.isStopped = true;
            }
            _frozenAgents.Add(agent);
        }

        // ── Animator: speed=0 замораживает анимацию ──────────────────────
        foreach (var anim in FindObjectsOfType<Animator>())
        {
            if (anim == null || !anim.isActiveAndEnabled) continue;
            anim.updateMode = AnimatorUpdateMode.Normal; // переводим в режим уважающий timeScale
            anim.speed      = 0f;
            _frozenAnimators.Add(anim);
        }

        // ── Helicopter: отключаем Update() ───────────────────────────────
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
            // Warp синхронизирует внутреннюю позицию агента с transform
            // (на случай если за время паузы они разошлись)
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
