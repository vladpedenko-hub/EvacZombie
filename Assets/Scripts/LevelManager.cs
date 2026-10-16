using UnityEngine;
using Unity.AI.Navigation;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class LevelManager : MonoBehaviour
{
	public static LevelManager Instance;

	// Observational only (BalanceProbe, Docs/CLAUDE_CODE_TASK_LevelProgression.md §4.1) -- counts
	// zombies spawned by the wave timeline (not Sudden Death, not infection). Not read by any
	// gameplay/flow code.
	public static int DaySpawnCount = 0;

	[Header("World References")]
	[SerializeField] private NavMeshSurface navSurface;
	[SerializeField] private GameObject humanPrefab;
	[SerializeField] private GameObject zombiePrefab;
	[SerializeField] private GameObject defaultScientistPrefab;

	[Header("Planning / Warning Visualization")]
	[SerializeField] private GameObject indicatorPrefab;
	[SerializeField] private float indicatorHeight = 1.5f;

	[Header("Human Spawn Settings")]
	[SerializeField] private float minDistanceFromSpawnPoints = 8f;
	[SerializeField] private int maxSpawnAttempts = 30;

	[Header("Camera Fit - Reserved UI")]
	[Tooltip("Top-anchored HUD elements whose screen-space extent the camera should not hide the map behind (e.g. TimerText, RescuedCounter).")]
	[SerializeField] private RectTransform[] topReservedUI;

	// Core Loop v2 (Docs/CORE_LOOP_V2.md Phase 3): runtime-built HUD (TopHudV2, etc.) can't be
	// wired into the serialized array above (it doesn't exist until the level loads), so it
	// registers itself here instead. Read alongside topReservedUI in GetReservedScreenFractions().
	private readonly List<RectTransform> runtimeTopReservedUI = new List<RectTransform>();

	public void RegisterTopReservedUI(RectTransform rt)
	{
		if (rt != null && !runtimeTopReservedUI.Contains(rt)) runtimeTopReservedUI.Add(rt);
	}

	// Same idea for the bottom (thumb) zone -- the v2 control row (Supply pill, GO/ad, ultimate)
	// sits above CardsPanel, so the camera needs to reserve more than CardsPanel's own extent.
	private readonly List<RectTransform> runtimeBottomReservedUI = new List<RectTransform>();

	public void RegisterBottomReservedUI(RectTransform rt)
	{
		if (rt != null && !runtimeBottomReservedUI.Contains(rt)) runtimeBottomReservedUI.Add(rt);
	}

	[Header("Lighting")]
	public Light sunLight;
	public Color nightColor = new Color(0.1f, 0.1f, 0.3f);
	public float nightIntensity = 0.2f;

	private Color dayColor;
	private float dayIntensity;

	private int currentLevelIndex = 0;

	private List<SpawnPointMarker> allSpawnPoints = new List<SpawnPointMarker>();
	private List<SpawnPointMarker> nightSpawnPoints = new List<SpawnPointMarker>();
	private List<GameObject> activeIndicators = new List<GameObject>();

	public LevelData currentData;
	private GameObject currentLevelEnvironment;

	private float timeElapsed = 0f;
	private bool isTimelineRunning = false;
	private Coroutine timelineCoroutine;

	public bool IsTimelineSpawningFinished { get; private set; }
	private int activeSpawnCoroutines = 0;
	private bool allWavesTriggered = false;

	// Core Loop v2 (Docs/CORE_LOOP_V2.md): counts zombies actually instantiated for the wave
	// currently being sequenced by WaveSequenceRoutineV2, reset per wave. Guards against declaring
	// a wave "cleared" when activeSpawnCoroutines hit 0 without ever spawning anything (e.g. a
	// misconfigured spawn group with no matching points) -- see WaveSequenceRoutineV2.
	private int v2SpawnedThisWave = 0;

	// Core Loop v2 sequential-wave state (Docs/CORE_LOOP_V2.md Phase 3), polled by TopHudV2.
	// Not read by any legacy/gameplay code.
	public int V2CurrentWaveNumber { get; private set; }
	public int V2TotalWaves { get; private set; }
	public bool V2InBreather { get; private set; }
	public float V2BreatherRemaining { get; private set; }
	public string V2NextWaveHint { get; private set; } = "";

	// Per-attempt instrumentation (Docs/CORE_LOOP_V2.md Phase 7 / PlaytestLog), reset in LoadLevel.
	public int V2WavesClearedCount { get; private set; }
	public int V2WavesForcedCount { get; private set; }

	// Generic tutorial-trigger vocabulary (Docs/CORE_LOOP_V2.md Phase 3 tutorial rewrite,
	// TutorialCompletionTrigger.WaveStarted). Static since GameManager/LevelManager aren't
	// DontDestroyOnLoad -- TutorialManager subscribes fresh per step rather than caching this.
	public static event System.Action<int, int> OnAnyWaveStarted;

	private Coroutine v2WaveCoroutine;

	private void Awake() => Instance = this;

	private void Start()
	{
		if (sunLight != null)
		{
			dayColor = sunLight.color;
			dayIntensity = sunLight.intensity;
		}

		int regionIdx = PlayerProfile.Instance.currentRegionIndex;
		regionIdx = Mathf.Clamp(regionIdx, 0, PlayerProfile.Instance.allRegions.Count - 1);

		RegionConfig currentRegion = PlayerProfile.Instance.allRegions[regionIdx];
		currentLevelIndex = PlayerPrefs.GetInt("SelectedLevelToPlay", 0);

		if (currentLevelIndex >= currentRegion.levels.Count)
			currentLevelIndex = 0;

		if (currentRegion.levels.Count > 0)
			LoadLevel(currentRegion.levels[currentLevelIndex]);
	}

	public void LoadLevel(LevelData data)
	{
		ClearIndicators();
		currentData = data;
		V2WavesClearedCount = 0;
		V2WavesForcedCount = 0;

		AbilityManager.Instance?.Initialize(data);
		SupplyManager.Instance?.OnLevelLoaded(data);
		BottomZoneV2.EnsureBuilt();
		SupplyHud.OnLevelLoaded(data);
		PlanningControllerV2.OnLevelLoaded(data);
		TopHudV2.OnLevelLoaded(data);
		NightLastCallController.OnLevelLoaded(data);

		if (currentLevelEnvironment != null)
			Destroy(currentLevelEnvironment);

		currentLevelEnvironment = Instantiate(data.levelPrefab, Vector3.zero, Quaternion.identity);

		if (CameraController.Instance != null)
		{
			(float topFrac, float bottomFrac) = GetReservedScreenFractions();
			CameraController.Instance.SetupCamera(data, topFrac, bottomFrac);
		}

		if (navSurface != null)
			navSurface.BuildNavMesh();

		CrowdFieldManager.Instance?.OnLevelLoaded();

		allSpawnPoints.Clear();
		nightSpawnPoints.Clear();

		allSpawnPoints.AddRange(FindObjectsOfType<SpawnPointMarker>());
		foreach (var sp in allSpawnPoints)
		{
			if (sp.isNightSpawn)
			{
				nightSpawnPoints.Add(sp);
			}
		}

		SpawnInitialPlanningIndicators();

		SpawnHumans(data.humanCount);
		SpawnScientists(data.scientistCount);

		GameManager.Instance.SetTotalHumans(GameObject.FindGameObjectsWithTag("Human").Length);
		GameManager.Instance.SetupTimer(data.levelTimer);

		// --- START TUTORIAL ON LEVEL START ---
		if (currentData != null && currentData.onStartTutorial != null && TutorialManager.Instance != null)
		{
			StartCoroutine(StartTutorialNextFrame(currentData.onStartTutorial));
		}
	}

	// LoadLevel() runs from this object's own Start(), with no guaranteed order relative to other
	// objects' Start() (e.g. CardManager.SpawnDeck(), which builds the card hand). Starting the
	// tutorial here synchronously could resolve a step's target (e.g. "Card_Helicopter") before the
	// hand exists or before its freshly-instantiated RectTransform has gone through a layout pass,
	// pointing the finger at a stale/zero position (left screen edge). One frame is enough for every
	// Start() in the scene -- and a layout rebuild -- to have already run.
	private IEnumerator StartTutorialNextFrame(TutorialSequence sequence)
	{
		yield return null;
		if (TutorialManager.Instance != null) TutorialManager.Instance.StartTutorial(sequence);
	}

	// Fraction of the screen height covered by top-anchored HUD elements / the bottom card hand,
	// read from their actual on-screen RectTransform corners (Screen Space - Overlay canvases,
	// so GetWorldCorners already returns screen pixels, safe-area insets included).
	private (float top, float bottom) GetReservedScreenFractions()
	{
		Vector3[] corners = new Vector3[4];
		float topPixels = 0f;

		if (topReservedUI != null)
		{
			foreach (RectTransform rt in topReservedUI)
			{
				if (rt == null || !rt.gameObject.activeInHierarchy) continue;
				rt.GetWorldCorners(corners);
				topPixels = Mathf.Max(topPixels, Screen.height - corners[0].y);
			}
		}

		foreach (RectTransform rt in runtimeTopReservedUI)
		{
			if (rt == null || !rt.gameObject.activeInHierarchy) continue;
			rt.GetWorldCorners(corners);
			topPixels = Mathf.Max(topPixels, Screen.height - corners[0].y);
		}

		float bottomPixels = 0f;
		RectTransform cardsPanel = CardManager.Instance != null ? CardManager.Instance.cardsPanel as RectTransform : null;
		if (cardsPanel != null)
		{
			cardsPanel.GetWorldCorners(corners);
			bottomPixels = corners[1].y; // top-left corner's screen Y == panel height from the bottom edge
		}

		foreach (RectTransform rt in runtimeBottomReservedUI)
		{
			if (rt == null || !rt.gameObject.activeInHierarchy) continue;
			rt.GetWorldCorners(corners);
			bottomPixels = Mathf.Max(bottomPixels, corners[1].y);
		}

		return (topPixels / Screen.height, bottomPixels / Screen.height);
	}

	private void SpawnInitialPlanningIndicators()
	{
		if (indicatorPrefab == null) return;

		foreach (var sp in allSpawnPoints)
		{
			if (!sp.isNightSpawn)
			{
				Vector3 pos = sp.transform.position + Vector3.up * indicatorHeight;
				GameObject indicator = Instantiate(indicatorPrefab, pos, indicatorPrefab.transform.rotation);
				activeIndicators.Add(indicator);

				SpawnIndicator logic = indicator.GetComponent<SpawnIndicator>();
				if (logic != null) logic.isPlanningIndicator = true;
			}
		}
	}

	private void ClearIndicators()
	{
		foreach (GameObject ind in activeIndicators)
			if (ind != null) Destroy(ind);

		activeIndicators.Clear();
	}

	private bool IsTooCloseToSpawnPoints(Vector3 pos)
	{
		foreach (var sp in allSpawnPoints)
		{
			if (sp.isNightSpawn) continue;
			if (Vector3.Distance(pos, sp.transform.position) < minDistanceFromSpawnPoints)
				return true;
		}
		return false;
	}

	private void SpawnHumans(int count)
	{
		for (int i = 0; i < count; i++)
		{
			for (int attempt = 0; attempt < maxSpawnAttempts; attempt++)
			{
				Vector3 randomPos = Random.insideUnitSphere * 20f;
				randomPos.y = 0;

				if (NavMesh.SamplePosition(randomPos, out NavMeshHit hit, 20f, NavMesh.AllAreas) && !IsTooCloseToSpawnPoints(hit.position))
				{
					Instantiate(humanPrefab, hit.position, Quaternion.identity);
					break;
				}
			}
		}
	}

	private void SpawnScientists(int count)
	{
		if (count <= 0) return;

		GameObject prefabToUse = currentData.scientistPrefab != null ? currentData.scientistPrefab : defaultScientistPrefab;
		if (prefabToUse == null)
		{
			Debug.LogWarning("[LevelManager] Scientist prefab is not assigned.");
			return;
		}

		for (int i = 0; i < count; i++)
		{
			for (int attempt = 0; attempt < maxSpawnAttempts; attempt++)
			{
				Vector3 randomPos = Random.insideUnitSphere * 20f;
				randomPos.y = 0;

				if (NavMesh.SamplePosition(randomPos, out NavMeshHit hit, 20f, NavMesh.AllAreas) && !IsTooCloseToSpawnPoints(hit.position))
				{
					Instantiate(prefabToUse, hit.position, Quaternion.identity);
					break;
				}
			}
		}
	}

	public void StartInitialSpawns()
	{
		ClearIndicators();

		timeElapsed = 0f;
		isTimelineRunning = true;
		IsTimelineSpawningFinished = false;
		activeSpawnCoroutines = 0;
		allWavesTriggered = false;

		if (timelineCoroutine != null) StopCoroutine(timelineCoroutine);
		if (v2WaveCoroutine != null) { StopCoroutine(v2WaveCoroutine); v2WaveCoroutine = null; }

		// Core Loop v2 (Docs/CORE_LOOP_V2.md Phase 3): sequential waves + breathers replace the
		// legacy timer-driven timeline. Legacy levels are completely untouched.
		if (currentData.useCoreLoopV2)
			v2WaveCoroutine = StartCoroutine(WaveSequenceRoutineV2());
		else
			timelineCoroutine = StartCoroutine(WaveTimelineRoutine());
	}

	// Perfect-Evacuation early-out (brief Phase 4): stops the v2 wave sequencer mid-sequence so no
	// further waves spawn once GameManager has already ended the level.
	public void StopV2WaveSequencer()
	{
		if (v2WaveCoroutine != null) { StopCoroutine(v2WaveCoroutine); v2WaveCoroutine = null; }
		isTimelineRunning = false;
		V2InBreather = false;
	}

	private class RuntimeWaveAction
	{
		public WaveAction actionData;
		public List<SpawnPointMarker> chosenSpawners = new List<SpawnPointMarker>();
	}

	private class TrackedWave
	{
		public WaveData data;
		public bool warningTriggered = false;
		public bool waveTriggered = false;
		public List<RuntimeWaveAction> runtimeActions = new List<RuntimeWaveAction>();
	}

	private IEnumerator WaveTimelineRoutine()
	{
		List<TrackedWave> pendingWaves = new List<TrackedWave>();
		foreach (var wave in currentData.waves)
		{
			if (wave != null)
				pendingWaves.Add(new TrackedWave { data = wave });
		}

		while (isTimelineRunning && GameManager.Instance.State != GameManager.GameState.SuddenDeath)
		{
			timeElapsed += Time.deltaTime;

			foreach (var waveTracker in pendingWaves)
			{
				if (!waveTracker.warningTriggered && timeElapsed >= (waveTracker.data.startTime - waveTracker.data.warningDuration))
				{
					waveTracker.warningTriggered = true;
					PrepareAndWarnWave(waveTracker);
				}

				if (!waveTracker.waveTriggered && timeElapsed >= waveTracker.data.startTime)
				{
					waveTracker.waveTriggered = true;

					if (!waveTracker.warningTriggered)
					{
						waveTracker.warningTriggered = true;
						PrepareAndWarnWave(waveTracker);
					}

					StartCoroutine(ExecuteWave(waveTracker));
				}
			}

			pendingWaves.RemoveAll(w => w.waveTriggered);

			if (pendingWaves.Count == 0)
			{
				allWavesTriggered = true;
			}

			IsTimelineSpawningFinished = allWavesTriggered && (activeSpawnCoroutines == 0);

			yield return null;
		}
	}

	private void PrepareAndWarnWave(TrackedWave waveTracker)
	{
		HashSet<SpawnPointMarker> warnedSpawners = new HashSet<SpawnPointMarker>();

		foreach (var action in waveTracker.data.actions)
		{
			RuntimeWaveAction runtimeAction = new RuntimeWaveAction { actionData = action };

			if (action.spawnGroup == SpawnGroup.Any)
			{
				var dayPoints = allSpawnPoints.Where(sp => !sp.isNightSpawn).ToList();
				if (dayPoints.Count > 0)
				{
					runtimeAction.chosenSpawners.Add(dayPoints[Random.Range(0, dayPoints.Count)]);
				}
			}
			else if (action.spawnGroup == SpawnGroup.All)
			{
				runtimeAction.chosenSpawners.AddRange(allSpawnPoints.Where(sp => !sp.isNightSpawn));
			}
			else
			{
				runtimeAction.chosenSpawners = allSpawnPoints.Where(sp => sp.group == action.spawnGroup).ToList();
			}

			waveTracker.runtimeActions.Add(runtimeAction);

			if (indicatorPrefab != null)
			{
				foreach (var sp in runtimeAction.chosenSpawners)
				{
					if (!warnedSpawners.Contains(sp))
					{
						warnedSpawners.Add(sp);
						Vector3 pos = sp.transform.position + Vector3.up * indicatorHeight;
						GameObject indicator = Instantiate(indicatorPrefab, pos, indicatorPrefab.transform.rotation);

						SpawnIndicator logic = indicator.GetComponent<SpawnIndicator>();
						if (logic != null) logic.isPlanningIndicator = false;

						Destroy(indicator, waveTracker.data.warningDuration);
					}
				}
			}
		}

		Feedback.Play(FeedbackEvent.WaveWarning);
	}

	private IEnumerator ExecuteWave(TrackedWave waveTracker)
	{
		foreach (var runtimeAction in waveTracker.runtimeActions)
		{
			StartCoroutine(ExecuteWaveAction(runtimeAction));
		}
		yield return null;
	}

	private IEnumerator ExecuteWaveAction(RuntimeWaveAction runtimeAction)
	{
		activeSpawnCoroutines++;

		if (runtimeAction.actionData.delayBeforeStart > 0f)
		{
			yield return new WaitForSeconds(runtimeAction.actionData.delayBeforeStart);
		}

		List<SpawnPointMarker> validPoints = runtimeAction.chosenSpawners;

		if (validPoints.Count == 0)
		{
			Debug.LogWarning($"[LevelManager] No spawn points found for group {runtimeAction.actionData.spawnGroup}. Spawn skipped.");
			activeSpawnCoroutines--;
			yield break;
		}

		int finalSpawnCount = runtimeAction.actionData.GetRandomCount();

		for (int i = 0; i < finalSpawnCount; i++)
		{
			Transform spawnPoint = validPoints[Random.Range(0, validPoints.Count)].transform;
			GameObject prefabToUse = runtimeAction.actionData.zombiePrefab != null ? runtimeAction.actionData.zombiePrefab : zombiePrefab;

			if (ZombiePool.Instance != null && prefabToUse == zombiePrefab)
			{
				ZombiePool.Instance.Get(spawnPoint.position, Quaternion.identity);
			}
			else
			{
				Instantiate(prefabToUse, spawnPoint.position, Quaternion.identity);
			}
			DaySpawnCount++;
			v2SpawnedThisWave++;

			if (i < finalSpawnCount - 1)
			{
				switch (runtimeAction.actionData.pattern)
				{
					case SpawnPattern.Linear:
						yield return new WaitForSeconds(runtimeAction.actionData.spawnInterval);
						break;

					case SpawnPattern.Burst:
						yield return new WaitForSeconds(0.05f);
						break;

					case SpawnPattern.RandomInterval:
						yield return new WaitForSeconds(Random.Range(0.1f, runtimeAction.actionData.spawnInterval));
						break;
				}
			}
		}

		activeSpawnCoroutines--;
	}

	// Core Loop v2 (Docs/CORE_LOOP_V2.md Phase 3): waves play in list order (WaveData.startTime is
	// ignored -- only each action's own delayBeforeStart/pattern/interval still applies, relative
	// to the wave's own start). Reuses PrepareAndWarnWave/ExecuteWaveAction unchanged -- only the
	// scheduling around them is new.
	private IEnumerator WaveSequenceRoutineV2()
	{
		V2TotalWaves = currentData.waves.Count;
		V2CurrentWaveNumber = 0;

		CoreLoopV2Config cfg = CoreLoopV2Config.Active;
		float waveMaxDuration = cfg != null ? cfg.waveMaxDuration : 25f;
		float breatherDuration = cfg != null ? cfg.breatherDuration : 6f;

		for (int i = 0; i < currentData.waves.Count; i++)
		{
			WaveData wave = currentData.waves[i];
			if (wave == null) continue;
			if (GameManager.Instance.State != GameManager.GameState.Playing) yield break;

			V2CurrentWaveNumber = i + 1;
			V2NextWaveHint = "";
			OnAnyWaveStarted?.Invoke(V2CurrentWaveNumber, V2TotalWaves);

			var tracker = new TrackedWave { data = wave };
			v2SpawnedThisWave = 0;
			PrepareAndWarnWave(tracker);
			yield return new WaitForSeconds(wave.warningDuration);

			if (GameManager.Instance.State != GameManager.GameState.Playing) yield break;

			foreach (var runtimeAction in tracker.runtimeActions)
				StartCoroutine(ExecuteWaveAction(runtimeAction));

			// Sequential model: no two waves' actions ever run concurrently, so
			// activeSpawnCoroutines==0 unambiguously means THIS wave finished issuing spawns.
			yield return new WaitUntil(() => activeSpawnCoroutines == 0);

			float sinceSpawningDone = 0f;
			bool cleared = false;
			while (sinceSpawningDone < waveMaxDuration)
			{
				// A wave can only be "cleared" once it has actually spawned something -- otherwise a
				// misconfigured/empty wave (no matching spawn points, zero-count action) would hit
				// activeSpawnCoroutines==0 with zero zombies ever having existed and instantly report
				// a false "WAVE CLEARED".
				if (v2SpawnedThisWave > 0 && Zombie.AllZombies.Count == 0) { cleared = true; break; }
				if (GameManager.Instance.State != GameManager.GameState.Playing) yield break;
				sinceSpawningDone += Time.deltaTime;
				yield return null;
			}

			if (cleared)
			{
				V2WavesClearedCount++;
				SupplyManager.Instance?.AddWaveClearedBonus();

				// On levels with the Supply HUD hidden, the "+2" means nothing to the player.
				bool showSupplyNote = currentData.v2ShowSupplyHud;
				CenterBannerV2.Show(showSupplyNote ? "WAVE CLEARED +2" : "WAVE CLEARED");
			}
			else
			{
				V2WavesForcedCount++;
				CenterBannerV2.Show("NEXT WAVE INCOMING");
			}

			bool hasNext = i + 1 < currentData.waves.Count;
			if (hasNext)
			{
				WaveData next = currentData.waves[i + 1];
				V2NextWaveHint = BuildNextWaveHintV2(next);
				PreviewNextWaveSpawnPointsV2(next, breatherDuration);
			}

			V2InBreather = true;
			float breatherTimer = 0f;
			while (breatherTimer < breatherDuration)
			{
				if (GameManager.Instance.State != GameManager.GameState.Playing) { V2InBreather = false; yield break; }
				V2BreatherRemaining = breatherDuration - breatherTimer;
				breatherTimer += Time.deltaTime;
				yield return null;
			}
			V2InBreather = false;
		}

		allWavesTriggered = true;
		IsTimelineSpawningFinished = true;

		if (GameManager.Instance.State == GameManager.GameState.Playing)
			GameManager.Instance.StartV2Night();
	}

	private string BuildNextWaveHintV2(WaveData next)
	{
		int min = 0, max = 0;
		foreach (var a in next.actions) { min += a.minCount; max += a.maxCount; }
		return min == max ? $"NEXT WAVE: ~{min} zombies" : $"NEXT WAVE: ~{min}-{max} zombies";
	}

	// Breather-only preview of the upcoming wave's spawn points -- resolves groups the same way
	// PrepareAndWarnWave does, but doesn't execute any spawn and doesn't play the warning sound
	// (that still fires once, for real, when the wave itself warns).
	private void PreviewNextWaveSpawnPointsV2(WaveData next, float duration)
	{
		if (indicatorPrefab == null) return;

		HashSet<SpawnPointMarker> shown = new HashSet<SpawnPointMarker>();

		foreach (var action in next.actions)
		{
			List<SpawnPointMarker> points;

			if (action.spawnGroup == SpawnGroup.Any)
			{
				var dayPoints = allSpawnPoints.Where(sp => !sp.isNightSpawn).ToList();
				points = dayPoints.Count > 0
					? new List<SpawnPointMarker> { dayPoints[Random.Range(0, dayPoints.Count)] }
					: new List<SpawnPointMarker>();
			}
			else if (action.spawnGroup == SpawnGroup.All)
			{
				points = allSpawnPoints.Where(sp => !sp.isNightSpawn).ToList();
			}
			else
			{
				points = allSpawnPoints.Where(sp => sp.group == action.spawnGroup).ToList();
			}

			foreach (var sp in points)
			{
				if (!shown.Add(sp)) continue;

				Vector3 pos = sp.transform.position + Vector3.up * indicatorHeight;
				GameObject indicator = Instantiate(indicatorPrefab, pos, indicatorPrefab.transform.rotation);

				SpawnIndicator logic = indicator.GetComponent<SpawnIndicator>();
				if (logic != null) logic.isPlanningIndicator = false;

				Destroy(indicator, duration);
			}
		}
	}

	public void StartSuddenDeath()
	{
		isTimelineRunning = false;
		if (timelineCoroutine != null) StopCoroutine(timelineCoroutine);

		if (indicatorPrefab != null)
		{
			foreach (var sp in nightSpawnPoints)
			{
				Vector3 pos = sp.transform.position + Vector3.up * indicatorHeight;
				GameObject indicator = Instantiate(indicatorPrefab, pos, indicatorPrefab.transform.rotation);

				SpawnIndicator logic = indicator.GetComponent<SpawnIndicator>();
				if (logic != null) logic.isPlanningIndicator = false;

				Destroy(indicator, 3f);
			}
		}

		StartCoroutine(SuddenDeathRoutine());
		StartCoroutine(NightTransitionRoutine());
	}

	private IEnumerator NightTransitionRoutine()
	{
		if (sunLight == null) yield break;

		float transitionTime = 2f;
		float t = 0;
		float startAmbient = RenderSettings.ambientIntensity;

		while (t < 1)
		{
			t += Time.deltaTime / transitionTime;
			sunLight.color = Color.Lerp(dayColor, nightColor, t);
			sunLight.intensity = Mathf.Lerp(dayIntensity, nightIntensity, t);
			RenderSettings.ambientIntensity = Mathf.Lerp(startAmbient, 0.1f, t);
			yield return null;
		}
	}

	private IEnumerator SuddenDeathRoutine()
	{
		while (GameManager.Instance.State == GameManager.GameState.SuddenDeath)
		{
			yield return new WaitForSeconds(currentData.suddenDeathSpawnRate);

			if (nightSpawnPoints.Count <= 0) continue;

			Transform spawnPoint = nightSpawnPoints[Random.Range(0, nightSpawnPoints.Count)].transform;

			if (ZombiePool.Instance != null)
			{
				ZombiePool.Instance.Get(spawnPoint.position, Quaternion.identity);
			}
			else
			{
				Instantiate(zombiePrefab, spawnPoint.position, Quaternion.identity);
			}
		}
	}
}