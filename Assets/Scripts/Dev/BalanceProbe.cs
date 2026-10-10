using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;

// Dev-only balance measurement tool. See Docs/CLAUDE_CODE_TASK_LevelProgression.md §4.1.
// Runs a level with zero player input at accelerated Time.timeScale and logs, per run:
// t25/t50/t75 (seconds from StartGame() until that fraction of initial residents are infected),
// daySpawns, peakZombies, dayLength, totalLength, bossSpawnTime. Writes one CSV row per run to
// Application.persistentDataPath/balance_probe.csv.
// [RELEASE]: the whole body is compiled out unless UNITY_EDITOR or DEBUG is defined -- same
// pattern as CheatManager.cs. Never attach this to a shipped scene's active hierarchy; it's meant
// to be added to a temporary GameObject at dev/probe time only.
public class BalanceProbe : MonoBehaviour
{
#if UNITY_EDITOR || DEBUG
	public static BalanceProbe Instance;

	[System.Serializable]
	public class RunResult
	{
		public string levelName;
		public int seed;
		public float t25 = -1f, t50 = -1f, t75 = -1f;
		public int daySpawns;
		public int peakZombies;
		public float dayLength = -1f;
		public float totalLength;
		public float bossSpawnTime = -1f;
	}

	[Tooltip("Time.timeScale while probing. Kept moderate for NavMesh/physics stability at high speed.")]
	public float probeTimeScale = 6f;

	[Tooltip("Safety net: abort a single run after this many scaled seconds (something's stuck, e.g. an unreachable resident).")]
	public float perRunTimeoutSeconds = 240f;

	public bool IsRunning { get; private set; }
	public readonly List<RunResult> AllResults = new List<RunResult>();

	private void Awake() => Instance = this;

	// Runs every (level, seed) pair in order and writes the CSV once at the end.
	public void RunBatch(List<LevelData> levels, int seedsPerLevel)
	{
		if (IsRunning) return;
		StartCoroutine(RunBatchRoutine(levels, seedsPerLevel));
	}

	private IEnumerator RunBatchRoutine(List<LevelData> levels, int seedsPerLevel)
	{
		IsRunning = true;
		float originalTimeScale = Time.timeScale;
		Time.timeScale = probeTimeScale;

		foreach (LevelData level in levels)
		{
			for (int seed = 0; seed < seedsPerLevel; seed++)
			{
				yield return RunOne(level, seed);
			}
		}

		Time.timeScale = originalTimeScale;
		WriteCsv();
		IsRunning = false;
	}

	// One full playthrough with no card ever placed: infection runs unopposed until every
	// resident is either infected (nothing is ever rescued) or the safety timeout trips.
	private IEnumerator RunOne(LevelData level, int seed)
	{
		Random.InitState(seed);

		if (InputManager.Instance != null) InputManager.Instance.IsPaused = true;

		Zombie.InfectedThisLevel = 0;
		LevelManager.DaySpawnCount = 0;

		LevelManager.Instance.LoadLevel(level);
		yield return null; // let the freshly-instantiated level's Awake/OnEnable settle first

		int initialResidents = Human.AllHumans.Count + Scientist.AllScientists.Count;

		GameManager.Instance.StartGame();
		float startTime = Time.time;

		var result = new RunResult { levelName = level.name, seed = seed };
		bool reachedSuddenDeath = false;

		while (true)
		{
			float elapsed = Time.time - startTime;

			if (initialResidents > 0)
			{
				float infectedFrac = (float)Zombie.InfectedThisLevel / initialResidents;
				if (result.t25 < 0f && infectedFrac >= 0.25f) result.t25 = elapsed;
				if (result.t50 < 0f && infectedFrac >= 0.50f) result.t50 = elapsed;
				if (result.t75 < 0f && infectedFrac >= 0.75f) result.t75 = elapsed;
			}

			result.peakZombies = Mathf.Max(result.peakZombies, Zombie.AllZombies.Count);

			if (result.bossSpawnTime < 0f)
			{
				foreach (Zombie z in Zombie.AllZombies)
				{
					if (z is ZombieBoss) { result.bossSpawnTime = elapsed; break; }
				}
			}

			if (!reachedSuddenDeath && GameManager.Instance.State == GameManager.GameState.SuddenDeath)
			{
				reachedSuddenDeath = true;
				result.dayLength = elapsed;
			}

			bool residentsGone = (Human.AllHumans.Count + Scientist.AllScientists.Count) == 0;
			bool timedOut = elapsed > perRunTimeoutSeconds;

			if (residentsGone || timedOut) break;

			yield return null;
		}

		result.daySpawns = LevelManager.DaySpawnCount;
		result.totalLength = Time.time - startTime;
		if (!reachedSuddenDeath) result.dayLength = result.totalLength;

		AllResults.Add(result);

		if (InputManager.Instance != null) InputManager.Instance.IsPaused = false;
	}

	private void WriteCsv()
	{
		string path = Path.Combine(Application.persistentDataPath, "balance_probe.csv");
		var sb = new System.Text.StringBuilder();
		sb.AppendLine("level,seed,t25,t50,t75,daySpawns,peakZombies,dayLength,totalLength,bossSpawnTime");

		foreach (RunResult r in AllResults)
		{
			sb.AppendLine(string.Join(",", new[]
			{
				r.levelName,
				r.seed.ToString(CultureInfo.InvariantCulture),
				r.t25.ToString(CultureInfo.InvariantCulture),
				r.t50.ToString(CultureInfo.InvariantCulture),
				r.t75.ToString(CultureInfo.InvariantCulture),
				r.daySpawns.ToString(CultureInfo.InvariantCulture),
				r.peakZombies.ToString(CultureInfo.InvariantCulture),
				r.dayLength.ToString(CultureInfo.InvariantCulture),
				r.totalLength.ToString(CultureInfo.InvariantCulture),
				r.bossSpawnTime.ToString(CultureInfo.InvariantCulture)
			}));
		}

		File.WriteAllText(path, sb.ToString());
		Debug.Log($"[BalanceProbe] Wrote {AllResults.Count} runs to {path}");
	}

	// Median / p10 / p90 of a field across all runs for one level, for quick console inspection
	// (LEVEL_BALANCE.md's numbers are copied from here / from the CSV).
	public static (float median, float p10, float p90) Stats(IEnumerable<float> values)
	{
		List<float> sorted = values.Where(v => v >= 0f).OrderBy(v => v).ToList();
		if (sorted.Count == 0) return (-1f, -1f, -1f);

		float At(float frac)
		{
			int idx = Mathf.Clamp(Mathf.RoundToInt(frac * (sorted.Count - 1)), 0, sorted.Count - 1);
			return sorted[idx];
		}

		return (At(0.5f), At(0.1f), At(0.9f));
	}
#endif
}
