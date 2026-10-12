using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using DG.Tweening;

// Core Loop v2 time-limited Night / "Last Call" (Docs/CORE_LOOP_V2.md Phase 4). Drives the
// countdown value (displayed by TopHudV2's left region, not built here -- HUD rebuild moved every
// persistent v2 panel into the shared zone system), spawns the free one-shot "LAST TRANSPORT"
// card into the hand with a glow/pulse until placed, and auto-places it as a safety net if the
// player doesn't. Scene-scoped, rebuilt per level, same pattern as the other v2 controllers.
public class NightLastCallController : MonoBehaviour
{
	public static NightLastCallController Instance { get; private set; }

	/// <summary>Absolute Time.time the Last Transport departs at. Read by HelicopterController.ConfigureAsLastTransport callers.</summary>
	public float DepartAtTime { get; private set; }

	/// <summary>Seconds left in Last Call. Read by TopHudV2 for the countdown display.</summary>
	public float LastCallRemaining { get; private set; }

	public bool IsLastCallActive { get; private set; }

	// Per-attempt instrumentation (Docs/CORE_LOOP_V2.md Phase 7 / PlaytestLog), reset in
	// HandleLevelLoaded.
	public bool NightReached { get; private set; }
	public float LastTransportPlacedAt { get; private set; } = -1f;
	public bool LastTransportWasAutoPlaced { get; private set; }
	public int RescuedInLastCall { get; private set; }

	private bool placed;
	private Coroutine countdownCoroutine;
	private Coroutine autoPlaceCoroutine;
	private CardData lastTransportCardData;
	private CardUI spawnedCardUi;
	private Tween cardGlowTween;

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
		NightReached = false;
		IsLastCallActive = false;
		LastCallRemaining = 0f;
		LastTransportPlacedAt = -1f;
		LastTransportWasAutoPlaced = false;
		RescuedInLastCall = 0;
		cardGlowTween?.Kill();

		bool v2 = data != null && data.useCoreLoopV2;
		CoreLoopV2Config cfg = CoreLoopV2Config.Active;
		lastTransportCardData = v2 && cfg != null ? cfg.lastTransportCard : null;
	}

	// Called from GameManager.StartV2Night() once all waves finish.
	public void BeginLastCall()
	{
		if (!CoreLoopV2.IsActive) return;

		NightReached = true;
		IsLastCallActive = true;

		CoreLoopV2Config cfg = CoreLoopV2Config.Active;
		float duration = cfg != null ? cfg.lastCallDuration : 18f;
		float autoPlaceDelay = cfg != null ? cfg.lastTransportAutoPlaceDelay : 6f;

		DepartAtTime = Time.time + duration;
		LastCallRemaining = duration;

		if (lastTransportCardData != null && lastTransportCardData.uiButtonPrefab != null && CardManager.Instance != null)
		{
			spawnedCardUi = CardManager.Instance.SpawnTemporaryCard(lastTransportCardData, lastTransportCardData.uiButtonPrefab);
			FixSpawnedCardTutorialTarget();
			StartCardGlow();
		}

		countdownCoroutine = StartCoroutine(CountdownRoutine(duration));
		autoPlaceCoroutine = StartCoroutine(AutoPlaceRoutine(autoPlaceDelay));
	}

	// The spawned card is a clone of the regular Helicopter card's uiButtonPrefab, so it inherits
	// that prefab's TutorialTarget ("Card_Helicopter") -- wrong if a real Helicopter card is also
	// in the hand. Give this instance its own distinct id instead.
	private void FixSpawnedCardTutorialTarget()
	{
		if (spawnedCardUi == null) return;

		foreach (TutorialTarget existing in spawnedCardUi.GetComponents<TutorialTarget>())
			Destroy(existing);

		TutorialTarget target = spawnedCardUi.gameObject.AddComponent<TutorialTarget>();
		target.targetId = "Card_LastTransport";
	}

	private void StartCardGlow()
	{
		if (spawnedCardUi == null) return;

		cardGlowTween?.Kill();
		Transform t = spawnedCardUi.transform;
		t.localScale = Vector3.one;
		cardGlowTween = t.DOScale(1.08f, 0.5f).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine).SetUpdate(true);
	}

	private void StopCardGlow()
	{
		cardGlowTween?.Kill();
		cardGlowTween = null;
		if (spawnedCardUi != null) spawnedCardUi.transform.localScale = Vector3.one;
	}

	// Called by InputManager right after the player manually places the Last Transport card.
	public void NotifyPlaced()
	{
		placed = true;
		LastTransportPlacedAt = Time.time;
		StopCardGlow();
		if (autoPlaceCoroutine != null) { StopCoroutine(autoPlaceCoroutine); autoPlaceCoroutine = null; }
	}

	// Called by HelicopterController.TakeOff() when the Last Transport departs.
	public void OnTransportDeparted(int rescuedCount)
	{
		RescuedInLastCall = rescuedCount;
		IsLastCallActive = false;
		StopCardGlow();
		if (countdownCoroutine != null) { StopCoroutine(countdownCoroutine); countdownCoroutine = null; }
		if (autoPlaceCoroutine != null) { StopCoroutine(autoPlaceCoroutine); autoPlaceCoroutine = null; }
	}

	private IEnumerator CountdownRoutine(float duration)
	{
		float remaining = duration;
		while (remaining > 0f)
		{
			LastCallRemaining = remaining;
			yield return null;
			remaining -= Time.deltaTime;
		}
		LastCallRemaining = 0f;
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
		LastTransportWasAutoPlaced = true;
		LastTransportPlacedAt = Time.time;
		StopCardGlow();
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
}
