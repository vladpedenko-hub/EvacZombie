using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(NavMeshAgent))]
public class Zombie : MonoBehaviour
{
	public static List<Zombie> AllZombies = new List<Zombie>();

	// Observational only (BalanceProbe, Docs/CLAUDE_CODE_TASK_LevelProgression.md §4.1) -- counts
	// humans/scientists turned into zombies. Not read by any gameplay/flow code.
	public static int InfectedThisLevel = 0;

	[Header("Settings")]
	public int maxHealth = 100;
	public float detectRadius = 10f;
	public float attackDistance = 1.4f;
	public float attackCooldown = 1.0f;
	public float moveSpeed = 2.8f;

	[Header("Infection")]
	public float infectDelay = 0.15f;
	public GameObject zombiePrefab;

	[Header("Chaos")]
	public ChaosSettings chaosSettings;

	[Header("Line of Sight")]
	[Tooltip("Layers treated as vision-blocking obstacles (e.g. buildings). Leave as Nothing to skip the raycast check and rely on NavMesh path distance alone.")]
	public LayerMask lineOfSightObstacles = 0;

	[Header("Barricades")]
	[Tooltip("Radius within which the zombie switches to attacking the barricade instead of walking around it")]
	public float barricadeCheckRadius = 3.5f;
	[Tooltip("Damage dealt per hit to a barricade")]
	public int barricadeDamage = 25;

	[Header("Bait Behavior")]
	[Tooltip("Radius within which zombies spread out slightly around the bait point")]
	public float crowdRadius = 0.8f;

	[Header("Hit Feedback")]
	public Color hitFlashColor = Color.white;
	public float hitFlashDuration = 0.08f;
	public float deathShrinkDuration = 0.12f;

	protected NavMeshAgent agent;
	protected int currentHealth;
	protected bool isDead = false;
	protected bool isAttacking = false;
	protected Coroutine brainRoutine;

	private bool isDistracted = false;
	private bool isStunned = false;
	private float nextChaosCheckTime = 0f;

	private Bait currentBait = null;
	private Vector3 currentBaitDestination;

	// Street AI: Wanderer patrols random road-grid intersections until it spots a civilian down
	// the same straight street within aggroRadius, at which point it becomes a Hunter (one-way —
	// it never reverts). Rolled per spawn/pool reuse in StartBrainIfNeeded(), from
	// chaosSettings.hunterShare (or nightHunterShare during GameManager.GameState.SuddenDeath).
	//
	// feature/azs-crowd-feel (1): Hunter no longer descends the shared F_civ field — each Hunter
	// directly chases one specific civilian (huntTarget), picked at random among the
	// hunterCandidateCount nearest reachable ones, re-picked on death/infection/rescue or on
	// retargetCooldown (+ jitter). F_civ is kept only as a cheap "is anyone even reachable?" gate
	// before bothering to pick (see DecideStreetTarget) — this is what makes zombies scatter onto
	// individual targets instead of moving as one blob along a shared gradient.
	private enum ZombieRole { Hunter, Wanderer }
	private ZombieRole role = ZombieRole.Hunter;

	// fix/zombie-siege-mode: display/debug-only sub-state, set alongside each decision branch
	// below. Hunt = chasing a civilian with a verified complete path. Siege = no candidate has a
	// complete path right now (e.g. a barricade blocks every route) — follows the F_civ lookahead
	// gradient instead, which is finite-cost through barricades, until close enough to attack one.
	// Wander = the existing intersection patrol. Read by CrowdFieldManager's debug overlay.
	public enum ZombieMode { Hunt, Wander, Siege }
	private ZombieMode currentMode = ZombieMode.Wander;
	public ZombieMode CurrentMode => currentMode;
	public NavMeshAgent Agent => agent;

	private Vector3 wanderDestination;
	private bool hasWanderDestination = false;

	private EvacueeBase huntTarget;
	private float nextHuntRepickTime = 0f;

	// fix/zombie-siege-mode (4): continuous "has this agent been ~stationary for StallTime"
	// tracking, updated once per Brain tick (see BrainTickInterval). IsStalled is a live read for
	// the debug overlay's counter; stallHandledThisEpisode makes the actual "drop target, enter
	// siege" reaction one-shot per stall episode instead of re-firing every tick while stalled.
	private const float BrainTickInterval = 0.15f;
	private float zeroVelocityDuration = 0f;
	private bool stallHandledThisEpisode = false;
	public bool IsStalled => zeroVelocityDuration >= StallTime;

	// feature/azs-crowd-feel (5): per-instance variance so a wave of zombies doesn't move as one
	// uniform blob. Rolled once per spawn/pool-reuse in ApplySpawnVariance().
	private float perZombieSpeedMult = 1f;
	private int perZombieAvoidancePriority = 50;
	private ObstacleAvoidanceType originalAvoidanceType;

	// Reused across GatherNearestCivilians() calls to avoid a per-pick allocation.
	private readonly List<EvacueeBase> huntCandidates = new List<EvacueeBase>();
	private readonly List<float> huntCandidateSqrDist = new List<float>();

	private Renderer[] allRenderers;
	private Coroutine hitFlashRoutine;
	private Coroutine deathRoutine;

	private MaterialPropertyBlock propertyBlock;
	private Dictionary<Renderer, Color> originalColors = new Dictionary<Renderer, Color>();
	private Dictionary<Renderer, int> colorPropertyIds = new Dictionary<Renderer, int>();

	private Vector3 initialScale;
	private Collider cachedCollider;
	private NavMeshPath cachedPath;

	// Cached so AttackRoutine/HitFlashRoutine/LoseTargetRoutine's repeated yields don't allocate a
	// new WaitForSeconds every attack/hit/distraction — these durations are fixed per-prefab values
	// (infectDelay/attackCooldown/hitFlashDuration are Inspector fields, chaosSettings isn't
	// reassigned at runtime), so one instance per zombie, created once, covers every pool reuse too.
	private WaitForSeconds infectDelayWait;
	private WaitForSeconds attackCooldownWait;
	private WaitForSeconds hitFlashWait;
	private WaitForSeconds loseTargetWait;

	public bool IsDead => isDead;

	// Read fresh (not cached) so a pooled zombie reused across level loads, or one that outlives
	// a LevelManager swap, always reflects the CURRENT level's multipliers. Always re-applied from
	// the base maxHealth/moveSpeed fields below, never from the current value -- no compounding
	// across repeated pool reuse.
	protected static float SpeedMultiplier =>
		LevelManager.Instance != null && LevelManager.Instance.currentData != null
			? LevelManager.Instance.currentData.zombieSpeedMultiplier : 1f;

	protected static float HealthMultiplier =>
		LevelManager.Instance != null && LevelManager.Instance.currentData != null
			? LevelManager.Instance.currentData.zombieHealthMultiplier : 1f;

	private float RetargetCooldown => chaosSettings != null ? Mathf.Max(0.1f, chaosSettings.retargetCooldown) : 1.5f;
	private float CellSizeOrDefault =>
		CrowdFieldManager.Instance != null && CrowdFieldManager.Instance.GridReady ? CrowdFieldManager.Instance.CellSize : 2f;
	private float AggroRadiusWorld => (chaosSettings != null ? chaosSettings.aggroRadiusTiles : 5f) * CellSizeOrDefault;
	private int HunterCandidateCount => chaosSettings != null ? Mathf.Max(1, chaosSettings.hunterCandidateCount) : 4;
	private int MaxZombiesPerTarget => chaosSettings != null ? Mathf.Max(1, chaosSettings.maxZombiesPerTarget) : 2;
	private float SpeedVariance => chaosSettings != null ? Mathf.Max(0f, chaosSettings.speedVariance) : 0.15f;
	private int LookaheadCells => chaosSettings != null ? Mathf.Max(1, chaosSettings.lookaheadCells) : 6;
	private float StallTime => chaosSettings != null ? Mathf.Max(0.1f, chaosSettings.stallTime) : 1f;

	private float HunterShareForSpawn
	{
		get
		{
			if (chaosSettings == null) return 0.7f;
			bool night = GameManager.Instance != null && GameManager.Instance.State == GameManager.GameState.SuddenDeath;
			return night ? chaosSettings.nightHunterShare : chaosSettings.hunterShare;
		}
	}

	protected virtual void Awake()
	{
		// Covers ZombieBoss too (it doesn't override Awake) -- both prefabs have a same-named
		// "AttractedVisual" bait-attraction hitbox child that must keep its original world size.
		ActorVisualScaler.Apply(transform, transform.Find("AttractedVisual"));

		agent = GetComponent<NavMeshAgent>();
		cachedCollider = GetComponent<Collider>();
		currentHealth = Mathf.RoundToInt(maxHealth * HealthMultiplier);
		initialScale = transform.localScale;
		cachedPath = new NavMeshPath();
		originalAvoidanceType = agent.obstacleAvoidanceType;

		allRenderers = GetComponentsInChildren<Renderer>();
		propertyBlock = new MaterialPropertyBlock();

		infectDelayWait = new WaitForSeconds(infectDelay);
		attackCooldownWait = new WaitForSeconds(attackCooldown);
		hitFlashWait = new WaitForSeconds(hitFlashDuration);
		loseTargetWait = new WaitForSeconds(chaosSettings != null ? chaosSettings.zombieLoseTargetDuration : 0.6f);

		for (int i = 0; i < allRenderers.Length; i++)
		{
			Renderer r = allRenderers[i];
			if (r == null) continue;

			Material sharedMat = r.sharedMaterial;
			if (sharedMat == null) continue;

			if (sharedMat.HasProperty("_BaseColor"))
			{
				colorPropertyIds[r] = Shader.PropertyToID("_BaseColor");
				originalColors[r] = sharedMat.GetColor("_BaseColor");
			}
			else if (sharedMat.HasProperty("_Color"))
			{
				colorPropertyIds[r] = Shader.PropertyToID("_Color");
				originalColors[r] = sharedMat.GetColor("_Color");
			}
		}
	}

	protected virtual void OnEnable()
	{
		AllZombies.Add(this);
	}

	protected virtual void OnDisable()
	{
		AllZombies.Remove(this);
	}

	protected virtual void Start()
	{
		ApplySpawnVariance();

		CrowdFieldManager.Instance?.EnsureConfigured(chaosSettings);
		StartBrainIfNeeded();
	}

	// feature/azs-crowd-feel (5, 6): rolls per-spawn speed/avoidance-priority variance and applies
	// the zombieAvoidanceLow A/B toggle. Called from both Start() and OnTakenFromPool() so a pooled
	// zombie gets a fresh roll each life instead of carrying over its previous one.
	private void ApplySpawnVariance()
	{
		perZombieSpeedMult = 1f + Random.Range(-SpeedVariance, SpeedVariance);
		perZombieAvoidancePriority = Random.Range(30, 71);

		if (agent != null)
		{
			agent.speed = moveSpeed * SpeedMultiplier * perZombieSpeedMult;
			agent.avoidancePriority = perZombieAvoidancePriority;
			agent.obstacleAvoidanceType = (chaosSettings != null && chaosSettings.zombieAvoidanceLow)
				? ObstacleAvoidanceType.LowQualityObstacleAvoidance
				: originalAvoidanceType;
		}
	}

	protected void StartBrainIfNeeded()
	{
		role = Random.value < HunterShareForSpawn ? ZombieRole.Hunter : ZombieRole.Wanderer;
		hasWanderDestination = false;
		huntTarget = null;
		nextHuntRepickTime = 0f;

		if (brainRoutine != null)
		{
			StopCoroutine(brainRoutine);
		}

		brainRoutine = StartCoroutine(Brain());
	}

	public virtual void OnTakenFromPool(Vector3 position, Quaternion rotation)
	{
		transform.position = position;
		transform.rotation = rotation;
		transform.localScale = initialScale;

		isDead = false;
		isAttacking = false;
		isDistracted = false;
		isStunned = false;
		isStunned = false;
		currentHealth = Mathf.RoundToInt(maxHealth * HealthMultiplier);
		nextChaosCheckTime = 0f;

		currentBait = null;
		currentBaitDestination = Vector3.zero;

		if (hitFlashRoutine != null)
		{
			StopCoroutine(hitFlashRoutine);
			hitFlashRoutine = null;
		}

		if (deathRoutine != null)
		{
			StopCoroutine(deathRoutine);
			deathRoutine = null;
		}

		RestoreOriginalColors();

		if (cachedCollider != null)
		{
			cachedCollider.enabled = true;
		}

		if (agent != null)
		{
			agent.enabled = true;
			agent.isStopped = false;
			agent.ResetPath();

			if (NavMesh.SamplePosition(position, out NavMeshHit hit, 3f, NavMesh.AllAreas))
			{
				agent.Warp(hit.position);
			}
		}

		ApplySpawnVariance();
		StartBrainIfNeeded();
	}

	public virtual void OnReturnedToPool()
	{
		if (brainRoutine != null)
		{
			StopCoroutine(brainRoutine);
			brainRoutine = null;
		}

		if (hitFlashRoutine != null)
		{
			StopCoroutine(hitFlashRoutine);
			hitFlashRoutine = null;
		}

		if (deathRoutine != null)
		{
			StopCoroutine(deathRoutine);
			deathRoutine = null;
		}

		RestoreOriginalColors();

		currentBait = null;
		currentBaitDestination = Vector3.zero;
		isAttacking = false;
		isDistracted = false;

		if (agent != null && agent.enabled)
		{
			agent.isStopped = true;
			agent.ResetPath();
			agent.enabled = false;
		}

		if (cachedCollider != null)
		{
			cachedCollider.enabled = false;
		}

		transform.localScale = initialScale;
	}

	protected virtual IEnumerator Brain()
	{
		WaitForSeconds wait = new WaitForSeconds(BrainTickInterval);

		while (!isDead)
		{
			if (GameManager.Instance != null &&
				(GameManager.Instance.State == GameManager.GameState.Planning ||
				 GameManager.Instance.State == GameManager.GameState.GameOver ||
				 GameManager.Instance.State == GameManager.GameState.Lose))
			{
				if (agent != null && agent.enabled && agent.isOnNavMesh)
				{
					agent.isStopped = true;
				}

				yield return wait;
				continue;
			}

			if (isStunned)
			{
				yield return wait;
				continue;
			}

			if (agent != null && agent.enabled && agent.isOnNavMesh)
			{
				agent.isStopped = false;
			}

			if (isDistracted)
			{
				yield return wait;
				continue;
			}

			UpdateStallTracking();

			Transform target;
			Vector3? overrideDestination;
			bool isCruising;
			FindTargetWithBaitPriority(out target, out overrideDestination, out isCruising);

			if (target != null)
			{
				if (ShouldLoseTarget())
				{
					StartCoroutine(LoseTargetRoutine());
					yield return wait;
					continue;
				}

				Vector3 targetPos = overrideDestination ?? target.position;
				float dist = GetPathDistance(targetPos, out bool pathReachable);
				bool hasLOS = HasLineOfSight(targetPos);

				// A barricade's own carved hole in the NavMesh legitimately makes its exact center
				// PathPartial even when it's the right, in-range target -- the straight-line
				// fallback above and the barricade's wider effectiveRange (AttackRoutine) already
				// account for that. Bait crowding targets the bait itself, not a civilian, so it's
				// unaffected. A civilian target with no complete path means a wall in between, not
				// open ground, so "close by straight line" must never count as in range to attack.
				bool targetIsBarricade = currentBait == null && target.GetComponent<Barricade>() != null;
				bool blockedByWall = currentBait == null && !targetIsBarricade && !pathReachable;

				if (dist > attackDistance || !hasLOS || blockedByWall)
				{
					if (blockedByWall && currentBait == null && !targetIsBarricade)
					{
						// fix/zombie-siege-mode (3): the path to this civilian just went Partial
						// (e.g. a barricade placed mid-chase) -- Unity would otherwise send the
						// agent to the nearest reachable point, usually a sidewalk nowhere near
						// the actual obstruction. Drop the target now instead; the next Brain
						// tick re-evaluates (DecideStreetTarget) and falls into siege mode.
						huntTarget = null;
					}
					else if (agent != null && agent.enabled && agent.isOnNavMesh)
					{
						agent.SetDestination(targetPos);
					}

					StopCrowdBehaviorIfAny();
				}
				else
				{
					if (currentBait != null)
					{
						StartCrowdBehaviorAroundBait();
					}
					else if (targetIsBarricade)
					{
						// Barricades keep the original windup + cooldown attack flow unchanged.
						if (!isAttacking)
						{
							StartCoroutine(AttackRoutine(target));
						}
					}
					else
					{
						// feature/azs-crowd-feel (4): bite on contact — immediate, no windup, no
						// cooldown, and movement is never interrupted (no ResetPath here at all).
						// Clearing huntTarget makes the very next Brain tick re-pick instead of
						// waiting out the usual retargetCooldown+jitter.
						InfectTarget(target.gameObject);
						huntTarget = null;
					}
				}
			}
			else if (!isCruising)
			{
				// Only a genuine "nothing to do" (no bait, no close civilian/barricade, and the
				// shared road grid isn't ready yet either) resets the path. When isCruising is true,
				// FindTargetWithBaitPriority already issued its own (cooldown-gated) SetDestination
				// toward the next field-descent step or wander intersection — resetting here would
				// undo that every single tick and reintroduce the old jitter.
				StopCrowdBehaviorIfAny();

				if (agent != null && agent.enabled && agent.isOnNavMesh)
				{
					agent.ResetPath();
				}
			}

			yield return wait;
		}
	}

	private bool ShouldLoseTarget()
	{
		if (chaosSettings == null) return false;
		if (!chaosSettings.chaosEnabled) return false;
		if (Time.time < nextChaosCheckTime) return false;

		nextChaosCheckTime = Time.time + chaosSettings.zombieLoseTargetCheckInterval;
		return Random.value < chaosSettings.zombieLoseTargetChance;
	}

	private IEnumerator LoseTargetRoutine()
	{
		isDistracted = true;

		if (agent != null && agent.enabled && agent.isOnNavMesh)
		{
			agent.ResetPath();
		}

		yield return loseTargetWait;
		isDistracted = false;
	}

	// fix/zombie-siege-mode (4): accumulates how long this zombie has been under ~0.2 speed while
	// not mid-attack (a barricade windup or a civilian bite doesn't count as stalled). Runs once
	// per Brain tick, so the accumulation step is a fixed BrainTickInterval rather than
	// Time.deltaTime (coroutine-paced, not frame-paced).
	private void UpdateStallTracking()
	{
		bool canStall = agent != null && agent.enabled && agent.isOnNavMesh && !isAttacking;
		bool movingEnough = canStall && agent.velocity.magnitude >= 0.2f;

		if (!canStall || movingEnough)
		{
			zeroVelocityDuration = 0f;
			stallHandledThisEpisode = false;
			return;
		}

		zeroVelocityDuration += BrainTickInterval;
	}

	protected virtual void FindTargetWithBaitPriority(out Transform target, out Vector3? overrideDestination, out bool isCruising)
	{
		target = null;
		overrideDestination = null;
		isCruising = false;

		currentBait = TargetScanUtil.FindNearest(Bait.AllBaits, transform.position, float.MaxValue,
			bait => bait.hasValidAttractPoint ? bait.attractPoint : bait.transform.position, out _,
			accept: (bait, dist) => dist <= bait.attractRadius);

		if (currentBait != null)
		{
			Vector3 baitTargetPoint = currentBait.hasValidAttractPoint ? currentBait.attractPoint : currentBait.transform.position;

			Vector2 offset2D = Random.insideUnitCircle * crowdRadius;
			Vector3 offset = new Vector3(offset2D.x, 0f, offset2D.y);
			currentBaitDestination = baitTargetPoint + offset;

			target = currentBait.transform;
			overrideDestination = currentBaitDestination;
			return;
		}

		currentBaitDestination = Vector3.zero;
		target = DecideStreetTarget(out isCruising);
		overrideDestination = null;
	}

	// feature/azs-crowd-feel (1): Hunter no longer descends F_civ — it directly chases a specific
	// civilian (huntTarget), re-picked on death/infection/(near-)rescue or on a cooldown+jitter
	// timer. A Transform target is returned for Brain()'s dist/LOS/attack-range handling in three
	// cases: the hunt target itself, a barricade blocking the way to it (unchanged from before),
	// or — when Hunter has no valid target right now (e.g. every nearby candidate capped out) —
	// the same Wanderer patrol logic, so the zombie keeps moving instead of standing idle.
	protected virtual Transform DecideStreetTarget(out bool isCruising)
	{
		isCruising = false;
		CrowdFieldManager cfm = CrowdFieldManager.Instance;
		bool fieldReady = cfm != null && cfm.GridReady;

		bool isHunterNow = role == ZombieRole.Hunter || (cfm != null && cfm.ForceAllHunters);

		if (isHunterNow)
		{
			isCruising = true;

			// fix/zombie-siege-mode (4): a Hunter barely moving for StallTime while still
			// "chasing" something (not an attack windup) is almost certainly wedged against
			// geometry it can't actually reach — drop it and go straight to siege mode instead of
			// re-picking (which could just select the same unreachable/jammed situation again).
			if (huntTarget != null && IsStalled && !stallHandledThisEpisode)
			{
				stallHandledThisEpisode = true;
				huntTarget = null;
				nextHuntRepickTime = Time.time + RetargetCooldown + Random.Range(0f, 0.5f);
			}

			bool needsRepick = huntTarget == null || !IsValidHuntTarget(huntTarget) || Time.time >= nextHuntRepickTime;

			if (needsRepick)
			{
				// F_civ kept only as the cheap "is anyone even reachable?" gate (brief: "Keep
				// F_civ only for the cheap 'civilian nearby?' check") before paying for the
				// nearest-N scan + a CalculatePath verification below.
				bool worthLooking = !fieldReady || !float.IsPositiveInfinity(cfm.SampleField(transform.position, CrowdFieldType.Civilian));
				huntTarget = worthLooking ? PickNewHuntTarget() : null;
				nextHuntRepickTime = Time.time + RetargetCooldown + Random.Range(0f, 0.5f);
			}

			if (huntTarget != null)
			{
				currentMode = ZombieMode.Hunt;

				// Barricade handling stays as-is: a barricade right next to the zombie takes
				// priority over the chase (NavMesh naturally stalls the agent at its carved hole).
				if (TryFindNearestBarricade(out Barricade blocker)) return blocker.transform;
				return huntTarget.transform;
			}

			// fix/zombie-siege-mode (2): no under-cap candidate has a complete path right now —
			// follow F_civ's lookahead gradient (barricades are finite cost there, so it still
			// leads toward whatever's blocking) instead of patrolling like a Wanderer.
			// TryFindNearestBarricade (inside SiegeStep, same radius as above) takes over the
			// moment that gets the zombie close enough to actually attack.
			currentMode = ZombieMode.Siege;
			return SiegeStep(cfm, fieldReady);
		}

		// Wanderer: patrol a random intersection; promote to Hunter if a civilian is visible down
		// the same straight street within aggroRadius (one-way — never reverts to Wanderer).
		currentMode = ZombieMode.Wander;
		return WanderStep(cfm, fieldReady, allowPromote: true, out isCruising);
	}

	private Transform SiegeStep(CrowdFieldManager cfm, bool fieldReady)
	{
		if (fieldReady && cfm.TryGetLookaheadDescent(transform.position, CrowdFieldType.Civilian, LookaheadCells, out Vector3 step))
		{
			if (agent != null && agent.enabled && agent.isOnNavMesh) agent.SetDestination(step);
		}

		if (TryFindNearestBarricade(out Barricade blocker)) return blocker.transform;

		return null;
	}

	private Transform WanderStep(CrowdFieldManager cfm, bool fieldReady, bool allowPromote, out bool isCruising)
	{
		isCruising = true;

		if (allowPromote && fieldReady)
		{
			EvacueeBase sighted = FindNearestCivilianDirectBase(AggroRadiusWorld);
			if (sighted != null && cfm.IsSameStreetVisible(transform.position, sighted.transform.position))
			{
				role = ZombieRole.Hunter;
				return null;
			}
		}

		bool arrived = agent != null && agent.enabled && agent.isOnNavMesh &&
			!agent.pathPending && (!agent.hasPath || agent.remainingDistance < CellSizeOrDefault);

		if (!hasWanderDestination || arrived)
		{
			if (fieldReady && cfm.TryGetRandomIntersection(out Vector3 dest))
			{
				wanderDestination = dest;
				hasWanderDestination = true;
			}
		}

		if (hasWanderDestination && agent != null && agent.enabled && agent.isOnNavMesh)
		{
			agent.SetDestination(wanderDestination);
		}

		if (TryFindNearestBarricade(out Barricade wanderBlocker)) return wanderBlocker.transform;

		return null;
	}

	// feature/azs-crowd-feel (1, 2), patched by fix/zombie-siege-mode (1): a random civilian
	// among the hunterCandidateCount nearest (straight-line, cheap approximation) ones, respecting
	// the maxZombiesPerTarget cap. A candidate is only ever adopted with a VERIFIED complete
	// NavMesh path — there is deliberately no under-cap fallback to an unreachable candidate
	// anymore: that was exactly what sent Hunters to stand at the nearest sidewalk point to a
	// barricaded civilian instead of approaching the barricade. Returning null here (no complete
	// path found among any under-cap candidate) is the caller's cue to enter siege mode.
	private EvacueeBase PickNewHuntTarget()
	{
		GatherNearestCivilians(HunterCandidateCount);
		if (huntCandidates.Count == 0) return null;

		ShuffleInPlace(huntCandidates);

		foreach (EvacueeBase candidate in huntCandidates)
		{
			if (CountZombiesTargeting(candidate) >= MaxZombiesPerTarget) continue;

			if (agent != null && agent.enabled && agent.isOnNavMesh &&
				agent.CalculatePath(candidate.transform.position, cachedPath) &&
				cachedPath.status == NavMeshPathStatus.PathComplete)
			{
				return candidate;
			}
		}

		return null;
	}

	// feature/azs-crowd-feel (2): same-class private access lets this read every other Zombie's
	// huntTarget directly — no public API needed just to enforce the per-civilian cap.
	private int CountZombiesTargeting(EvacueeBase candidate)
	{
		int count = 0;
		foreach (Zombie z in AllZombies)
		{
			if (z != null && z != this && z.huntTarget == candidate) count++;
		}
		return count;
	}

	// feature/azs-crowd-feel (3): a hunt target stops being valid the instant it's infected (even
	// before it turns) or is actively being lifted into a vehicle — chasing a civilian that's
	// already escaping doesn't fit "fast chain reactions, fluid crowds".
	private static bool IsValidHuntTarget(EvacueeBase e) => e != null && !e.IsInfected && !e.IsBoarding;

	private static void ShuffleInPlace<T>(List<T> list)
	{
		for (int i = list.Count - 1; i > 0; i--)
		{
			int j = Random.Range(0, i + 1);
			(list[i], list[j]) = (list[j], list[i]);
		}
	}

	// Up to maxCount nearest Human/Scientist by straight-line distance, excluding infected
	// civilians (feature/azs-crowd-feel 3) — nearest first, into the reused huntCandidates buffer.
	private void GatherNearestCivilians(int maxCount)
	{
		huntCandidates.Clear();
		huntCandidateSqrDist.Clear();

		void Consider(EvacueeBase e)
		{
			if (e == null || e.IsInfected) return;

			float sq = (e.transform.position - transform.position).sqrMagnitude;
			int insertAt = huntCandidates.Count;
			for (int i = 0; i < huntCandidates.Count; i++)
			{
				if (sq < huntCandidateSqrDist[i]) { insertAt = i; break; }
			}
			if (insertAt >= maxCount) return;

			huntCandidates.Insert(insertAt, e);
			huntCandidateSqrDist.Insert(insertAt, sq);
			if (huntCandidates.Count > maxCount)
			{
				huntCandidates.RemoveAt(maxCount);
				huntCandidateSqrDist.RemoveAt(maxCount);
			}
		}

		foreach (Human h in Human.AllHumans) Consider(h);
		foreach (Scientist s in Scientist.AllScientists) Consider(s);
	}

	// Nearest Human/Scientist by straight-line distance, excluding infected civilians
	// (feature/azs-crowd-feel 3). Used by the Wanderer's aggro-sighting check and as the
	// grid-not-ready fallback.
	private EvacueeBase FindNearestCivilianDirectBase(float maxDistance = float.MaxValue)
	{
		Human nearestHuman = TargetScanUtil.FindNearest(Human.AllHumans, transform.position, maxDistance,
			h => h.transform.position, out float distH, accept: (h, d) => !h.IsInfected);
		Scientist nearestScientist = TargetScanUtil.FindNearest(Scientist.AllScientists, transform.position, maxDistance,
			s => s.transform.position, out float distS, accept: (s, d) => !s.IsInfected);

		if (nearestHuman != null && nearestScientist != null)
			return distH <= distS ? (EvacueeBase)nearestHuman : nearestScientist;

		if (nearestHuman != null) return nearestHuman;
		if (nearestScientist != null) return nearestScientist;
		return null;
	}

	private static float PathCornersLength(NavMeshPath path)
	{
		float dist = 0f;
		Vector3[] corners = path.corners;
		for (int i = 1; i < corners.Length; i++)
		{
			dist += Vector3.Distance(corners[i - 1], corners[i]);
		}
		return dist;
	}

	/// Real distance to a point, measured along the NavMesh path rather than a straight line.
	/// A barricade carves a hole in the NavMesh, so the path to its exact center is often
	/// PathPartial (unreachable) — in that case we fall back to straight-line distance,
	/// which is what the barricade's existing effectiveRange allowance was tuned against.
	/// `reachable` reports whether a COMPLETE path was actually found, so a caller deciding
	/// whether to stop and attack a non-barricade target can refuse to treat "close by straight
	/// line only" (a wall in between) as being in range.
	protected float GetPathDistance(Vector3 targetPos, out bool reachable)
	{
		if (agent != null && agent.enabled && agent.isOnNavMesh &&
			agent.CalculatePath(targetPos, cachedPath) &&
			cachedPath.status == NavMeshPathStatus.PathComplete)
		{
			reachable = true;
			return PathCornersLength(cachedPath);
		}

		reachable = false;
		return Vector3.Distance(transform.position, targetPos);
	}

	protected float GetPathDistance(Vector3 targetPos)
	{
		return GetPathDistance(targetPos, out _);
	}

	protected bool HasLineOfSight(Vector3 targetPos)
	{
		if (lineOfSightObstacles.value == 0) return true;

		Vector3 origin = transform.position + Vector3.up * 0.5f;
		Vector3 destination = targetPos + Vector3.up * 0.5f;
		Vector3 delta = destination - origin;
		float distance = delta.magnitude;

		if (distance <= 0.01f) return true;

		return !Physics.Raycast(origin, delta / distance, distance, lineOfSightObstacles, QueryTriggerInteraction.Ignore);
	}

	private bool TryFindNearestBarricade(out Barricade result)
	{
		result = TargetScanUtil.FindNearest(Barricade.AllBarricades, transform.position, barricadeCheckRadius,
			b => b.transform.position, out _);

		return result != null;
	}

	protected virtual IEnumerator AttackRoutine(Transform target)
	{
		isAttacking = true;

		if (agent != null && agent.enabled && agent.isOnNavMesh)
		{
			agent.ResetPath();
		}

		yield return infectDelayWait;

		if (target != null && !isDead)
		{
			float dist = GetPathDistance(target.position);

			// Barricades carve NavMesh, so the zombie stops slightly past the center —
			// give extra distance allowance
			bool targetIsBarricade = target.GetComponent<Barricade>() != null;
			float effectiveRange = targetIsBarricade
				? attackDistance + 1.5f
				: attackDistance + 0.25f;

			if (dist <= effectiveRange && HasLineOfSight(target.position))
			{
				InfectTarget(target.gameObject);
			}
		}

		yield return attackCooldownWait;
		isAttacking = false;
	}

	protected virtual void InfectTarget(GameObject targetObj)
	{
		if (targetObj == null) return;

		// Barricade — deal damage instead of infecting
		Barricade barricade = targetObj.GetComponent<Barricade>();
		if (barricade != null)
		{
			barricade.TakeDamage(barricadeDamage);
			float stunDur = RunSessionData.Instance?.GetModifier("barricade_stun_dur") ?? 0f;
			if (stunDur > 0f) Stun(stunDur);
			return;
		}

		Human human = targetObj.GetComponent<Human>();
		if (human != null)
		{
			InfectHuman(human);
			return;
		}

		Scientist scientist = targetObj.GetComponent<Scientist>();
		if (scientist != null)
		{
			InfectScientist(scientist);
			return;
		}
	}

	protected virtual void InfectHuman(Human human)
	{
		if (human == null || human.IsInfected) return;

		Feedback.Play(FeedbackEvent.ZombieInfect, human.transform.position + Vector3.up * 0.5f);

		float delay = chaosSettings != null ? chaosSettings.turnDelay : 1.5f;
		human.BeginInfection(delay, evacuee => TurnIntoZombie((Human)evacuee));
	}

	private void TurnIntoZombie(Human human)
	{
		Vector3 pos = human.transform.position;
		Quaternion rot = human.transform.rotation;

		Human.AllHumans.Remove(human);
		Destroy(human.gameObject);
		InfectedThisLevel++;

		if (ZombiePool.Instance != null)
		{
			ZombiePool.Instance.Get(pos, rot);
		}
		else if (zombiePrefab != null)
		{
			Instantiate(zombiePrefab, pos, rot);
		}
	}

	protected virtual void InfectScientist(Scientist scientist)
	{
		if (scientist == null || scientist.IsInfected) return;

		Feedback.Play(FeedbackEvent.ZombieInfect, scientist.transform.position + Vector3.up * 0.5f);

		float delay = chaosSettings != null ? chaosSettings.turnDelay : 1.5f;
		scientist.BeginInfection(delay, evacuee => TurnIntoZombie((Scientist)evacuee));
	}

	private void TurnIntoZombie(Scientist scientist)
	{
		Vector3 pos = scientist.transform.position;
		Quaternion rot = scientist.transform.rotation;

		Scientist.AllScientists.Remove(scientist);
		Destroy(scientist.gameObject);
		InfectedThisLevel++;

		if (ZombiePool.Instance != null)
		{
			ZombiePool.Instance.Get(pos, rot);
		}
		else if (zombiePrefab != null)
		{
			Instantiate(zombiePrefab, pos, rot);
		}
	}

	public virtual void SetBaitTarget(Bait bait)
	{
		if (bait == null) return;

		currentBait = bait;

		Vector3 baitTargetPoint = bait.hasValidAttractPoint ? bait.attractPoint : bait.transform.position;

		Vector2 offset2D = Random.insideUnitCircle * crowdRadius;
		Vector3 offset = new Vector3(offset2D.x, 0f, offset2D.y);
		currentBaitDestination = baitTargetPoint + offset;

		if (agent != null && agent.enabled && agent.isOnNavMesh)
		{
			agent.SetDestination(currentBaitDestination);
		}
	}

	// Crowding around a bait used to also DOShakePosition() the root transform for a visual
	// wobble, directly alongside the NavMeshAgent that owns that same transform every frame. A
	// Tween writing to transform.position while the agent is independently driving it desyncs the
	// agent's internal corridor from the actual rendered position -- the agent can end up thinking
	// it is somewhere it no longer is, producing exactly the "walks straight into a building and
	// gets stuck" pathing failures seen in Docs/CORE_LOOP_V2.md. SetBaitTarget() already gives each
	// zombie its own randomized point (crowdRadius) around the bait, which is enough on its own for
	// zombies to visibly spread out without any extra transform writes.
	private void StartCrowdBehaviorAroundBait()
	{
		if (currentBait == null || agent == null || !agent.enabled || !agent.isOnNavMesh) return;
		agent.SetDestination(currentBaitDestination);
	}

	private void StopCrowdBehaviorIfAny()
	{
		currentBait = null;
	}

	public virtual void TakeDamage(int damageTaken)
	{
		if (isDead) return;

		currentHealth -= damageTaken;

		if (currentHealth <= 0)
		{
			Die();
			return;
		}

		Feedback.Play(FeedbackEvent.ZombieHit, transform.position + Vector3.up * 0.8f);

		if (hitFlashRoutine != null)
		{
			StopCoroutine(hitFlashRoutine);
		}
		hitFlashRoutine = StartCoroutine(HitFlashRoutine());
	}

	private IEnumerator HitFlashRoutine()
	{
		SetAllRendererColors(hitFlashColor);
		yield return hitFlashWait;
		RestoreOriginalColors();
	}

	private void SetAllRendererColors(Color color)
	{
		if (allRenderers == null) return;

		foreach (var r in allRenderers)
		{
			if (r == null) continue;
			if (!colorPropertyIds.ContainsKey(r)) continue;

			r.GetPropertyBlock(propertyBlock);
			propertyBlock.SetColor(colorPropertyIds[r], color);
			r.SetPropertyBlock(propertyBlock);
		}
	}

	private void RestoreOriginalColors()
	{
		if (allRenderers == null) return;

		foreach (var r in allRenderers)
		{
			if (r == null) continue;

			if (!colorPropertyIds.ContainsKey(r) || !originalColors.ContainsKey(r))
			{
				r.SetPropertyBlock(null);
				continue;
			}

			r.GetPropertyBlock(propertyBlock);
			propertyBlock.SetColor(colorPropertyIds[r], originalColors[r]);
			r.SetPropertyBlock(propertyBlock);
		}
	}

	protected virtual void Die()
	{
		if (isDead) return;
		isDead = true;

		Feedback.Play(FeedbackEvent.ZombieDeath, transform.position + Vector3.up * 0.5f);
		XPManager.Instance?.OnZombieKilled(false);

		if (brainRoutine != null)
		{
			StopCoroutine(brainRoutine);
			brainRoutine = null;
		}

		if (hitFlashRoutine != null)
		{
			StopCoroutine(hitFlashRoutine);
			hitFlashRoutine = null;
		}

		if (agent != null && agent.enabled)
		{
			agent.isStopped = true;
			agent.enabled = false;
		}

		if (cachedCollider != null)
		{
			cachedCollider.enabled = false;
		}

		deathRoutine = StartCoroutine(DeathRoutine());
	}

	private IEnumerator DeathRoutine()
	{
		Vector3 startScale = transform.localScale;
		float t = 0f;

		while (t < 1f)
		{
			t += Time.deltaTime / Mathf.Max(0.01f, deathShrinkDuration);
			float eased = Mathf.SmoothStep(0f, 1f, t);
			transform.localScale = Vector3.Lerp(startScale, Vector3.zero, eased);
			yield return null;
		}

		if (ZombiePool.Instance != null)
		{
			ZombiePool.Instance.Release(this);
		}
		else
		{
			Destroy(gameObject);
		}
	}
	public void Stun(float duration)
	{
		if (!isDead)
			StartCoroutine(StunRoutine(duration));
	}

	private System.Collections.IEnumerator StunRoutine(float duration)
	{
		isStunned = true;
		if (agent != null && agent.enabled && agent.isOnNavMesh)
			agent.isStopped = true;
		yield return new WaitForSeconds(duration);
		isStunned = false;
		if (agent != null && agent.enabled && agent.isOnNavMesh)
			agent.isStopped = false;
	}

}