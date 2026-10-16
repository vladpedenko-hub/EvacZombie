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

	// Street AI (feature/zombie-street-ai): Hunter descends the shared F_civ distance field toward
	// civilians; Wanderer patrols random road-grid intersections until it spots a civilian down the
	// same straight street within aggroRadius, at which point it becomes a Hunter (one-way — it
	// never reverts, so this can't become a new source of flip-flopping). Rolled per spawn/pool
	// reuse in StartBrainIfNeeded(), from chaosSettings.hunterShare (or nightHunterShare during
	// GameManager.GameState.SuddenDeath).
	private enum ZombieRole { Hunter, Wanderer }
	private ZombieRole role = ZombieRole.Hunter;

	// Field-descent/wander navigation decisions (picking the next grid step or a new wander
	// intersection) happen at most once per RetargetCooldown, or sooner if the zombie arrives at
	// its current destination first (fix/zombie-corner-stuck 3). The opportunistic close-range
	// civilian scan below is NOT gated by this; it only runs when the shared field already reports
	// a civilian within CloseRangeTiles, which keeps it cheap.
	private const float CloseRangeTiles = 2f;
	private float nextDecisionTime = 0f;
	private Vector3 wanderDestination;
	private bool hasWanderDestination = false;

	// fix/zombie-corner-stuck (2): a close-range civilian is only adopted as a direct chase target
	// if its NavMesh path is short enough (CloseChaseMaxPath), and then held for VictimLockTime —
	// re-scanning by straight line every tick could pick a civilian behind a wall and fight the
	// field direction every single Brain tick.
	private Transform closeChaseLockTarget;
	private float closeChaseLockUntil = -999f;

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
	private int LookaheadCells => chaosSettings != null ? Mathf.Max(1, chaosSettings.lookaheadCells) : 6;
	private float CloseChaseMaxPath => chaosSettings != null ? Mathf.Max(0.1f, chaosSettings.closeChaseMaxPath) : 6f;
	private float VictimLockTime => chaosSettings != null ? Mathf.Max(0f, chaosSettings.victimLockTime) : 1f;
	private float CellSizeOrDefault =>
		CrowdFieldManager.Instance != null && CrowdFieldManager.Instance.GridReady ? CrowdFieldManager.Instance.CellSize : 2f;
	private float AggroRadiusWorld => (chaosSettings != null ? chaosSettings.aggroRadiusTiles : 5f) * CellSizeOrDefault;

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
		if (agent != null)
		{
			agent.speed = moveSpeed * SpeedMultiplier;
		}

		CrowdFieldManager.Instance?.EnsureConfigured(chaosSettings);
		StartBrainIfNeeded();
	}

	protected void StartBrainIfNeeded()
	{
		role = Random.value < HunterShareForSpawn ? ZombieRole.Hunter : ZombieRole.Wanderer;
		nextDecisionTime = 0f;
		hasWanderDestination = false;
		closeChaseLockTarget = null;
		closeChaseLockUntil = -999f;

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
			agent.speed = moveSpeed * SpeedMultiplier;
			agent.isStopped = false;
			agent.ResetPath();

			if (NavMesh.SamplePosition(position, out NavMeshHit hit, 3f, NavMesh.AllAreas))
			{
				agent.Warp(hit.position);
			}
		}

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
		WaitForSeconds wait = new WaitForSeconds(0.15f);

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
					if (agent != null && agent.enabled && agent.isOnNavMesh)
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
					else
					{
						if (!isAttacking)
						{
							StartCoroutine(AttackRoutine(target));
						}
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

	// Street AI (feature/zombie-street-ai, patched by fix/zombie-corner-stuck): replaces the old
	// per-tick 3-candidate shortlist + CalculatePath + sticky-target damping. Long-range
	// navigation is now a read of the shared F_civ field at this zombie's own cell, decided at
	// most once per RetargetCooldown (or sooner on arrival) — SetDestination is issued directly
	// from here for that case (isCruising = true, target = null) rather than being handed back up
	// to Brain(). A Transform target is only returned for the two cases Brain() still needs exact
	// position/LOS/attack-range handling for: a civilian already close enough to engage, or a
	// barricade blocking the chosen step.
	protected virtual Transform DecideStreetTarget(out bool isCruising)
	{
		isCruising = false;
		CrowdFieldManager cfm = CrowdFieldManager.Instance;
		bool fieldReady = cfm != null && cfm.GridReady;

		// fix/zombie-corner-stuck (2): a locked close-range victim is held for VictimLockTime
		// regardless of everything else below, so chasing it can't fight the field direction
		// every single Brain tick. The Unity null check clears the lock the moment it dies.
		if (closeChaseLockTarget != null && Time.time < closeChaseLockUntil)
		{
			return closeChaseLockTarget;
		}

		if (fieldReady)
		{
			// Cheap O(1) pre-filter: the field value IS already a step count (1 per open tile,
			// barricadePathCost per barricaded one), so compare it to CloseRangeTiles directly —
			// dividing by CellSize here was the bug that made this trigger far too early.
			float civSteps = cfm.SampleField(transform.position, CrowdFieldType.Civilian);
			if (civSteps <= CloseRangeTiles)
			{
				Transform candidate = FindNearestCivilianDirect();
				if (candidate != null)
				{
					// Only commit to a direct chase if the real NavMesh path is short — straight-
					// line "nearby" can point through a wall, which is exactly what fought the
					// field direction and produced the corner stutter.
					float pathLen = GetPathDistance(candidate.position, out bool reachable);
					if (reachable && pathLen <= CloseChaseMaxPath)
					{
						closeChaseLockTarget = candidate;
						closeChaseLockUntil = Time.time + VictimLockTime;
						return candidate;
					}
				}
			}
		}
		else
		{
			// Grid not ready yet (first frame or two of a level) — simple direct fallback so
			// zombies don't stand idle while the grid finishes building.
			Transform direct = FindNearestCivilianDirect();
			if (direct != null) return direct;
		}

		// fix/zombie-corner-stuck (3): re-decide early on arrival (within ~1 cell of the current
		// destination) instead of always waiting out the full RetargetCooldown — otherwise a short
		// lookahead step finishes well before the cooldown and the zombie just stands still.
		bool arrivedAtDestination = agent != null && agent.enabled && agent.isOnNavMesh &&
			!agent.pathPending && agent.hasPath && agent.remainingDistance < CellSizeOrDefault;

		if (Time.time < nextDecisionTime && !arrivedAtDestination)
		{
			// Already cruising toward a previously chosen destination — nothing new to decide
			// this tick, but we ARE still moving, so Brain() must not ResetPath() (fix 1).
			isCruising = true;
			return null;
		}

		nextDecisionTime = Time.time + RetargetCooldown;

		bool isHunterNow = role == ZombieRole.Hunter || (cfm != null && cfm.ForceAllHunters);

		if (isHunterNow)
		{
			// fix/zombie-corner-stuck (1): mark cruising BEFORE attempting a new step. A failed
			// lookahead (e.g. truly isolated cell) must never fall through to Brain()'s
			// ResetPath() — that was the permanent corner stall.
			isCruising = true;

			if (fieldReady && cfm.TryGetLookaheadDescent(transform.position, CrowdFieldType.Civilian, LookaheadCells, out Vector3 step))
			{
				if (agent != null && agent.enabled && agent.isOnNavMesh) agent.SetDestination(step);
			}

			// Stepping toward a barricade-occupied tile: NavMesh carves a hole there, so the agent
			// naturally stalls at its edge — TryFindNearestBarricade (existing barricadeCheckRadius
			// logic, unchanged) catches that and hands Brain() a precise attack target.
			if (TryFindNearestBarricade(out Barricade blocker)) return blocker.transform;

			return null;
		}

		// Wanderer: patrol a random intersection; promote to Hunter if a civilian is visible down
		// the same straight street within aggroRadius (one-way — never reverts to Wanderer).
		isCruising = true;

		if (fieldReady)
		{
			Transform sighted = FindNearestCivilianDirect(AggroRadiusWorld);
			if (sighted != null && cfm.IsSameStreetVisible(transform.position, sighted.position))
			{
				role = ZombieRole.Hunter;
				return null;
			}
		}

		if (!hasWanderDestination || arrivedAtDestination)
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

	// Exact-nearest Human/Scientist by straight-line distance. Only called when the shared field
	// (or, as a fallback, the caller) already narrowed things down to "close" or "within aggro
	// range" — never scans the whole level every Brain tick the way the old shortlist did.
	private Transform FindNearestCivilianDirect(float maxDistance = float.MaxValue)
	{
		Human nearestHuman = TargetScanUtil.FindNearest(Human.AllHumans, transform.position, maxDistance, h => h.transform.position, out float distH);
		Scientist nearestScientist = TargetScanUtil.FindNearest(Scientist.AllScientists, transform.position, maxDistance, s => s.transform.position, out float distS);

		if (nearestHuman != null && nearestScientist != null)
			return distH <= distS ? nearestHuman.transform : nearestScientist.transform;

		if (nearestHuman != null) return nearestHuman.transform;
		if (nearestScientist != null) return nearestScientist.transform;
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