using UnityEngine;
using System.Collections;

// [ГДЕ ВИСИТ]: На префабе Солдата.
// [НАСТРОЙКИ]: Назначить только myCardData.
// Для примитивов оружие не нужно: выстрел идёт из верхней передней части куба.
[RequireComponent(typeof(LineRenderer))]
public class Soldier : MonoBehaviour
{
	[Header("Связь с карточкой")]
	public CardData myCardData;

	[Header("Визуал и стрельба")]
	public LayerMask shootMask = ~0;
	public float aimTurnSpeed = 10f;
	public float tracerLifeTime = 0.08f;
	public float shootOriginHeight = 0.9f;
	public float shootOriginForwardOffset = 0.45f;

	[Header("Точность")]
	public float targetHeightOffset = 0.7f;
	public float aimForgivenessRadius = 0.3f;
	public float retargetThreshold = 0.35f;

	private float fireRate;
	private float attackRange;
	private int damage;
	private float lifespan;

	private float currentFireRate;
	private bool isExtracting = false;

	private LineRenderer tracerLine;
	private Coroutine shootRoutine;
	private Coroutine tracerRoutine;

	private void Awake()
	{
		tracerLine = GetComponent<LineRenderer>();
		if (tracerLine == null)
		{
			tracerLine = gameObject.AddComponent<LineRenderer>();
		}

		tracerLine.positionCount = 2;
		tracerLine.startWidth = 0.14f;
		tracerLine.endWidth = 0.05f;
		tracerLine.material = new Material(Shader.Find("Sprites/Default"));
		tracerLine.startColor = new Color(1f, 0.95f, 0.2f, 1f);
		tracerLine.endColor = new Color(1f, 0.45f, 0.05f, 0.9f);
		tracerLine.enabled = false;
	}

	private void Start()
	{
		int currentLevel = 1;
		if (PlayerProfile.Instance != null && myCardData != null)
		{
			var progress = PlayerProfile.Instance.ownedCardsProgress.Find(p => p.cardId == myCardData.name);
			if (progress != null) currentLevel = progress.currentLevel;

			fireRate = myCardData.GetCalculatedStat(StatType.FireRate, currentLevel);
			attackRange = myCardData.GetCalculatedStat(StatType.Radius, currentLevel);
			damage = (int)myCardData.GetCalculatedStat(StatType.Damage, currentLevel);
			lifespan = myCardData.GetCalculatedStat(StatType.Duration, currentLevel);
		}
		else
		{
			fireRate = 1.5f;
			attackRange = 12f;
			damage = 20;
			lifespan = 8f;
		}

		currentFireRate = Mathf.Max(0.05f, fireRate);
		shootRoutine = StartCoroutine(ShootRoutine());
	}

	private void Update()
	{
		if (isExtracting) return;

		lifespan -= Time.deltaTime;
		if (lifespan <= 0f)
		{
			StartCoroutine(ExtractRoutine());
			return;
		}

		Zombie target = FindTarget();
		if (target != null)
		{
			Vector3 lookDir = target.transform.position - transform.position;
			lookDir.y = 0f;

			if (lookDir.sqrMagnitude > 0.0001f)
			{
				Quaternion targetRot = Quaternion.LookRotation(lookDir);
				transform.rotation = Quaternion.Lerp(transform.rotation, targetRot, Time.deltaTime * aimTurnSpeed);
			}
		}
	}

	private IEnumerator ExtractRoutine()
	{
		if (isExtracting) yield break;

		isExtracting = true;
		tracerLine.enabled = false;

		if (shootRoutine != null)
		{
			StopCoroutine(shootRoutine);
			shootRoutine = null;
		}

		var agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
		if (agent != null) agent.enabled = false;

		var col = GetComponent<Collider>();
		if (col != null) col.enabled = false;

		float t = 0f;
		Vector3 startScale = transform.localScale;

		while (t < 1f)
		{
			t += Time.deltaTime * 3f;
			transform.localScale = Vector3.Lerp(startScale, Vector3.zero, t);
			yield return null;
		}

		Destroy(gameObject);
	}

	private IEnumerator ShootRoutine()
	{
		WaitForSeconds idleWait = new WaitForSeconds(0.03f);

		while (!isExtracting)
		{
			Zombie target = FindTarget();

			if (target != null)
			{
				DoShot(target);
				yield return new WaitForSeconds(currentFireRate);
			}
			else
			{
				yield return idleWait;
			}
		}
	}

	private void DoShot(Zombie target)
	{
		if (target == null || target.IsDead) return;

		Vector3 shootOrigin = GetShootOrigin();
		Vector3 targetPoint = GetAimPoint(target);

		if (!TryResolveShot(target, shootOrigin, targetPoint, out Zombie hitZombie, out Vector3 hitPoint))
		{
			ShowTracer(shootOrigin, targetPoint);
			return;
		}

		if (hitZombie != null && !hitZombie.IsDead)
		{
			hitZombie.TakeDamage(damage);
		}

		ShowTracer(shootOrigin, hitPoint);
	}

	private bool TryResolveShot(Zombie intendedTarget, Vector3 origin, Vector3 targetPoint, out Zombie hitZombie, out Vector3 hitPoint)
	{
		hitZombie = null;
		hitPoint = targetPoint;

		Vector3 dir = targetPoint - origin;
		float dist = dir.magnitude;
		if (dist <= 0.01f) return false;

		dir /= dist;

		if (Physics.Raycast(origin, dir, out RaycastHit rayHit, attackRange, shootMask))
		{
			hitPoint = rayHit.point;
			hitZombie = rayHit.collider.GetComponentInParent<Zombie>();

			if (hitZombie != null)
			{
				return true;
			}

			return true;
		}

		if (Physics.SphereCast(origin, aimForgivenessRadius, dir, out RaycastHit sphereHit, attackRange, shootMask))
		{
			Zombie sphereZombie = sphereHit.collider.GetComponentInParent<Zombie>();
			if (sphereZombie != null)
			{
				hitZombie = sphereZombie;
				hitPoint = sphereHit.point;
				return true;
			}
		}

		if (intendedTarget != null && !intendedTarget.IsDead)
		{
			Vector3 intendedPoint = intendedTarget.transform.position + Vector3.up * targetHeightOffset;
			Vector3 centerToLine = ClosestPointOnRay(origin, dir, intendedPoint);
			float off = Vector3.Distance(centerToLine, intendedPoint);
			float directDist = Vector3.Distance(origin, intendedPoint);

			if (directDist <= attackRange + retargetThreshold && off <= aimForgivenessRadius + retargetThreshold)
			{
				hitZombie = intendedTarget;
				hitPoint = intendedPoint;
				return true;
			}
		}

		return false;
	}

	private Vector3 ClosestPointOnRay(Vector3 rayOrigin, Vector3 rayDirNormalized, Vector3 point)
	{
		float t = Vector3.Dot(point - rayOrigin, rayDirNormalized);
		t = Mathf.Max(0f, t);
		return rayOrigin + rayDirNormalized * t;
	}

	private Vector3 GetShootOrigin()
	{
		return transform.position
			+ Vector3.up * shootOriginHeight
			+ transform.forward * shootOriginForwardOffset;
	}

	private Vector3 GetAimPoint(Zombie target)
	{
		Collider col = target.GetComponent<Collider>();

		if (col != null)
		{
			Bounds b = col.bounds;
			return new Vector3(
				b.center.x,
				b.min.y + b.size.y * 0.7f,
				b.center.z
			);
		}

		return target.transform.position + Vector3.up * targetHeightOffset;
	}

	private void ShowTracer(Vector3 start, Vector3 end)
	{
		tracerLine.SetPosition(0, start);
		tracerLine.SetPosition(1, end);
		tracerLine.enabled = true;

		if (tracerRoutine != null)
		{
			StopCoroutine(tracerRoutine);
		}
		tracerRoutine = StartCoroutine(HideTracer());
	}

	private IEnumerator HideTracer()
	{
		float t = 0f;
		float baseStartWidth = 0.14f;
		float baseEndWidth = 0.05f;

		while (t < tracerLifeTime)
		{
			t += Time.deltaTime;
			float k = 1f - (t / tracerLifeTime);

			tracerLine.startWidth = Mathf.Lerp(0.02f, baseStartWidth, k);
			tracerLine.endWidth = Mathf.Lerp(0.01f, baseEndWidth, k);

			yield return null;
		}

		tracerLine.enabled = false;
		tracerLine.startWidth = baseStartWidth;
		tracerLine.endWidth = baseEndWidth;
	}

	private Zombie FindTarget()
	{
		Zombie best = null;
		float minD = attackRange;

		foreach (var z in Zombie.AllZombies)
		{
			if (z == null || z.IsDead) continue;

			float d = Vector3.Distance(transform.position, z.transform.position);
			if (d < minD)
			{
				minD = d;
				best = z;
			}
		}

		return best;
	}
}