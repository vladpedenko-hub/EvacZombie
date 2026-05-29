using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class ZombieBoss : Zombie
{
	[Header("Boss Settings")]
	[Tooltip("How many seconds between each rage attack")]
	public float rageInterval = 8f;

	[Tooltip("Duration of each rage attack")]
	public float rageDuration = 2.5f;

	[Tooltip("Radius within which the boss destroys buildings during rage")]
	public float rageBreakRadius = 4f;

	[Tooltip("Maximum number of buildings destroyed per rage attack")]
	public int maxBuildingsPerRage = 1;

	[Tooltip("Bounce height of the visual during rage")]
	public float rageBounceHeight = 0.6f;

	[Tooltip("Bounce animation speed")]
	public float rageBounceSpeed = 9f;

	[Tooltip("Destroy the building outright if it has no IDamageable component")]
	public bool destroyBuildingIfNoDamageable = true;

	[Tooltip("Damage dealt to buildings with an IDamageable component")]
	public int buildingDamage = 999;

	[Tooltip("Tag of objects that the boss can destroy")]
	public string buildingTag = "Building";

	[Tooltip("Child transform used for the bounce animation. Typically named Visual")]
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
			Debug.LogWarning("[Boss] visualRoot not found. Boss will still rage, but without the bounce animation.");
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
			Debug.Log("[Boss] ENTERING RAGE");

			BreakBuildingsAround();

			yield return new WaitForSeconds(rageDuration);

			isRaging = false;

			if (canBounce)
				ResetVisualPosition();

			Debug.Log("[Boss] RAGE ENDED");
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

		Debug.Log("[Boss] Buildings destroyed: " + brokenCount + " / " + maxBuildingsPerRage);
	}

	private void OnDrawGizmosSelected()
	{
		Gizmos.color = Color.red;
		Gizmos.DrawWireSphere(transform.position, rageBreakRadius);
	}
}