using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// [ГДЕ ВИСИТ]: На префабе Снайпера (спавнится только на зданиях).
// [НАСТРОЙКИ]: В инспекторе нужно назначить myCardData.
public class Sniper : MonoBehaviour
{
	[Header("Связь с карточкой")]
	public CardData myCardData;

	[Header("Технические настройки")]
	public float aimDuration = 1.5f;       // базовое время прицеливания
	public float firstShotAimFactor = 0.4f; // во сколько раз первый выстрел быстрее (0.4 = 60% быстрее)
	public float muzzleHeight = 1.5f;
	public float targetHeight = 1.0f;

	[Header("Пробивание по линии")]
	public int maxPierceTargets = 3;      // максимум зомбей, которых можно задеть одним выстрелом
	public float pierceDamageFalloff = 0.5f; // коэффициент урона для каждого следующего (1, 0.5, 0.25 и т.д.)

	[Header("Приоритет спасения")]
	public float civilianThreatRadius = 6f; // если зомби дальше этого расстояния от мирных, он не считается срочной угрозой
	public float progressBias = 0.05f;      // лёгкий бонус тем, кто дальше продвинулся по Z

	private float cooldownDelay;
	private float attackRange;
	private int damage;
	private float lifespan;

	private bool isExtracting = false;
	private bool isFirstShot = true;
	private LineRenderer laserLine;
	private Transform myBuilding;

	private readonly List<Transform> civilianBuffer = new List<Transform>();

	public void Init(Transform buildingTransform)
	{
		myBuilding = buildingTransform;
	}

	private void Awake()
	{
		laserLine = gameObject.AddComponent<LineRenderer>();
		laserLine.material = new Material(Shader.Find("Sprites/Default"));
		laserLine.enabled = false;
		laserLine.positionCount = 2;
	}

	private void Start()
	{
		int currentLevel = 1;
		if (PlayerProfile.Instance != null && myCardData != null)
		{
			var progress = PlayerProfile.Instance.ownedCardsProgress.Find(p => p.cardId == myCardData.name);
			if (progress != null) currentLevel = progress.currentLevel;

			cooldownDelay = myCardData.GetCalculatedStat(StatType.Cooldown, currentLevel);
			attackRange = myCardData.GetCalculatedStat(StatType.Radius, currentLevel);
			damage = (int)myCardData.GetCalculatedStat(StatType.Damage, currentLevel);
			lifespan = myCardData.GetCalculatedStat(StatType.Duration, currentLevel);
		}
		else
		{
			cooldownDelay = 2.0f;
			attackRange = 25f;
			damage = 100;
			lifespan = 15f;
		}

		if (cooldownDelay <= 0f) cooldownDelay = 2f;
		if (attackRange <= 0f) attackRange = 25f;
		if (damage <= 0) damage = 100;
		if (lifespan <= 0f) lifespan = 15f;
		if (civilianThreatRadius <= 0f) civilianThreatRadius = 6f;

		StartCoroutine(SniperRoutine());
	}

	private void Update()
	{
		if (isExtracting) return;

		lifespan -= Time.deltaTime;
		if (lifespan <= 0f)
			StartCoroutine(ExtractRoutine());
	}

	private IEnumerator ExtractRoutine()
	{
		isExtracting = true;
		laserLine.enabled = false;

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

	private IEnumerator SniperRoutine()
	{
		while (!isExtracting)
		{
			Zombie target = FindTarget();

			if (target != null)
			{
				laserLine.enabled = true;
				float aimTimer = 0f;

				float currentAimDuration = isFirstShot
					? Mathf.Max(aimDuration * firstShotAimFactor, 0.1f)
					: aimDuration;

				bool targetLost = false;

				while (aimTimer < currentAimDuration)
				{
					if (target == null || !HasLineOfSight(target.transform))
					{
						targetLost = true;
						break;
					}

					float progress = aimTimer / currentAimDuration;

					float currentWidth = Mathf.Lerp(0.02f, 0.06f, progress);
					laserLine.startWidth = currentWidth;
					laserLine.endWidth = currentWidth;

					Color currentColor = new Color(1f, 0f, 0f, Mathf.Lerp(0.2f, 1f, progress));
					laserLine.startColor = currentColor;
					laserLine.endColor = currentColor;

					laserLine.SetPosition(0, transform.position + Vector3.up * muzzleHeight);
					laserLine.SetPosition(1, target.transform.position + Vector3.up * targetHeight);

					aimTimer += Time.deltaTime;
					yield return null;
				}

				if (!targetLost && target != null)
				{
					laserLine.startWidth = 0.15f;
					laserLine.endWidth = 0.05f;
					laserLine.startColor = Color.yellow;
					laserLine.endColor = new Color(1f, 0.5f, 0f);

					ApplyPiercingDamage(target);

					isFirstShot = false;

					yield return new WaitForSeconds(0.15f);

					laserLine.enabled = false;
					yield return new WaitForSeconds(cooldownDelay);
				}
				else
				{
					laserLine.enabled = false;
					yield return new WaitForSeconds(0.2f);
				}
			}
			else
			{
				laserLine.enabled = false;
				yield return new WaitForSeconds(0.35f);
			}
		}
	}

	// Находим зомби, который ближе всего к любому человеку/учёному.
	// Если рядом с мирными никого нет, fallback = самый дальний по Z в радиусе и с LOS.
	private Zombie FindTarget()
	{
		CollectCivilians();

		Zombie bestThreatTarget = null;
		float bestThreatScore = float.PositiveInfinity;

		Zombie bestFallbackTarget = null;
		float bestFallbackProgress = float.NegativeInfinity;

		float threatRadiusSq = civilianThreatRadius * civilianThreatRadius;

		foreach (var z in Zombie.AllZombies)
		{
			if (z == null) continue;

			float d = Vector3.Distance(transform.position, z.transform.position);
			if (d > attackRange) continue;
			if (!HasLineOfSight(z.transform)) continue;

			// fallback-цель: как раньше, самый дальний по Z
			float progressScore = z.transform.position.z;
			if (progressScore > bestFallbackProgress)
			{
				bestFallbackProgress = progressScore;
				bestFallbackTarget = z;
			}

			// основной приоритет: зомби рядом с мирными
			float nearestCivilianDistSq = GetNearestCivilianDistanceSq(z.transform.position);
			if (nearestCivilianDistSq > threatRadiusSq) continue;

			float score = nearestCivilianDistSq - (z.transform.position.z * progressBias);

			if (score < bestThreatScore)
			{
				bestThreatScore = score;
				bestThreatTarget = z;
			}
		}

		if (bestThreatTarget != null)
			return bestThreatTarget;

		return bestFallbackTarget;
	}

	private void CollectCivilians()
	{
		civilianBuffer.Clear();

		foreach (var h in Human.AllHumans)
		{
			if (h == null) continue;
			if (!h.isActiveAndEnabled) continue;
			civilianBuffer.Add(h.transform);
		}

		foreach (var s in Scientist.AllScientists)
		{
			if (s == null) continue;
			if (!s.isActiveAndEnabled) continue;
			civilianBuffer.Add(s.transform);
		}
	}

	private float GetNearestCivilianDistanceSq(Vector3 zombiePos)
	{
		if (civilianBuffer.Count == 0)
			return float.PositiveInfinity;

		float bestDistSq = float.PositiveInfinity;

		for (int i = 0; i < civilianBuffer.Count; i++)
		{
			Transform civ = civilianBuffer[i];
			if (civ == null) continue;

			Vector3 delta = civ.position - zombiePos;
			delta.y = 0f; // считаем только по земле

			float distSq = delta.sqrMagnitude;
			if (distSq < bestDistSq)
				bestDistSq = distSq;
		}

		return bestDistSq;
	}

	private bool HasLineOfSight(Transform target)
	{
		Vector3 start = transform.position + Vector3.up * muzzleHeight;
		Vector3 end = target.position + Vector3.up * targetHeight;
		Vector3 dir = end - start;
		float dist = dir.magnitude;

		if (dist <= 0.01f) return true;

		if (Physics.Raycast(start, dir.normalized, out RaycastHit hit, dist))
		{
			Zombie hitZombie = hit.collider.GetComponent<Zombie>();
			if (hitZombie != null && hitZombie.transform == target)
				return true;

			if (hit.collider.CompareTag("Building"))
			{
				if (myBuilding != null && hit.collider.transform == myBuilding)
					return true;

				return false;
			}

			if (hit.collider.transform != target)
				return false;
		}

		return true;
	}

	private void ApplyPiercingDamage(Zombie primaryTarget)
	{
		Vector3 start = transform.position + Vector3.up * muzzleHeight;
		Vector3 end = primaryTarget.transform.position + Vector3.up * targetHeight;
		Vector3 dir = (end - start).normalized;
		float dist = Vector3.Distance(start, end);

		RaycastHit[] hits = Physics.RaycastAll(start, dir, dist);
		System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

		int targetsHit = 0;
		float currentDamage = damage;

		foreach (var hit in hits)
		{
			if (targetsHit >= maxPierceTargets) break;

			if (hit.collider.CompareTag("Building") && (myBuilding == null || hit.collider.transform != myBuilding))
			{
				break;
			}

			Zombie z = hit.collider.GetComponent<Zombie>();
			if (z != null)
			{
				z.TakeDamage(Mathf.RoundToInt(currentDamage));
				targetsHit++;

				currentDamage *= pierceDamageFalloff;
				if (currentDamage < 1f) break;
			}
		}

		if (targetsHit == 0 && primaryTarget != null)
		{
			primaryTarget.TakeDamage(damage);
		}
	}
}