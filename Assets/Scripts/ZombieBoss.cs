using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class ZombieBoss : Zombie
{
	[Header("Настройки босса")]
	[Tooltip("Через сколько секунд босс входит в ярость")]
	public float rageInterval = 8f;

	[Tooltip("Сколько секунд длится ярость")]
	public float rageDuration = 2.5f;

	[Tooltip("Радиус, в котором босс ломает здания во время ярости")]
	public float rageBreakRadius = 4f;

	[Tooltip("Сколько максимум зданий может сломать за одну ярость")]
	public int maxBuildingsPerRage = 1;

	[Tooltip("Насколько высоко подпрыгивает визуально")]
	public float rageBounceHeight = 0.6f;

	[Tooltip("Скорость подпрыгивания")]
	public float rageBounceSpeed = 9f;

	[Tooltip("Ломать здание целиком, даже если на нём нет IDamageable")]
	public bool destroyBuildingIfNoDamageable = true;

	[Tooltip("Урон зданию, если у него есть IDamageable")]
	public int buildingDamage = 999;

	[Tooltip("Тег зданий, которые можно ломать")]
	public string buildingTag = "Building";

	[Tooltip("Объект, который будет визуально прыгать. Лучше сделать дочерний объект Visual")]
	public Transform visualRoot;

	private bool isRaging = false;
	private bool canBounce = false;
	private Vector3 visualStartLocalPos;

	protected override void Start()
	{
		base.Start();

		if (visualRoot == null)
		{
			Transform autoFound = transform.Find("Visual");
			if (autoFound != null)
				visualRoot = autoFound;
		}

		if (visualRoot != null)
		{
			visualStartLocalPos = visualRoot.localPosition;
			canBounce = true;
		}
		else
		{
			Debug.LogWarning("[Boss] Не найден visualRoot. Босс будет ломать здания, но без подпрыгивания.");
		}

		StartCoroutine(RageLoop());
	}

	private void Update()
	{
		if (canBounce)
			UpdateVisualBounce();
	}

	private IEnumerator RageLoop()
	{
		while (true)
		{
			yield return new WaitForSeconds(rageInterval);

			isRaging = true;
			Debug.Log("[Boss] ВОШЁЛ В ЯРОСТЬ");

			BreakBuildingsAround();

			yield return new WaitForSeconds(rageDuration);

			isRaging = false;

			if (canBounce)
				ResetVisualPosition();

			Debug.Log("[Boss] ЯРОСТЬ ЗАКОНЧИЛАСЬ");
		}
	}

	private void UpdateVisualBounce()
	{
		if (visualRoot == null) return;

		if (!isRaging)
		{
			ResetVisualPositionSmooth();
			return;
		}

		float bounceY = Mathf.Abs(Mathf.Sin(Time.time * rageBounceSpeed)) * rageBounceHeight;

		Vector3 pos = visualRoot.localPosition;
		pos.y = visualStartLocalPos.y + bounceY;
		visualRoot.localPosition = pos;
	}

	private void ResetVisualPosition()
	{
		if (visualRoot == null) return;
		visualRoot.localPosition = visualStartLocalPos;
	}

	private void ResetVisualPositionSmooth()
	{
		if (visualRoot == null) return;

		Vector3 pos = visualRoot.localPosition;
		pos.y = Mathf.Lerp(pos.y, visualStartLocalPos.y, Time.deltaTime * 8f);
		visualRoot.localPosition = pos;
	}

	private void BreakBuildingsAround()
	{
		Collider[] hits = Physics.OverlapSphere(transform.position, rageBreakRadius);

		List<GameObject> buildingsInRange = new List<GameObject>();

		foreach (Collider hit in hits)
		{
			if (hit == null) continue;
			if (!hit.CompareTag(buildingTag)) continue;

			GameObject target = hit.gameObject;

			if (!buildingsInRange.Contains(target))
				buildingsInRange.Add(target);
		}

		buildingsInRange.Sort((a, b) =>
		{
			float distA = Vector3.Distance(transform.position, a.transform.position);
			float distB = Vector3.Distance(transform.position, b.transform.position);
			return distA.CompareTo(distB);
		});

		int brokenCount = 0;
		int limit = Mathf.Max(0, maxBuildingsPerRage);

		for (int i = 0; i < buildingsInRange.Count; i++)
		{
			if (brokenCount >= limit) break;

			GameObject target = buildingsInRange[i];
			if (target == null) continue;

			IDamageable damageable = target.GetComponent<IDamageable>();
			if (damageable != null)
			{
				damageable.TakeDamage(buildingDamage);
				brokenCount++;
				continue;
			}

			if (destroyBuildingIfNoDamageable)
			{
				Destroy(target);
				brokenCount++;
			}
		}

		Debug.Log("[Boss] Сломал зданий: " + brokenCount + " / " + maxBuildingsPerRage);
	}

	private void OnDrawGizmosSelected()
	{
		Gizmos.color = Color.red;
		Gizmos.DrawWireSphere(transform.position, rageBreakRadius);
	}
}