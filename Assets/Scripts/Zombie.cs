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

	// Civilian target stickiness (FindClosestVictim): straight-line "nearest" alone flips between
	// two humans on opposite sides of a building every Brain tick, each flip re-pathing toward the
	// new target's straight-line direction before the NavMeshAgent settles onto the real route --
	// visible as a jerk toward the wall. A sticky target is held for at least this long unless it
	// dies/is destroyed or a candidate's path is meaningfully shorter.
	private const float TargetStickyDuration = 1.5f;
	private const float TargetSwitchPathRatio = 0.7f; // candidate must be <=70% of the sticky path length (>=30% shorter) to switch early
	private const int TargetShortlistSize = 3;
	private Transform stickyTarget;
	private float stickyTargetPathLength;
	private float stickyTargetAcquiredTime = -999f;
	private readonly List<Transform> targetShortlist = new List<Transform>(TargetShortlistSize);
	private readonly List<float> targetShortlistSqrDist = new List<float>(TargetShortlistSize);

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

		StartBrainIfNeeded();
	}

	protected void StartBrainIfNeeded()
	{
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
			FindTargetWithBaitPriority(out target, out overrideDestination);

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
			else
			{
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

	protected virtual void FindTargetWithBaitPriority(out Transform target, out Vector3? overrideDestination)
	{
		target = null;
		overrideDestination = null;

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
		target = FindClosestVictim();
		overrideDestination = null;
	}

	protected virtual Transform FindClosestVictim()
	{
		// Straight-line "nearest" alone picks a target through a wall, then flips to whichever
		// side is momentarily closer -- each flip is a fresh SetDestination that jerks the agent
		// toward the new target's straight-line direction before it settles onto the real route.
		// Shortlist a few candidates by straight line (cheap), then only CalculatePath for those
		// few and pick the one with the shortest COMPLETE path.
		FindNearestEvacueeShortlist(TargetShortlistSize, targetShortlist);

		Transform bestByPath = null;
		float bestPathLen = float.MaxValue;
		foreach (Transform candidate in targetShortlist)
		{
			if (agent != null && agent.enabled && agent.isOnNavMesh &&
				agent.CalculatePath(candidate.position, cachedPath) &&
				cachedPath.status == NavMeshPathStatus.PathComplete)
			{
				float len = PathCornersLength(cachedPath);
				if (len < bestPathLen)
				{
					bestPathLen = len;
					bestByPath = candidate;
				}
			}
		}

		// None of the shortlisted candidates have a complete path right now (e.g. agent not yet
		// placed) -- fall back to the nearest by straight line rather than picking no target at all.
		Transform closest = bestByPath;
		float closestPathLen = bestPathLen;
		if (closest == null && targetShortlist.Count > 0)
		{
			closest = targetShortlist[0];
			closestPathLen = GetPathDistance(closest.position);
		}

		closest = ApplyTargetStickiness(closest, closestPathLen);

		// Target found — check if a barricade is blocking the path to it.
		// PathPartial = NavMesh only reached the barricade, cannot go further.
		if (closest != null)
		{
			bool pathBlocked = agent != null
				&& agent.enabled
				&& agent.isOnNavMesh
				&& agent.pathStatus == NavMeshPathStatus.PathPartial;

			if (pathBlocked && TryFindNearestBarricade(out Barricade blocker))
				return blocker.transform;

			return closest;
		}

		// No targets at all — use barricade as fallback target
		if (TryFindNearestBarricade(out Barricade fallback))
			return fallback.transform;

		return null;
	}

	// Up to maxCount nearest Human/Scientist transforms by straight-line distance, nearest first.
	// Humans are considered before scientists so an exact-distance tie keeps the same winner as
	// the single combined loop this replaces. A small shortlist so FindClosestVictim only needs to
	// CalculatePath for a handful of candidates instead of every evacuee on the level.
	private void FindNearestEvacueeShortlist(int maxCount, List<Transform> result)
	{
		result.Clear();
		targetShortlistSqrDist.Clear();

		void Consider(Transform t)
		{
			float sq = (t.position - transform.position).sqrMagnitude;
			int insertAt = result.Count;
			for (int i = 0; i < result.Count; i++)
			{
				if (sq < targetShortlistSqrDist[i]) { insertAt = i; break; }
			}
			if (insertAt >= maxCount) return;

			result.Insert(insertAt, t);
			targetShortlistSqrDist.Insert(insertAt, sq);
			if (result.Count > maxCount)
			{
				result.RemoveAt(maxCount);
				targetShortlistSqrDist.RemoveAt(maxCount);
			}
		}

		foreach (Human h in Human.AllHumans) if (h != null) Consider(h.transform);
		foreach (Scientist s in Scientist.AllScientists) if (s != null) Consider(s.transform);
	}

	// Keeps the current civilian target for at least TargetStickyDuration even if straight-line
	// "nearest" would momentarily prefer a different one (the oscillation that causes the jerk
	// toward walls) -- released early only if it dies/is destroyed (candidate goes through Unity's
	// null check) or a candidate's path is at least 30% shorter.
	private Transform ApplyTargetStickiness(Transform candidate, float candidatePathLength)
	{
		if (stickyTarget != null)
		{
			if (stickyTarget == candidate)
			{
				stickyTargetPathLength = candidatePathLength;
				return stickyTarget;
			}

			bool stickyExpired = Time.time - stickyTargetAcquiredTime >= TargetStickyDuration;
			bool candidateMuchShorter = candidate != null && candidatePathLength <= stickyTargetPathLength * TargetSwitchPathRatio;

			if (!stickyExpired && !candidateMuchShorter)
				return stickyTarget;
		}

		stickyTarget = candidate;
		stickyTargetPathLength = candidatePathLength;
		stickyTargetAcquiredTime = Time.time;
		return stickyTarget;
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
		if (human == null) return;

		Vector3 pos = human.transform.position;
		Quaternion rot = human.transform.rotation;
		Feedback.Play(FeedbackEvent.ZombieInfect, pos + Vector3.up * 0.5f);

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
		if (scientist == null) return;

		Vector3 pos = scientist.transform.position;
		Quaternion rot = scientist.transform.rotation;
		Feedback.Play(FeedbackEvent.ZombieInfect, pos + Vector3.up * 0.5f);

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