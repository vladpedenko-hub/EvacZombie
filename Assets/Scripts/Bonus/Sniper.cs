using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// [File name]: Sniper (stationary unit placed on a building).
// [Setup]: assign myCardData in the inspector.
public class Sniper : MonoBehaviour
{
	[Header("Card & Settings")]
	public CardData myCardData;

	[Header("Runtime Parameters")]
	public float aimDuration = 1.5f;       // Time spent aiming before firing
	public float firstShotAimFactor = 0.4f; // Aim time reduction for the first shot (0.4 = 60% faster)
	public float muzzleHeight = 1.5f;
	public float targetHeight = 1.0f;

	[Header("Piercing")]
	public int maxPierceTargets = 3;      // Maximum targets the bullet can pass through
	public float pierceDamageFalloff = 0.5f; // Damage multiplier per pierced target (1, 0.5, 0.25, etc.)

	[Header("Civilian Safety")]
	public float civilianThreatRadius = 6f; // If a zombie is this close to a civilian, it becomes a priority target
	public float progressBias = 0.05f;      // Slight preference for zombies further along the Z axis

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

		// Run-modifiers (roguelite)
		var run = RunSessionData.Instance;
		if (run != null)
		{
			attackRange *= (1f + run.GetModifier("sniper_range_mult"));
			damage = Mathf.RoundToInt(damage * (1f + run.GetModifier("sniper_damage_mult")));
			cooldownDelay *= Mathf.Max(0.1f, 1f - run.GetModifier("sniper_cooldown_red"));
			lifespan += run.GetModifier("sniper_duration_add");
			maxPierceTargets += (int)run.GetModifier("sniper_pierce_add");

			if (run.HasFlag("sniper_global_range"))
				attackRange = 9999f;

			if (run.HasFlag("sniper_permanent"))
				lifespan = 99999f;
		}

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

					// Triple target upgrade
					if (RunSessionData.Instance != null && RunSessionData.Instance.HasFlag("sniper_triple_target"))
					{
						var extras = FindTopNTargets(3);
						foreach (var extra in extras)
						{
							if (extra != target)
								ApplyPiercingDamage(extra);
						}
					}

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

	// Find a target that poses a threat to nearby civilians/scientists.
	// If no threat target exists, fallback = closest zombie by Z progress within LOS.
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

			// Fallback candidate: best zombie by Z progress
			float progressScore = z.transform.position.z;
			if (progressScore > bestFallbackProgress)
			{
				bestFallbackProgress = progressScore;
				bestFallbackTarget = z;
			}

			// Priority check: zombie near a civilian
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
			delta.y = 0f; // ignore vertical difference

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
				int actualDamage = (RunSessionData.Instance != null && RunSessionData.Instance.HasFlag("sniper_instakill"))
					? 99999
					: Mathf.RoundToInt(currentDamage);
				z.TakeDamage(actualDamage);
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
	private System.Collections.Generic.List<Zombie> FindTopNTargets(int n)
	{
		var result = new System.Collections.Generic.List<Zombie>();
		foreach (var z in Zombie.AllZombies)
		{
			if (z == null) continue;
			float d = Vector3.Distance(transform.position, z.transform.position);
			if (d <= attackRange)
				result.Add(z);
		}
		result.Sort((a, b) => Vector3.Distance(transform.position, a.transform.position)
			.CompareTo(Vector3.Distance(transform.position, b.transform.position)));
		if (result.Count > n) result.RemoveRange(n, result.Count - n);
		return result;
	}

}