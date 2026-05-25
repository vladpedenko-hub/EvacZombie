using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class CombatHelicopter : MonoBehaviour
{
	public enum State { FlyingIn, Loading, FlyingOut }
	public State currentState;

	[Header("Связь с карточкой")]
	public CardData myCardData; // Перетащи сюда CardData Боевого Вертолета

	[Header("Технические настройки")]
	public float pickupRadius = 6f; // Радиус подбора людей (не меняется с уровнем)

	// СТАТЫ ИЗ CARD DATA
	private float flySpeed;
	private float loadTime;
	private int maxCapacity;
	private float shootRadius;
	private float fireRate;
	private int sniperDamage;

	private Vector3 targetPos;
	private int currentLoad = 0;
	private float shootTimer = 0f;

	private void Start()
	{
		int currentLevel = 1;
		if (PlayerProfile.Instance != null && myCardData != null)
		{
			var progress = PlayerProfile.Instance.ownedCardsProgress.Find(p => p.cardId == myCardData.name);
			if (progress != null) currentLevel = progress.currentLevel;

			maxCapacity = (int)myCardData.GetCalculatedStat(StatType.Capacity, currentLevel);
			flySpeed = myCardData.GetCalculatedStat(StatType.Speed, currentLevel);
			loadTime = myCardData.GetCalculatedStat(StatType.Duration, currentLevel);
			shootRadius = myCardData.GetCalculatedStat(StatType.Radius, currentLevel);
			fireRate = myCardData.GetCalculatedStat(StatType.FireRate, currentLevel);
			sniperDamage = (int)myCardData.GetCalculatedStat(StatType.Damage, currentLevel);
		}
		else
		{
			Debug.LogWarning("У Combat Helicopter нет CardData! Берем базу.");
			maxCapacity = 10; flySpeed = 25f; loadTime = 5f;
			shootRadius = 20f; fireRate = 0.4f; sniperDamage = 15;
		}

		// Предохранители
		if (maxCapacity <= 0) maxCapacity = 10;
		if (flySpeed <= 0) flySpeed = 25f;
		if (loadTime <= 0) loadTime = 5f;
		if (shootRadius <= 0) shootRadius = 20f;
		if (fireRate <= 0) fireRate = 0.4f;
	}

	public void Launch(Vector3 target)
	{
		// Если Launch вызвался до Start - инициализируем вручную
		if (maxCapacity == 0) Start();

		targetPos = target;
		transform.position = targetPos + new Vector3(0, 40f, -30f);
		currentState = State.FlyingIn;
		StartCoroutine(HelicopterRoutine());
	}

	private IEnumerator HelicopterRoutine()
	{
		while (Vector3.Distance(transform.position, targetPos) > 0.5f)
		{
			transform.position = Vector3.MoveTowards(transform.position, targetPos, flySpeed * Time.deltaTime);
			SniperShoot();
			yield return null;
		}

		currentState = State.Loading;
		float timer = 0;

		while (timer < loadTime && currentLoad < maxCapacity)
		{
			Collider[] hits = Physics.OverlapSphere(transform.position, pickupRadius);
			foreach (var hit in hits)
			{
				if (hit.CompareTag("Human"))
				{
					Destroy(hit.gameObject);
					currentLoad++;
				}
			}

			SniperShoot();

			timer += Time.deltaTime;
			yield return null;
		}

		currentState = State.FlyingOut;
		Vector3 exitPos = targetPos + new Vector3(0, 50f, 30f);

		while (Vector3.Distance(transform.position, exitPos) > 0.5f)
		{
			transform.position = Vector3.MoveTowards(transform.position, exitPos, flySpeed * Time.deltaTime);
			SniperShoot();
			yield return null;
		}

		GameManager.Instance.AddRescuedHumans(currentLoad, transform.position);
		Destroy(gameObject);
	}

	private void SniperShoot()
	{
		shootTimer += Time.deltaTime;
		if (shootTimer >= fireRate)
		{
			Zombie targetZombie = FindClosestZombie();
			if (targetZombie != null)
			{
				targetZombie.TakeDamage(sniperDamage);
				shootTimer = 0f;
				StartCoroutine(DrawTracer(transform.position, targetZombie.transform.position + Vector3.up));
			}
		}
	}

	private Zombie FindClosestZombie()
	{
		Zombie closest = null;
		float minDist = shootRadius;

		foreach (var zombie in Zombie.AllZombies)
		{
			if (zombie == null) continue;
			float dist = Vector3.Distance(transform.position, zombie.transform.position);
			if (dist < minDist)
			{
				minDist = dist;
				closest = zombie;
			}
		}
		return closest;
	}

	private IEnumerator DrawTracer(Vector3 start, Vector3 end)
	{
		GameObject tracerLine = new GameObject("Tracer");
		LineRenderer lr = tracerLine.AddComponent<LineRenderer>();

		lr.material = new Material(Shader.Find("Sprites/Default"));
		lr.startColor = Color.yellow;
		lr.endColor = new Color(1, 0.5f, 0, 0);
		lr.startWidth = 0.1f;
		lr.endWidth = 0.02f;

		lr.SetPosition(0, start);
		lr.SetPosition(1, end);

		yield return new WaitForSeconds(0.05f);
		Destroy(tracerLine);
	}
}