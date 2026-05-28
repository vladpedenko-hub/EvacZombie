using UnityEngine;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.AI;

public class HelicopterController : MonoBehaviour
{
	public enum HeliState { Landing, Loading, TakingOff }
	public HeliState currentState;

	[Header("Card Data")]
	public CardData myCardData;

	[Header("Movement Settings")]
	public float buffRadius = 15f;
	public float exitHeight = 40f;

	[Header("Landing Zone Visuals")]
	public float landingRadius = 3f;
	public Color landingColor = new Color(0f, 1f, 0.2f, 0.5f);
	public GameObject customLandingPrefab;

	[Header("Boarding")]
	public float boardingRadius = 2.5f;
	public float boardingAnimDuration = 0.28f;
	public float boardingLiftHeight = 1.2f;

	[Tooltip("Radius within which zombies trigger the helicopter to abort loading and flee")]
	public float panicRadius = 3f;

	public GameObject sirenRingPrefab;
	public GameObject humanAlertPrefab;
	public float alertDuration = 2.0f;

	[Header("UI")]
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

		// Run-modifiers (roguelite)
		var run = RunSessionData.Instance;
		if (run != null)
		{
			maxCapacity += (int)run.GetModifier("heli_capacity_add");
			verticalSpeed *= (1f + run.GetModifier("heli_speed_mult"));
			attractRadius *= (1f + run.GetModifier("heli_radius_mult"));
			loadTime += run.GetModifier("heli_loadtime_add");
			boardingCooldown *= Mathf.Max(0.05f, 1f - run.GetModifier("heli_boarding_reduction"));

			if (run.HasFlag("heli_no_panic"))
				panicRadius = 0f;

			if (run.HasFlag("heli_instant_land"))
				verticalSpeed = 9999f;

			if (run.HasFlag("heli_unlimited_capacity"))
				maxCapacity = 999;
		}
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

		// Ultimate: global attract - all civilians run to heli
		if (RunSessionData.Instance != null && RunSessionData.Instance.HasFlag("heli_global_attract"))
		{
			foreach (var h in Human.AllHumans)
			{
				if (h == null || h.rescueTarget != null) continue;
				h.SetRescueTarget(transform);
			}
			foreach (var s in Scientist.AllScientists)
			{
				if (s == null || s.rescueTarget != null) continue;
				s.SetRescueTarget(transform);
			}
		}

		StartCoroutine(LoadRoutine());
	}

	private IEnumerator LoadRoutine()
	{
		float waitTimer = 0;

		// Local timers using Time.deltaTime — pause automatically when timeScale=0
		float boardCooldownTimer = boardingCooldown; // start ready to board
		float panicCheckTimer = 0f;
		const float panicCheckInterval = 0.2f;
		float panicRadiusSqr = panicRadius * panicRadius;

		while (currentLoad < maxCapacity && !isTooHot && waitTimer < loadTime)
		{
			// Wait for time to resume — do not advance timers during pause
			if (Time.timeScale < 0.001f)
			{
				yield return null;
				continue;
			}

			float dt = Time.deltaTime;
			waitTimer       += dt;
			boardCooldownTimer += dt;
			panicCheckTimer += dt;

			// Periodic panic check (zombie nearby)
			if (panicCheckTimer >= panicCheckInterval)
			{
				panicCheckTimer = 0f;
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

			if (boardCooldownTimer >= boardingCooldown)
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
					boardCooldownTimer = 0f;

					// ── Early takeoff: all remaining civilians are already on board ──
					// Don't wait for the timer — if there's no one left to load, take off immediately.
					if (AllCiviliansHandled())
						break;
				}
			}

			// One more check at the end of the iteration (in case there were no civilians at all)
			if (currentLoad > 0 && AllCiviliansHandled())
				break;

			yield return null;
		}

		TakeOff(isTooHot);
	}

	/// <summary>
	/// Returns true if all humans and scientists on the map are already on board this helicopter.
	/// Used for early takeoff without waiting for the timer.
	/// </summary>
	private bool AllCiviliansHandled()
	{
		foreach (var h in Human.AllHumans)
			if (h != null && !loadedUnits.Contains(h.gameObject)) return false;

		foreach (var s in Scientist.AllScientists)
			if (s != null && !loadedUnits.Contains(s.gameObject)) return false;

		return true;
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