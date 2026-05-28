using UnityEngine;
using System.Collections.Generic;

public class Bomb : MonoBehaviour
{
	[Header("Связь с карточкой")]
	public CardData myCardData; // Перетащи сюда CardData Бомбы

	[Header("Визуал")]
	public GameObject explosionPrefab;

	// Скрытые статы (берутся из CardData)
	private float damageRadius;
	private int damage;
	private float fallSpeed = 30f;

	private Vector3 targetPos;
	private bool isFalling = false;
	private float startHeight = 40f;
	private GameObject warningCircle;
	private LineRenderer circleRenderer;

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
			Debug.LogWarning("У Бомбы не назначен CardData! Используем базовые значения.");
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
		circleRenderer.material = new Material(Shader.Find("Sprites/Default"));
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
		Collider[] hits = Physics.OverlapSphere(transform.position, damageRadius);
		HashSet<GameObject> processedObjects = new HashSet<GameObject>();

		foreach (var hit in hits)
		{
			if (hit == null) continue;

			GameObject target = hit.gameObject;

			if (processedObjects.Contains(target))
				continue;

			processedObjects.Add(target);

			Zombie zombie = target.GetComponent<Zombie>();
			if (zombie != null)
			{
				zombie.TakeDamage(damage);
				continue;
			}

			Human human = target.GetComponent<Human>();
			if (human != null)
			{
				Destroy(human.gameObject);
				continue;
			}

			if (target.CompareTag("Building"))
			{
				Destroy(target);
				continue;
			}

			if (target.CompareTag("Soldier") || target.CompareTag("Sniper"))
			{
				Destroy(target);
				continue;
			}
		}

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
				Collider[] stunHits = Physics.OverlapSphere(transform.position, damageRadius);
				foreach (var h in stunHits)
				{
					Zombie z = h?.GetComponent<Zombie>();
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
				Collider[] miniHits = Physics.OverlapSphere(miniPos, damageRadius * 0.3f);
				foreach (var mh in miniHits)
					mh?.GetComponent<Zombie>()?.TakeDamage(150);
			}
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