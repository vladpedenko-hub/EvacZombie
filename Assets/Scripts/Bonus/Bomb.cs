using UnityEngine;
using System.Collections.Generic;

public class Bomb : MonoBehaviour
{
	[Header("Card & Settings")]
	public CardData myCardData; // Assign the bomb's CardData here

	[Header("Effects")]
	public GameObject explosionPrefab;

	// feature/bomb-combo (step 3): false by default -- the current Bomb card no longer destroys
	// buildings. Kept as a plain field (not a ComboTuning knob) so a future "Mega Bomb" prefab
	// variant can simply flip this on its own instance; the SandboxDevPanel "Bomb: Old" toggle also
	// forces this on (via ComboTuning.legacyFeelEnabled) to reproduce the original behavior for
	// comparison in the sandbox.
	[Header("Mega Bomb (future, see Explode())")]
	public bool destroysBuildings = false;

	// Runtime stats (read from CardData)
	private float damageRadius;
	private int damage;
	private float fallSpeed = 30f;

	private Vector3 targetPos;
	private bool isFalling = false;
	private float startHeight = 40f;
	private GameObject warningCircle;
	private LineRenderer circleRenderer;

	// Reused buffer for Physics.OverlapSphereNonAlloc — Explode() calls it up to three times
	// (hit, stun, cluster) back-to-back, each fully consumed before the next, so one buffer
	// is safe to reuse across all three.
	private Collider[] overlapBuffer = new Collider[8];

	private static ComboTuning Tuning => ComboTuning.Active;

	private void Start()
	{
		int currentLevel = 1;

		if (PlayerProfile.Instance != null && myCardData != null)
		{
			var progress = PlayerProfile.Instance.ownedCardsProgress.Find(p => p.cardId == myCardData.name);
			if (progress != null) currentLevel = progress.currentLevel;

			damageRadius = myCardData.GetCalculatedStat(StatType.Radius, currentLevel);
			damage = (int)myCardData.GetCalculatedStat(StatType.Damage, currentLevel);
		}
		else
		{
			Debug.LogWarning("Bomb has no CardData assigned! Using fallback values.");
			damageRadius = 6f;
			damage = 1000;
		}

		// Run-modifiers (roguelite)
		var runStart = RunSessionData.Instance;
		if (runStart != null)
		{
			damageRadius *= (1f + runStart.GetModifier("bomb_radius_mult"));
			damage = Mathf.RoundToInt(damage * (1f + runStart.GetModifier("bomb_damage_mult")));

			if (runStart.HasFlag("bomb_mega_radius"))
				damageRadius = 150f;
		}
	}

	public void Launch(Vector3 pos)
	{
		if (damageRadius == 0) Start();

		targetPos = pos;
		transform.position = new Vector3(pos.x, startHeight, pos.z);
		isFalling = true;
		CreateWarningCircle(pos);
	}

	private void CreateWarningCircle(Vector3 pos)
	{
		warningCircle = new GameObject("BombWarning");
		warningCircle.transform.position = new Vector3(pos.x, 0.1f, pos.z);

		circleRenderer = warningCircle.AddComponent<LineRenderer>();
		circleRenderer.startWidth = 0.2f;
		circleRenderer.endWidth = 0.2f;
		circleRenderer.material = SharedMaterials.SpritesDefaultMaterial;
		circleRenderer.startColor = Color.red;
		circleRenderer.endColor = Color.red;
		circleRenderer.loop = true;
		circleRenderer.positionCount = 32;
	}

	private void Update()
	{
		if (!isFalling) return;

		transform.position = Vector3.MoveTowards(transform.position, targetPos, fallSpeed * Time.deltaTime);
		float progress = 1f - (transform.position.y / startHeight);
		DrawCircle(damageRadius * progress);

		if (Vector3.Distance(transform.position, targetPos) < 0.2f)
		{
			Explode();
		}
	}

	private void DrawCircle(float radius)
	{
		if (circleRenderer == null) return;

		float angle = 0f;
		for (int i = 0; i < 32; i++)
		{
			float x = Mathf.Sin(Mathf.Deg2Rad * angle) * radius;
			float z = Mathf.Cos(Mathf.Deg2Rad * angle) * radius;
			circleRenderer.SetPosition(i, warningCircle.transform.position + new Vector3(x, 0, z));
			angle += (360f / 32f);
		}
	}

	private void Explode()
	{
		ComboTuning tuning = Tuning;
		bool legacy = tuning != null && tuning.legacyFeelEnabled;
		bool useStreetBlast = !legacy && (tuning == null || tuning.useStreetBlast);
		bool sparesOwnUnits = !legacy && (tuning == null || tuning.sparesOwnUnits);
		bool shouldDestroyBuildings = destroysBuildings || legacy;
		bool comboEnabled = !legacy;

		// feature/bomb-combo (step 3): "street reach" -- the blast only affects targets with an
		// unobstructed NavMesh path from the epicenter (not through a building wall). Sampled once
		// here, not per-candidate.
		Vector3 sampledEpicenter = targetPos;
		bool epicenterSampled = useStreetBlast && BombBlastUtil.TrySampleEpicenter(targetPos, out sampledEpicenter);

		int hitCount = PhysicsNonAlloc.OverlapSphere(transform.position, damageRadius, ref overlapBuffer);
		HashSet<GameObject> processedObjects = new HashSet<GameObject>();

		int gatheredKilled = 0;

		for (int i = 0; i < hitCount; i++)
		{
			Collider hit = overlapBuffer[i];
			if (hit == null) continue;

			GameObject target = hit.gameObject;

			if (processedObjects.Contains(target))
				continue;

			processedObjects.Add(target);

			if (useStreetBlast && !BombBlastUtil.HasStreetReach(sampledEpicenter, epicenterSampled, target.transform.position))
				continue; // blocked by a building wall -- the blast doesn't reach around corners

			Zombie zombie = target.GetComponent<Zombie>();
			if (zombie != null)
			{
				// feature/bomb-combo (step 5): cache gathered status + alive state BEFORE damage,
				// count it only if this exact hit killed it.
				bool wasGathered = ZombieStatus.IsGathered(zombie);
				bool wasAlive = !zombie.IsDead;

				zombie.TakeDamage(damage);

				if (wasGathered && wasAlive && zombie.IsDead)
				{
					gatheredKilled++;
					// Stronger death burst for a bomb kill, on top of the zombie's own normal
					// ZombieDeath feedback (already fired inside TakeDamage's death path).
					VfxManager.Play(VfxId.DeathPuff, target.transform.position, 1.8f);
				}
				continue;
			}

			Human human = target.GetComponent<Human>();
			if (human != null)
			{
				Feedback.Play(FeedbackEvent.CivilianLost, human.transform.position + Vector3.up * 0.5f);
				Destroy(human.gameObject);
				continue;
			}

			// feature/bomb-combo (step 3): Scientists were never handled before -- same removal path
			// as Human (Destroy + the Scientist's own OnDisable unregisters it from AllScientists,
			// exactly mirroring Human/AllHumans).
			Scientist scientist = target.GetComponent<Scientist>();
			if (scientist != null)
			{
				Feedback.Play(FeedbackEvent.CivilianLost, scientist.transform.position + Vector3.up * 0.5f);
				Destroy(scientist.gameObject);
				continue;
			}

			if (shouldDestroyBuildings && target.CompareTag("Building"))
			{
				Destroy(target);
				continue;
			}

			if (!sparesOwnUnits && (target.CompareTag("Soldier") || target.CompareTag("Sniper")))
			{
				Feedback.Play(FeedbackEvent.CivilianLost, target.transform.position + Vector3.up * 0.5f);
				Destroy(target);
				continue;
			}
		}

		Feedback.Play(FeedbackEvent.BombExplosion, targetPos);

		if (explosionPrefab != null)
		{
			GameObject boom = Instantiate(explosionPrefab, targetPos, Quaternion.identity);
			Destroy(boom, 3f);
		}
		else
		{
			CreateProceduralExplosion(targetPos);
		}

		// Roguelite effects
		var run = RunSessionData.Instance;
		if (run != null)
		{
			// Stun survivors
			if (run.HasFlag("bomb_stun"))
			{
				int stunHitCount = PhysicsNonAlloc.OverlapSphere(transform.position, damageRadius, ref overlapBuffer);
				for (int i = 0; i < stunHitCount; i++)
				{
					Zombie z = overlapBuffer[i]?.GetComponent<Zombie>();
					if (z != null && !z.IsDead)
						z.Stun(5f);
				}
			}

			// Cluster bombs
			int clusterCount = (int)run.GetModifier("bomb_cluster_count");
			for (int i = 0; i < clusterCount; i++)
			{
				Vector2 rndOffset = UnityEngine.Random.insideUnitCircle * damageRadius * 0.7f;
				Vector3 miniPos = targetPos + new Vector3(rndOffset.x, 0, rndOffset.y);
				int miniHitCount = PhysicsNonAlloc.OverlapSphere(miniPos, damageRadius * 0.3f, ref overlapBuffer);
				for (int j = 0; j < miniHitCount; j++)
					overlapBuffer[j]?.GetComponent<Zombie>()?.TakeDamage(150);
			}
		}

		// feature/bomb-combo (step 5): "Bait then Bomb" combo moment.
		if (comboEnabled && tuning != null && gatheredKilled >= tuning.gatheredKillThreshold && tuning.gatheredKillThreshold > 0)
		{
			int refund = tuning.supplyRefundOnCombo ? Mathf.Min(tuning.supplyRefundMax, tuning.supplyRefundAmount) : 0;
			ComboFeedback.Trigger("TRAP", gatheredKilled, targetPos, refund);
		}

		Destroy(warningCircle);
		Destroy(gameObject);
	}

	private void CreateProceduralExplosion(Vector3 pos)
	{
		GameObject fx = new GameObject("ProceduralExplosion");
		fx.transform.position = pos;
		ParticleSystem ps = fx.AddComponent<ParticleSystem>();

		var main = ps.main;
		main.duration = 1f;
		main.startLifetime = 0.5f;
		main.startSpeed = 15f;
		main.startSize = 1.5f;
		main.startColor = new Color(1f, 0.3f, 0f);

		var emission = ps.emission;
		emission.rateOverTime = 0;
		emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 100) });

		var shape = ps.shape;
		shape.shapeType = ParticleSystemShapeType.Sphere;
		shape.radius = damageRadius / 2f;

		Destroy(fx, 2f);
	}
}
