using UnityEngine;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.AI;

public class HelicopterController : MonoBehaviour
{
	public enum HeliState { Landing, Loading, TakingOff }
	public HeliState currentState;

	[Header("Связь с карточкой")]
	public CardData myCardData;

	[Header("Технические настройки")]
	public float buffRadius = 15f;
	public float exitHeight = 40f;

	[Header("Визуал посадочной зоны")]
	public float landingRadius = 3f;
	public Color landingColor = new Color(0f, 1f, 0.2f, 0.5f);
	public GameObject customLandingPrefab;

	[Header("Посадка")]
	public float boardingRadius = 2.5f;
	public float boardingAnimDuration = 0.28f;
	public float boardingLiftHeight = 1.2f;

	[Tooltip("Дистанция, при которой вертолет пугается зомби и экстренно взлетает")]
	public float panicRadius = 3f;

	public GameObject sirenRingPrefab;
	public GameObject humanAlertPrefab;
	public float alertDuration = 2.0f;

	[Header("Ссылки")]
	public TextMeshProUGUI loadText;
	public GameObject hotWarning;

	private int maxCapacity;
	private float verticalSpeed;
	private float attractRadius;
	private float loadTime;
	private float boardingCooldown;

	private int currentLoad = 0;
	private int loadedHumanCount = 0;
	private int loadedScientistCount = 0;
	private Vector3 targetPos;
	private GameObject landingMarker;
	private bool isTooHot = false;

	private List<GameObject> loadedUnits = new List<GameObject>();

	private void Start()
	{
		int currentLevel = 1;
		if (PlayerProfile.Instance != null && myCardData != null)
		{
			var progress = PlayerProfile.Instance.ownedCardsProgress.Find(p => p.cardId == myCardData.name);
			if (progress != null) currentLevel = progress.currentLevel;

			maxCapacity = (int)myCardData.GetCalculatedStat(StatType.Capacity, currentLevel);
			verticalSpeed = myCardData.GetCalculatedStat(StatType.Speed, currentLevel);
			attractRadius = myCardData.GetCalculatedStat(StatType.Radius, currentLevel);
			loadTime = myCardData.GetCalculatedStat(StatType.Duration, currentLevel);
			boardingCooldown = myCardData.GetCalculatedStat(StatType.Cooldown, currentLevel);
		}

		if (maxCapacity <= 0) maxCapacity = 6;
		if (verticalSpeed <= 0) verticalSpeed = 15f;
		if (attractRadius <= 0) attractRadius = 12f;
		if (loadTime <= 0) loadTime = 15f;
		if (boardingCooldown <= 0) boardingCooldown = 0.5f;
	}

	public void Launch(Vector3 pos)
	{
		if (maxCapacity == 0) Start();

		currentLoad = 0;
		loadedHumanCount = 0;
		loadedScientistCount = 0;
		isTooHot = false;
		loadedUnits.Clear();

		targetPos = pos;
		transform.position = new Vector3(pos.x, exitHeight, pos.z);
		currentState = HeliState.Landing;

		if (hotWarning != null) hotWarning.SetActive(false);
		if (loadText != null)
		{
			loadText.gameObject.SetActive(true);
			loadText.text = "";
		}

		if (customLandingPrefab != null)
		{
			landingMarker = Instantiate(customLandingPrefab, new Vector3(pos.x, 0.1f, pos.z), Quaternion.identity);
		}
		else
		{
			landingMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
			landingMarker.transform.position = new Vector3(pos.x, 0.1f, pos.z);
			landingMarker.transform.localScale = new Vector3(landingRadius * 2, 0.01f, landingRadius * 2);
			Destroy(landingMarker.GetComponent<Collider>());

			Renderer r = landingMarker.GetComponent<Renderer>();
			r.material = new Material(Shader.Find("Sprites/Default"));
			r.material.color = landingColor;
		}
	}

	private void Update()
	{
		if (currentState == HeliState.Landing)
		{
			transform.position = Vector3.MoveTowards(transform.position, new Vector3(targetPos.x, 1f, targetPos.z), verticalSpeed * Time.deltaTime);
			if (transform.position.y <= 1.1f) StartLoading();
		}
		else if (currentState == HeliState.TakingOff)
		{
			transform.position += Vector3.up * verticalSpeed * Time.deltaTime;
			if (transform.position.y > exitHeight)
			{
				Destroy(gameObject);
			}
		}
	}

	private void StartLoading()
	{
		currentState = HeliState.Loading;
		if (landingMarker) Destroy(landingMarker);
		if (loadText != null) loadText.gameObject.SetActive(true);

		if (sirenRingPrefab != null)
		{
			GameObject ring = Instantiate(sirenRingPrefab, transform.position + Vector3.up * 0.1f, Quaternion.Euler(90, 0, 0));
			ring.GetComponent<SirenEffect>()?.Setup(attractRadius, 1.2f);
		}

		foreach (var h in Human.AllHumans)
		{
			if (h == null) continue;

			Vector2 heliPos2D = new Vector2(transform.position.x, transform.position.z);
			Vector2 humanPos2D = new Vector2(h.transform.position.x, h.transform.position.z);

			if (Vector2.Distance(heliPos2D, humanPos2D) < attractRadius)
			{
				h.SetRescueTarget(transform);

				if (humanAlertPrefab != null)
				{
					GameObject alert = Instantiate(humanAlertPrefab, h.transform.position + Vector3.up * 0.1f, Quaternion.Euler(90, 0, 0), h.transform);
					Destroy(alert, alertDuration);
				}
			}
		}

		foreach (var s in Scientist.AllScientists)
		{
			if (s == null) continue;

			Vector2 heliPos2D = new Vector2(transform.position.x, transform.position.z);
			Vector2 scientistPos2D = new Vector2(s.transform.position.x, s.transform.position.z);

			if (Vector2.Distance(heliPos2D, scientistPos2D) < attractRadius)
			{
				s.SetRescueTarget(transform);

				if (humanAlertPrefab != null)
				{
					GameObject alert = Instantiate(humanAlertPrefab, s.transform.position + Vector3.up * 0.1f, Quaternion.Euler(90, 0, 0), s.transform);
					Destroy(alert, alertDuration);
				}
			}
		}

		StartCoroutine(LoadRoutine());
	}

	private IEnumerator LoadRoutine()
	{
		float nextBoardTime = 0;
		float waitTimer = 0;

		// Переменные для оптимизации проверки зомби
		float panicCheckInterval = 0.2f;
		float nextPanicCheckTime = 0;
		float panicRadiusSqr = panicRadius * panicRadius;

		while (currentLoad < maxCapacity && !isTooHot && waitTimer < loadTime)
		{
			waitTimer += Time.deltaTime;

			// Оптимизированный блок проверки паники
			if (Time.time >= nextPanicCheckTime)
			{
				nextPanicCheckTime = Time.time + panicCheckInterval;
				foreach (var z in Zombie.AllZombies)
				{
					if (z != null && (z.transform.position - transform.position).sqrMagnitude < panicRadiusSqr)
					{
						isTooHot = true;
						break;
					}
				}
			}

			if (isTooHot) break;

			if (Time.time >= nextBoardTime)
			{
				bool boarded = false;

				foreach (var hum in Human.AllHumans)
				{
					if (hum == null || loadedUnits.Contains(hum.gameObject)) continue;

					Vector2 heliPos2D = new Vector2(transform.position.x, transform.position.z);
					Vector2 humanPos2D = new Vector2(hum.transform.position.x, hum.transform.position.z);

					if (Vector2.Distance(heliPos2D, humanPos2D) < boardingRadius)
					{
						yield return StartCoroutine(hum.PlayBoardingAnimation(transform.position, boardingAnimDuration, boardingLiftHeight));
						loadedUnits.Add(hum.gameObject);

						currentLoad++;
						loadedHumanCount++;
						boarded = true;
						break;
					}
				}

				if (!boarded)
				{
					foreach (var sci in Scientist.AllScientists)
					{
						if (sci == null || loadedUnits.Contains(sci.gameObject)) continue;

						Vector2 heliPos2D = new Vector2(transform.position.x, transform.position.z);
						Vector2 scientistPos2D = new Vector2(sci.transform.position.x, sci.transform.position.z);

						if (Vector2.Distance(heliPos2D, scientistPos2D) < boardingRadius)
						{
							yield return StartCoroutine(sci.PlayBoardingAnimation(transform.position, boardingAnimDuration, boardingLiftHeight));
							loadedUnits.Add(sci.gameObject);

							currentLoad++;
							loadedScientistCount++;
							boarded = true;
							break;
						}
					}
				}

				if (boarded)
				{
					if (loadText) loadText.text = currentLoad.ToString();
					nextBoardTime = Time.time + boardingCooldown;
				}
			}

			yield return null;
		}

		TakeOff(isTooHot);
	}

	private void TakeOff(bool fromPanic = false)
	{
		if (fromPanic && hotWarning != null) hotWarning.SetActive(true);
		if (loadText != null) loadText.gameObject.SetActive(false);

		foreach (var h in Human.AllHumans)
		{
			if (h != null && h.rescueTarget == transform) h.CancelRescue();
		}

		foreach (var s in Scientist.AllScientists)
		{
			if (s != null && s.rescueTarget == transform) s.CancelRescue();
		}

		currentState = HeliState.TakingOff;

		if (loadedHumanCount > 0 || loadedScientistCount > 0)
		{
			GameManager.Instance.AddRescuedFromTransport(
				loadedHumanCount,
				loadedScientistCount,
				transform.position
			);

			foreach (var unit in loadedUnits)
			{
				if (unit != null) Destroy(unit);
			}

			loadedUnits.Clear();
		}
	}
}