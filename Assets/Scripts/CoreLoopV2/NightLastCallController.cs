using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.AI;

// Core Loop v2 time-limited Night / "Last Call" (Docs/CORE_LOOP_V2.md Phase 4). Shows the
// countdown, spawns the free one-shot "LAST TRANSPORT" card into the hand, and auto-places it as
// a safety net if the player doesn't. Scene-scoped, rebuilt per level, same pattern as the other
// v2 controllers.
public class NightLastCallController : MonoBehaviour
{
	public static NightLastCallController Instance { get; private set; }

	/// <summary>Absolute Time.time the Last Transport departs at. Read by HelicopterController.ConfigureAsLastTransport callers.</summary>
	public float DepartAtTime { get; private set; }

	private RectTransform root;
	private TextMeshProUGUI countdownText;
	private bool builtUi;
	private bool placed;
	private Coroutine countdownCoroutine;
	private Coroutine autoPlaceCoroutine;
	private CardData lastTransportCardData;
	private CardUI spawnedCardUi;

	private void Awake()
	{
		if (Instance != null && Instance != this) { Destroy(gameObject); return; }
		Instance = this;
	}

	private void OnDestroy()
	{
		if (Instance == this) Instance = null;
	}

	// Called from LevelManager.LoadLevel() -- the sole initialization entry point.
	public static void OnLevelLoaded(LevelData data)
	{
		if (Instance == null)
		{
			var go = new GameObject("[NightLastCallController]");
			go.AddComponent<NightLastCallController>();
		}

		Instance.HandleLevelLoaded(data);
	}

	private void HandleLevelLoaded(LevelData data)
	{
		if (countdownCoroutine != null) { StopCoroutine(countdownCoroutine); countdownCoroutine = null; }
		if (autoPlaceCoroutine != null) { StopCoroutine(autoPlaceCoroutine); autoPlaceCoroutine = null; }
		placed = false;
		spawnedCardUi = null;

		if (builtUi) root.gameObject.SetActive(false);

		bool v2 = data != null && data.useCoreLoopV2;
		CoreLoopV2Config cfg = CoreLoopV2Config.Active;
		lastTransportCardData = v2 && cfg != null ? cfg.lastTransportCard : null;
	}

	// Called from GameManager.StartV2Night() once all waves finish.
	public void BeginLastCall()
	{
		if (!CoreLoopV2.IsActive) return;

		BuildUiIfNeeded();

		CoreLoopV2Config cfg = CoreLoopV2Config.Active;
		float duration = cfg != null ? cfg.lastCallDuration : 18f;
		float autoPlaceDelay = cfg != null ? cfg.lastTransportAutoPlaceDelay : 6f;

		DepartAtTime = Time.time + duration;

		if (lastTransportCardData != null && lastTransportCardData.uiButtonPrefab != null && CardManager.Instance != null)
			spawnedCardUi = CardManager.Instance.SpawnTemporaryCard(lastTransportCardData, lastTransportCardData.uiButtonPrefab);

		root.gameObject.SetActive(true);
		countdownCoroutine = StartCoroutine(CountdownRoutine(duration));
		autoPlaceCoroutine = StartCoroutine(AutoPlaceRoutine(autoPlaceDelay));
	}

	// Called by InputManager right after the player manually places the Last Transport card.
	public void NotifyPlaced()
	{
		placed = true;
		if (autoPlaceCoroutine != null) { StopCoroutine(autoPlaceCoroutine); autoPlaceCoroutine = null; }
	}

	// Called by HelicopterController.TakeOff() when the Last Transport departs.
	public void OnTransportDeparted()
	{
		if (countdownCoroutine != null) { StopCoroutine(countdownCoroutine); countdownCoroutine = null; }
		if (autoPlaceCoroutine != null) { StopCoroutine(autoPlaceCoroutine); autoPlaceCoroutine = null; }
		if (builtUi) root.gameObject.SetActive(false);
	}

	private IEnumerator CountdownRoutine(float duration)
	{
		float remaining = duration;
		while (remaining > 0f)
		{
			if (countdownText != null) countdownText.text = "LAST CALL: " + Mathf.CeilToInt(remaining) + "s";
			yield return null;
			remaining -= Time.deltaTime;
		}
		if (countdownText != null) countdownText.text = "LAST CALL: 0s";
	}

	private IEnumerator AutoPlaceRoutine(float delay)
	{
		yield return new WaitForSeconds(delay);
		if (!placed) AutoPlaceLastTransport();
	}

	// Safety net (brief Phase 4): places the Last Transport at the NavMesh ground point nearest
	// the centroid of still-living civilians if the player hasn't placed it themselves.
	private void AutoPlaceLastTransport()
	{
		if (placed || lastTransportCardData == null || lastTransportCardData.cardPrefab == null) return;

		Vector3 centroid = ComputeCivilianCentroid();
		Vector3 spawnPos = centroid;

		if (NavMesh.SamplePosition(centroid, out NavMeshHit hit, 50f, NavMesh.AllAreas))
			spawnPos = hit.position;

		GameObject go = Instantiate(lastTransportCardData.cardPrefab);
		HelicopterController heli = go.GetComponent<HelicopterController>();
		if (heli == null) { Destroy(go); return; }

		heli.Launch(spawnPos);
		heli.ConfigureAsLastTransport(DepartAtTime);

		placed = true;
		if (spawnedCardUi != null) spawnedCardUi.StartCooldown();
	}

	private Vector3 ComputeCivilianCentroid()
	{
		Vector3 sum = Vector3.zero;
		int count = 0;

		foreach (var h in Human.AllHumans) { if (h == null) continue; sum += h.transform.position; count++; }
		foreach (var s in Scientist.AllScientists) { if (s == null) continue; sum += s.transform.position; count++; }

		return count > 0 ? sum / count : Vector3.zero;
	}

	private void BuildUiIfNeeded()
	{
		if (builtUi) return;

		Transform safeArea = FindSafeAreaContainer();

		root = RuntimeUiKit.CreateRect("LastCallCountdownV2", safeArea);
		root.anchorMin = new Vector2(0.5f, 0.5f);
		root.anchorMax = new Vector2(0.5f, 0.5f);
		root.pivot = new Vector2(0.5f, 0.5f);
		root.sizeDelta = new Vector2(520f, 90f);
		root.anchoredPosition = new Vector2(0f, 420f);

		countdownText = RuntimeUiKit.CreateText("Countdown", root, "", 40f, new Color(1f, 0.3f, 0.3f), TextAlignmentOptions.Center, FontStyles.Bold);
		RuntimeUiKit.Stretch(countdownText.rectTransform);

		builtUi = true;
	}

	private Transform FindSafeAreaContainer()
	{
		GameObject go = GameObject.Find("SafeAreaContainer");
		return go != null ? go.transform : null;
	}
}
