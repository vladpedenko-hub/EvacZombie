using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;
using TMPro;

[RequireComponent(typeof(NavMeshAgent))]
public class CarController : MonoBehaviour
{
	public enum CarState { DrivingToTarget, Loading, Leaving }
	public CarState currentState;

	[Header("Card & Settings")]
	public CardData myCardData;

	[Header("Runtime Parameters")]
	public float crushRadius = 2.5f;
	public float boardingRadius = 1.8f;
	public int boardPerTick = 1;
	public float dangerRadius = 2.0f;

	[Header("Animation")]
	public float boardingAnimDuration = 0.28f;
	public float boardingLiftHeight = 0.8f;

	[Header("UI & Effects")]
	public TextMeshProUGUI loadText;
	public float textHeight = 2f;
	public GameObject hotWarning;
	public GameObject sirenRingPrefab;
	public GameObject humanAlertPrefab;
	public float alertDuration = 2.0f;

	private int maxCapacity;
	private float loadTime;
	private float sirenRadius;
	private float boardingCooldown;
	private float moveSpeed;

	private NavMeshAgent agent;
	private int currentLoad = 0;
	private int loadedHumanCount = 0;
	private int loadedScientistCount = 0;
	private Transform exitWaypoint;
	private bool isTooHot = false;
	private bool sirenFired = false;

	private List<GameObject> loadedUnits = new List<GameObject>();

	private void Awake()
	{
		agent = GetComponent<NavMeshAgent>();
	}

	private void Start()
	{
		int currentLevel = 1;
		if (PlayerProfile.Instance != null && myCardData != null)
		{
			var progress = PlayerProfile.Instance.ownedCardsProgress.Find(p => p.cardId == myCardData.name);
			if (progress != null) currentLevel = progress.currentLevel;

			maxCapacity = (int)myCardData.GetCalculatedStat(StatType.Capacity, currentLevel);
			loadTime = myCardData.GetCalculatedStat(StatType.Duration, currentLevel);
			sirenRadius = myCardData.GetCalculatedStat(StatType.Radius, currentLevel);
			boardingCooldown = myCardData.GetCalculatedStat(StatType.Cooldown, currentLevel);
			moveSpeed = myCardData.GetCalculatedStat(StatType.Speed, currentLevel);
		}

		if (maxCapacity <= 0) maxCapacity = 5;
		if (loadTime <= 0) loadTime = 10f;
		if (sirenRadius <= 0) sirenRadius = 5f;
		if (boardingCooldown <= 0) boardingCooldown = 1f;
		if (moveSpeed <= 0) moveSpeed = 3.5f;

		agent.speed = moveSpeed;

		if (hotWarning != null) hotWarning.SetActive(false);

		if (loadText != null)
		{
			loadText.gameObject.SetActive(true);
			loadText.text = "";
			loadText.transform.position = transform.position + Vector3.up * textHeight;
			loadText.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
		}
	}

	public void Launch(Vector3 targetPos)
	{
		currentLoad = 0;
		loadedHumanCount = 0;
		loadedScientistCount = 0;
		isTooHot = false;
		sirenFired = false;
		loadedUnits.Clear();

		if (hotWarning != null) hotWarning.SetActive(false);

		if (loadText != null)
		{
			loadText.gameObject.SetActive(true);
			loadText.text = "";
			loadText.transform.position = transform.position + Vector3.up * textHeight;
			loadText.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
		}

		GameObject[] waypoints = GameObject.FindGameObjectsWithTag("CarWaypoint");
		if (waypoints.Length < 2)
		{
			Destroy(gameObject);
			return;
		}

		Transform entryWaypoint = waypoints[0].transform;
		exitWaypoint = waypoints[1].transform;
		float minDist = float.MaxValue;
		float maxDist = float.MinValue;

		foreach (var wp in waypoints)
		{
			float d = Vector3.Distance(targetPos, wp.transform.position);
			if (d < minDist) { minDist = d; entryWaypoint = wp.transform; }
			if (d > maxDist) { maxDist = d; exitWaypoint = wp.transform; }
		}

		agent.Warp(entryWaypoint.position);
		currentState = CarState.DrivingToTarget;
		agent.SetDestination(targetPos);

		StartCoroutine(CarRoutine());
	}

	private void Update()
	{
		if (loadText != null)
		{
			loadText.transform.position = transform.position + Vector3.up * textHeight;
			loadText.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
		}

		if (agent.velocity.magnitude > 1f)
		{
			Collider[] hits = Physics.OverlapSphere(transform.position, crushRadius);
			foreach (var h in hits)
			{
				if (h.CompareTag("Zombie"))
					h.GetComponent<Zombie>()?.TakeDamage(1000);
			}
		}
	}

	private IEnumerator CarRoutine()
	{
		while (agent.pathPending || agent.remainingDistance > 1.5f)
			yield return null;

		currentState = CarState.Loading;
		agent.isStopped = true;
		agent.velocity = Vector3.zero;

		if (!sirenFired)
		{
			if (sirenRingPrefab != null)
			{
				GameObject ring = Instantiate(
					sirenRingPrefab,
					transform.position + Vector3.up * 0.1f,
					Quaternion.Euler(90, 0, 0));
				ring.GetComponent<SirenEffect>()?.Setup(sirenRadius, 1.2f);
			}

			foreach (var h in Human.AllHumans)
			{
				if (h != null && Vector3.Distance(transform.position, h.transform.position) <= sirenRadius)
				{
					h.SetRescueTarget(transform);

					if (humanAlertPrefab != null)
					{
						GameObject alert = Instantiate(
							humanAlertPrefab,
							h.transform.position + Vector3.up * 0.1f,
							Quaternion.Euler(90, 0, 0),
							h.transform);
						Destroy(alert, alertDuration);
					}
				}
			}

			foreach (var s in Scientist.AllScientists)
			{
				if (s != null && Vector3.Distance(transform.position, s.transform.position) <= sirenRadius)
				{
					s.SetRescueTarget(transform);

					if (humanAlertPrefab != null)
					{
						GameObject alert = Instantiate(
							humanAlertPrefab,
							s.transform.position + Vector3.up * 0.1f,
							Quaternion.Euler(90, 0, 0),
							s.transform);
						Destroy(alert, alertDuration);
					}
				}
			}

			sirenFired = true;
		}

		float nextBoardTime = 0f;
		float waitTimer = 0f;

		while (currentLoad < maxCapacity && !isTooHot && waitTimer < loadTime)
		{
			waitTimer += Time.deltaTime;

			foreach (var z in Zombie.AllZombies)
			{
				if (z != null && Vector3.Distance(transform.position, z.transform.position) < dangerRadius)
				{
					isTooHot = true;
					if (hotWarning != null) hotWarning.SetActive(true);
					break;
				}
			}

			if (isTooHot) break;

			if (Time.time >= nextBoardTime)
			{
				int boarded = 0;
				Collider[] unitsAtDoors = Physics.OverlapSphere(transform.position, boardingRadius);

				foreach (var unit in unitsAtDoors)
				{
					if (currentLoad >= maxCapacity) break;
					if (boarded >= boardPerTick) break;

					Human human = unit.GetComponent<Human>();
					if (human != null && !loadedUnits.Contains(human.gameObject))
					{
						yield return StartCoroutine(human.PlayBoardingAnimation(transform.position, boardingAnimDuration, boardingLiftHeight));

						// Human may have been infected/destroyed mid-animation — skip if so
						if (human == null) continue;

						loadedUnits.Add(human.gameObject);

						currentLoad++;
						loadedHumanCount++;
						boarded++;
						continue;
					}

					Scientist scientist = unit.GetComponent<Scientist>();
					if (scientist != null && !loadedUnits.Contains(scientist.gameObject))
					{
						yield return StartCoroutine(scientist.PlayBoardingAnimation(transform.position, boardingAnimDuration, boardingLiftHeight));

						// Scientist may have been infected/destroyed mid-animation — skip if so
						if (scientist == null) continue;

						loadedUnits.Add(scientist.gameObject);

						currentLoad++;
						loadedScientistCount++;
						boarded++;
					}
				}

				if (boarded > 0)
				{
					if (loadText != null)
						loadText.text = currentLoad.ToString();

					nextBoardTime = Time.time + boardingCooldown;
				}
			}

			yield return null;
		}

		if (isTooHot && hotWarning != null)
		{
			hotWarning.SetActive(true);
			yield return new WaitForSeconds(0.8f);
		}

		foreach (var h in Human.AllHumans)
			if (h != null && h.rescueTarget == transform) h.CancelRescue();
		foreach (var s in Scientist.AllScientists)
			if (s != null && s.rescueTarget == transform) s.CancelRescue();

		currentState = CarState.Leaving;
		agent.isStopped = false;
		agent.SetDestination(exitWaypoint.position);

		if (loadText != null)
			loadText.gameObject.SetActive(false);

		if (loadedHumanCount > 0 || loadedScientistCount > 0)
		{
			GameManager.Instance.AddRescuedFromTransport(
				loadedHumanCount,
				loadedScientistCount,
				transform.position
			);

			foreach (var unit in loadedUnits)
				if (unit != null) Destroy(unit);

			loadedUnits.Clear();
		}

		while (agent.pathPending || agent.remainingDistance > 2f)
			yield return null;

		Destroy(gameObject);
	}
}