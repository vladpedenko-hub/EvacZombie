using UnityEngine;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.AI;

public class HelicopterController : MonoBehaviour, IEvacPoint
{
	public enum HeliState { Landing, Loading, TakingOff }
	public HeliState currentState;

	// IEvacPoint: see CarController — only "active" for the street-AI F_evac field while Loading.
	public Vector3 EvacPosition => transform.position;
	public bool IsAcceptingPassengers => currentState == HeliState.Loading;

	private void OnEnable() => CrowdFieldManager.Instance?.RegisterEvacPoint(this);
	private void OnDisable() => CrowdFieldManager.Instance?.UnregisterEvacPoint(this);

	[Header("Card Data")]
	public CardData myCardData;

	[Header("Movement Settings")]
	public float buffRadius = 15f;
	public float exitHeight = 40f;

	[Header("Landing Zone Visuals")]
	public float landingRadius = 3f;
	public Color landingColor = new Color(0f, 1f, 0.2f, 0.5f);
	public GameObject customLandingPrefab;

	[Header("Boarding")]
	public float boardingRadius = 2.5f;
	public float boardingAnimDuration = 0.28f;
	public float boardingLiftHeight = 1.2f;

	[Tooltip("Radius within which zombies create boarding pressure (heat) and, inside hardAbortRadius, force an immediate takeoff. 0 (or the heli_no_panic run flag) disables both.")]
	public float panicRadius = 3f;

	public GameObject sirenRingPrefab;
	public GameObject humanAlertPrefab;
	public float alertDuration = 2.0f;

	[Header("UI")]
	public TextMeshProUGUI loadText;
	public GameObject hotWarning;

	private int maxCapacity;
	private float verticalSpeed;
	private float attractRadius;
	private float loadTime;
	private float boardingCooldown;

	private int currentLoad = 0;
	private int loadedHumanCount = 0;
	private int loadedScientistCount = 0;
	private Vector3 targetPos;
	private GameObject landingMarker;
	private bool isTooHot = false;
	private bool hasTakenOff = false;
	private bool fullLoadHandled = false;

	// feature/heli-rework: ease-out descent timing (Landing state), replacing the old fixed
	// verticalSpeed MoveTowards for every heli EXCEPT the Last Transport (see isLastTransport
	// branches below -- its descent is explicitly left unchanged).
	private Vector3 inboundStartPos;
	private float inboundElapsed;

	// feature/heli-rework: heat pressure (replaces the old instant panicRadius abort). Still
	// read/clamped every heatCheckInterval inside NormalLoadRoutine; hardAbortRadius sets isTooHot
	// the old way regardless of the heat multiplier.
	private float currentHeatMultiplier = 1f;

	// feature/heli-rework: parallel boarding throughput. reservedUnits holds units with a boarding
	// coroutine in flight (picked, but not yet in loadedUnits) so a second slot never double-picks
	// the same civilian.
	private int activeBoardingCoroutines = 0;
	private readonly HashSet<GameObject> reservedUnits = new HashSet<GameObject>();

	// Core Loop v2 (Docs/CORE_LOOP_V2.md Phase 4): set via ConfigureAsLastTransport when this
	// instance is the free, one-shot Night "LAST TRANSPORT" card.
	private bool isLastTransport = false;
	private float lastTransportDepartAtTime;

	private List<GameObject> loadedUnits = new List<GameObject>();

	// feature/heli-rework: live, per-call read of the shared tunables asset -- Inspector/Resources
	// edits apply immediately, same spirit as Zombie.cs's crowdTuning-backed properties.
	private HeliTuning Tuning => HeliTuning.Active;
	private float InboundTime => Tuning != null ? Mathf.Max(0.05f, Tuning.inboundTime) : 2.8f;
	private bool EnableLiveRescan => Tuning == null || Tuning.enableLiveRescan;
	private float RescanInterval => Tuning != null ? Mathf.Max(0.05f, Tuning.rescanInterval) : 0.5f;
	private int BoardingSlots => Tuning != null ? Mathf.Max(1, Tuning.boardingSlots) : 3;
	private float BoardingStagger => Tuning != null ? Mathf.Max(0f, Tuning.boardingStagger) : 0.2f;
	private bool EnableHeat => Tuning == null || Tuning.enableHeat;
	private float HeatRadius => Tuning != null ? Tuning.heatRadius : 5f;
	private float HeatPerZombiePerSec => Tuning != null ? Tuning.heatPerZombiePerSec : 1f;
	private float HeatMaxMultiplier => Tuning != null ? Mathf.Max(1f, Tuning.heatMaxMultiplier) : 3.5f;
	private float HeatBoardSlowFactor => Tuning != null ? Mathf.Max(0.01f, Tuning.heatBoardSlowFactor) : 0.5f;
	private float HardAbortRadius => Tuning != null ? Tuning.hardAbortRadius : 1.5f;
	private int FullLoadSupplyBonus => Tuning != null ? Tuning.fullLoadSupplyBonus : 1;

	private float EffectiveBoardingCooldown
	{
		get
		{
			float maxMult = HeatMaxMultiplier;
			float normalized = maxMult > 1f ? Mathf.Clamp01((currentHeatMultiplier - 1f) / (maxMult - 1f)) : 0f;
			float slow = Mathf.Lerp(1f, 1f / HeatBoardSlowFactor, normalized);
			return boardingCooldown * slow;
		}
	}

	private void Start()
	{
		int currentLevel = 1;
		if (PlayerProfile.Instance != null && myCardData != null)
		{
			var progress = PlayerProfile.Instance.ownedCardsProgress.Find(p => p.cardId == myCardData.name);
			if (progress != null) currentLevel = progress.currentLevel;

			maxCapacity = (int)myCardData.GetCalculatedStat(StatType.Capacity, currentLevel);
			verticalSpeed = myCardData.GetCalculatedStat(StatType.Speed, currentLevel);
			attractRadius = myCardData.GetCalculatedStat(StatType.Radius, currentLevel);
			loadTime = myCardData.GetCalculatedStat(StatType.Duration, currentLevel);
			boardingCooldown = myCardData.GetCalculatedStat(StatType.Cooldown, currentLevel);
		}

		if (maxCapacity <= 0) maxCapacity = 6;
		if (verticalSpeed <= 0) verticalSpeed = 15f;
		if (attractRadius <= 0) attractRadius = 12f;
		if (loadTime <= 0) loadTime = 15f;
		if (boardingCooldown <= 0) boardingCooldown = 0.5f;

		// Run-modifiers (roguelite)
		var run = RunSessionData.Instance;
		if (run != null)
		{
			maxCapacity += (int)run.GetModifier("heli_capacity_add");
			verticalSpeed *= (1f + run.GetModifier("heli_speed_mult"));
			attractRadius *= (1f + run.GetModifier("heli_radius_mult"));
			loadTime += run.GetModifier("heli_loadtime_add");
			boardingCooldown *= Mathf.Max(0.05f, 1f - run.GetModifier("heli_boarding_reduction"));

			if (run.HasFlag("heli_no_panic"))
				panicRadius = 0f;

			if (run.HasFlag("heli_instant_land"))
				verticalSpeed = 9999f;

			if (run.HasFlag("heli_unlimited_capacity"))
				maxCapacity = 999;
		}
	}

	// Core Loop v2 (Docs/CORE_LOOP_V2.md Phase 4): configures this instance as the free, one-shot
	// Night "LAST TRANSPORT". Called by InputManager right after Launch(). departAtTime is an
	// absolute Time.time value -- the transport stays loading until then regardless of capacity
	// or the normal load timer, then departs. panicRadius = 0 reuses the existing panic-radius
	// check (below) to mean "no too-hot abort" without a new branch.
	//
	// feature/heli-rework: isLastTransport also routes Update()'s Landing branch and LoadRoutine()
	// to their original, byte-for-byte unchanged paths -- none of the new inbound-easing/live-
	// rescan/heat/parallel-boarding logic applies to it.
	public void ConfigureAsLastTransport(float departAtTime)
	{
		isLastTransport = true;
		lastTransportDepartAtTime = departAtTime;

		maxCapacity = 9999;
		panicRadius = 0f;
		verticalSpeed = Mathf.Max(verticalSpeed, 9999f);

		CoreLoopV2Config cfg = CoreLoopV2Config.Active;
		boardingCooldown = cfg != null ? cfg.lastTransportBoardingInterval : 0.1f;
	}

	public void Launch(Vector3 pos)
	{
		if (maxCapacity == 0) Start();

		currentLoad = 0;
		loadedHumanCount = 0;
		loadedScientistCount = 0;
		isTooHot = false;
		hasTakenOff = false;
		fullLoadHandled = false;
		currentHeatMultiplier = 1f;
		activeBoardingCoroutines = 0;
		loadedUnits.Clear();
		reservedUnits.Clear();

		targetPos = pos;
		transform.position = new Vector3(pos.x, exitHeight, pos.z);
		inboundStartPos = transform.position;
		inboundElapsed = 0f;
		currentState = HeliState.Landing;

		if (hotWarning != null) hotWarning.SetActive(false);
		if (loadText != null)
		{
			loadText.gameObject.SetActive(true);
			loadText.text = "";
		}

		if (customLandingPrefab != null)
		{
			landingMarker = Instantiate(customLandingPrefab, new Vector3(pos.x, 0.1f, pos.z), Quaternion.identity);
		}
		else
		{
			landingMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
			landingMarker.transform.position = new Vector3(pos.x, 0.1f, pos.z);
			landingMarker.transform.localScale = new Vector3(landingRadius * 2, 0.01f, landingRadius * 2);
			Destroy(landingMarker.GetComponent<Collider>());

			// Not SharedMaterials.SpritesDefaultMaterial: this instance's .color is mutated below,
			// which would leak landingColor onto every other user of a shared material.
			Renderer r = landingMarker.GetComponent<Renderer>();
			r.material = new Material(SharedMaterials.SpritesDefaultShader);

			// feature/readability-palette: this landing-zone ring is the closest thing to an
			// "evac/transport marker" with a renderer in a generated city -- tinted with the
			// palette's evacMarkerColor there instead of the prefab's own landingColor. Null on
			// hand-built levels, so they keep landingColor unchanged.
			LevelPalette palette = LevelPaletteService.Active;
			r.material.color = palette != null ? palette.evacMarkerColor : landingColor;
		}

		// feature/heli-rework (step 2): civilians start running to the call point the moment the
		// card is placed, not at touchdown -- SetRescueTarget's own run speed means they're
		// visibly already headed this way well before the heli actually lands.
		ScanAndAttractCivilians();
	}

	private void Update()
	{
		if (currentState == HeliState.Landing)
		{
			if (isLastTransport)
			{
				// feature/heli-rework: unchanged -- the Last Transport keeps the original
				// speed-based descent (verticalSpeed is forced to >=9999 by ConfigureAsLastTransport,
				// so this still reads as an instant landing).
				transform.position = Vector3.MoveTowards(transform.position, new Vector3(targetPos.x, 1f, targetPos.z), verticalSpeed * Time.deltaTime);
				if (transform.position.y <= 1.1f) StartLoading();
			}
			else
			{
				// feature/heli-rework (step 2): descent now takes exactly InboundTime seconds,
				// eased out, instead of being driven by verticalSpeed.
				inboundElapsed += Time.deltaTime;
				float t = Mathf.Clamp01(inboundElapsed / InboundTime);
				float eased = 1f - (1f - t) * (1f - t);

				transform.position = Vector3.Lerp(inboundStartPos, new Vector3(targetPos.x, 1f, targetPos.z), eased);
				UpdateInboundCountdownRing(t);

				if (t >= 1f) StartLoading();
			}
		}
		else if (currentState == HeliState.TakingOff)
		{
			transform.position += Vector3.up * verticalSpeed * Time.deltaTime;
			if (transform.position.y > exitHeight)
			{
				Destroy(gameObject);
			}
		}
	}

	// feature/heli-rework (step 2): shrinks the landing marker over the inbound descent so it
	// reads as a countdown ring, reaching a small "about to land" size right as t hits 1.
	private void UpdateInboundCountdownRing(float t)
	{
		if (landingMarker == null) return;

		float scale = Mathf.Lerp(landingRadius * 2f, landingRadius * 2f * 0.12f, t);
		Vector3 s = landingMarker.transform.localScale;
		landingMarker.transform.localScale = new Vector3(scale, s.y, scale);
	}

	private void StartLoading()
	{
		currentState = HeliState.Loading;
		Feedback.Play(FeedbackEvent.HeliArrive, transform.position);

		// feature/heli-rework (step 4 visual): the landing marker survives touchdown now -- reset
		// from its shrunk countdown-ring size back to a full ground ring, which the heat check
		// below tints green->red for the rest of Loading. Destroyed in TakeOff() instead.
		if (landingMarker != null)
		{
			Vector3 s = landingMarker.transform.localScale;
			landingMarker.transform.localScale = new Vector3(landingRadius * 2f, s.y, landingRadius * 2f);
			Renderer r = landingMarker.GetComponent<Renderer>();
			if (r != null) r.material.color = Color.green;
		}

		if (loadText != null) loadText.gameObject.SetActive(true);

		if (sirenRingPrefab != null)
		{
			GameObject ring = Instantiate(sirenRingPrefab, transform.position + Vector3.up * 0.1f, Quaternion.Euler(90, 0, 0));
			ring.GetComponent<SirenEffect>()?.Setup(attractRadius, 1.2f);
		}

		// Ultimate: global attract - all civilians run to heli
		if (RunSessionData.Instance != null && RunSessionData.Instance.HasFlag("heli_global_attract"))
		{
			foreach (var h in Human.AllHumans)
			{
				if (h == null || h.rescueTarget != null) continue;
				h.SetRescueTarget(transform);
			}
			foreach (var s in Scientist.AllScientists)
			{
				if (s == null || s.rescueTarget != null) continue;
				s.SetRescueTarget(transform);
			}
		}

		// Core Loop v2 (Docs/CORE_LOOP_V2.md Phase 4): the Last Transport attracts every living
		// civilian unconditionally, independent of attractRadius and of the roguelite flag above.
		if (isLastTransport)
		{
			foreach (var h in Human.AllHumans)
			{
				if (h == null || h.rescueTarget != null) continue;
				h.SetRescueTarget(transform);
			}
			foreach (var s in Scientist.AllScientists)
			{
				if (s == null || s.rescueTarget != null) continue;
				s.SetRescueTarget(transform);
			}
		}

		// feature/heli-rework: the Last Transport keeps the original single-slot, non-heat
		// LoadRoutine byte-for-byte; every other heli gets the new feel.
		StartCoroutine(isLastTransport ? LastTransportLoadRoutine() : NormalLoadRoutine());
	}

	// feature/heli-rework (step 2/3): shared by the call-time scan (Launch) and the periodic
	// live-magnet rescan (NormalLoadRoutine) -- SetRescueTarget for anyone within attractRadius
	// that doesn't already have a rescue target, with the same alert-prefab visual either way.
	private void ScanAndAttractCivilians()
	{
		Vector2 heliPos2D = Flat(transform.position);

		foreach (var h in Human.AllHumans)
		{
			if (h == null || h.rescueTarget != null) continue;
			if (Vector2.Distance(heliPos2D, Flat(h.transform.position)) >= attractRadius) continue;

			h.SetRescueTarget(transform);
			SpawnAttractAlert(h.transform);
		}

		foreach (var s in Scientist.AllScientists)
		{
			if (s == null || s.rescueTarget != null) continue;
			if (Vector2.Distance(heliPos2D, Flat(s.transform.position)) >= attractRadius) continue;

			s.SetRescueTarget(transform);
			SpawnAttractAlert(s.transform);
		}
	}

	private void SpawnAttractAlert(Transform target)
	{
		if (humanAlertPrefab == null) return;
		GameObject alert = Instantiate(humanAlertPrefab, target.position + Vector3.up * 0.1f, Quaternion.Euler(90, 0, 0), target);
		Destroy(alert, alertDuration);
	}

	private static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);

	// feature/heli-rework: byte-for-byte the original LoadRoutine(), kept as its own method and
	// used ONLY for the Last Transport -- the brief requires its path stay unchanged while every
	// other heli gets the new inbound/rescan/heat/parallel-boarding feel below.
	private IEnumerator LastTransportLoadRoutine()
	{
		float waitTimer = 0;

		float boardCooldownTimer = boardingCooldown;
		float panicCheckTimer = 0f;
		const float panicCheckInterval = 0.2f;
		float panicRadiusSqr = panicRadius * panicRadius;

		while (isLastTransport ? Time.time < lastTransportDepartAtTime : (currentLoad < maxCapacity && !isTooHot && waitTimer < loadTime))
		{
			if (Time.timeScale < 0.001f)
			{
				yield return null;
				continue;
			}

			float dt = Time.deltaTime;
			waitTimer += dt;
			boardCooldownTimer += dt;
			panicCheckTimer += dt;

			if (panicCheckTimer >= panicCheckInterval)
			{
				panicCheckTimer = 0f;
				foreach (var z in Zombie.AllZombies)
				{
					if (z != null && (z.transform.position - transform.position).sqrMagnitude < panicRadiusSqr)
					{
						isTooHot = true;
						break;
					}
				}
			}

			if (isTooHot) break;

			if (boardCooldownTimer >= boardingCooldown)
			{
				bool boarded = false;

				foreach (var hum in Human.AllHumans)
				{
					if (hum == null || loadedUnits.Contains(hum.gameObject)) continue;

					Vector2 heliPos2D = new Vector2(transform.position.x, transform.position.z);
					Vector2 humanPos2D = new Vector2(hum.transform.position.x, hum.transform.position.z);

					if (Vector2.Distance(heliPos2D, humanPos2D) < boardingRadius)
					{
						yield return StartCoroutine(hum.PlayBoardingAnimation(transform.position, boardingAnimDuration, boardingLiftHeight));

						if (hum == null) break;

						loadedUnits.Add(hum.gameObject);

						currentLoad++;
						loadedHumanCount++;
						boarded = true;
						break;
					}
				}

				if (!boarded)
				{
					foreach (var sci in Scientist.AllScientists)
					{
						if (sci == null || loadedUnits.Contains(sci.gameObject)) continue;

						Vector2 heliPos2D = new Vector2(transform.position.x, transform.position.z);
						Vector2 scientistPos2D = new Vector2(sci.transform.position.x, sci.transform.position.z);

						if (Vector2.Distance(heliPos2D, scientistPos2D) < boardingRadius)
						{
							yield return StartCoroutine(sci.PlayBoardingAnimation(transform.position, boardingAnimDuration, boardingLiftHeight));

							if (sci == null) break;

							loadedUnits.Add(sci.gameObject);

							currentLoad++;
							loadedScientistCount++;
							boarded = true;
							break;
						}
					}
				}

				if (boarded)
				{
					if (loadText) loadText.text = currentLoad.ToString();
					boardCooldownTimer = 0f;

					if (!isLastTransport && AllCiviliansHandled())
						break;
				}
			}

			if (!isLastTransport && currentLoad > 0 && AllCiviliansHandled())
				break;

			yield return null;
		}

		TakeOff(isTooHot);
	}

	// feature/heli-rework: the new feel for every non-Last-Transport heli -- waitTimer/heat/rescan
	// advance every frame via `yield return null` only (never blocked by a boarding animation),
	// heat replaces the old instant panic-abort (hardAbortRadius is the one thing that still
	// aborts immediately), and up to BoardingSlots units board in parallel.
	private IEnumerator NormalLoadRoutine()
	{
		float waitTimer = 0f;
		float boardCooldownTimer = boardingCooldown;
		float heatCheckTimer = 0f;
		float rescanTimer = 0f;
		const float heatCheckInterval = 0.2f;

		float heatRadiusSqr = HeatRadius * HeatRadius;
		float hardAbortRadiusSqr = HardAbortRadius * HardAbortRadius;

		// Step 4: panicRadius <= 0 (set directly, or via the heli_no_panic run flag in Start())
		// disables heat AND hard-abort entirely, not just the old single panic check.
		bool heatAndAbortActive = panicRadius > 0f;

		while (currentState == HeliState.Loading && currentLoad < maxCapacity && !isTooHot && waitTimer < loadTime)
		{
			if (Time.timeScale < 0.001f)
			{
				yield return null;
				continue;
			}

			float dt = Time.deltaTime;
			waitTimer += dt * currentHeatMultiplier;
			boardCooldownTimer += dt;
			heatCheckTimer += dt;
			rescanTimer += dt;

			if (heatAndAbortActive)
			{
				if (heatCheckTimer >= heatCheckInterval)
				{
					heatCheckTimer = 0f;

					int zombiesInRange = 0;
					bool hardAbort = false;

					foreach (var z in Zombie.AllZombies)
					{
						if (z == null) continue;
						float distSqr = (z.transform.position - transform.position).sqrMagnitude;
						if (distSqr <= heatRadiusSqr) zombiesInRange++;
						if (distSqr <= hardAbortRadiusSqr) hardAbort = true;
					}

					if (hardAbort) isTooHot = true;

					currentHeatMultiplier = EnableHeat
						? Mathf.Clamp(1f + zombiesInRange * HeatPerZombiePerSec, 1f, HeatMaxMultiplier)
						: 1f;

					UpdateLandingRingHeatColor();
				}
			}
			else
			{
				currentHeatMultiplier = 1f;
			}

			if (isTooHot) break;

			// Step 3: live magnet -- periodic rescan for stragglers who wandered into attractRadius
			// after the initial call-time scan.
			if (EnableLiveRescan && rescanTimer >= RescanInterval)
			{
				rescanTimer = 0f;
				ScanAndAttractCivilians();
			}

			// Step 5: throughput -- try to fill up to BoardingSlots parallel boarding coroutines,
			// staggered by BoardingStagger, instead of boarding one unit at a time inline.
			if (boardCooldownTimer >= EffectiveBoardingCooldown)
			{
				int slotsAvailable = Mathf.Max(0, BoardingSlots - activeBoardingCoroutines);
				int capacityLeft = maxCapacity - currentLoad - activeBoardingCoroutines;
				slotsAvailable = Mathf.Min(slotsAvailable, Mathf.Max(0, capacityLeft));

				if (slotsAvailable > 0)
				{
					GameObject firstCandidate = FindBoardingCandidate(out bool firstIsHuman);
					if (firstCandidate != null)
					{
						ReserveAndBoard(firstCandidate, firstIsHuman);
						boardCooldownTimer = 0f;

						if (slotsAvailable > 1)
							StartCoroutine(FillBoardingSlotsRoutine(slotsAvailable - 1));
					}
				}
			}

			// Early takeoff: nobody left to load (a unit still mid-animation keeps this false
			// until it lands in loadedUnits, so this never cuts a parallel boarding short).
			if (currentLoad > 0 && AllCiviliansHandled())
				break;

			yield return null;
		}

		TakeOff(isTooHot);
	}

	// feature/heli-rework (step 5): fills up to slotsToFill additional parallel boarding slots,
	// one start per BoardingStagger -- runs independently of NormalLoadRoutine's own yields, so it
	// never blocks waitTimer/heat/rescan.
	private IEnumerator FillBoardingSlotsRoutine(int slotsToFill)
	{
		for (int i = 0; i < slotsToFill; i++)
		{
			yield return new WaitForSeconds(BoardingStagger);

			if (currentState != HeliState.Loading) yield break;
			if (activeBoardingCoroutines >= BoardingSlots) yield break;

			int capacityLeft = maxCapacity - currentLoad - activeBoardingCoroutines;
			if (capacityLeft <= 0) yield break;

			GameObject candidate = FindBoardingCandidate(out bool isHuman);
			if (candidate == null) yield break;

			ReserveAndBoard(candidate, isHuman);
		}
	}

	private void ReserveAndBoard(GameObject unitGO, bool isHuman)
	{
		reservedUnits.Add(unitGO);
		activeBoardingCoroutines++;
		StartCoroutine(BoardUnitRoutine(unitGO, isHuman));
	}

	// feature/heli-rework (step 5): one unit's full boarding animation, as its own coroutine so
	// several can run in parallel. Never yielded on by NormalLoadRoutine.
	private IEnumerator BoardUnitRoutine(GameObject unitGO, bool isHuman)
	{
		EvacueeBase evacuee = unitGO != null ? unitGO.GetComponent<EvacueeBase>() : null;

		if (evacuee != null)
			yield return StartCoroutine(evacuee.PlayBoardingAnimation(transform.position, boardingAnimDuration, boardingLiftHeight));

		activeBoardingCoroutines--;
		reservedUnits.Remove(unitGO);

		// Destroyed or infected-and-turned mid-animation (Unity fake-null) -- just free the slot.
		if (unitGO == null) yield break;

		// Step 6: the heli may have already departed (early-leave tap, hard abort, or a sibling
		// parallel slot reaching full load first) while this one was still finishing its
		// animation -- it still visibly made it aboard, so count/destroy it directly instead of
		// leaving it stuck with a disabled NavMeshAgent after TakeOff() already ran.
		if (currentState != HeliState.Loading)
		{
			if (isHuman) GameManager.Instance.AddRescuedFromTransport(1, 0, transform.position);
			else GameManager.Instance.AddRescuedFromTransport(0, 1, transform.position);
			Destroy(unitGO);
			yield break;
		}

		loadedUnits.Add(unitGO);
		currentLoad++;
		if (isHuman) loadedHumanCount++; else loadedScientistCount++;
		if (loadText) loadText.text = currentLoad.ToString();

		CheckFullLoad();
	}

	// feature/heli-rework (step 5): nearest-eligible-first scan, skipping anyone already boarded
	// or currently reserved by another in-flight boarding coroutine.
	private GameObject FindBoardingCandidate(out bool isHuman)
	{
		Vector2 heliPos2D = Flat(transform.position);

		foreach (var hum in Human.AllHumans)
		{
			if (hum == null) continue;
			GameObject go = hum.gameObject;
			if (loadedUnits.Contains(go) || reservedUnits.Contains(go)) continue;

			if (Vector2.Distance(heliPos2D, Flat(hum.transform.position)) < boardingRadius)
			{
				isHuman = true;
				return go;
			}
		}

		foreach (var sci in Scientist.AllScientists)
		{
			if (sci == null) continue;
			GameObject go = sci.gameObject;
			if (loadedUnits.Contains(go) || reservedUnits.Contains(go)) continue;

			if (Vector2.Distance(heliPos2D, Flat(sci.transform.position)) < boardingRadius)
			{
				isHuman = false;
				return go;
			}
		}

		isHuman = false;
		return null;
	}

	// feature/heli-rework (step 4 visual): tints the landing ring green->red as heat climbs.
	// hotWarning (the hard-abort flash) is untouched -- that still only shows on a real abort.
	private void UpdateLandingRingHeatColor()
	{
		if (landingMarker == null) return;

		Renderer r = landingMarker.GetComponent<Renderer>();
		if (r == null) return;

		float maxMult = HeatMaxMultiplier;
		float normalized = maxMult > 1f ? Mathf.Clamp01((currentHeatMultiplier - 1f) / (maxMult - 1f)) : 0f;
		r.material.color = Color.Lerp(Color.green, Color.red, normalized);
	}

	// feature/heli-rework (step 7): full load takes off immediately and grants a Supply bonus
	// through the existing Supply API (SupplyManager.Gain -- a no-op on levels where Supply isn't
	// active, e.g. hand-built levels outside Core Loop v2).
	private void CheckFullLoad()
	{
		if (fullLoadHandled) return;
		if (currentLoad < maxCapacity) return;

		fullLoadHandled = true;
		SupplyManager.Instance?.Gain(FullLoadSupplyBonus);
		Feedback.Play(FeedbackEvent.HeliFull, transform.position);
		TakeOff(false);
	}

	// feature/heli-rework (step 6): a tap on the heli while Loading leaves right away with
	// whoever's already aboard. Not offered on the Last Transport -- its departure timing is one
	// of the things the brief says must stay unchanged.
	public void RequestEarlyLeave()
	{
		if (isLastTransport) return;
		if (currentState != HeliState.Loading) return;

		TakeOff(false);
	}

	/// <summary>
	/// Returns true if all humans and scientists on the map are already on board this helicopter.
	/// Used for early takeoff without waiting for the timer.
	/// </summary>
	private bool AllCiviliansHandled()
	{
		foreach (var h in Human.AllHumans)
			if (h != null && !loadedUnits.Contains(h.gameObject)) return false;

		foreach (var s in Scientist.AllScientists)
			if (s != null && !loadedUnits.Contains(s.gameObject)) return false;

		return true;
	}

	private void TakeOff(bool fromPanic = false)
	{
		// feature/heli-rework: idempotency guard -- full load, a hard abort, an early-leave tap,
		// and the loop's own trailing call can now all race to call this once each.
		if (hasTakenOff) return;
		hasTakenOff = true;

		if (landingMarker) { Destroy(landingMarker); landingMarker = null; }

		if (fromPanic && hotWarning != null) hotWarning.SetActive(true);
		if (loadText != null) loadText.gameObject.SetActive(false);

		foreach (var h in Human.AllHumans)
		{
			if (h != null && h.rescueTarget == transform) h.CancelRescue();
		}

		foreach (var s in Scientist.AllScientists)
		{
			if (s != null && s.rescueTarget == transform) s.CancelRescue();
		}

		currentState = HeliState.TakingOff;
		Feedback.Play(FeedbackEvent.HeliTakeoff, transform.position);

		if (loadedHumanCount > 0 || loadedScientistCount > 0)
		{
			GameManager.Instance.AddRescuedFromTransport(
				loadedHumanCount,
				loadedScientistCount,
				transform.position
			);

			foreach (var unit in loadedUnits)
			{
				if (unit != null) Destroy(unit);
			}

			loadedUnits.Clear();
		}

		// Core Loop v2 (Docs/CORE_LOOP_V2.md Phase 4): the level ends when the Last Transport
		// departs -- civilians still on the map (not boarded) are lost, matching the brief.
		if (isLastTransport)
		{
			GameManager.Instance.OnV2LastTransportDeparted();
			NightLastCallController.Instance?.OnTransportDeparted(loadedHumanCount + loadedScientistCount);
		}
	}
}
