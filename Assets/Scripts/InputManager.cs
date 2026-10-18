using UnityEngine;
using System.Collections.Generic;

// [WHERE IT LIVES]: On the InputManager object on the main scene.
public class InputManager : MonoBehaviour
{
	public static InputManager Instance;

	[Header("Icons")]
	public GameObject invalidPlacementIcon;

	// feature/city-gen-v2: streets are now ~1-1.6m wide -- a finger-sized tap routinely misses the
	// exact street collider. Comfortably covers 3x the widest clamped street (CityGenConfig.roadWidth
	// is clamped to [1.0, 1.6]), so the per-candidate snapRadius (segment.Width * 3) computed inside
	// the OverlapSphere results is never larger than what this first broad-phase pass already covers.
	private const float BarricadeSnapSearchRadius = 5f;

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

	// feature/heli-rework (step 6): a plain tap (no card being dragged) on a Loading heli's own
	// collider leaves early with whoever's already aboard. Reuses this project's existing picking
	// pattern (Physics.Raycast off mainCam.ScreenPointToRay) -- the Helicopter prefab's root
	// already carries a non-trigger BoxCollider for exactly this kind of hit.
	private void Update()
	{
		if (isDragging) return;
		if (IsPaused) return;
		if (!Input.GetMouseButtonDown(0)) return;
		if (CardManager.Instance != null && CardManager.Instance.IsScreenPointOverCardsPanel(Input.mousePosition)) return;

		Ray ray = mainCam.ScreenPointToRay(Input.mousePosition);
		if (Physics.Raycast(ray, out RaycastHit hit))
		{
			HelicopterController heli = hit.collider.GetComponentInParent<HelicopterController>();
			heli?.RequestEarlyLeave();
		}
	}

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

			// feature/heli-rework (step 9): preview the ring at the actual snapped landing point,
			// not the raw tap, so the drag preview matches where the heli will really touch down.
			Vector3 ringCenter = hit.point;
			if (draggingCard.cardType == CardManager.CardType.Helicopter)
				TryResolveHelicopterLandingPoint(hit.point, out ringCenter);
			// feature/bait-rework (step 2): preview ring must use the snapped point + the final
			// (CardData + radiusMult + run modifiers) radius GetCardRadius already applies below.
			else if (draggingCard.cardType == CardManager.CardType.Bait)
				TryResolveBaitSnapPoint(hit.point, out ringCenter);
			else if (draggingCard.cardType == CardManager.CardType.Bomb
				&& (ComboTuning.Active == null || !ComboTuning.Active.legacyFeelEnabled))
				BombBlastUtil.TrySampleEpicenter(hit.point, out ringCenter);

			DrawRadiusCircle(ringCenter, radius);

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

			// feature/bomb-combo (step 4): friendly-fire preview, throttled to at most once per 0.1s.
			if (draggingCard.cardType == CardManager.CardType.Bomb)
				RefreshBombDangerPreview(ringCenter, radius);
			else
				ClearBombDangerPreview();
		}
		else
		{
			ClearBuildingOutline();
			ClearBombDangerPreview();
		}
	}

	public bool EndDragging()
	{
		if (!isDragging) return false;
		if (IsPaused) { isDragging = false; return false; }  // Block during pause

		isDragging = false;
		radiusCircle.enabled = false;
		ClearBombDangerPreview();

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

		// feature/sniper-feel (step 0/5): generated-city buildings (CityGenerator.CreateBspBuilding)
		// only ever get a MeshCollider, never a BoxCollider -- the old BoxCollider-only lookup made
		// this silently return null on every generated building, so outlinedBuilding/buildingCorners
		// were NEVER populated there and ExecuteSniperLogic's "if (outlinedBuilding == null) return
		// false" made Sniper placement a silent no-op on every sandbox/generated level. Collider.bounds
		// is identical to BoxCollider.bounds for a box (same AABB), so hand-built levels are unaffected.
		Collider col = building.GetComponent<Collider>();
		if (col == null)
		{
			ClearBuildingOutline();
			return;
		}

		Bounds b = col.bounds;
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

	// feature/bomb-combo (step 4): friendly-fire preview -- a small pool of red marker sprites, one
	// per endangered Human/Scientist, refreshed at most every bombPreviewRefreshInterval (no
	// per-frame allocations: the list only grows, markers are reused and just toggled inactive).
	private const float BombPreviewRefreshInterval = 0.1f;
	private readonly List<GameObject> bombDangerMarkers = new List<GameObject>();
	private float lastBombPreviewRefreshTime = -999f;
	private static Sprite dangerMarkerSprite;

	private void RefreshBombDangerPreview(Vector3 epicenter, float radius)
	{
		if (ComboTuning.Active != null && ComboTuning.Active.legacyFeelEnabled)
		{
			ClearBombDangerPreview();
			return;
		}

		if (Time.unscaledTime - lastBombPreviewRefreshTime < BombPreviewRefreshInterval) return;
		lastBombPreviewRefreshTime = Time.unscaledTime;

		bool epicenterSampled = BombBlastUtil.TrySampleEpicenter(epicenter, out Vector3 sampledEpicenter);
		float radiusSqr = radius * radius;
		int used = 0;

		for (int i = 0; i < Human.AllHumans.Count; i++)
		{
			Human h = Human.AllHumans[i];
			if (h == null) continue;
			used = TryMarkDanger(h.transform.position, epicenter, sampledEpicenter, epicenterSampled, radiusSqr, used);
		}

		for (int i = 0; i < Scientist.AllScientists.Count; i++)
		{
			Scientist s = Scientist.AllScientists[i];
			if (s == null) continue;
			used = TryMarkDanger(s.transform.position, epicenter, sampledEpicenter, epicenterSampled, radiusSqr, used);
		}

		for (int i = used; i < bombDangerMarkers.Count; i++)
			bombDangerMarkers[i].SetActive(false);
	}

	private int TryMarkDanger(Vector3 targetPos, Vector3 epicenter, Vector3 sampledEpicenter, bool epicenterSampled, float radiusSqr, int used)
	{
		if ((targetPos - epicenter).sqrMagnitude > radiusSqr) return used;
		if (!BombBlastUtil.HasStreetReach(sampledEpicenter, epicenterSampled, targetPos)) return used;

		GameObject marker = GetOrCreateBombDangerMarker(used);
		marker.transform.position = targetPos + Vector3.up * 2f;
		marker.SetActive(true);
		return used + 1;
	}

	private GameObject GetOrCreateBombDangerMarker(int index)
	{
		if (dangerMarkerSprite == null) dangerMarkerSprite = CreateDangerMarkerSprite();

		while (bombDangerMarkers.Count <= index)
		{
			GameObject marker = new GameObject("BombDangerMarker");
			SpriteRenderer sr = marker.AddComponent<SpriteRenderer>();
			sr.sprite = dangerMarkerSprite;
			sr.color = new Color(1f, 0.15f, 0.1f, 0.95f);
			sr.sortingOrder = 60;
			marker.AddComponent<Billboard>();
			marker.SetActive(false);
			bombDangerMarkers.Add(marker);
		}

		return bombDangerMarkers[index];
	}

	private static Sprite CreateDangerMarkerSprite()
	{
		const int size = 16;
		var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
		tex.wrapMode = TextureWrapMode.Clamp;
		float c = (size - 1) * 0.5f;

		for (int y = 0; y < size; y++)
		{
			for (int x = 0; x < size; x++)
			{
				float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
				float a = Mathf.Clamp01(1f - d);
				tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
			}
		}
		tex.Apply();

		return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
	}

	private void ClearBombDangerPreview()
	{
		for (int i = 0; i < bombDangerMarkers.Count; i++)
			if (bombDangerMarkers[i] != null) bombDangerMarkers[i].SetActive(false);
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
				// feature/bomb-combo (step 3): rejects exactly like any other invalid placement (no
				// Supply spent) when there's no NavMesh point within BombBlastUtil.SnapMaxDistance --
				// "Old feel" (ComboTuning.legacyFeelEnabled) skips this, matching original Bomb.cs
				// (no placement validation at all).
				if (ComboTuning.Active != null && ComboTuning.Active.legacyFeelEnabled) return true;
				return BombBlastUtil.TrySampleEpicenter(hit.point, out _);

			case CardManager.CardType.Bait:
				// feature/bait-rework (step 2): rejects exactly like any other invalid placement
				// (no Supply spent) when BaitTuning.requireNavMeshPoint is on and no street-grid
				// point is found within navSnapMaxDistance.
				return TryResolveBaitSnapPoint(hit.point, out _);

			case CardManager.CardType.Barricade:
			{
				StreetSegment segment = ResolveBarricadeSegment(hit, out bool legacy);
				if (legacy) return !isBuilding; // no StreetSegment data at all here -- old behavior, unchanged
				if (segment == null) return false; // no usable (non-intersection) segment within the snap radius

				// feature/barricade-feel (step 7): repairOnPlace off, or the dev "Old feel" toggle,
				// is byte-for-byte the pre-feature behavior -- any segment hit is valid, no same-street
				// overlap check (placing twice just stacks, as it always has).
				if (!RepairOnPlaceEnabled()) return true;

				Barricade existing = FindBarricadeOnSegment(segment);
				if (existing != null && existing.IsFullHealth) return false; // full HP on this street -- reject, no Supply spent
				return true;
			}

			case CardManager.CardType.Helicopter:
				// feature/heli-rework (step 8): same "not a building" rule as before, plus the
				// new snap-to-walkable requirement -- no walkable NavMesh point within
				// snapMaxDistance rejects the placement exactly like any other invalid spot.
				return !isBuilding && TryResolveHelicopterLandingPoint(hit.point, out _);

			default:
				return !isBuilding;
		}
	}

	// feature/heli-rework (step 8): snaps a raw tap/hit point to the nearest NavMesh point within
	// HeliTuning.snapMaxDistance (if snapToWalkable is on). Returns false (rejecting the
	// placement) when snapping is on but no walkable point was found in range.
	private bool TryResolveHelicopterLandingPoint(Vector3 rawPoint, out Vector3 resolved)
	{
		resolved = rawPoint;

		HeliTuning tuning = HeliTuning.Active;
		if (tuning == null || !tuning.snapToWalkable) return true;

		if (UnityEngine.AI.NavMesh.SamplePosition(rawPoint, out UnityEngine.AI.NavMeshHit navHit, tuning.snapMaxDistance, UnityEngine.AI.NavMesh.AllAreas))
		{
			resolved = navHit.position;
			return true;
		}

		return false;
	}

	// feature/bait-rework (step 2): same snap-helper shape as TryResolveHelicopterLandingPoint above,
	// reused for Bait -- BaitTuning.navSnapMaxDistance replaces the old hardcoded 6m search radius
	// (far too large for 1.2-1.6m streets). requireNavMeshPoint off keeps the pre-feature fallback
	// (place at the raw point even off-mesh).
	private bool TryResolveBaitSnapPoint(Vector3 rawPoint, out Vector3 resolved)
	{
		resolved = rawPoint;

		BaitTuning tuning = BaitTuning.Active;
		float snapDistance = tuning != null ? (tuning.legacyFeelEnabled ? 6f : tuning.navSnapMaxDistance) : 6f;
		// feature/card-scale (step 4): matches the same CardScale.RadiusMult Bait.cs itself applies
		// to navMeshSearchRadius, so this preview/reject check agrees with what Launch() will do.
		if (CardScale.ScaleHardcodedRanges) snapDistance *= CardScale.RadiusMult;
		bool require = tuning != null && !tuning.legacyFeelEnabled && tuning.requireNavMeshPoint;

		if (UnityEngine.AI.NavMesh.SamplePosition(rawPoint, out UnityEngine.AI.NavMeshHit navHit, snapDistance, UnityEngine.AI.NavMesh.AllAreas))
		{
			resolved = navHit.position;
			return true;
		}

		return !require;
	}

	// feature/city-gen-v2: resolves which StreetSegment a barricade placement should snap to.
	// Streets are narrow (~1-1.6m) -- a tap routinely lands on something other than the exact street
	// strip (an intersection tile a hair off, or the BaseGround/Building collider right next to it),
	// so this widens the search with an OverlapSphere around the tap instead of trusting only the
	// exact hit collider.
	//
	// legacy=true means the directly-hit collider carries no StreetSegment anywhere in its parent
	// chain at all -- i.e. this hit has nothing to do with the StreetSegment system (a hand-built
	// level whose RoadZone hasn't been touched/synced yet, a Building wall, etc.) -- callers should
	// fall back to the pre-existing behavior byte-for-byte rather than reject the placement.
	//
	// legacy=false with a null return means the opposite: the hit IS part of the system (confirmed
	// by the direct hit), but no usable (non-intersection) segment was found within its own
	// Width*3 snap radius -- callers must treat this as invalid placement.
	private StreetSegment ResolveBarricadeSegment(RaycastHit hit, out bool legacy)
	{
		legacy = false;
		if (hit.collider == null) { legacy = true; return null; }

		StreetSegment directHit = hit.collider.GetComponentInParent<StreetSegment>();
		if (directHit == null)
		{
			legacy = true;
			return null;
		}

		Collider[] nearby = Physics.OverlapSphere(hit.point, BarricadeSnapSearchRadius);
		StreetSegment best = null;
		float bestDistSqr = float.MaxValue;

		foreach (Collider col in nearby)
		{
			StreetSegment candidate = col.GetComponentInParent<StreetSegment>();
			if (candidate == null || candidate.IsIntersection) continue;

			float snapRadius = candidate.Width * 3f;
			float distSqr = (candidate.transform.position - hit.point).sqrMagnitude;
			if (distSqr > snapRadius * snapRadius) continue;
			if (distSqr < bestDistSqr)
			{
				bestDistSqr = distSqr;
				best = candidate;
			}
		}

		return best;
	}

	// feature/barricade-feel (step 7): true unless the dev "Old feel" toggle or repairOnPlace is off
	// -- gates the whole same-street overlap check so disabling it restores byte-for-byte old behavior.
	private bool RepairOnPlaceEnabled()
	{
		BarricadeFeelTuning tuning = BarricadeFeelTuning.Active;
		return tuning != null && !tuning.legacyFeelEnabled && tuning.repairOnPlace;
	}

	// feature/barricade-feel (step 7): reuses the existing AllBarricades roster (no new spatial
	// system) -- "the same street" means the same StreetSegment reference, stamped onto each
	// Barricade at spawn via SetSegment.
	private Barricade FindBarricadeOnSegment(StreetSegment segment)
	{
		if (segment == null) return null;

		foreach (Barricade b in Barricade.AllBarricades)
			if (b != null && !b.IsDead && b.Segment == segment)
				return b;

		return null;
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
			// feature/card-scale (step 1 audit): these fallback constants never pass through
			// CardData.GetCalculatedStat (the branch above already returns before reaching it), so
			// RadiusMult is applied explicitly here -- otherwise this degenerate "CardData has no
			// Radius stat" case would show a ring that doesn't match the real (scaled) radius.
			float fallback;
			switch (card.cardType)
			{
				case CardManager.CardType.Car: fallback = 4f; break;
				case CardManager.CardType.Soldier: fallback = 12f; break;
				case CardManager.CardType.Sniper: fallback = 25f; break;
				case CardManager.CardType.Helicopter: fallback = 12f; break;
				case CardManager.CardType.Bait: fallback = 15f; break;
				case CardManager.CardType.Barricade: return 0f; // 0 means "no ring", not a real range -- never scaled
				default: fallback = 5f; break;
			}

			radius = CardScale.ScaleHardcodedRanges ? fallback * CardScale.RadiusMult : fallback;
		}

		// feature/heli-rework (step 9): HelicopterController.Start() multiplies its attractRadius
		// by (1 + heli_radius_mult) at run time -- the drag-preview ring must reflect that SAME
		// final radius, not just the raw card stat/fallback, or it under-/over-states the real
		// attract radius whenever a roguelite run has this modifier active.
		if (card.cardType == CardManager.CardType.Helicopter)
		{
			var run = RunSessionData.Instance;
			if (run != null) radius *= (1f + run.GetModifier("heli_radius_mult"));
		}

		// feature/bait-rework (step 2): Bait.InitStatsFromCardData multiplies the CardData radius by
		// BaitTuning.radiusMult (unless the "Old feel" toggle is on) -- the drag-preview ring must
		// reflect that same final radius. No bait_range_mult roguelite modifier exists yet; add it
		// here alongside radiusMult if one ever does, same as heli_radius_mult above.
		if (card.cardType == CardManager.CardType.Bait)
		{
			BaitTuning tuning = BaitTuning.Active;
			if (tuning != null && !tuning.legacyFeelEnabled) radius *= tuning.radiusMult;
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
				// feature/heli-rework (step 8): land at the snapped point, not the raw tap --
				// CanPlaceCardAtScreenPoint already rejected this placement if none was found.
				TryResolveHelicopterLandingPoint(spawnPos, out spawnPos);

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
			{
				// feature/bomb-combo (step 3): snap to the same point CanPlaceCardAtScreenPoint
				// already validated. "Old feel" keeps dropping at the raw tap point.
				ComboTuning comboTuning = ComboTuning.Active;
				if (comboTuning == null || !comboTuning.legacyFeelEnabled)
					BombBlastUtil.TrySampleEpicenter(spawnPos, out spawnPos);

				spawnedObject = Instantiate(card.cardPrefab);
				spawnedObject.GetComponent<Bomb>().Launch(spawnPos);
				break;
			}

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
				StreetSegment segment = ResolveBarricadeSegment(hit, out bool legacy);

				// feature/barricade-feel (step 7): CanPlaceCardAtScreenPoint already guaranteed this
				// path is valid (either no same-street Barricade, or one below max HP) -- repair it
				// in place instead of spawning a duplicate on top of it.
				if (!legacy && segment != null && RepairOnPlaceEnabled())
				{
					Barricade existingBarricade = FindBarricadeOnSegment(segment);
					if (existingBarricade != null && !existingBarricade.IsFullHealth)
					{
						BarricadeFeelTuning tuning = BarricadeFeelTuning.Active;
						existingBarricade.Repair(Mathf.RoundToInt(tuning.repairFraction * existingBarricade.MaxHealth));
						Feedback.Play(FeedbackEvent.BarricadeRepair, existingBarricade.transform.position + Vector3.up * 0.5f);
						break;
					}
				}

				Quaternion barricadeRotation = Quaternion.identity;
				bool snapped = false;

				if (!legacy && segment != null)
				{
					Vector3 dir = segment.Direction;
					dir.y = 0f;
					if (dir.sqrMagnitude > 0.001f)
					{
						dir.Normalize();
						// Perpendicular to the travel direction, in the XZ plane -- purely a visual
						// facing now that the footprint is a square (see below), not something the
						// blocking coverage depends on.
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

				if (legacy && hit.collider != null)
				{
					// Old behavior, unchanged: no StreetSegment data anywhere near this hit (hand-built
					// level whose RoadZone hasn't been touched/synced, or a Building/other collider) --
					// orient from the road mesh's own transform like before.
					Vector3 roadDir = hit.collider.transform.forward;
					roadDir.y = 0f;
					if (roadDir.sqrMagnitude > 0.001f)
						barricadeRotation = Quaternion.LookRotation(roadDir.normalized) * Quaternion.Euler(0, 90, 0);
				}

				GameObject spawnedBarricade = Instantiate(card.cardPrefab, spawnPos, barricadeRotation);

				if (segment != null)
					spawnedBarricade.GetComponent<Barricade>()?.SetSegment(segment);

				if (snapped)
				{
					// AZS-style: a small square as wide as the street (+~5% overlap) so zombies
					// cannot walk around either end. Both the long (collider size.x) and the
					// originally-thin (size.z) authored sides scale independently to the same target
					// span. Barricade.Start() later stacks "barricade_width_mult" on top of this via
					// transform.localScale *= (uniform on X/Y/Z), which keeps X and Z equal to each
					// other (still square) and only ever grows the footprint further -- and since the
					// NavMeshObstacle's extents are authored proportionally to the same collider size,
					// scaling the transform this way keeps its carve matching the final square exactly.
					BoxCollider box = spawnedBarricade.GetComponent<BoxCollider>();
					if (box != null && box.size.x > 0.001f && box.size.z > 0.001f)
					{
						float targetSpan = segment.Width * 1.05f;
						float scaleX = targetSpan / box.size.x;
						float scaleZ = targetSpan / box.size.z;
						spawnedBarricade.transform.localScale = new Vector3(scaleX, 1f, scaleZ);
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