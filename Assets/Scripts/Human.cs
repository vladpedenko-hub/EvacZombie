using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;

// [ГДЕ ВИСИТ]: На префабе человека (Human).
[RequireComponent(typeof(NavMeshAgent))]
public class Human : MonoBehaviour
{
	public static List<Human> AllHumans = new List<Human>();

	[Header("Настройки баланса")]
	public float walkSpeed = 1.5f;
	public float runSpeed = 4.0f;
	public float panicRadius = 8f;

	[Header("Хаос")]
	public ChaosSettings chaosSettings;

	[Header("Эвакуация")]
	public float carStoppingDistance = 1.4f;
	public float heliStoppingDistance = 2.0f;

	private NavMeshAgent agent;

	[HideInInspector] public bool isRescuing = false;
	[HideInInspector] public Transform rescueTarget = null;

	private bool isPanicking = false;
	private float panicEndTime = 0f;
	private float nextPanicCheckTime = 0f;

	private bool hasRescueApproachPoint = false;
	private Vector3 rescueApproachPoint;
	private bool isBoarding = false;

	private void Awake() => agent = GetComponent<NavMeshAgent>();
	private void OnEnable() => AllHumans.Add(this);
	private void OnDisable() => AllHumans.Remove(this);

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

		// Если сейчас в панике — просто ждём окончания
		if (isPanicking)
		{
			agent.speed = runSpeed;

			if (Time.time >= panicEndTime)
			{
				isPanicking = false;
			}

			return;
		}

		// --- ПРИОРИТЕТ 1: ЭВАКУАЦИЯ ---
		if (isRescuing && rescueTarget != null)
		{
			agent.speed = runSpeed;

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

		// --- ПРИОРИТЕТ 2: ПОБЕГ ОТ ЗОМБИ ---
		Zombie nearest = null;
		float minD = panicRadius;

		foreach (var z in Zombie.AllZombies)
		{
			if (z == null) continue;

			float d = Vector3.Distance(transform.position, z.transform.position);
			if (d < minD)
			{
				minD = d;
				nearest = z;
			}
		}

		if (nearest != null)
		{
			agent.speed = runSpeed;

			// Редкая паника
			if (ShouldPanic(minD))
			{
				StartPanicMove();
				return;
			}

			Vector3 runDir = (transform.position - nearest.transform.position).normalized;
			Vector3 targetFlee = transform.position + runDir * 6f;

			if (NavMesh.SamplePosition(targetFlee, out NavMeshHit hit, 4f, NavMesh.AllAreas))
			{
				if (!agent.hasPath || Vector3.Distance(agent.destination, hit.position) > 1f)
				{
					agent.SetDestination(hit.position);
				}
			}
			else
			{
				SetRandomDest();
			}

			return;
		}

		// --- ПРИОРИТЕТ 3: МИРНАЯ ЖИЗНЬ ---
		agent.speed = walkSpeed;

		if (!agent.pathPending)
		{
			if (!agent.hasPath || agent.remainingDistance < 0.5f || agent.pathStatus != NavMeshPathStatus.PathComplete)
			{
				SetRandomDest();
			}
		}
	}

	private bool ShouldPanic(float nearestZombieDistance)
	{
		if (chaosSettings == null) return false;
		if (!chaosSettings.chaosEnabled) return false;
		if (Time.time < nextPanicCheckTime) return false;
		if (nearestZombieDistance > chaosSettings.humanPanicTriggerRadius) return false;

		nextPanicCheckTime = Time.time + chaosSettings.humanPanicCheckInterval;

		return Random.value < chaosSettings.humanPanicChance;
	}

	private void StartPanicMove()
	{
		isPanicking = true;

		float duration = 0.4f;
		float moveDistance = 3f;

		if (chaosSettings != null)
		{
			duration = chaosSettings.humanPanicDuration;
			moveDistance = chaosSettings.humanPanicMoveDistance;
		}

		panicEndTime = Time.time + duration;

		Vector2 random2D = Random.insideUnitCircle.normalized;
		Vector3 panicDir = new Vector3(random2D.x, 0f, random2D.y);
		Vector3 panicTarget = transform.position + panicDir * moveDistance;

		if (NavMesh.SamplePosition(panicTarget, out NavMeshHit hit, 4f, NavMesh.AllAreas))
		{
			agent.SetDestination(hit.position);
		}
		else
		{
			SetRandomDest();
		}
	}

	public void SetRescueTarget(Transform t)
	{
		isRescuing = true;
		rescueTarget = t;
		isPanicking = false;
		hasRescueApproachPoint = false;

		if (!agent.isActiveAndEnabled || !agent.isOnNavMesh)
			return;

		// Магия притяжения: цель = центр транспорта, но остановка на расстоянии
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
			t += Time.deltaTime / Mathf.Max(0.01f, duration);
			float eased = Mathf.SmoothStep(0f, 1f, t);

			transform.position = Vector3.Lerp(startPos, endPos, eased);
			transform.localScale = Vector3.Lerp(startScale, endScale, eased);

			yield return null;
		}

		transform.position = endPos;
		transform.localScale = endScale;
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