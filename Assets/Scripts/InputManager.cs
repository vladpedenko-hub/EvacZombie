using UnityEngine;

// [WHERE IT LIVES]: On the InputManager object on the main scene.
public class InputManager : MonoBehaviour
{
	public static InputManager Instance;

	[Header("Icons")]
	public GameObject invalidPlacementIcon;

	private Camera mainCam;
	private CardData draggingCard = null;
	private bool isDragging;
	private LineRenderer radiusCircle;

	// Building outline state
	private LineRenderer buildingOutline;
	private GameObject outlinedBuilding;
	private Vector3[] buildingCorners = new Vector3[4];

	/// <summary>
	/// Set to true to fully block card placement (e.g. during pause).
	/// </summary>
	public bool IsPaused = false;

	private void Awake() => Instance = this;

	private void Start()
	{
		mainCam = Camera.main;

		// Radius circle
		GameObject circleObj = new GameObject("DragRadiusVisual");
		radiusCircle = circleObj.AddComponent<LineRenderer>();
		radiusCircle.startWidth = 0.2f;
		radiusCircle.endWidth = 0.2f;
		radiusCircle.material = SharedMaterials.SpritesDefaultMaterial;
		radiusCircle.loop = true;
		radiusCircle.enabled = false;

		// Building outline
		GameObject outlineObj = new GameObject("BuildingOutline");
		buildingOutline = outlineObj.AddComponent<LineRenderer>();
		buildingOutline.startWidth = 0.15f;
		buildingOutline.endWidth = 0.15f;
		buildingOutline.material = SharedMaterials.SpritesDefaultMaterial;
		buildingOutline.loop = true;
		buildingOutline.positionCount = 5;
		buildingOutline.startColor = new Color(1f, 0.9f, 0.3f, 0.9f);
		buildingOutline.endColor = new Color(1f, 0.9f, 0.3f, 0.9f);
		buildingOutline.enabled = false;
		buildingOutline.sortingOrder = 100;

		if (invalidPlacementIcon != null)
			invalidPlacementIcon.SetActive(false);
	}

	public void StartDragging(CardData card)
	{
		if (IsPaused) return;   // Block during pause (LevelUpScreen)

		draggingCard = card;
		isDragging = true;

		if (TimeManager.Instance != null)
		{
			TimeManager.Instance.StartSlowMo();
		}
	}

	public void UpdateDragging(Vector2 screenPos)
	{
		if (!isDragging || draggingCard == null) return;

		CameraZoomPanController.Instance?.NotifyCardDragScreenPosition(screenPos);

		Ray ray = mainCam.ScreenPointToRay(screenPos);
		if (Physics.Raycast(ray, out RaycastHit hit))
		{
			float radius = GetCardRadius(draggingCard);
			DrawRadiusCircle(hit.point, radius);

			bool canPlace = CanPlaceCardAtScreenPoint(draggingCard, screenPos, hit);

			if (!canPlace)
			{
				radiusCircle.startColor = new Color(1, 0, 0, 0.5f);
				radiusCircle.endColor = new Color(1, 0, 0, 0.5f);

				if (invalidPlacementIcon != null)
				{
					invalidPlacementIcon.SetActive(true);
					invalidPlacementIcon.transform.position = hit.point + Vector3.up * 1.5f;
					invalidPlacementIcon.transform.forward = mainCam.transform.forward;
				}
			}
			else
			{
				radiusCircle.startColor = new Color(0, 1, 0, 0.5f);
				radiusCircle.endColor = new Color(0, 1, 0, 0.5f);

				if (invalidPlacementIcon != null)
					invalidPlacementIcon.SetActive(false);
			}

			HandleSniperOutline(hit, canPlace);
		}
		else
		{
			ClearBuildingOutline();
		}
	}

	public bool EndDragging()
	{
		if (!isDragging) return false;
		if (IsPaused) { isDragging = false; return false; }  // Block during pause

		isDragging = false;
		radiusCircle.enabled = false;

		if (TimeManager.Instance != null)
		{
			TimeManager.Instance.StopSlowMo();
		}

		if (invalidPlacementIcon != null)
			invalidPlacementIcon.SetActive(false);

		CardData cardToPlay = draggingCard;
		draggingCard = null;

		if (cardToPlay == null)
		{
			ClearBuildingOutline();
			return false;
		}

		if (cardToPlay.cardType == CardManager.CardType.Sniper)
		{
			bool placed = ExecuteSniperLogic(cardToPlay);
			ClearBuildingOutline();
			if (placed) Feedback.Play(FeedbackEvent.CardPlaceOk);

			// Core Loop v2 (Docs/CORE_LOOP_V2.md Phase 2): Planning no longer ends on first
			// placement -- PlanningControllerV2's GO button / auto-start timer does it instead.
			if (placed && !CoreLoopV2.IsActive && GameManager.Instance.State == GameManager.GameState.Planning)
				GameManager.Instance.StartGame();

			// Tutorial step completion is now generic (Docs/CORE_LOOP_V2.md Phase 3 tutorial
			// rewrite): TutorialManager subscribes to CardUI.OnCardPlaced itself when a step's
			// completionTrigger is CardPlaced, instead of this blindly calling NextStep() on any
			// successful placement regardless of which step (or whether one) is active.

			return placed;
		}

		if (CardManager.Instance != null && CardManager.Instance.IsScreenPointOverCardsPanel(Input.mousePosition))
		{
			ClearBuildingOutline();
			return false;
		}

		Ray ray = mainCam.ScreenPointToRay(Input.mousePosition);
		if (Physics.Raycast(ray, out RaycastHit hit))
		{
			if (!CanPlaceCardAtScreenPoint(cardToPlay, Input.mousePosition, hit))
			{
				ClearBuildingOutline();
				return false;
			}

			ExecuteCardLogic(cardToPlay, hit);
			Feedback.Play(FeedbackEvent.CardPlaceOk, hit.point);
			ClearBuildingOutline();

			// Core Loop v2 (Docs/CORE_LOOP_V2.md Phase 2): see the Sniper branch above.
			if (!CoreLoopV2.IsActive && GameManager.Instance.State == GameManager.GameState.Planning)
				GameManager.Instance.StartGame();

			// Tutorial step completion is generic now -- see the Sniper branch above.

			return true;
		}

		ClearBuildingOutline();
		return false;
	}

	private void HandleSniperOutline(RaycastHit hit, bool canPlace)
	{
		if (draggingCard == null || draggingCard.cardType != CardManager.CardType.Sniper)
		{
			ClearBuildingOutline();
			return;
		}

		if (hit.collider != null && hit.collider.CompareTag("Building") && canPlace)
		{
			GameObject building = hit.collider.gameObject;
			if (outlinedBuilding != building)
			{
				outlinedBuilding = building;
				DrawBuildingOutline(outlinedBuilding);
			}
		}
		else
		{
			ClearBuildingOutline();
		}
	}

	private void DrawBuildingOutline(GameObject building)
	{
		if (building == null)
		{
			ClearBuildingOutline();
			return;
		}

		BoxCollider box = building.GetComponent<BoxCollider>();
		if (box == null)
		{
			ClearBuildingOutline();
			return;
		}

		Bounds b = box.bounds;
		float y = b.max.y + 0.05f;

		Vector3 p0 = new Vector3(b.min.x, y, b.min.z);
		Vector3 p1 = new Vector3(b.max.x, y, b.min.z);
		Vector3 p2 = new Vector3(b.max.x, y, b.max.z);
		Vector3 p3 = new Vector3(b.min.x, y, b.max.z);

		buildingCorners[0] = p0;
		buildingCorners[1] = p1;
		buildingCorners[2] = p2;
		buildingCorners[3] = p3;

		buildingOutline.enabled = true;
		buildingOutline.SetPosition(0, p0);
		buildingOutline.SetPosition(1, p1);
		buildingOutline.SetPosition(2, p2);
		buildingOutline.SetPosition(3, p3);
		buildingOutline.SetPosition(4, p0);
	}

	private void ClearBuildingOutline()
	{
		buildingOutline.enabled = false;
		outlinedBuilding = null;
	}

	private bool CanPlaceCardAtScreenPoint(CardData card, Vector2 screenPos, RaycastHit hit)
	{
		if (card == null) return false;

		bool isUI = CardManager.Instance != null && CardManager.Instance.IsScreenPointOverCardsPanel(screenPos);
		if (isUI) return false;

		bool isBuilding = hit.collider.CompareTag("Building");

		switch (card.cardType)
		{
			case CardManager.CardType.Sniper:
				return isBuilding;

			case CardManager.CardType.Bomb:
				return true;

			case CardManager.CardType.Bait:
				return true;

			case CardManager.CardType.Barricade:
			{
				StreetSegment segment = hit.collider != null ? hit.collider.GetComponentInParent<StreetSegment>() : null;
				if (segment != null && segment.IsIntersection) return false; // no single travel direction to block
				return !isBuilding;
			}

			default:
				return !isBuilding;
		}
	}

	private void DrawRadiusCircle(Vector3 center, float radius)
	{
		radiusCircle.enabled = true;
		radiusCircle.positionCount = 32;

		float angle = 0f;
		for (int i = 0; i < 32; i++)
		{
			float x = Mathf.Sin(Mathf.Deg2Rad * angle) * radius;
			float z = Mathf.Cos(Mathf.Deg2Rad * angle) * radius;
			radiusCircle.SetPosition(i, center + new Vector3(x, 0.2f, z));
			angle += 360f / 32f;
		}
	}

	private float GetCardRadius(CardData card)
	{
		int currentLevel = 1;
		if (PlayerProfile.Instance != null)
		{
			var progress = PlayerProfile.Instance.ownedCardsProgress.Find(p => p.cardId == card.name);
			if (progress != null) currentLevel = progress.currentLevel;
		}

		float radius = card.GetCalculatedStat(StatType.Radius, currentLevel);

		if (radius <= 0)
		{
			switch (card.cardType)
			{
				case CardManager.CardType.Car: return 4f;
				case CardManager.CardType.Soldier: return 12f;
				case CardManager.CardType.Sniper: return 25f;
				case CardManager.CardType.Helicopter: return 12f;
				case CardManager.CardType.Bait: return 15f;
				case CardManager.CardType.Barricade: return 0f;
				default: return 5f;
			}
		}

		return radius;
	}

	private bool ExecuteSniperLogic(CardData card)
	{
		if (outlinedBuilding == null) return false;

		Vector3 center = (buildingCorners[0] + buildingCorners[1] + buildingCorners[2] + buildingCorners[3]) / 4f;

		GameObject spawnedObject = Instantiate(card.cardPrefab, center, Quaternion.identity);
		Sniper sniper = spawnedObject.GetComponent<Sniper>();
		if (sniper != null)
			sniper.Init(outlinedBuilding.transform);

		return true;
	}

	private void ExecuteCardLogic(CardData card, RaycastHit hit)
	{
		if (card.cardPrefab == null) return;

		Vector3 spawnPos = hit.point;
		GameObject spawnedObject = null;

		switch (card.cardType)
		{
			case CardManager.CardType.Helicopter:
			{
				spawnedObject = Instantiate(card.cardPrefab);
				HelicopterController heli = spawnedObject.GetComponent<HelicopterController>();
				heli.Launch(spawnPos);

				// Core Loop v2 (Docs/CORE_LOOP_V2.md Phase 4): the free, one-shot Night card.
				if (card.isLastTransport && NightLastCallController.Instance != null)
				{
					heli.ConfigureAsLastTransport(NightLastCallController.Instance.DepartAtTime);
					NightLastCallController.Instance.NotifyPlaced();
				}
				break;
			}

			case CardManager.CardType.Bomb:
				spawnedObject = Instantiate(card.cardPrefab);
				spawnedObject.GetComponent<Bomb>().Launch(spawnPos);
				break;

			case CardManager.CardType.Bait:
				spawnedObject = Instantiate(card.cardPrefab);
				spawnedObject.GetComponent<Bait>().Launch(spawnPos);
				break;

			case CardManager.CardType.Car:
				spawnedObject = Instantiate(card.cardPrefab);
				spawnedObject.GetComponent<CarController>().Launch(spawnPos);
				break;

			case CardManager.CardType.CombatHelicopter:
				spawnedObject = Instantiate(card.cardPrefab);
				spawnedObject.SendMessage("Launch", spawnPos, SendMessageOptions.DontRequireReceiver);
				break;

			case CardManager.CardType.Soldier:
				int currentLevel = 1;
				if (PlayerProfile.Instance != null)
				{
					var progress = PlayerProfile.Instance.ownedCardsProgress.Find(p => p.cardId == card.name);
					if (progress != null) currentLevel = progress.currentLevel;
				}

				int count = (int)card.GetCalculatedStat(StatType.Count, currentLevel);
				if (count <= 0) count = 1;

				for (int i = 0; i < count; i++)
				{
					Vector3 offset = count > 1
						? new Vector3(Random.Range(-1.2f, 1.2f), 0, Random.Range(-1.2f, 1.2f))
						: Vector3.zero;

					Instantiate(card.cardPrefab, spawnPos + offset, Quaternion.identity);
				}
				break;

			case CardManager.CardType.Barricade:
			{
				Quaternion barricadeRotation = Quaternion.identity;
				StreetSegment segment = hit.collider != null ? hit.collider.GetComponentInParent<StreetSegment>() : null;
				bool snapped = false;

				if (segment != null && !segment.IsIntersection)
				{
					Vector3 dir = segment.Direction;
					dir.y = 0f;
					if (dir.sqrMagnitude > 0.001f)
					{
						dir.Normalize();
						// Perpendicular to the travel direction, in the XZ plane -- the axis the
						// barricade's long side must span to actually block the street.
						Vector3 perp = new Vector3(dir.z, 0f, -dir.x);

						// Snap to the centerline: keep the tap's coordinate along the street, zero
						// out the cross-street offset.
						float tapHeight = spawnPos.y;
						float alongTap = Vector3.Dot(spawnPos - segment.transform.position, dir);
						spawnPos = segment.transform.position + dir * alongTap;
						spawnPos.y = tapHeight;

						// The barricade mesh's long side (the collider's size.x) is local X, not
						// local Z/forward -- the extra 90-degree twist re-points local X at perp
						// instead of leaving it on perp's "forward" companion axis.
						barricadeRotation = Quaternion.LookRotation(perp) * Quaternion.Euler(0, 90, 0);
						snapped = true;
					}
				}

				if (!snapped && hit.collider != null)
				{
					// Old behavior, unchanged: no StreetSegment data (hand-built level without a
					// synced RoadZone, or an unrecognized collider) -- orient from the road mesh's
					// own transform like before.
					Vector3 roadDir = hit.collider.transform.forward;
					roadDir.y = 0f;
					if (roadDir.sqrMagnitude > 0.001f)
						barricadeRotation = Quaternion.LookRotation(roadDir.normalized) * Quaternion.Euler(0, 90, 0);
				}

				GameObject spawnedBarricade = Instantiate(card.cardPrefab, spawnPos, barricadeRotation);

				if (snapped)
				{
					// Stretch the long (width-spanning) side to cover the street + ~5% overlap so
					// zombies cannot walk around the ends. Barricade.Start() later stacks
					// "barricade_width_mult" on top of this via transform.localScale *=, so the
					// final footprint only ever grows further from here.
					BoxCollider box = spawnedBarricade.GetComponent<BoxCollider>();
					if (box != null && box.size.x > 0.001f)
					{
						float scaleX = (segment.Width * 1.05f) / box.size.x;
						spawnedBarricade.transform.localScale = new Vector3(scaleX, 1f, 1f);
					}
				}
				break;
			}

			default:
				Instantiate(card.cardPrefab, spawnPos, Quaternion.identity);
				break;
		}
	}
}