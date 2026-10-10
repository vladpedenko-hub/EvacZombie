using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// Dev-only playtest logger. See Docs/CLAUDE_CODE_TASK_LevelProgression.md §7.
// Appends one JSON line per level attempt to Application.persistentDataPath/playtest_log.jsonl.
// Polls GameManager.State transitions rather than adding new events there, to keep
// GameManager/LevelManager untouched; the one unavoidable hook is CardUI.OnCardPlaced (a card
// placement is a discrete, fast event that polling could miss or double count).
// [RELEASE]: the whole body is compiled out unless UNITY_EDITOR or DEBUG is defined -- same
// pattern as CheatManager.cs.
public class PlaytestLog : MonoBehaviour
{
#if UNITY_EDITOR || DEBUG
	public static PlaytestLog Instance;

	// Any dev/meta code can call this when the player buys an upgrade/building/research node.
	// Not wired to CardInfoPopup/BaseView/MetaSkillTreeScreen yet (would be three more small
	// hooks) -- see Proposals in LEVEL_BALANCE.md. Safe to call from anywhere; counts toward the
	// *next* attempt's metaActionsSinceLastAttempt.
	public static void NotifyMetaAction() => pendingMetaActions++;
	private static int pendingMetaActions = 0;

	[Serializable]
	private class CardPlacedEntry
	{
		public string id;
		public int count;
	}

	[Serializable]
	private class LogLine
	{
		public string timestamp;
		public string sessionId;
		public string level;
		public int attemptNumber;
		public string result; // win | lose | quit
		public int stars;
		public int rescued;
		public int required;
		public int initialResidents;
		public float durationDay;
		public float durationTotal;
		public List<CardPlacedEntry> cardsPlaced = new List<CardPlacedEntry>();
		public string[] deck = new string[5];
		public List<string> cardLevelKeys = new List<string>();
		public List<int> cardLevelValues = new List<int>();
		public int peopleBefore;
		public int peopleAfter;
		public int scientistsBefore;
		public int scientistsAfter;
		public int metaActionsSinceLastAttempt;
	}

	private readonly string sessionId = Guid.NewGuid().ToString("N").Substring(0, 8);
	private readonly Dictionary<string, int> cardsPlacedThisAttempt = new Dictionary<string, int>();

	private GameManager.GameState lastState = GameManager.GameState.Planning;
	private string attemptLevelName;
	private int attemptNumber;
	private int peopleBefore;
	private int scientistsBefore;
	private float attemptStartTime;
	private bool dayEnded;
	private float dayEndedAt;
	private bool attemptInProgress;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ResetStatics() { Instance = null; pendingMetaActions = 0; }

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Bootstrap()
	{
		if (Instance != null) return;
		var go = new GameObject("[PlaytestLog]");
		DontDestroyOnLoad(go);
		Instance = go.AddComponent<PlaytestLog>();
	}

	private void Awake()
	{
		if (Instance != null && Instance != this) { Destroy(gameObject); return; }
		Instance = this;
		CardUI.OnCardPlaced += HandleCardPlaced;
	}

	private void OnDestroy()
	{
		CardUI.OnCardPlaced -= HandleCardPlaced;
		if (attemptInProgress) WriteLine("quit", 0);
	}

	private void HandleCardPlaced(CardManager.CardType type)
	{
		string id = type.ToString();
		cardsPlacedThisAttempt.TryGetValue(id, out int c);
		cardsPlacedThisAttempt[id] = c + 1;
	}

	private void Update()
	{
		if (GameManager.Instance == null || LevelManager.Instance == null) return;

		GameManager.GameState state = GameManager.Instance.State;

		if (lastState != GameManager.GameState.Playing && state == GameManager.GameState.Playing)
		{
			BeginAttempt();
		}
		else if (attemptInProgress && lastState != GameManager.GameState.SuddenDeath && state == GameManager.GameState.SuddenDeath)
		{
			dayEnded = true;
			dayEndedAt = Time.time - attemptStartTime;
		}
		else if (attemptInProgress && state == GameManager.GameState.Lose && lastState != GameManager.GameState.Lose)
		{
			WriteLine("lose", 0);
		}
		else if (attemptInProgress && state == GameManager.GameState.GameOver && lastState != GameManager.GameState.GameOver)
		{
			LevelData level = LevelManager.Instance.currentData;
			int stars = (level != null && PlayerProfile.Instance != null) ? PlayerProfile.Instance.GetSavedStarsForLevel(level) : 0;
			WriteLine("win", stars);
		}

		lastState = state;
	}

	private void BeginAttempt()
	{
		attemptInProgress = true;
		cardsPlacedThisAttempt.Clear();
		attemptLevelName = LevelManager.Instance.currentData != null ? LevelManager.Instance.currentData.name : "unknown";
		attemptNumber = PlayerPrefs.GetInt("PlaytestAttempt_" + attemptLevelName, 0) + 1;
		PlayerPrefs.SetInt("PlaytestAttempt_" + attemptLevelName, attemptNumber);

		peopleBefore = PlayerProfile.Instance != null ? PlayerProfile.Instance.totalCurrency : 0;
		scientistsBefore = PlayerProfile.Instance != null ? PlayerProfile.Instance.totalScientistsCurrency : 0;
		attemptStartTime = Time.time;
		dayEnded = false;
		dayEndedAt = 0f;
	}

	private void WriteLine(string result, int stars)
	{
		attemptInProgress = false;

		GameManager gm = GameManager.Instance;
		var line = new LogLine
		{
			timestamp = DateTime.UtcNow.ToString("o"),
			sessionId = sessionId,
			level = attemptLevelName,
			attemptNumber = attemptNumber,
			result = result,
			stars = stars,
			rescued = gm != null ? gm.rescuedHumans + gm.rescuedScientists : 0,
			required = gm != null ? gm.requiredHumans : 0,
			initialResidents = gm != null ? gm.totalHumans + gm.totalScientists : 0,
			durationDay = dayEnded ? dayEndedAt : Time.time - attemptStartTime,
			durationTotal = Time.time - attemptStartTime,
			peopleBefore = peopleBefore,
			peopleAfter = PlayerProfile.Instance != null ? PlayerProfile.Instance.totalCurrency : 0,
			scientistsBefore = scientistsBefore,
			scientistsAfter = PlayerProfile.Instance != null ? PlayerProfile.Instance.totalScientistsCurrency : 0,
			metaActionsSinceLastAttempt = pendingMetaActions
		};
		pendingMetaActions = 0;

		foreach (var kv in cardsPlacedThisAttempt)
			line.cardsPlaced.Add(new CardPlacedEntry { id = kv.Key, count = kv.Value });

		if (PlayerProfile.Instance != null)
		{
			for (int i = 0; i < line.deck.Length && i < PlayerProfile.Instance.currentDeck.Length; i++)
				line.deck[i] = PlayerProfile.Instance.currentDeck[i] != null ? PlayerProfile.Instance.currentDeck[i].name : null;

			foreach (var progress in PlayerProfile.Instance.ownedCardsProgress)
			{
				line.cardLevelKeys.Add(progress.cardId);
				line.cardLevelValues.Add(progress.currentLevel);
			}
		}

		string path = Path.Combine(Application.persistentDataPath, "playtest_log.jsonl");
		File.AppendAllText(path, JsonUtility.ToJson(line) + "\n");
	}

	// Called by the dev debug panel's Export button (CheatManager) to hand the log off.
	public static string ExportToClipboard()
	{
		string path = Path.Combine(Application.persistentDataPath, "playtest_log.jsonl");
		string contents = File.Exists(path) ? File.ReadAllText(path) : "";
		GUIUtility.systemCopyBuffer = contents;
		return path;
	}
#endif
}
