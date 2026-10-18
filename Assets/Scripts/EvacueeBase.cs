using UnityEngine;
using UnityEngine.AI;
using System.Collections;

// Shared AI for Human and Scientist: Calm/Panic/Evacuating street-AI state machine (driven by
// CrowdFieldManager's shared road-grid distance fields — feature/zombie-street-ai) plus
// rescue/boarding/infection, all on top of a NavMeshAgent. Human.cs and Scientist.cs are
// otherwise identical except which static roster they register themselves in
// (Human.AllHumans vs Scientist.AllScientists) — see each subclass's OnEnable/OnDisable.
//
// feature/crowd-tuning-pass: walkSpeed/runSpeed/panicRadius used to be duplicated per-prefab
// fields here — they now live on the single shared CrowdTuning asset (civCalmSpeed,
// civPanicSpeed, fearRadiusCells), as ABSOLUTE world-units/second speeds, not multipliers.
// "Running" (rescue sprint and panic flee) both now read civPanicSpeed — they already shared the
// exact same runSpeed value before this pass, so this isn't a behavior change, just one fewer
// field saying the same number twice.
[RequireComponent(typeof(NavMeshAgent))]
public abstract class EvacueeBase : MonoBehaviour
{
	[Header("Tuning")]
	public CrowdTuning crowdTuning;

	[Header("Stopping Distances")]
	public float carStoppingDistance = 1.4f;
	public float heliStoppingDistance = 2.0f;

	[Header("Infection (feature/zombie-street-ai)")]
	[Tooltip("Optional — spawned above the civilian's head while infected and counting down to turning. Code also tints the civilian's renderers; this is purely an extra visual.")]
	public GameObject infectedMarkerPrefab;
	public Color infectedTintColor = new Color(0.65f, 0.95f, 0.25f, 1f);

	protected NavMeshAgent agent;

	[HideInInspector] public bool isRescuing = false;
	[HideInInspector] public Transform rescueTarget = null;

	public bool IsInfected { get; private set; }

	// feature/azs-crowd-feel (3): lets Zombie's hunt-target validity check see "is this civilian
	// already being lifted away" without a new flag — isBoarding already tracks exactly that.
	public bool IsBoarding => isBoarding;

	// feature/crowd-tuning-pass (Part B.4): lets a nearby Calm civilian detect this one is
	// already panicking (contagion), without exposing any more state than that.
	public bool IsPanicking => isPanicking;

	private bool isPanicking = false;
	private float panicEndTime = 0f;
	private bool panicUsesEvac = false;

	// Street-AI decisions (Calm wander target, Evacuating step) are only ever issued at most once
	// per RetargetCooldown or on arrival. Panic (Part B.3) no longer uses this timer at all — it
	// re-steps purely on arrival, so it can't freeze mid-flight waiting out a cooldown.
	private float nextStreetDecisionTime = 0f;
	private bool hasWanderDestination = false;
	private Vector3 wanderDestination;

	private bool hasRescueApproachPoint = false;
	private Vector3 rescueApproachPoint;
	private bool isBoarding = false;

	// fix/civilian-flee (step 4): set when a rescue run is temporarily aborted into a flee (a
	// zombie was close and ahead) -- rescueTarget/hasRescueApproachPoint/rescueApproachPoint are
	// deliberately left untouched during the flee so the rescue can resume from them unchanged.
	private bool rescuePendingResume = false;

	// feature/crowd-tuning-pass: every tunable below reads crowdTuning directly (a property, not
	// a cached field) so Inspector edits apply immediately in Play Mode.
	private float CalmSpeed => crowdTuning != null ? crowdTuning.civCalmSpeed : 0.45f;
	private float PanicSpeed => crowdTuning != null ? crowdTuning.civPanicSpeed : 1.4f;
	private float PanicDuration => crowdTuning != null ? crowdTuning.panicDuration : 2.5f;
	private float RetargetCooldown => crowdTuning != null ? Mathf.Max(0.1f, crowdTuning.retargetCooldown) : 1.5f;
	private int PanicLookaheadCells => crowdTuning != null ? Mathf.Max(1, crowdTuning.panicLookaheadCells) : 6;

	private float CellSizeOrDefault =>
		CrowdFieldManager.Instance != null && CrowdFieldManager.Instance.GridReady
			? CrowdFieldManager.Instance.CellSize : 2f;

	// fix/civilian-flee: true unless the dev "Old/New" toggle (SandboxDevPanel) is set to Old --
	// gates every new safety check in this file so it stays byte-for-byte the pre-fix behavior.
	private bool LegacyCivAi => crowdTuning != null && crowdTuning.civAiLegacyBehavior;

	// Part B.1: a single radius, compared against a straight-line zombie distance — no more
	// panicRadius pre-cap shrinking the effective trigger range.
	//
	// fix/civilian-flee: floored at fearRadiusMinMeters (world units) -- fearRadiusCells*cellSize
	// alone can shrink below a sensible trigger distance on a level with small grid cells. Old
	// behavior (cells only, no floor) is kept byte-for-byte under LegacyCivAi.
	private float FearRadiusWorld
	{
		get
		{
			float cellsWorld = (crowdTuning != null ? crowdTuning.fearRadiusCells : 4f) * CellSizeOrDefault;
			if (LegacyCivAi) return cellsWorld;
			float minMeters = crowdTuning != null ? crowdTuning.fearRadiusMinMeters : 3.0f;
			return Mathf.Max(cellsWorld, minMeters);
		}
	}

	// Part B.4: contagion range, also a straight-line comparison (same reasoning as fear radius).
	// fix/civilian-flee: same minMeters floor as FearRadiusWorld above.
	private float PanicContagionWorld
	{
		get
		{
			float cellsWorld = (crowdTuning != null ? crowdTuning.panicContagionCells : 2f) * CellSizeOrDefault;
			if (LegacyCivAi) return cellsWorld;
			float minMeters = crowdTuning != null ? crowdTuning.contagionRadiusMinMeters : 1.5f;
			return Mathf.Max(cellsWorld, minMeters);
		}
	}

	// Part B.5: Calm civilians only wander to intersections at least this many F_zed *steps* away.
	private float CalmSafeDistanceCells => crowdTuning != null ? crowdTuning.calmSafeDistanceCells : 6f;

	// Part B.6: F_evac is sampled as a step count, so the threshold must be a step count too — no
	// *CellSizeOrDefault conversion here. (Previously compared a step count against a world-unit
	// threshold, which silently shrank or inflated the effective evac radius depending on cell
	// size — exactly the unit mismatch this pass fixes.)
	private float EvacRadiusCells => crowdTuning != null ? crowdTuning.evacRadiusCells : 8f;

	private Renderer[] paletteRenderers;

	private void Awake()
	{
		ActorVisualScaler.Apply(transform);
		agent = GetComponent<NavMeshAgent>();

		// feature/readability-palette: generated-city-only (LevelPaletteService.Active is null on
		// hand-built levels). Human/Scientist are never pooled -- a fresh Instantiate per spawn --
		// so Awake() alone covers every instance.
		if (LevelPaletteService.Active != null) ApplyPaletteTint(GetPaletteTintColor(LevelPaletteService.Active));
	}

	// Overridden by Scientist to use the palette's scientist color instead of the civilian one.
	protected virtual Color GetPaletteTintColor(LevelPalette palette) => palette.civilianColor;

	// feature/readability-palette: same renderer/property-block pattern as ShowInfectionMarker
	// below -- no restore pass needed here either (SandboxDevPanel's live palette-cycle button is
	// the only thing that re-tints an already-spawned civilian, and it always passes a real
	// palette color, never needs to revert to "no tint").
	public void ApplyPaletteTint(Color color)
	{
		if (paletteRenderers == null) paletteRenderers = GetComponentsInChildren<Renderer>();

		for (int i = 0; i < paletteRenderers.Length; i++)
		{
			Renderer r = paletteRenderers[i];
			if (r == null) continue;

			Material mat = r.sharedMaterial;
			if (mat == null) continue;

			int propId;
			if (mat.HasProperty("_BaseColor")) propId = Shader.PropertyToID("_BaseColor");
			else if (mat.HasProperty("_Color")) propId = Shader.PropertyToID("_Color");
			else continue;

			MaterialPropertyBlock pb = new MaterialPropertyBlock();
			r.GetPropertyBlock(pb);
			pb.SetColor(propId, color);
			r.SetPropertyBlock(pb);
		}
	}

	private void Start()
	{
		agent.speed = CalmSpeed;

		if (GameManager.Instance != null && GameManager.Instance.State != GameManager.GameState.Planning)
		{
			SetRandomDest();
		}
	}

	private void Update()
	{
		if (!agent.isActiveAndEnabled || !agent.isOnNavMesh) return;

		// Full pause (LevelUp screen, time-stop, etc.) — stop the agent
		if (Time.timeScale < 0.001f)
		{
			if (!agent.isStopped) agent.isStopped = true;
			return;
		}

		if (GameManager.Instance != null && GameManager.Instance.State == GameManager.GameState.Planning)
		{
			if (!agent.isStopped) agent.isStopped = true;
			return;
		}
		else if (agent.isStopped)
		{
			agent.isStopped = false;
		}

		if (isBoarding) return;

		float boost = AbilityManager.Instance != null ? AbilityManager.Instance.GetSpeedMultiplier() : 1f;

		CrowdFieldManager.Instance?.EnsureConfigured(crowdTuning);

		// --- Priority 1: Rescue ---
		if (isRescuing && rescueTarget != null)
		{
			agent.speed = PanicSpeed * boost;

			Vector3 targetPoint = hasRescueApproachPoint
				? rescueApproachPoint
				: new Vector3(rescueTarget.position.x, transform.position.y, rescueTarget.position.z);

			Vector3 groundTarget = new Vector3(targetPoint.x, transform.position.y, targetPoint.z);

			// fix/civilian-flee (step 4): don't run straight at a zombie standing between here and
			// the pickup point -- abort the run temporarily (flee for panicDuration) and resume it
			// (from the unchanged rescueTarget/approach point) once the flee ends.
			if (!LegacyCivAi && IsZombieAheadDuringRescue(groundTarget - transform.position))
			{
				rescuePendingResume = true;
				isRescuing = false;
				StartPanicFleeOnly();
				return;
			}

			if (!agent.hasPath || Vector3.Distance(agent.destination, groundTarget) > 0.35f)
			{
				agent.SetDestination(groundTarget);
			}
			return;
		}

		// --- Priority 2: Panic — deterministic trigger (a zombie within fearRadius cells,
		// straight line, Part B.1), then direction is LOCKED for panicDuration without
		// re-checking threats. Locked only means the choice of "flee the zombie" vs "run for a
		// nearby evac point" is held; re-stepping itself is now purely arrival-based (Part B.3).
		if (isPanicking)
		{
			agent.speed = PanicSpeed * boost;

			if (Time.time >= panicEndTime)
			{
				isPanicking = false;

				// fix/civilian-flee (step 4): resume the rescue run a nearby zombie interrupted, if
				// the target is still there -- SetRescueTarget's own checks handle a destroyed/
				// departed target (rescueTarget's Unity-null check / CancelRescue from the car/heli).
				if (rescuePendingResume)
				{
					rescuePendingResume = false;
					if (rescueTarget != null)
					{
						if (hasRescueApproachPoint) SetRescueTarget(rescueTarget, rescueApproachPoint);
						else SetRescueTarget(rescueTarget);
					}
				}
			}
			else
			{
				bool arrived = !agent.pathPending && (!agent.hasPath || agent.remainingDistance < CellSizeOrDefault);
				if (arrived) StepPanic();
			}

			return;
		}

		Zombie nearestZombie = TargetScanUtil.FindNearest(Zombie.AllZombies, transform.position, float.MaxValue, z => z.transform.position, out float minD);
		if (nearestZombie != null && minD <= FearRadiusWorld)
		{
			StartPanic();
			return;
		}

		// --- Priority 2.5: Panic contagion (Part B.4) — a nearby civilian is already panicking.
		EvacueeBase nearestPanicker = FindNearestPanickingCivilian(PanicContagionWorld);
		if (nearestPanicker != null)
		{
			StartPanic();
			return;
		}

		// --- Priority 3: Evacuating — an active evac point (car/heli currently loading) is
		// within evacRadiusCells field-steps; descend F_evac instead of wandering.
		if (StepEvacuatingIfInRange(boost)) return;

		// --- Priority 4: Calm — slow wander between intersections that are far enough from
		// zombies (Part B.5); flee instead if none qualify.
		agent.speed = CalmSpeed * boost;
		StepCalmWander();
	}

	private void StartPanic()
	{
		isPanicking = true;
		panicEndTime = Time.time + PanicDuration;

		CrowdFieldManager cfm = CrowdFieldManager.Instance;
		panicUsesEvac = cfm != null && cfm.GridReady &&
			cfm.SampleField(transform.position, CrowdFieldType.Evac) <= EvacRadiusCells;

		StepPanic();
	}

	// fix/civilian-flee (step 4): forces the flee branch (not evac) regardless of the normal
	// F_evac check above -- a rescue abort means "a zombie is in the way of this specific pickup
	// point", not "go wander toward whatever evac point is nearest", so it must always flee.
	private void StartPanicFleeOnly()
	{
		isPanicking = true;
		panicEndTime = Time.time + PanicDuration;
		panicUsesEvac = false;
		StepPanic();
	}

	// Part B.3: a multi-cell lookahead step (not a single grid cell) in one SetDestination,
	// re-called only on arrival from Update() above — no fixed-cooldown freeze mid-flight.
	//
	// fix/civilian-flee (step 3): panicUsesEvac is re-validated on EVERY step (not just once at
	// StartPanic) -- an evac step that would run straight at a nearby zombie is rejected in favor
	// of fleeing for that step only; panicUsesEvac itself is untouched, so the next re-step (on
	// arrival) can resume evac once it's safe again. Old behavior (blind evac, no re-validation)
	// is kept byte-for-byte under LegacyCivAi.
	private void StepPanic()
	{
		CrowdFieldManager cfm = CrowdFieldManager.Instance;
		if (cfm == null || !cfm.GridReady)
		{
			SetRandomDest();
			return;
		}

		if (panicUsesEvac)
		{
			if (LegacyCivAi)
			{
				if (cfm.TryGetLookaheadDescent(transform.position, CrowdFieldType.Evac, PanicLookaheadCells, out Vector3 legacyStep))
					agent.SetDestination(legacyStep);
				else
					SetRandomDest();
				return;
			}

			if (TryEvacStepIsSafe(cfm, out Vector3 evacStep))
			{
				agent.SetDestination(evacStep);
				return;
			}

			if (TryFleeStep(cfm, out Vector3 fleeFromEvacStep)) agent.SetDestination(fleeFromEvacStep);
			else agent.ResetPath(); // cornered -- stand still instead of running into the zombie
			return;
		}

		if (LegacyCivAi)
		{
			if (cfm.TryGetLookaheadAscent(transform.position, CrowdFieldType.Zombie, PanicLookaheadCells, out Vector3 legacyFlee))
				agent.SetDestination(legacyFlee);
			else
				SetRandomDest();
			return;
		}

		if (TryFleeStep(cfm, out Vector3 step)) agent.SetDestination(step);
		else agent.ResetPath(); // cornered -- stand still instead of running into the zombie
	}

	// fix/civilian-flee (step 3): before committing to the evac lookahead, check it doesn't run
	// straight at the zombie that's currently scaring us -- only rejects when a zombie is within
	// FearRadiusWorld right now AND the evac step would reduce the (straight-line) distance to it.
	private bool TryEvacStepIsSafe(CrowdFieldManager cfm, out Vector3 evacStep)
	{
		evacStep = transform.position;
		if (!cfm.TryGetLookaheadDescent(transform.position, CrowdFieldType.Evac, PanicLookaheadCells, out evacStep))
			return false;

		Zombie nearestZombie = TargetScanUtil.FindNearest(Zombie.AllZombies, transform.position, float.MaxValue, z => z.transform.position, out float distNow);
		if (nearestZombie == null || distNow > FearRadiusWorld) return true;

		float distAtStep = Vector3.Distance(evacStep, nearestZombie.transform.position);
		return distAtStep >= distNow;
	}

	// fix/civilian-flee (step 5): the shared "flee the nearest zombie" step, hardened against
	// running further INTO a zombie than standing still would -- used by the normal flee branch
	// and by the evac-step-rejected fallback above.
	private bool TryFleeStep(CrowdFieldManager cfm, out Vector3 destination)
	{
		Vector3 here = transform.position;
		Zombie nearestZombie = TargetScanUtil.FindNearest(Zombie.AllZombies, here, float.MaxValue, z => z.transform.position, out float distNow);

		if (cfm.TryGetLookaheadAscent(here, CrowdFieldType.Zombie, PanicLookaheadCells, out Vector3 lookaheadStep)
			&& (nearestZombie == null || Vector3.Distance(lookaheadStep, nearestZombie.transform.position) > distNow))
		{
			destination = lookaheadStep;
			return true;
		}

		// Cornered: the multi-cell lookahead either failed or doesn't actually gain distance (a
		// dead end, or the zombie is on the far side of the only street) -- fall back to a single
		// best neighbor (the same "steepest ascent of F_zed" the lookahead itself walks, one cell
		// at a time -- exactly "the best of the neighboring branches by F_zed value" when standing
		// at or near an intersection).
		float hereFzed = cfm.SampleField(here, CrowdFieldType.Zombie);
		if (cfm.TryGetAscentDirection(here, CrowdFieldType.Zombie, out Vector3 neighborStep)
			&& cfm.SampleField(neighborStep, CrowdFieldType.Zombie) > hereFzed)
		{
			destination = neighborStep;
			return true;
		}

		// Every option is worse (or none exists) -- stop moving rather than run into the zombie.
		destination = here;
		return false;
	}

	// fix/civilian-flee (step 4): a zombie close enough (0.8x fear radius) and roughly in the way
	// (within ~60 degrees of the rescue heading) means continuing the run would walk toward it.
	private bool IsZombieAheadDuringRescue(Vector3 movementDirection)
	{
		Zombie nearestZombie = TargetScanUtil.FindNearest(Zombie.AllZombies, transform.position, FearRadiusWorld * 0.8f, z => z.transform.position, out float _);
		if (nearestZombie == null) return false;

		Vector3 toZombie = nearestZombie.transform.position - transform.position;
		toZombie.y = 0f;
		if (toZombie.sqrMagnitude < 0.0001f) return true; // zombie is right on top of us

		Vector3 dir = movementDirection;
		dir.y = 0f;
		if (dir.sqrMagnitude < 0.0001f) return false;

		float cosAngle = Vector3.Dot(dir.normalized, toZombie.normalized);
		return cosAngle >= 0.5f; // cos(60 degrees) = 0.5
	}

	// Part B.4: nearest OTHER civilian (Human or Scientist) currently panicking, straight-line.
	private EvacueeBase FindNearestPanickingCivilian(float maxDistance)
	{
		Human nearestHuman = TargetScanUtil.FindNearest(Human.AllHumans, transform.position, maxDistance,
			h => h.transform.position, out float distH, accept: (h, d) => !ReferenceEquals(h, this) && h.IsPanicking);
		Scientist nearestScientist = TargetScanUtil.FindNearest(Scientist.AllScientists, transform.position, maxDistance,
			s => s.transform.position, out float distS, accept: (s, d) => !ReferenceEquals(s, this) && s.IsPanicking);

		if (nearestHuman != null && nearestScientist != null)
			return distH <= distS ? (EvacueeBase)nearestHuman : nearestScientist;

		if (nearestHuman != null) return nearestHuman;
		if (nearestScientist != null) return nearestScientist;
		return null;
	}

	private bool StepEvacuatingIfInRange(float boost)
	{
		CrowdFieldManager cfm = CrowdFieldManager.Instance;
		if (cfm == null || !cfm.GridReady) return false;

		// Part B.6: evacDist is a field STEP COUNT — compare directly to EvacRadiusCells, no
		// world-unit conversion.
		float evacDist = cfm.SampleField(transform.position, CrowdFieldType.Evac);
		if (float.IsPositiveInfinity(evacDist) || evacDist > EvacRadiusCells) return false;

		agent.speed = CalmSpeed * boost;

		if (Time.time >= nextStreetDecisionTime)
		{
			nextStreetDecisionTime = Time.time + RetargetCooldown;
			if (cfm.TryGetDescentDirection(transform.position, CrowdFieldType.Evac, out Vector3 step))
				agent.SetDestination(step);
		}

		return true;
	}

	// Part B.5: only ever picks an intersection whose F_zed reading is at least
	// CalmSafeDistanceCells; if none of the grid's intersections currently qualify (zombies have
	// spread out enough to make everywhere "close"), flee-steps (ascend F_zed) instead of
	// wandering into danger.
	private void StepCalmWander()
	{
		bool arrived = !agent.pathPending &&
			(!agent.hasPath || agent.remainingDistance < 0.5f || agent.pathStatus != NavMeshPathStatus.PathComplete);

		if (hasWanderDestination && !arrived && Time.time < nextStreetDecisionTime) return;

		nextStreetDecisionTime = Time.time + RetargetCooldown;

		CrowdFieldManager cfm = CrowdFieldManager.Instance;
		if (cfm != null && cfm.GridReady && cfm.TryGetRandomSafeIntersection(CrowdFieldType.Zombie, CalmSafeDistanceCells, out Vector3 dest))
		{
			wanderDestination = dest;
			hasWanderDestination = true;
			agent.SetDestination(wanderDestination);
			return;
		}

		hasWanderDestination = false;

		if (cfm != null && cfm.GridReady && cfm.TryGetLookaheadAscent(transform.position, CrowdFieldType.Zombie, PanicLookaheadCells, out Vector3 fleeStep))
		{
			agent.SetDestination(fleeStep);
		}
		else
		{
			SetRandomDest();
		}
	}

	// Zombie contact starts this instead of instantly destroying/replacing the civilian. It keeps
	// running (Panic/Evacuating/Calm above are untouched by this flag) with a visible marker for
	// `delay` seconds; `onTurned` is invoked only if it's still alive then. Killing the civilian
	// (Bomb friendly fire, etc.) before the delay elapses naturally prevents the turn — Unity stops
	// a destroyed GameObject's coroutines, no extra bookkeeping needed.
	public void BeginInfection(float delay, System.Action<EvacueeBase> onTurned)
	{
		if (IsInfected) return;
		IsInfected = true;

		RecordDeathTelemetry();

		CancelRescue();
		ShowInfectionMarker();
		StartCoroutine(InfectionRoutine(delay, onTurned));
	}

	// fix/civilian-flee (step 1): snapshot state BEFORE CancelRescue() below clears isRescuing, so
	// the counted state reflects what this civilian was actually doing when the bite landed.
	// Sandbox-only (gated inside CivilianDeathTelemetry) -- a no-op on real levels.
	private void RecordDeathTelemetry()
	{
		CivilianDeathTelemetry.DeathState state;
		if (isBoarding) state = CivilianDeathTelemetry.DeathState.Boarding;
		else if (isRescuing) state = CivilianDeathTelemetry.DeathState.Rescue;
		else if (isPanicking) state = panicUsesEvac ? CivilianDeathTelemetry.DeathState.PanicEvac : CivilianDeathTelemetry.DeathState.PanicFlee;
		else state = CivilianDeathTelemetry.DeathState.Calm;

		Zombie nearestZombie = TargetScanUtil.FindNearest(Zombie.AllZombies, transform.position, float.MaxValue, z => z.transform.position, out float _);
		Vector3? zombiePos = nearestZombie != null ? nearestZombie.transform.position : (Vector3?)null;

		CivilianDeathTelemetry.RecordDeath(state, name, transform.position, agent != null ? agent.velocity : Vector3.zero, zombiePos);
	}

	private IEnumerator InfectionRoutine(float delay, System.Action<EvacueeBase> onTurned)
	{
		yield return new WaitForSeconds(delay);
		onTurned?.Invoke(this);
	}

	private void ShowInfectionMarker()
	{
		if (infectedMarkerPrefab != null)
		{
			Instantiate(infectedMarkerPrefab, transform.position + Vector3.up * 0.5f, Quaternion.identity, transform);
		}

		// No restore pass: this civilian is guaranteed to be destroyed one way or another (turns
		// into a zombie or is killed outright) before the tint would ever need to come off.
		Renderer[] renderers = GetComponentsInChildren<Renderer>();
		for (int i = 0; i < renderers.Length; i++)
		{
			Renderer r = renderers[i];
			if (r == null) continue;

			Material mat = r.sharedMaterial;
			if (mat == null) continue;

			int propId;
			if (mat.HasProperty("_BaseColor")) propId = Shader.PropertyToID("_BaseColor");
			else if (mat.HasProperty("_Color")) propId = Shader.PropertyToID("_Color");
			else continue;

			MaterialPropertyBlock pb = new MaterialPropertyBlock();
			r.GetPropertyBlock(pb);
			pb.SetColor(propId, infectedTintColor);
			r.SetPropertyBlock(pb);
		}
	}

	// Stopping distance logic: 0 for helicopter, adjusted for car
	public void SetRescueTarget(Transform t)
	{
		if (IsInfected) return;

		isRescuing = true;
		rescueTarget = t;
		isPanicking = false;
		hasRescueApproachPoint = false;

		if (!agent.isActiveAndEnabled || !agent.isOnNavMesh)
			return;

		float stop = 0.0f;

		if (t.GetComponent<CarController>() != null)
			stop = carStoppingDistance;
		else if (t.GetComponent<HelicopterController>() != null)
			stop = heliStoppingDistance;

		agent.stoppingDistance = stop;
		Vector3 groundTarget = new Vector3(t.position.x, transform.position.y, t.position.z);
		agent.SetDestination(groundTarget);
	}

	public void SetRescueTarget(Transform t, Vector3 approachPoint)
	{
		if (IsInfected) return;

		isRescuing = true;
		rescueTarget = t;
		isPanicking = false;
		hasRescueApproachPoint = true;
		rescueApproachPoint = approachPoint;

		if (agent.isActiveAndEnabled && agent.isOnNavMesh)
		{
			agent.stoppingDistance = 0f;
			Vector3 groundTarget = new Vector3(approachPoint.x, transform.position.y, approachPoint.z);
			agent.SetDestination(groundTarget);
		}
	}

	public void CancelRescue()
	{
		isRescuing = false;
		rescueTarget = null;
		hasRescueApproachPoint = false;
		isBoarding = false;
		rescuePendingResume = false; // fix/civilian-flee (step 4): no target left to resume toward

		if (agent.isActiveAndEnabled && agent.isOnNavMesh)
		{
			agent.stoppingDistance = 0f;
			agent.ResetPath();
		}
	}

	public IEnumerator PlayBoardingAnimation(Vector3 boardCenter, float duration, float liftHeight = 0.6f)
	{
		// Safety: object already destroyed
		if (this == null || gameObject == null) yield break;

		isBoarding = true;
		isRescuing = false;
		hasRescueApproachPoint = false;
		isPanicking = false;

		if (agent != null && agent.isActiveAndEnabled)
		{
			agent.ResetPath();
			agent.enabled = false;
		}

		Vector3 startPos = transform.position;
		Vector3 endPos = boardCenter + Vector3.up * liftHeight;
		Vector3 startScale = transform.localScale;
		Vector3 endScale = Vector3.zero;

		float t = 0f;
		while (t < 1f)
		{
			// Safety: stop if the object was destroyed mid-animation
			if (this == null || gameObject == null || transform == null)
			{
				yield break;
			}

			t += Time.deltaTime / Mathf.Max(0.01f, duration);
			float eased = Mathf.SmoothStep(0f, 1f, t);

			transform.position = Vector3.Lerp(startPos, endPos, eased);
			transform.localScale = Vector3.Lerp(startScale, endScale, eased);

			yield return null;
		}

		// Safety: final assignment
		if (this != null && gameObject != null && transform != null)
		{
			transform.position = endPos;
			transform.localScale = endScale;
		}
	}

	private void SetRandomDest()
	{
		Vector3 rd = transform.position + Random.insideUnitSphere * 10f;
		rd.y = transform.position.y;

		if (NavMesh.SamplePosition(rd, out NavMeshHit h, 10f, NavMesh.AllAreas))
		{
			agent.SetDestination(h.position);
		}
	}
}
