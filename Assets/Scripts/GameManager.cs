using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;

public class GameManager : MonoBehaviour
{
	public static GameManager Instance;

	public enum GameState { Planning, Playing, SuddenDeath, GameOver, Lose }
	public GameState State = GameState.Planning;

	// feature/hud-sandbox-ui (step 5): why ShowLosePopup fired -- lets UIManager show a specific
	// English title instead of one generic string. AllCiviliansLost/GoalUnreachable both come from
	// CheckWinLoseCondition's math (possibleToRescue == 0 vs merely < requiredHumans); TimeIsUp is
	// the V2 Last Transport departing without enough rescued.
	public enum LoseReason { AllCiviliansLost, GoalUnreachable, TimeIsUp }

	// feature/hud-sandbox-ui (step 5): sandbox-only -- set by the lose popup's "Keep Playing"
	// button, permanently skipping future lose checks for the rest of this attempt so a dev can
	// keep watching the sim instead of being kicked back to the popup every frame. Never set
	// outside that sandbox-only path, and resets to false on any level (re)load since GameManager
	// isn't DontDestroyOnLoad -- non-sandbox lose/win rules are unaffected.
	private bool sandboxLoseContinue = false;

	[Header("Human Counters")]
	public int totalHumans;
	public int rescuedHumans;
	public int requiredHumans;

	[Header("Scientist Counters")]
	public int totalScientists;
	public int rescuedScientists;

	[HideInInspector] public int pendingHumans = 0;
	[HideInInspector] public int pendingScientists = 0;

	// Core Loop v2 instrumentation (Docs/CORE_LOOP_V2.md Phase 7 / PlaytestLog), reset per level
	// in SetTotalHumans.
	public bool PerfectEvacuationTriggered { get; private set; }

	// Generic tutorial-trigger vocabulary (Docs/CORE_LOOP_V2.md Phase 3 tutorial rewrite).
	// Static since GameManager isn't DontDestroyOnLoad -- TutorialManager subscribes fresh per step.
	public static event System.Action OnAnyRescueCredited;
	public static event System.Action OnAnyNightStarted;

	private float currentTimer;
	private float startTimerAmount;
	private bool perfectClearStarted = false;

	private void Awake() => Instance = this;

	private void Start()
	{
		RunSessionData.Instance?.ResetForNewLevel();
	}

	public void SetTotalHumans(int count)
	{
		totalHumans = count;
		rescuedHumans = 0;
		pendingHumans = 0;
		PerfectEvacuationTriggered = false;

		totalScientists = Scientist.AllScientists.Count;
		rescuedScientists = 0;
		pendingScientists = 0;

		requiredHumans = LevelManager.Instance.currentData.requiredRescuedHumans;

		UIManager.Instance.UpdateRescuedCount(GetTotalRescued(), requiredHumans, false);
	}

	public void AddRescuedFromTransport(int humans, int scientists, Vector3 transportWorldPos)
	{
		if (State == GameState.GameOver || State == GameState.Lose) return;

		int total = humans + scientists;
		if (total <= 0) return;

		pendingHumans += humans;
		pendingScientists += scientists;

		UIManager.Instance.SpawnFlyingText(transportWorldPos, total);

		XPManager.Instance?.OnCivilianEvacuated(humans, scientists);
		AbilityManager.Instance?.AddCharge(total);
	}

	public void AddRescuedHumans(int count, Vector3 transportWorldPos)
	{
		AddRescuedFromTransport(count, 0, transportWorldPos);
	}

	public void AddRescuedScientists(int count, Vector3 transportWorldPos)
	{
		AddRescuedFromTransport(0, count, transportWorldPos);
	}

	public void OnFlyingTextReached(int totalAmount)
	{
		int totalPending = pendingHumans + pendingScientists;
		int humansFromThis;
		int scientistsFromThis;

		if (totalPending <= 0)
		{
			humansFromThis = totalAmount;
			scientistsFromThis = 0;
			rescuedHumans += totalAmount;
		}
		else
		{
			float humansRatio = (float)pendingHumans / totalPending;
			humansFromThis = Mathf.RoundToInt(totalAmount * humansRatio);
			scientistsFromThis = totalAmount - humansFromThis;

			pendingHumans -= humansFromThis;
			pendingScientists -= scientistsFromThis;

			rescuedHumans += humansFromThis;
			rescuedScientists += scientistsFromThis;
		}

		// Core Loop v2 (Docs/CORE_LOOP_V2.md Phase 1): Supply income is credited here, when the
		// +N fly-in actually lands at the counter, not when the transport takes off.
		if (CoreLoopV2.IsActive)
		{
			CoreLoopV2Config cfg = CoreLoopV2Config.Active;
			int supplyGain = humansFromThis * (cfg != null ? cfg.supplyIncomeHuman : 1)
				+ scientistsFromThis * (cfg != null ? cfg.supplyIncomeScientist : 3);
			SupplyManager.Instance?.Gain(supplyGain);
		}

		UIManager.Instance.UpdateRescuedCount(GetTotalRescued(), requiredHumans, true);
		OnAnyRescueCredited?.Invoke();
		CheckWinLoseCondition();
	}

	public void OnFlyingScientistTextReached(int amount) { }

	public void SetupTimer(float time)
	{
		currentTimer = time;
		startTimerAmount = time;
		UIManager.Instance.UpdateTimer(currentTimer, true);
	}

	public void StartGame()
	{
		if (State != GameState.Planning) return;

		State = GameState.Playing;
		LevelManager.Instance.StartInitialSpawns();
	}

	private void Update()
	{
		// Core Loop v2 (Docs/CORE_LOOP_V2.md Phase 3/4): the day timer and its "map empty -> win"
		// legacy branch are replaced by the sequential wave sequencer (LevelManager) and the time-
		// limited Night/Last Transport flow. The early-lose rule still applies to both.
		bool isV2 = CoreLoopV2.IsActive;

		if (State == GameState.Playing)
		{
			if (!isV2)
			{
				currentTimer -= Time.deltaTime;
				UIManager.Instance.UpdateTimer(currentTimer, false);
			}

			CheckPerfectClear();

			if (!isV2 && currentTimer <= 0 && State == GameState.Playing)
			{
				State = GameState.SuddenDeath;
				LevelManager.Instance.StartSuddenDeath();
				UIManager.Instance.ShowNightPopup();
			}
		}

		if (State == GameState.Playing || State == GameState.SuddenDeath)
		{
			if (isV2)
			{
				CheckWinLoseCondition(false);
			}
			else
			{
				bool levelEmpty = GetAliveResidentsCount() == 0 && (pendingHumans + pendingScientists) == 0;

				if (levelEmpty) CheckWinLoseCondition(true);
				else CheckWinLoseCondition(false);
			}
		}
	}

	// Core Loop v2 (Docs/CORE_LOOP_V2.md Phase 3/4): called by LevelManager once its sequential
	// wave sequencer finishes (or skipped entirely by a Perfect Evacuation ending the level
	// first). Mirrors the legacy inline Night transition in Update() above, but is its own method
	// since v2 reaches it from a different trigger (waves done, not a timer).
	public void StartV2Night()
	{
		if (State != GameState.Playing) return;

		State = GameState.SuddenDeath;
		LevelManager.Instance.StartSuddenDeath();
		UIManager.Instance.ShowNightPopup();
		CenterBannerV2.Show("NIGHT FALLS");
		NightLastCallController.Instance?.BeginLastCall();
		OnAnyNightStarted?.Invoke();

		LevelData level = LevelManager.Instance.currentData;
		if (level != null && level.onNightTutorial != null && TutorialManager.Instance != null)
			TutorialManager.Instance.StartTutorial(level.onNightTutorial);
	}

	// Core Loop v2 (Docs/CORE_LOOP_V2.md Phase 4): called by HelicopterController.TakeOff() when
	// the Last Transport departs. The level ends then, not on an empty map -- civilians not
	// aboard are already lost (TakeOff only counts loadedUnits). Waits for any still-animating
	// +N fly-ins so the win check sees their final rescued count, same pattern as
	// PerfectClearRoutine above.
	public void OnV2LastTransportDeparted()
	{
		if (State == GameState.GameOver || State == GameState.Lose) return;
		StartCoroutine(V2LastTransportEndRoutine());
	}

	private IEnumerator V2LastTransportEndRoutine()
	{
		while (pendingHumans > 0 || pendingScientists > 0)
			yield return null;

		yield return new WaitForSeconds(0.5f);

		if (State == GameState.GameOver || State == GameState.Lose) yield break;

		if (GetTotalRescued() >= requiredHumans)
			EndLevel(false);
		else
		{
			State = GameState.Lose;
			UIManager.Instance.ShowLosePopup(LoseReason.TimeIsUp);
		}
	}

	private int GetAliveResidentsCount()
	{
		return Human.AllHumans.Count + Scientist.AllScientists.Count;
	}

	private int GetTotalRescued()
	{
		return rescuedHumans + rescuedScientists;
	}

	private int GetPendingTotal()
	{
		return pendingHumans + pendingScientists;
	}

	private void CheckPerfectClear()
	{
		if (perfectClearStarted) return;
		if (LevelManager.Instance == null || LevelManager.Instance.currentData == null) return;

		// Core Loop v2 (Docs/CORE_LOOP_V2.md Phase 4): Perfect Evacuation replaces the legacy
		// auto-evacuate-survivors routine. Checked continuously ("at any point before night"),
		// not gated on wave-timeline completion -- it can fire mid-sequence if every civilian is
		// already rescued.
		if (LevelManager.Instance.currentData.useCoreLoopV2)
		{
			bool noOneLeftToSave = GetAliveResidentsCount() == 0 && GetPendingTotal() == 0;
			bool metStar2 = GetTotalRescued() >= LevelManager.Instance.currentData.GetStar2Target();

			if (noOneLeftToSave && metStar2)
			{
				perfectClearStarted = true;
				PerfectEvacuationTriggered = true;
				LevelManager.Instance.StopV2WaveSequencer();
				CenterBannerV2.Show("PERFECT EVACUATION");
				EndLevel(true);
			}
			return;
		}

		// Ensure timeline spawning is complete before checking for perfect clear
		if (!LevelManager.Instance.IsTimelineSpawningFinished) return;

		// Perfect clear: all zombies are dead and survivors still remain on the map
		if (Zombie.AllZombies.Count == 0 && GetAliveResidentsCount() > 0)
		{
			perfectClearStarted = true;
			StartCoroutine(PerfectClearRoutine());
		}
	}

	private IEnumerator PerfectClearRoutine()
	{
		State = GameState.GameOver;

		List<Human> humanSurvivors = new List<Human>(Human.AllHumans);
		List<Scientist> scientistSurvivors = new List<Scientist>(Scientist.AllScientists);

		Human.AllHumans.Clear();
		Scientist.AllScientists.Clear();

		foreach (var h in humanSurvivors)
		{
			if (h != null)
			{
				pendingHumans += 1;
				UIManager.Instance.SpawnFlyingText(h.transform.position, 1);
				h.gameObject.SetActive(false);
				yield return new WaitForSeconds(0.08f);
			}
		}

		foreach (var s in scientistSurvivors)
		{
			if (s != null)
			{
				pendingScientists += 1;
				UIManager.Instance.SpawnFlyingText(s.transform.position, 1);
				s.gameObject.SetActive(false);
				yield return new WaitForSeconds(0.08f);
			}
		}

		while (pendingHumans > 0 || pendingScientists > 0)
		{
			yield return null;
		}

		yield return new WaitForSeconds(0.5f);

		EndLevel(true);
	}

	private void CheckWinLoseCondition(bool isLevelEmpty = false)
	{
		if (State == GameState.GameOver || State == GameState.Lose || sandboxLoseContinue) return;

		int possibleToRescue = GetTotalRescued() + GetPendingTotal() + GetAliveResidentsCount();

		if (possibleToRescue < requiredHumans)
		{
			State = GameState.Lose;
			// possibleToRescue already includes GetTotalRescued() -- 0 means nobody was ever
			// rescued and nobody is left alive/pending, i.e. a total wipe. Anything above 0 but
			// still short of the goal means some were saved, just not enough.
			UIManager.Instance.ShowLosePopup(possibleToRescue <= 0 ? LoseReason.AllCiviliansLost : LoseReason.GoalUnreachable);
		}
		else if (isLevelEmpty && GetTotalRescued() >= requiredHumans)
		{
			EndLevel(false);
		}
	}

	private int CalculateEarnedStars(bool isPerfect)
	{
		LevelData level = LevelManager.Instance.currentData;
		if (level == null) return 0;

		int rescuedTotal = GetTotalRescued();
		int stars = 0;

		if (rescuedTotal >= level.GetStar1Target())
			stars++;

		if (rescuedTotal >= level.GetStar2Target())
			stars++;

		if (isPerfect)
			stars++;

		return Mathf.Clamp(stars, 0, 3);
	}

	public void EndLevel(bool isPerfect = false)
	{
		State = GameState.GameOver;

		LevelData level = LevelManager.Instance.currentData;
		MetaRuntime.EnsureInitialized();
		CurrencyService currency = MetaRuntime.Currency;

		currency.Add(CurrencyType.People, rescuedHumans, CurrencySourceType.LevelClear, level.name);
		currency.Add(CurrencyType.Scientists, rescuedScientists, CurrencySourceType.LevelClear, level.name);

		string levelId = "LevelPassed_" + level.name;
		int hasPassedBefore = PlayerPrefs.GetInt(levelId, 0);
		CardData droppedCard = null;

		if (hasPassedBefore == 0)
		{
			PlayerPrefs.SetInt(levelId, 1);
			currency.Add(CurrencyType.People, level.currencyReward, CurrencySourceType.LevelClear, level.name);
			foreach (CurrencyGrant grant in level.clearGrants) currency.Add(grant, level.name);
			// ThreeStars / Quest grants: one currency.Add(...) line each, with their CurrencySourceType.

			if (LevelManager.Instance.currentData.levelRewardLootbox != null)
			{
				droppedCard = LevelManager.Instance.currentData.levelRewardLootbox.OpenBox();
				if (droppedCard != null)
					PlayerProfile.Instance.AddCardReward(droppedCard);
			}
		}

		int earnedStars = CalculateEarnedStars(isPerfect);
		int previousBestStars = PlayerProfile.Instance.GetSavedStarsForLevel(LevelManager.Instance.currentData);

		PlayerProfile.Instance.TrySetLevelStars(LevelManager.Instance.currentData.name, earnedStars, out int oldStarsFromSave);
		PlayerProfile.Instance.SaveProfile();
		MetaRuntime.OnLevelCleared(level);

		UIManager.Instance.ShowResultPopup(
			rescuedHumans,
			GetTotalRescued(),
			rescuedScientists,
			droppedCard,
			isPerfect,
			earnedStars,
			previousBestStars
		);
	}

	public void RestartLevel()
	{
		SceneManager.LoadScene(SceneManager.GetActiveScene().name);
	}

	// feature/hud-sandbox-ui (step 5): "Keep Playing" on the sandbox-only lose popup -- resumes
	// simulation instead of restarting/quitting, and permanently stops this attempt's lose checks
	// so the popup doesn't just reappear next frame (the underlying math that triggered it hasn't
	// changed). UIManager only offers this button when LevelData.isSandboxLevel is true.
	public void ContinuePlayingAfterSandboxLose()
	{
		sandboxLoseContinue = true;
		if (State == GameState.Lose) State = GameState.Playing;
	}

	public void GoToMainMenu()
	{
		Time.timeScale = 1f;
		SceneManager.LoadScene("MainMenu");
	}
}