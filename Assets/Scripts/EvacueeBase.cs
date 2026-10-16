using UnityEngine;
using UnityEngine.AI;
using System.Collections;

// Shared AI for Human and Scientist: Calm/Panic/Evacuating street-AI state machine (driven by
// CrowdFieldManager's shared road-grid distance fields — feature/zombie-street-ai) plus
// rescue/boarding/infection, all on top of a NavMeshAgent. Human.cs and Scientist.cs are
// otherwise identical except which static roster they register themselves in
// (Human.AllHumans vs Scientist.AllScientists) — see each subclass's OnEnable/OnDisable.
//
// Note: ChaosSettings' OLDER panic tuning fields are named "human..." (humanPanicChance, etc.)
// even though both Human and Scientist read them — that predates this extraction and predates
// the street-AI pass (which superseded them with fearRadiusTiles/panicDuration/panicSpeedMult;
// see ChaosSettings.cs), so they're left in place but unread here.
[RequireComponent(typeof(NavMeshAgent))]
public abstract class EvacueeBase : MonoBehaviour
{
	[Header("Movement Settings")]
	public float walkSpeed = 1.5f;
	public float runSpeed = 4.0f;
	public float panicRadius = 8f;

	[Header("Chaos")]
	public ChaosSettings chaosSettings;

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

	private bool isPanicking = false;
	private float panicEndTime = 0f;
	private bool panicUsesEvac = false;

	// Street-AI decisions (Calm wander target, Evacuating step, Panic step) are only ever issued
	// at most once per RetargetCooldown — see "F" in the task brief. Movement stays smooth via the
	// NavMeshAgent's own path-following in between; this is what stops the old per-frame
	// re-steering jitter at the root instead of damping it after the fact.
	private float nextStreetDecisionTime = 0f;
	private bool hasWanderDestination = false;
	private Vector3 wanderDestination;

	private bool hasRescueApproachPoint = false;
	private Vector3 rescueApproachPoint;
	private bool isBoarding = false;

	private float CellSizeOrDefault =>
		CrowdFieldManager.Instance != null && CrowdFieldManager.Instance.GridReady
			? CrowdFieldManager.Instance.CellSize : 2f;

	private float FearRadiusWorld => (chaosSettings != null ? chaosSettings.fearRadiusTiles : 4f) * CellSizeOrDefault;
	private float EvacRadiusWorld => (chaosSettings != null ? chaosSettings.evacRadiusTiles : 8f) * CellSizeOrDefault;
	private float PanicDuration => chaosSettings != null ? chaosSettings.panicDuration : 2.5f;
	private float PanicSpeedMult => chaosSettings != null ? chaosSettings.panicSpeedMult : 1.7f;
	private float CalmSpeedMult => chaosSettings != null ? chaosSettings.civCalmSpeedMult : 0.6f;
	private float RetargetCooldown => chaosSettings != null ? Mathf.Max(0.1f, chaosSettings.retargetCooldown) : 1.5f;

	private void Awake()
	{
		ActorVisualScaler.Apply(transform);
		agent = GetComponent<NavMeshAgent>();
	}

	private void Start()
	{
		agent.speed = walkSpeed;

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

		CrowdFieldManager.Instance?.EnsureConfigured(chaosSettings);

		// --- Priority 1: Rescue ---
		if (isRescuing && rescueTarget != null)
		{
			agent.speed = runSpeed * boost;

			Vector3 targetPoint = hasRescueApproachPoint
				? rescueApproachPoint
				: new Vector3(rescueTarget.position.x, transform.position.y, rescueTarget.position.z);

			Vector3 groundTarget = new Vector3(targetPoint.x, transform.position.y, targetPoint.z);

			if (!agent.hasPath || Vector3.Distance(agent.destination, groundTarget) > 0.35f)
			{
				agent.SetDestination(groundTarget);
			}
			return;
		}

		// --- Priority 2: Panic — deterministic trigger (a zombie within fearRadius tiles), then
		// direction is LOCKED for panicDuration without re-checking threats (brief "E"). Locked
		// only means the choice of "flee the zombie" vs "run for a nearby evac point" is held;
		// the actual next-step destination is still refreshed every RetargetCooldown so the
		// civilian keeps moving along that same locked direction instead of freezing mid-flight.
		if (isPanicking)
		{
			agent.speed = walkSpeed * PanicSpeedMult * boost;

			if (Time.time >= panicEndTime)
			{
				isPanicking = false;
			}
			else if (Time.time >= nextStreetDecisionTime)
			{
				nextStreetDecisionTime = Time.time + RetargetCooldown;
				StepPanic();
			}

			return;
		}

		Zombie nearestZombie = TargetScanUtil.FindNearest(Zombie.AllZombies, transform.position, panicRadius, z => z.transform.position, out float minD);
		if (nearestZombie != null && minD <= FearRadiusWorld)
		{
			StartPanic();
			return;
		}

		// --- Priority 3: Evacuating — an active evac point (car/heli currently loading) is
		// within evacRadius tiles; descend F_evac instead of wandering.
		if (StepEvacuatingIfInRange(boost)) return;

		// --- Priority 4: Calm — slow wander between intersections.
		agent.speed = walkSpeed * CalmSpeedMult * boost;
		StepCalmWander();
	}

	private void StartPanic()
	{
		isPanicking = true;
		panicEndTime = Time.time + PanicDuration;
		nextStreetDecisionTime = 0f; // step immediately, then on the normal cooldown

		CrowdFieldManager cfm = CrowdFieldManager.Instance;
		panicUsesEvac = cfm != null && cfm.GridReady &&
			cfm.SampleField(transform.position, CrowdFieldType.Evac) <= EvacRadiusWorld;
	}

	private void StepPanic()
	{
		CrowdFieldManager cfm = CrowdFieldManager.Instance;
		if (cfm == null || !cfm.GridReady)
		{
			SetRandomDest();
			return;
		}

		bool got = panicUsesEvac
			? cfm.TryGetDescentDirection(transform.position, CrowdFieldType.Evac, out Vector3 step)
			: cfm.TryGetAscentDirection(transform.position, CrowdFieldType.Zombie, out step);

		if (got) agent.SetDestination(step);
		else SetRandomDest();
	}

	private bool StepEvacuatingIfInRange(float boost)
	{
		CrowdFieldManager cfm = CrowdFieldManager.Instance;
		if (cfm == null || !cfm.GridReady) return false;

		float evacDist = cfm.SampleField(transform.position, CrowdFieldType.Evac);
		if (float.IsPositiveInfinity(evacDist) || evacDist > EvacRadiusWorld) return false;

		agent.speed = walkSpeed * boost;

		if (Time.time >= nextStreetDecisionTime)
		{
			nextStreetDecisionTime = Time.time + RetargetCooldown;
			if (cfm.TryGetDescentDirection(transform.position, CrowdFieldType.Evac, out Vector3 step))
				agent.SetDestination(step);
		}

		return true;
	}

	private void StepCalmWander()
	{
		bool arrived = !agent.pathPending &&
			(!agent.hasPath || agent.remainingDistance < 0.5f || agent.pathStatus != NavMeshPathStatus.PathComplete);

		if (hasWanderDestination && !arrived && Time.time < nextStreetDecisionTime) return;

		nextStreetDecisionTime = Time.time + RetargetCooldown;

		CrowdFieldManager cfm = CrowdFieldManager.Instance;
		if (cfm != null && cfm.GridReady && cfm.TryGetRandomIntersection(out Vector3 dest))
		{
			wanderDestination = dest;
			hasWanderDestination = true;
			agent.SetDestination(wanderDestination);
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

		CancelRescue();
		ShowInfectionMarker();
		StartCoroutine(InfectionRoutine(delay, onTurned));
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
