using UnityEngine;
using Unity.AI.Navigation;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class LevelManager : MonoBehaviour
{
	public static LevelManager Instance;

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

		AbilityManager.Instance?.Initialize(data);

		if (currentLevelEnvironment != null)
			Destroy(currentLevelEnvironment);

		currentLevelEnvironment = Instantiate(data.levelPrefab, Vector3.zero, Quaternion.identity);

		if (CameraController.Instance != null)
			CameraController.Instance.SetupCamera(data);

		if (navSurface != null)
			navSurface.BuildNavMesh();

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

		// --- START TUTORIAL IMMEDIATELY ON LEVEL START ---
		if (currentData != null && currentData.onStartTutorial != null && TutorialManager.Instance != null)
		{
			TutorialManager.Instance.StartTutorial(currentData.onStartTutorial);
		}
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
		timelineCoroutine = StartCoroutine(WaveTimelineRoutine());
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