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

	// feature/crowd-tuning-pass: moveSpeed/attackDistance/barricadeCheckRadius/barricadeDamage/
	// attackCooldown/infectDelay/crowdRadius used to be duplicated per-prefab fields here — they
	// now live on the single shared CrowdTuning asset (zombieSpeed, attackDistance,
	// barricadeCheckRadius, barricadeDamage, attackCooldown, infectDelay, crowdRadius). maxHealth
	// and detectRadius are intentionally NOT migrated: maxHealth already differs between this
	// prefab and ZombieBoss (60 vs 200) and centralizing it would erase that; detectRadius is
	// dead code (never read anywhere), not a live tunable.
	[Header("Settings")]
	public int maxHealth = 100;
	public float detectRadius = 10f;

	public GameObject zombiePrefab;

	// fix/barricade-jerk-wave1 (Bug 2a, diagnostic): set by the spawner (LevelManager) right
	// after Get()/Instantiate so a stuck zombie's warning/diagnostic log can name its origin.
	[HideInInspector] public string spawnPointName = "(unknown)";

	[Header("Tuning")]
	public CrowdTuning crowdTuning;

	[Header("Line of Sight")]
	[Tooltip("Layers treated as vision-blocking obstacles (e.g. buildings). Leave as Nothing to skip the raycast check and rely on NavMesh path distance alone.")]
	public LayerMask lineOfSightObstacles = 0;

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

	// fix/bite-and-bait (1): rolled once in LatchOntoBait(), kept until currentBait itself goes away
	// (Unity's fake-null on Destroy) -- previously FindTargetWithBaitPriority re-rolled this every
	// single Brain tick (0.15s), which is what made zombies jitter around a bait instead of settling
	// into a stable crowd point.
	private Vector3 currentBaitOffset;

	// fix/bite-and-bait (2): "a reachable civilian within opportunityRadiusCells by road" -- a
	// separate, higher-priority target than huntTarget (far hunt), re-resolved every tick in
	// ResolveOpportunityTarget() with its own switch hysteresis (opportunitySwitchRatio).
	private EvacueeBase opportunityTarget;

	// fix/bite-and-bait (3): small world-space marker shown above a zombie while LATCHED to a bait
	// (currentBait != null), independent of whether bait is this tick's ACTIVE target (Opportunity
	// can temporarily outrank it per baitOverridesOpportunity). Lazily created on first use.
	private Transform baitedMarker;

	// Street AI: Wanderer patrols random road-grid intersections until it spots a civilian down
	// the same straight street within aggroRadius, at which point it becomes a Hunter (one-way —
	// it never reverts). Rolled per spawn/pool reuse in StartBrainIfNeeded(), from
	// crowdTuning.hunterShare (or nightHunterShare during GameManager.GameState.SuddenDeath).
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
	public enum ZombieMode { Hunt, Wander, Siege, Opportunity, Bait }
	private ZombieMode currentMode = ZombieMode.Wander;
	public ZombieMode CurrentMode => currentMode;
	public NavMeshAgent Agent => agent;

	private Vector3 wanderDestination;
	private bool hasWanderDestination = false;

	private EvacueeBase huntTarget;
	private float nextHuntRepickTime = 0f;

	// feature/crowd-tuning-pass (Part C.3): once SiegeStep identifies a barricade, stay committed
	// to IT specifically (skip hunt re-evaluation entirely) until it's destroyed or no longer the
	// nearest barricade on our route — re-checked only every RetargetCooldown, not every tick, so
	// normal queueing at the chokepoint can't flip Hunt/Siege back and forth.
	private Barricade siegeBarricade;
	private float nextSiegeRouteCheckTime = 0f;

	// fix/zombie-siege-mode (4): continuous "has this agent been ~stationary for StallTime"
	// tracking, updated once per Brain tick (see BrainTickInterval). IsStalled is a live read for
	// the debug overlay's counter; stallHandledThisEpisode makes the actual "drop target, enter
	// siege" reaction one-shot per stall episode instead of re-firing every tick while stalled.
	private const float BrainTickInterval = 0.15f;
	private float zeroVelocityDuration = 0f;
	private bool stallHandledThisEpisode = false;
	public bool IsStalled => zeroVelocityDuration >= StallTime;

	// fix/barricade-jerk-wave1 (diagnostic): a longer, independent threshold purely for logging
	// -- fires once per stall episode, separate from stallHandledThisEpisode's 1s behavioral
	// reaction, which may already have fired (and the zombie still be stuck) well before this.
	private const float DiagnosticStallTime = 2f;
	private float diagnosticStallDuration = 0f;
	private bool diagnosticLoggedThisEpisode = false;

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
	// new WaitForSeconds every attack/hit/distraction — hitFlashDuration is a fixed per-prefab
	// value, and crowdTuning isn't reassigned at runtime, so one instance per zombie, created
	// once, covers every pool reuse too (re-rolled on each spawn/pool reuse in Awake()).
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

	// feature/crowd-tuning-pass: every tunable below reads crowdTuning directly (a property, not
	// a cached field) so Inspector edits apply immediately in Play Mode.
	private float ZombieSpeed => crowdTuning != null ? crowdTuning.zombieSpeed : 1.0f;
	private float AttackDistance => crowdTuning != null ? crowdTuning.attackDistance : 1.4f;
	private float BarricadeCheckRadius => crowdTuning != null ? crowdTuning.barricadeCheckRadius : 3.5f;
	private int BarricadeDamage => crowdTuning != null ? crowdTuning.barricadeDamage : 25;
	private float AttackCooldownSeconds => crowdTuning != null ? crowdTuning.attackCooldown : 1.0f;
	private float CrowdRadius => crowdTuning != null ? crowdTuning.crowdRadius : 0.8f;
	private float InfectDelaySeconds => crowdTuning != null ? crowdTuning.infectDelay : 0.15f;
	private float TurnDelay => crowdTuning != null ? crowdTuning.turnDelay : 0.7f;

	private float RetargetCooldown => crowdTuning != null ? Mathf.Max(0.1f, crowdTuning.retargetCooldown) : 1.5f;
	private float CellSizeOrDefault =>
		CrowdFieldManager.Instance != null && CrowdFieldManager.Instance.GridReady ? CrowdFieldManager.Instance.CellSize : 2f;
	private int HunterCandidateCount => crowdTuning != null ? Mathf.Max(1, crowdTuning.hunterCandidateCount) : 4;
	private int MaxZombiesPerTarget => crowdTuning != null ? Mathf.Max(1, crowdTuning.maxZombiesPerTarget) : 2;
	private float SpeedVariance => crowdTuning != null ? Mathf.Max(0f, crowdTuning.speedVariance) : 0.15f;
	private int LookaheadCells => crowdTuning != null ? Mathf.Max(1, crowdTuning.lookaheadCells) : 6;
	private float StallTime => crowdTuning != null ? Mathf.Max(0.1f, crowdTuning.stallTime) : 1f;

	// feature/crowd-tuning-pass (Part C.1): a Hunt target is only accepted if the real NavMesh
	// path to it isn't a wild detour compared to the zombie's own F_civ field reading.
	private float MaxDetourRatio => crowdTuning != null ? Mathf.Max(1f, crowdTuning.maxDetourRatio) : 1.5f;

	// feature/crowd-tuning-pass (Part C.2): standing still within this radius of a barricade
	// while sieging is normal queueing, not a stall.
	private float SiegeQueueRadiusWorld => (crowdTuning != null ? crowdTuning.siegeQueueRadiusCells : 3f) * CellSizeOrDefault;

	// fix/barricade-jerk-wave1 (Bug 2a): spawn-snap radius for OnTakenFromPool's NavMesh.SamplePosition.
	private float SpawnSnapRadius => crowdTuning != null ? Mathf.Max(3f, crowdTuning.spawnSnapRadius) : 10f;

	// fix/bite-and-bait: Opportunity/contact-bite tunables.
	private int BiteFieldGateSteps => crowdTuning != null ? Mathf.Max(0, crowdTuning.biteFieldGateSteps) : 2;
	private float OpportunityRadiusWorld => (crowdTuning != null ? Mathf.Max(1, crowdTuning.opportunityRadiusCells) : 4) * CellSizeOrDefault;
	private float OpportunitySwitchRatio => crowdTuning != null ? Mathf.Max(1f, crowdTuning.opportunitySwitchRatio) : 1.5f;
	private bool BaitOverridesOpportunity => crowdTuning != null && crowdTuning.baitOverridesOpportunity;
	private bool ShowBaitedMarker => crowdTuning == null || crowdTuning.showBaitedMarker;

	private float HunterShareForSpawn
	{
		get
		{
			if (crowdTuning == null) return 0.7f;
			bool night = GameManager.Instance != null && GameManager.Instance.State == GameManager.GameState.SuddenDeath;
			return night ? crowdTuning.nightHunterShare : crowdTuning.hunterShare;
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

		// These few durations are still cached into WaitForSeconds objects once (an existing,
		// unrelated GC-avoidance decision — Docs/REFACTOR_AUDIT.md Phase B), so an Inspector edit
		// to infectDelay/attackCooldown/zombieLoseTargetDuration on crowdTuning only takes effect
		// on this zombie's next spawn/pool-reuse, not mid-attack.
		infectDelayWait = new WaitForSeconds(InfectDelaySeconds);
		attackCooldownWait = new WaitForSeconds(AttackCooldownSeconds);
		hitFlashWait = new WaitForSeconds(hitFlashDuration);
		loseTargetWait = new WaitForSeconds(crowdTuning != null ? crowdTuning.zombieLoseTargetDuration : 0.6f);

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

		CrowdFieldManager.Instance?.EnsureConfigured(crowdTuning);
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
			agent.speed = ZombieSpeed * SpeedMultiplier * perZombieSpeedMult;
			agent.avoidancePriority = perZombieAvoidancePriority;
			agent.obstacleAvoidanceType = (crowdTuning != null && crowdTuning.zombieAvoidanceLow)
				? ObstacleAvoidanceType.LowQualityObstacleAvoidance
				: originalAvoidanceType;
		}
	}

	protected void StartBrainIfNeeded()
	{
		role = Random.value < HunterShareForSpawn ? ZombieRole.Hunter : ZombieRole.Wanderer;
		hasWanderDestination = false;
		huntTarget = null;
		opportunityTarget = null;
		nextHuntRepickTime = 0f;
		siegeBarricade = null;
		nextSiegeRouteCheckTime = 0f;

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
		currentBaitOffset = Vector3.zero;
		opportunityTarget = null;
		UpdateBaitedMarker(false);

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

			// fix/barricade-jerk-wave1 (Bug 2a): the old fixed 3f radius left the agent off-mesh
			// (isOnNavMesh false, never warped, never moves again) for any spawn point farther
			// than that from the baked NavMesh. SpawnSnapRadius defaults to 10 instead.
			if (NavMesh.SamplePosition(position, out NavMeshHit hit, SpawnSnapRadius, NavMesh.AllAreas))
			{
				agent.Warp(hit.position);
			}
			else
			{
				Debug.LogWarning($"[Zombie] Spawn point '{spawnPointName}' at {position} is farther than {SpawnSnapRadius}m from the baked NavMesh -- agent could not be placed and will not move.", this);
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
		currentBaitOffset = Vector3.zero;
		opportunityTarget = null;
		UpdateBaitedMarker(false);
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
			BrainTick();
			yield return wait;
		}
	}

	// fix/barricade-jerk-wave1 (Bug 1): the coroutine's per-iteration body, extracted so
	// ForceImmediateRedecision() (called from CrowdFieldManager the instant a barricade changes)
	// can run exactly the same decision logic synchronously, same frame, instead of waiting for
	// the next 0.15s WaitForSeconds to elapse. Timing is unaffected for the normal coroutine path
	// — it still waits BrainTickInterval after every call regardless of which branch ran below,
	// identical to the original while-loop's yield-after-every-continue behavior.
	private void BrainTick()
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

			return;
		}

		if (isStunned) return;

		if (agent != null && agent.enabled && agent.isOnNavMesh)
		{
			agent.isStopped = false;
		}

		if (isDistracted) return;

		UpdateStallTracking();

		// fix/bite-and-bait (priority 1): contact bite -- ANY non-infected civilian within
		// attackDistance gets bitten immediately, in every mode (hunt/wander/siege/bait), on top
		// of whatever the zombie is otherwise doing this tick. Never interrupts movement (no
		// ResetPath/SetDestination here) -- same "bite on contact" philosophy as the old
		// current-target-only version (feature/azs-crowd-feel 4), just no longer limited to
		// whichever civilian happened to already be the chosen target.
		TryContactBite();

		Transform target;
		Vector3? overrideDestination;
		bool isCruising;
		FindTargetWithBaitPriority(out target, out overrideDestination, out isCruising);

		UpdateBaitedMarker(currentBait != null);

		if (target != null)
		{
			if (ShouldLoseTarget())
			{
				StartCoroutine(LoseTargetRoutine());
				return;
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
			bool isBaitTarget = currentMode == ZombieMode.Bait;
			bool targetIsBarricade = !isBaitTarget && target.GetComponent<Barricade>() != null;
			bool blockedByWall = !isBaitTarget && !targetIsBarricade && !pathReachable;

			if (dist > AttackDistance || !hasLOS || blockedByWall)
			{
				if (blockedByWall && !isBaitTarget && !targetIsBarricade)
				{
					// fix/barricade-jerk-wave1 (Bug 1): don't just drop the target and leave the
					// agent with no new destination until the next tick -- issue the siege step
					// RIGHT NOW, same frame, so NavMeshAgent's own instant auto-repath (to the
					// nearest reachable point the moment carving invalidates the path) gets
					// overridden immediately instead of visibly holding for up to 0.15s.
					huntTarget = null;
					opportunityTarget = null;
					CrowdFieldManager cfmNow = CrowdFieldManager.Instance;
					bool fieldReadyNow = cfmNow != null && cfmNow.GridReady;
					currentMode = ZombieMode.Siege;
					SiegeStep(cfmNow, fieldReadyNow, out _);
				}
				else if (agent != null && agent.enabled && agent.isOnNavMesh)
				{
					agent.SetDestination(targetPos);
				}
			}
			else
			{
				if (isBaitTarget)
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
					// Clearing huntTarget/opportunityTarget makes the very next Brain tick
					// re-pick instead of waiting out the usual retargetCooldown+jitter.
					InfectTarget(target.gameObject);
					huntTarget = null;
					opportunityTarget = null;
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
			if (agent != null && agent.enabled && agent.isOnNavMesh)
			{
				agent.ResetPath();
			}
		}
	}

	// fix/barricade-jerk-wave1 (Bug 1): called by CrowdFieldManager on every zombie within
	// barricadeReactRadiusCells the instant a barricade is placed/destroyed.
	public void ForceImmediateRedecision()
	{
		if (isDead) return;
		BrainTick();
	}

	private bool ShouldLoseTarget()
	{
		if (crowdTuning == null) return false;
		if (!crowdTuning.legacyChaosEnabled) return false;
		if (Time.time < nextChaosCheckTime) return false;

		nextChaosCheckTime = Time.time + crowdTuning.zombieLoseTargetCheckInterval;
		return Random.value < crowdTuning.zombieLoseTargetChance;
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

		// feature/crowd-tuning-pass (Part C.2): queueing at a barricade chokepoint is expected
		// standing-still, not a stuck agent — don't even accumulate stall time there.
		bool nearBarricadeQueue = canStall && IsNearBarricade(SiegeQueueRadiusWorld);
		if (nearBarricadeQueue) canStall = false;

		bool movingEnough = canStall && agent.velocity.magnitude >= 0.2f;

		if (!canStall || movingEnough)
		{
			zeroVelocityDuration = 0f;
			stallHandledThisEpisode = false;
		}
		else
		{
			zeroVelocityDuration += BrainTickInterval;
		}

		// fix/barricade-jerk-wave1 (diagnostic): deliberately broader/independent of the
		// behavioral stall above -- doesn't require agent.isOnNavMesh, so an off-mesh zombie
		// (Bug 2a: Warp failed, agent never attached) is caught too, not just an on-mesh one
		// that's merely jammed. Still excludes normal barricade queueing.
		bool diagnosticMoving = agent != null && agent.enabled && agent.velocity.magnitude >= 0.2f;
		bool diagnosticQueueing = agent != null && agent.enabled && agent.isOnNavMesh && nearBarricadeQueue;

		if (diagnosticMoving || diagnosticQueueing)
		{
			diagnosticStallDuration = 0f;
			diagnosticLoggedThisEpisode = false;
			return;
		}

		diagnosticStallDuration += BrainTickInterval;

		if (!diagnosticLoggedThisEpisode && diagnosticStallDuration >= DiagnosticStallTime)
		{
			diagnosticLoggedThisEpisode = true;
			LogStallDiagnostic();
		}
	}

	// fix/barricade-jerk-wave1 (diagnostic): logs the exact state needed to tell Bug 2a
	// (off-mesh spawn) apart from Bug 2b (own-cell field read) apart from anything else.
	private void LogStallDiagnostic()
	{
		CrowdFieldManager cfm = CrowdFieldManager.Instance;
		bool fieldReady = cfm != null && cfm.GridReady;

		string gameState = GameManager.Instance != null ? GameManager.Instance.State.ToString() : "null";
		bool isOnNavMesh = agent != null && agent.isOnNavMesh;
		bool hasPath = agent != null && agent.hasPath;
		string pathStatus = agent != null ? agent.pathStatus.ToString() : "n/a";
		bool ownCellWalkable = fieldReady && cfm.IsOwnCellWalkable(transform.position);
		float civField = fieldReady ? cfm.SampleField(transform.position, CrowdFieldType.Civilian) : float.PositiveInfinity;

		Debug.LogWarning($"[Zombie] Stalled >{DiagnosticStallTime}s outside barricade queueing: " +
			$"mode={currentMode}, gameState={gameState}, isOnNavMesh={isOnNavMesh}, hasPath={hasPath}, " +
			$"pathStatus={pathStatus}, ownCellWalkable={ownCellWalkable}, civField={civField}, " +
			$"spawnPoint='{spawnPointName}'", this);
	}

	// fix/bite-and-bait: top-to-bottom priority, re-evaluated every Brain tick (contact bite itself
	// lives in BrainTick(), run unconditionally before this is even called):
	//   2. Opportunity -- a reachable-by-road civilian within opportunityRadiusCells, for Hunter
	//      AND Wanderer alike (ResolveOpportunityTarget, replacing the old same-straight-street
	//      Wanderer-promotion check entirely).
	//   3. Bait -- only if latched AND (baitOverridesOpportunity OR no Opportunity target this tick).
	//   4-6. Siege / far Hunt / Wander, unchanged internally (DecideStreetTarget).
	// TryUpdateBaitLatch() runs regardless of which branch wins, so the SAME bait + crowd offset is
	// remembered (bug 1 fix) even on a tick where Opportunity outranks it.
	protected virtual void FindTargetWithBaitPriority(out Transform target, out Vector3? overrideDestination, out bool isCruising)
	{
		target = null;
		overrideDestination = null;
		isCruising = false;

		EvacueeBase opportunity = ResolveOpportunityTarget();
		bool hasBait = TryUpdateBaitLatch(out Vector3 baitDestination);

		if (hasBait && (BaitOverridesOpportunity || opportunity == null))
		{
			currentMode = ZombieMode.Bait;
			target = currentBait.transform;
			overrideDestination = baitDestination;
			return;
		}

		if (opportunity != null)
		{
			currentMode = ZombieMode.Opportunity;

			// Consistent with Hunt/Siege/Wander below: a barricade right next to the zombie takes
			// priority over the civilian chase.
			if (TryFindNearestBarricade(out Barricade blocker))
			{
				target = blocker.transform;
				return;
			}

			target = opportunity.transform;
			return;
		}

		target = DecideStreetTarget(out isCruising);
		overrideDestination = null;
	}

	// fix/bite-and-bait (2): attraction is by walking distance (NavMesh path length), not a
	// straight line -- a straight-line-only check could attract a zombie across a wall/river it
	// can't actually cross. Checked once right here (only while NOT already latched); once
	// currentBait != null, the SAME bait + offset is reused every tick until the bait itself goes
	// away (Unity fake-null on Destroy) -- fixes bug 1 (offset re-rolled every tick).
	private bool TryUpdateBaitLatch(out Vector3 destination)
	{
		destination = Vector3.zero;

		if (currentBait != null)
		{
			destination = GetBaitDestination();
			return true;
		}

		Bait candidate = TargetScanUtil.FindNearest(Bait.AllBaits, transform.position, float.MaxValue,
			bait => bait.hasValidAttractPoint ? bait.attractPoint : bait.transform.position, out _,
			accept: (bait, straightDist) =>
			{
				if (straightDist > bait.attractRadius) return false; // cheap straight-line reject first
				Vector3 point = bait.hasValidAttractPoint ? bait.attractPoint : bait.transform.position;
				float pathDist = GetPathDistance(point, out bool reachable);
				return reachable && pathDist <= bait.attractRadius;
			});

		if (candidate == null)
		{
			currentBaitDestination = Vector3.zero;
			return false;
		}

		LatchOntoBait(candidate);
		destination = GetBaitDestination();
		return true;
	}

	private void LatchOntoBait(Bait bait)
	{
		currentBait = bait;
		Vector2 offset2D = Random.insideUnitCircle * CrowdRadius;
		currentBaitOffset = new Vector3(offset2D.x, 0f, offset2D.y);
	}

	private Vector3 GetBaitDestination()
	{
		Vector3 baitTargetPoint = currentBait.hasValidAttractPoint ? currentBait.attractPoint : currentBait.transform.position;
		currentBaitDestination = baitTargetPoint + currentBaitOffset;
		return currentBaitDestination;
	}

	// fix/bite-and-bait (1, priority bullet): a reachable-by-road civilian within
	// opportunityRadiusCells, regardless of Hunter/Wanderer role, ignoring maxZombiesPerTarget.
	// Switching away from an already-chosen opportunity target requires the new candidate to be
	// opportunitySwitchRatio times closer (by path), so two near-equal candidates don't flicker.
	private EvacueeBase ResolveOpportunityTarget()
	{
		float radiusWorld = OpportunityRadiusWorld;
		EvacueeBase found = FindOpportunityTarget(radiusWorld, out float foundDist);

		if (opportunityTarget != null && IsValidHuntTarget(opportunityTarget))
		{
			float currentDist = GetPathDistance(opportunityTarget.transform.position, out bool reachable);
			if (reachable && currentDist <= radiusWorld)
			{
				if (found != null && found != opportunityTarget && foundDist * OpportunitySwitchRatio < currentDist)
				{
					opportunityTarget = found;
				}
				return opportunityTarget;
			}
		}

		opportunityTarget = found;
		return opportunityTarget;
	}

	// Nearest-by-path candidate (not nearest-by-straight-line) among the straight-line nearest-N,
	// within radiusWorld. The straight-line pre-filter is a safe early-break: a walking path is
	// never shorter than the straight line, so once a sorted-ascending candidate's straight-line
	// distance exceeds radiusWorld, every candidate after it is excluded too.
	private EvacueeBase FindOpportunityTarget(float radiusWorld, out float bestPathDist)
	{
		bestPathDist = float.PositiveInfinity;
		GatherNearestCivilians(HunterCandidateCount);

		EvacueeBase best = null;
		float radiusSqr = radiusWorld * radiusWorld;

		for (int i = 0; i < huntCandidates.Count; i++)
		{
			if (huntCandidateSqrDist[i] > radiusSqr) break;

			float d = GetPathDistance(huntCandidates[i].transform.position, out bool reachable);
			if (!reachable || d > radiusWorld) continue;

			if (d < bestPathDist)
			{
				bestPathDist = d;
				best = huntCandidates[i];
			}
		}

		return best;
	}

	// fix/bite-and-bait (priority 1): any non-infected civilian within attackDistance, regardless
	// of current mode/target -- gated by a cheap F_civ-at-own-cell check so most zombies most ticks
	// skip the nearest-civilian scan entirely (nobody's even close enough for it to matter).
	private bool TryContactBite()
	{
		CrowdFieldManager cfm = CrowdFieldManager.Instance;
		if (cfm != null && cfm.GridReady)
		{
			float civSteps = cfm.SampleField(transform.position, CrowdFieldType.Civilian);
			if (civSteps > BiteFieldGateSteps) return false;
		}

		EvacueeBase nearest = FindNearestCivilianDirectBase(AttackDistance);
		if (nearest == null || !HasLineOfSight(nearest.transform.position)) return false;

		InfectTarget(nearest.gameObject);

		if (huntTarget == nearest) huntTarget = null;
		if (opportunityTarget == nearest) opportunityTarget = null;

		return true;
	}

	// fix/bite-and-bait (3): lazily-created small world-space marker, shown above a zombie while
	// LATCHED to a bait (currentBait != null) -- independent of whether bait is the ACTIVE target
	// this tick (Opportunity can outrank it). "sandbox debug overlay or always" from the brief is
	// the showBaitedMarker toggle: on by default (a real gameplay read on crowding), off hides it.
	private void UpdateBaitedMarker(bool latched)
	{
		bool show = latched && ShowBaitedMarker;

		if (!show)
		{
			if (baitedMarker != null) baitedMarker.gameObject.SetActive(false);
			return;
		}

		if (baitedMarker == null) baitedMarker = CreateBaitedMarker();
		baitedMarker.gameObject.SetActive(true);
	}

	private const float BaitedMarkerHeight = 2.1f;
	private const float BaitedMarkerScale = 0.35f;

	private Transform CreateBaitedMarker()
	{
		GameObject go = new GameObject("BaitedMarker");
		go.transform.SetParent(transform, false);
		go.transform.localPosition = new Vector3(0f, BaitedMarkerHeight, 0f);
		// Faces up (+Y), matching Bait's own top-down-visible visuals (ResolveAttractPoint's
		// attractPointVisual rotation) -- a sprite's default forward (+Z) would be seen edge-on
		// from this project's steep downward camera angles.
		go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
		go.transform.localScale = Vector3.one * BaitedMarkerScale;

		SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
		sr.sprite = RuntimeUiKit.Diamond();
		sr.color = new Color(1f, 0.45f, 0.1f, 0.95f); // matches Bait.ringColor
		sr.sortingOrder = 50;

		return go.transform;
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

			// feature/crowd-tuning-pass (Part C.3): once committed to besieging a specific
			// barricade, stay there — skip hunt re-evaluation entirely — until it's destroyed or
			// no longer the nearest barricade on our route. Re-checked only every
			// RetargetCooldown, not every tick, so normal queueing jitter can't flip this.
			if (siegeBarricade != null)
			{
				bool recheck = Time.time >= nextSiegeRouteCheckTime;
				bool stillValid = !recheck || IsBarricadeStillOnRoute(siegeBarricade);

				if (stillValid)
				{
					if (recheck) nextSiegeRouteCheckTime = Time.time + RetargetCooldown;
					currentMode = ZombieMode.Siege;
					Transform lockedSiegeTarget = SiegeStep(cfm, fieldReady, out bool lockedDidSet);
					if (lockedSiegeTarget != null || lockedDidSet) return lockedSiegeTarget;

					// fix/barricade-jerk-wave1 (Bug 2b): even the locked barricade's own lookahead
					// produced nothing (e.g. this cell is unresolvable) -- release the lock and
					// fall through to a full re-evaluation below instead of standing idle.
					siegeBarricade = null;
				}
				else
				{
					siegeBarricade = null;
				}
			}

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
			Transform siegeTarget = SiegeStep(cfm, fieldReady, out bool didSetDestination);
			if (siegeTarget != null || didSetDestination) return siegeTarget;

			// fix/barricade-jerk-wave1 (Bug 2b): siege produced NO destination at all (own cell
			// unresolvable even via ResolveWalkableCell, field unreachable, whatever) -- never
			// leave the zombie standing idle. Fall back to the same patrol logic a Wanderer uses.
			currentMode = ZombieMode.Wander;
			return WanderStep(cfm, fieldReady, out isCruising);
		}

		// Wanderer: patrol a random intersection. No longer promotes itself to Hunter on sighting a
		// civilian (fix/bite-and-bait: ResolveOpportunityTarget, checked before DecideStreetTarget
		// is ever reached, covers that for Wanderers too).
		currentMode = ZombieMode.Wander;
		return WanderStep(cfm, fieldReady, out isCruising);
	}

	// fix/barricade-jerk-wave1 (Bug 2b): didSetDestination lets the caller tell "sieging, moving
	// toward a barricade/gradient step" apart from "produced nothing at all" so it can fall back
	// to WanderStep rather than leaving the zombie standing idle forever.
	private Transform SiegeStep(CrowdFieldManager cfm, bool fieldReady, out bool didSetDestination)
	{
		didSetDestination = false;

		if (fieldReady && cfm.TryGetLookaheadDescent(transform.position, CrowdFieldType.Civilian, LookaheadCells, out Vector3 step))
		{
			if (agent != null && agent.enabled && agent.isOnNavMesh)
			{
				agent.SetDestination(step);
				didSetDestination = true;
			}
		}

		if (TryFindNearestBarricade(out Barricade blocker))
		{
			// feature/crowd-tuning-pass (Part C.3): commit to this specific barricade so the
			// Hunter branch above stops re-evaluating hunt candidates every tick while sieging.
			siegeBarricade = blocker;
			nextSiegeRouteCheckTime = Time.time + RetargetCooldown;
			return blocker.transform;
		}

		return null;
	}

	// fix/bite-and-bait: used to also promote a Wanderer to Hunter on spotting a civilian down the
	// same straight street (fieldReady + allowPromote) -- removed, replaced entirely by
	// ResolveOpportunityTarget() in FindTargetWithBaitPriority(), which runs for Wanderers too and
	// is checked by road distance rather than same-row/column-only visibility.
	private Transform WanderStep(CrowdFieldManager cfm, bool fieldReady, out bool isCruising)
	{
		isCruising = true;

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

		// feature/crowd-tuning-pass (Part C.1): a complete path alone isn't enough — it must also
		// not be a wild detour compared to how far F_civ itself says civilians are from here.
		// Grid-not-ready/no-field-reading falls back to "any complete path", same as before.
		CrowdFieldManager cfm = CrowdFieldManager.Instance;
		float maxPath = float.PositiveInfinity;
		if (cfm != null && cfm.GridReady)
		{
			float civSteps = cfm.SampleField(transform.position, CrowdFieldType.Civilian);
			if (!float.IsPositiveInfinity(civSteps))
				maxPath = civSteps * cfm.CellSize * MaxDetourRatio;
		}

		foreach (EvacueeBase candidate in huntCandidates)
		{
			if (CountZombiesTargeting(candidate) >= MaxZombiesPerTarget) continue;

			if (agent != null && agent.enabled && agent.isOnNavMesh &&
				agent.CalculatePath(candidate.transform.position, cachedPath) &&
				cachedPath.status == NavMeshPathStatus.PathComplete &&
				PathCornersLength(cachedPath) <= maxPath)
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
		result = TargetScanUtil.FindNearest(Barricade.AllBarricades, transform.position, BarricadeCheckRadius,
			b => b.transform.position, out _);

		return result != null;
	}

	// feature/crowd-tuning-pass (Part C.2): a wider, separate radius from BarricadeCheckRadius —
	// used only to decide whether "standing still" counts as a stall, not whether to attack.
	private bool IsNearBarricade(float radius)
	{
		return TargetScanUtil.FindNearest(Barricade.AllBarricades, transform.position, radius,
			b => b.transform.position, out _) != null;
	}

	// feature/crowd-tuning-pass (Part C.3): still "on route" if it's still the nearest barricade
	// within the normal attack-check radius — if a different one (or none) is closer now, the
	// field has routed us elsewhere and the siege commitment should release.
	private bool IsBarricadeStillOnRoute(Barricade b)
	{
		if (b == null) return false;
		return TryFindNearestBarricade(out Barricade current) && current == b;
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
				? AttackDistance + 1.5f
				: AttackDistance + 0.25f;

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
			barricade.TakeDamage(BarricadeDamage);
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

		float delay = TurnDelay;
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

		float delay = TurnDelay;
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

		// fix/bite-and-bait (1): routed through the same latch helper FindTargetWithBaitPriority
		// uses, instead of rolling its own separate offset -- keeps this (currently unused by any
		// caller, but public API) consistent with the roll-once-per-latch fix.
		LatchOntoBait(bait);
		Vector3 destination = GetBaitDestination();

		if (agent != null && agent.enabled && agent.isOnNavMesh)
		{
			agent.SetDestination(destination);
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