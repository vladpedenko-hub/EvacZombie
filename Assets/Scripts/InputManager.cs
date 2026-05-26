using UnityEngine;

// [��� �����]: �� ������ ������� (InputManager) �� ������ �����.
public class InputManager : MonoBehaviour
{
	public static InputManager Instance;

	[Header("������")]
	public GameObject invalidPlacementIcon;

	private Camera mainCam;
	private CardData draggingCard = null;
	private bool isDragging;
	private LineRenderer radiusCircle;

	// ��������� ������ ������
	private LineRenderer buildingOutline;
	private GameObject outlinedBuilding;
	private Vector3[] buildingCorners = new Vector3[4];

	private void Awake() => Instance = this;

	private void Start()
	{
		mainCam = Camera.main;

		// ������ ����
		GameObject circleObj = new GameObject("DragRadiusVisual");
		radiusCircle = circleObj.AddComponent<LineRenderer>();
		radiusCircle.startWidth = 0.2f;
		radiusCircle.endWidth = 0.2f;
		radiusCircle.material = new Material(Shader.Find("Sprites/Default"));
		radiusCircle.loop = true;
		radiusCircle.enabled = false;

		// ������ ������
		GameObject outlineObj = new GameObject("BuildingOutline");
		buildingOutline = outlineObj.AddComponent<LineRenderer>();
		buildingOutline.startWidth = 0.15f;
		buildingOutline.endWidth = 0.15f;
		buildingOutline.material = new Material(Shader.Find("Sprites/Default"));
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

			if (placed && GameManager.Instance.State == GameManager.GameState.Planning)
				GameManager.Instance.StartGame();

			// --- �����: ������� ��������� ��� �������� ---
			if (placed && TutorialManager.Instance != null)
			{
				TutorialManager.Instance.NextStep();
			}
			// ---------------------------------------------

			return placed;
		}

		if (Input.mousePosition.y < Screen.height * 0.25f)
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
			ClearBuildingOutline();

			if (GameManager.Instance.State == GameManager.GameState.Planning)
				GameManager.Instance.StartGame();

			// --- �����: ������� ��������� ��� ��������� ���� (��������, ����� � �.�.) ---
			if (TutorialManager.Instance != null)
			{
				TutorialManager.Instance.NextStep();
			}
			// --------------------------------------------------------------------------

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

		bool isUI = screenPos.y < Screen.height * 0.25f;
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
				// Только на объектах с тегом "BarricadeSurface"
				return hit.collider.CompareTag("BarricadeSurface");

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
				spawnedObject = Instantiate(card.cardPrefab);
				spawnedObject.GetComponent<HelicopterController>().Launch(spawnPos);
				break;

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
				// Ориентируем барикаду поперёк дороги по направлению mesh'а дороги
				Quaternion barricadeRotation = Quaternion.identity;
				if (hit.collider != null)
				{
					Vector3 roadDir = hit.collider.transform.forward;
					roadDir.y = 0f;
					if (roadDir.sqrMagnitude > 0.001f)
						barricadeRotation = Quaternion.LookRotation(roadDir.normalized) * Quaternion.Euler(0, 90, 0);
				}
				Instantiate(card.cardPrefab, spawnPos, barricadeRotation);
				break;
			}

			default:
				Instantiate(card.cardPrefab, spawnPos, Quaternion.identity);
				break;
		}
	}
}