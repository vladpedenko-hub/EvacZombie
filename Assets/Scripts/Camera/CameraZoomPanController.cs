using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections;
using System.Collections.Generic;

// Pinch-to-zoom / one-finger-drag-to-pan for the gameplay camera, plus the level-intro shot.
// Lives on the same GameObject as CameraController (the Main Camera) and is driven by it:
// CameraController.SetupCamera() still owns the existing fit-to-width math unchanged, then calls
// OnLevelCameraSetup() here at the end, which treats that result as the max-zoom-out limit (per
// the brief: "keep CameraFraming's result as the zoom-out limit, don't delete it").
//
// Zoom is expressed as a single "ground distance" (how far the camera sits from the y=0 ground
// plane along its forward axis -- the same quantity CameraFraming.GetGroundDistance already
// computes for the perspective pullback math), lerped between maxZoomOutGroundDistance (the
// fit-to-width result) and maxZoomInGroundDistance (derived from CameraReadabilityConfig so a
// Human reads at the configured on-screen size). Pan is a world-space XZ point the camera looks
// straight down at, clamped to a padded bounds rect built from spawn points + Building-tagged
// collider bounds + the level's initial human/scientist cluster.
[RequireComponent(typeof(UnityEngine.Camera))]
public class CameraZoomPanController : MonoBehaviour
{
	public static CameraZoomPanController Instance;

	private UnityEngine.Camera cam;

	// 0 = max zoom-out (fit-to-width), 1 = max zoom-in (Human-readability limit).
	private float zoomLevel;
	private Vector2 panCenterXZ;

	private float maxZoomOutGroundDistance;
	private float maxZoomInGroundDistance;
	private Vector2 zoomOutGroundHitXZ; // where the authored fit-to-width shot is centered
	private Rect panBoundsXZ;
	private bool boundsReady;

	// Touch classification: a finger that starts on the card hand / a card is never a pan,
	// for the lifetime of that touch, regardless of what's under it mid-drag later.
	private readonly Dictionary<int, bool> touchIsUiOwned = new Dictionary<int, bool>();
	private float prevPinchScreenDistance = -1f;
	private Vector2 prevMouseWorldDragScreenPos;
	private bool mouseDragActive;
	private bool mouseDragIsUiOwned;

	// Edge auto-pan while a card is being dragged -- fed once/frame from InputManager.UpdateDragging.
	private bool edgeAutoPanRequested;
	private Vector2 edgeAutoPanScreenPos;

	private Coroutine introRoutine;
	private bool introActive;

	// Core Loop v2 (Docs/CORE_LOOP_V2.md): read by PlanningControllerV2 to delay the no-Planning
	// auto-start until the level-intro camera shot finishes.
	public bool IsIntroActive => introActive;

	private void Awake()
	{
		Instance = this;
		cam = GetComponent<UnityEngine.Camera>();
	}

	private void OnDisable()
	{
		touchIsUiOwned.Clear();
		mouseDragActive = false;
	}

	// Called by CameraController right after it applies the existing fit-to-width framing.
	public void OnLevelCameraSetup(LevelData data)
	{
		if (introRoutine != null) { StopCoroutine(introRoutine); introRoutine = null; }

		float? groundDist = CameraFraming.GetGroundDistance(transform.position, transform.forward);
		maxZoomOutGroundDistance = groundDist ?? Mathf.Max(1f, transform.position.y);

		Vector3 groundHit = transform.position + transform.forward * maxZoomOutGroundDistance;
		zoomOutGroundHitXZ = new Vector2(groundHit.x, groundHit.z);

		CameraReadabilityConfig config = CameraReadabilityConfig.Active;
		maxZoomInGroundDistance = ComputeMaxZoomInGroundDistance(data, config);
		// Zoom-in must never end up farther than zoom-out (an extreme FOV/config combo could
		// invert this) -- guarantee a visible range either way.
		maxZoomInGroundDistance = Mathf.Min(maxZoomInGroundDistance, maxZoomOutGroundDistance * 0.95f);

		zoomLevel = 0f;
		panCenterXZ = zoomOutGroundHitXZ;
		boundsReady = false;
		panBoundsXZ = new Rect(zoomOutGroundHitXZ.x - 10f, zoomOutGroundHitXZ.y - 10f, 20f, 20f);

		touchIsUiOwned.Clear();
		mouseDragActive = false;
		prevPinchScreenDistance = -1f;

		ApplyToCamera(); // show the zoom-out framing immediately, before the deferred bounds/intro
		StartCoroutine(DeferredInit(config));
	}

	private static float ComputeMaxZoomInGroundDistance(LevelData data, CameraReadabilityConfig config)
	{
		if (config == null) return 4f; // conservative fallback if the config asset is ever missing

		float targetFraction = config.targetHumanScreenHeightMm / Mathf.Max(1f, config.referenceScreenHeightMm);
		float requiredVisibleWorldHeight = config.humanReferenceHeightWorldUnits / Mathf.Max(0.0001f, targetFraction);

		float fovDeg = (data != null && data.cameraType != LevelData.CameraType.Orthographic) ? data.cameraFieldOfView : 60f;
		float fovRad = Mathf.Max(1f, fovDeg) * Mathf.Deg2Rad;

		float dist = requiredVisibleWorldHeight / (2f * Mathf.Tan(fovRad * 0.5f));
		return Mathf.Max(0.5f, dist);
	}

	// Waits one frame so LoadLevel()'s SpawnHumans/SpawnScientists (called synchronously, later in
	// the same frame as SetupCamera -> OnLevelCameraSetup) have finished before we read their
	// positions. Gathers playable bounds independently (spawn points + Building-tagged colliders +
	// the initial human/scientist cluster) rather than depending on LevelManager's own private
	// list, so none of this touches LevelManager.cs.
	private IEnumerator DeferredInit(CameraReadabilityConfig config)
	{
		yield return null;

		float padding = config != null ? config.panBoundsPadding : 4f;
		panBoundsXZ = ComputePlayableBoundsXZ(padding);
		boundsReady = true;

		Vector2 clusterCenter = ComputeHumanClusterCenterXZ(zoomOutGroundHitXZ);
		Vector2 defaultPanCenter = ClampToBounds(clusterCenter);
		float defaultZoom = config != null ? config.defaultZoomFraction : 0.475f;

		introRoutine = StartCoroutine(IntroShotRoutine(config, defaultPanCenter, defaultZoom));
	}

	private Rect ComputePlayableBoundsXZ(float padding)
	{
		bool any = false;
		float minX = 0f, maxX = 0f, minZ = 0f, maxZ = 0f;

		void Encapsulate(Vector3 p)
		{
			if (!any) { minX = maxX = p.x; minZ = maxZ = p.z; any = true; return; }
			if (p.x < minX) minX = p.x;
			if (p.x > maxX) maxX = p.x;
			if (p.z < minZ) minZ = p.z;
			if (p.z > maxZ) maxZ = p.z;
		}

		foreach (SpawnPointMarker sp in UnityEngine.Object.FindObjectsByType<SpawnPointMarker>(FindObjectsSortMode.None))
			Encapsulate(sp.transform.position);

		foreach (GameObject go in GameObject.FindGameObjectsWithTag("Building"))
		{
			BoxCollider box = go.GetComponent<BoxCollider>();
			if (box == null) continue;
			Bounds b = box.bounds;
			Encapsulate(new Vector3(b.min.x, 0f, b.min.z));
			Encapsulate(new Vector3(b.max.x, 0f, b.max.z));
		}

		foreach (Human h in Human.AllHumans) if (h != null) Encapsulate(h.transform.position);
		foreach (Scientist s in Scientist.AllScientists) if (s != null) Encapsulate(s.transform.position);

		if (!any)
		{
			// No spawn points/buildings/actors found (shouldn't happen on a valid level) -- fall
			// back to a small area around the authored shot so pan still has *some* room.
			minX = zoomOutGroundHitXZ.x - 15f; maxX = zoomOutGroundHitXZ.x + 15f;
			minZ = zoomOutGroundHitXZ.y - 15f; maxZ = zoomOutGroundHitXZ.y + 15f;
		}

		return new Rect(minX - padding, minZ - padding, (maxX - minX) + 2f * padding, (maxZ - minZ) + 2f * padding);
	}

	private static Vector2 ComputeHumanClusterCenterXZ(Vector2 fallback)
	{
		Vector3 sum = Vector3.zero;
		int count = 0;

		foreach (Human h in Human.AllHumans) { if (h == null) continue; sum += h.transform.position; count++; }
		foreach (Scientist s in Scientist.AllScientists) { if (s == null) continue; sum += s.transform.position; count++; }

		if (count == 0) return fallback;
		Vector3 avg = sum / count;
		return new Vector2(avg.x, avg.z);
	}

	private Vector2 ClampToBounds(Vector2 point)
	{
		if (!boundsReady) return point;
		return new Vector2(
			Mathf.Clamp(point.x, panBoundsXZ.xMin, panBoundsXZ.xMax),
			Mathf.Clamp(point.y, panBoundsXZ.yMin, panBoundsXZ.yMax));
	}

	private IEnumerator IntroShotRoutine(CameraReadabilityConfig config, Vector2 defaultPanCenter, float defaultZoom)
	{
		introActive = true;

		float holdDuration = config != null ? config.introHoldDuration : 1.2f;
		float zoomDuration = Mathf.Max(0.01f, config != null ? config.introZoomDuration : 0.6f);
		bool skippable = config == null || config.introSkippable;

		zoomLevel = 0f;
		panCenterXZ = zoomOutGroundHitXZ;
		ApplyToCamera();

		float t = 0f;
		while (t < holdDuration)
		{
			if (skippable && TapRequestedThisFrame()) break;
			t += Time.deltaTime;
			yield return null;
		}

		Vector2 startPan = panCenterXZ;
		float startZoom = zoomLevel;
		t = 0f;
		while (t < zoomDuration)
		{
			if (skippable && TapRequestedThisFrame()) break;
			t += Time.deltaTime;
			float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / zoomDuration));
			zoomLevel = Mathf.Lerp(startZoom, defaultZoom, eased);
			panCenterXZ = Vector2.Lerp(startPan, defaultPanCenter, eased);
			ApplyToCamera();
			yield return null;
		}

		zoomLevel = defaultZoom;
		panCenterXZ = defaultPanCenter;
		ApplyToCamera();

		introActive = false;
		introRoutine = null;
	}

	private bool TapRequestedThisFrame()
	{
		if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began) return true;
		if (Input.GetMouseButtonDown(0)) return true;
		return false;
	}

	private void Update()
	{
		if (introActive) return; // the intro coroutine drives the camera on its own

		HandleTouchInput();
		HandleMouseInput();
		HandleEdgeAutoPan();

		zoomLevel = Mathf.Clamp01(zoomLevel);
		panCenterXZ = ClampToBounds(panCenterXZ);
		ApplyToCamera();
	}

	// Called once per frame (while dragging a card) from InputManager.UpdateDragging.
	public void NotifyCardDragScreenPosition(Vector2 screenPos)
	{
		edgeAutoPanRequested = true;
		edgeAutoPanScreenPos = screenPos;
	}

	private void HandleEdgeAutoPan()
	{
		if (!edgeAutoPanRequested) return;
		bool requested = edgeAutoPanRequested;
		Vector2 screenPos = edgeAutoPanScreenPos;
		edgeAutoPanRequested = false;
		if (!requested) return;

		CameraReadabilityConfig config = CameraReadabilityConfig.Active;
		float edgeFraction = config != null ? config.edgeAutoPanScreenFraction : 0.1f;
		float speed = config != null ? config.edgeAutoPanSpeed : 18f;

		float marginX = Screen.width * edgeFraction;
		float marginY = Screen.height * edgeFraction;

		Vector2 worldRight = WorldRightXZ();
		Vector2 worldUp = WorldUpXZ(); // screen-up ground direction for this camera's fixed pitch

		Vector2 delta = Vector2.zero;
		if (screenPos.x < marginX) delta -= worldRight;
		else if (screenPos.x > Screen.width - marginX) delta += worldRight;

		if (screenPos.y < marginY) delta -= worldUp;
		else if (screenPos.y > Screen.height - marginY) delta += worldUp;

		if (delta.sqrMagnitude > 0f)
			panCenterXZ += delta.normalized * speed * Time.deltaTime;
	}

	private void HandleTouchInput()
	{
		if (Input.touchCount == 0) return;

		for (int i = 0; i < Input.touchCount; i++)
		{
			Touch touch = Input.GetTouch(i);
			if (touch.phase == TouchPhase.Began)
				touchIsUiOwned[touch.fingerId] = IsScreenPointUiOwned(touch.position);
			else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
				touchIsUiOwned.Remove(touch.fingerId);
		}

		List<Touch> worldTouches = new List<Touch>(2);
		for (int i = 0; i < Input.touchCount && worldTouches.Count < 2; i++)
		{
			Touch touch = Input.GetTouch(i);
			if (touchIsUiOwned.TryGetValue(touch.fingerId, out bool uiOwned) && uiOwned) continue;
			worldTouches.Add(touch);
		}

		if (worldTouches.Count == 1)
		{
			prevPinchScreenDistance = -1f;
			Touch t = worldTouches[0];
			if (t.phase == TouchPhase.Moved)
				ApplyScreenPan(t.deltaPosition);
		}
		else if (worldTouches.Count == 2)
		{
			Vector2 p0 = worldTouches[0].position;
			Vector2 p1 = worldTouches[1].position;
			float dist = Vector2.Distance(p0, p1);

			if (prevPinchScreenDistance > 0f)
			{
				CameraReadabilityConfig config = CameraReadabilityConfig.Active;
				float denom = config != null ? Mathf.Max(1f, config.pinchDistanceForFullZoom) : 800f;
				zoomLevel += (dist - prevPinchScreenDistance) / denom;

				Vector2 prevMid = (p0 - worldTouches[0].deltaPosition + p1 - worldTouches[1].deltaPosition) * 0.5f;
				Vector2 curMid = (p0 + p1) * 0.5f;
				ApplyScreenPan(curMid - prevMid);
			}
			prevPinchScreenDistance = dist;
		}
		else
		{
			prevPinchScreenDistance = -1f;
		}
	}

	private void HandleMouseInput()
	{
		// Editor/desktop testing fallback -- Input.touchCount stays 0 without real touch hardware.
		if (Input.touchCount > 0) return;

		if (Input.GetMouseButtonDown(0))
		{
			mouseDragIsUiOwned = IsScreenPointUiOwned(Input.mousePosition);
			prevMouseWorldDragScreenPos = Input.mousePosition;
			mouseDragActive = !mouseDragIsUiOwned;
		}
		else if (Input.GetMouseButton(0) && mouseDragActive)
		{
			Vector2 current = Input.mousePosition;
			ApplyScreenPan(current - prevMouseWorldDragScreenPos);
			prevMouseWorldDragScreenPos = current;
		}
		else if (Input.GetMouseButtonUp(0))
		{
			mouseDragActive = false;
		}

		float scroll = Input.GetAxis("Mouse ScrollWheel");
		if (!Mathf.Approximately(scroll, 0f))
		{
			CameraReadabilityConfig config = CameraReadabilityConfig.Active;
			float speed = config != null ? config.scrollZoomSpeed : 0.08f;
			zoomLevel += scroll * speed * 10f;
		}
	}

	private bool IsScreenPointUiOwned(Vector2 screenPos)
	{
		if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return true;
		if (CardManager.Instance != null && CardManager.Instance.IsScreenPointOverCardsPanel(screenPos)) return true;
		return false;
	}

	// Converts a screen-space drag delta into a world-space XZ pan, using the ground-plane scale
	// at the CURRENT zoom level so 1 finger-pixel keeps tracking roughly the same ground point
	// regardless of how zoomed in/out the camera currently is.
	private void ApplyScreenPan(Vector2 screenDelta)
	{
		if (screenDelta.sqrMagnitude <= 0f) return;

		CameraReadabilityConfig config = CameraReadabilityConfig.Active;
		float sensitivity = config != null ? config.panSensitivity : 1f;

		float groundDistance = Mathf.Lerp(maxZoomOutGroundDistance, maxZoomInGroundDistance, zoomLevel);
		float fovDeg = cam.orthographic ? 0f : cam.fieldOfView;
		float worldPerPixelY = cam.orthographic
			? (2f * cam.orthographicSize) / Mathf.Max(1f, Screen.height)
			: (2f * groundDistance * Mathf.Tan(fovDeg * 0.5f * Mathf.Deg2Rad)) / Mathf.Max(1f, Screen.height);

		Vector2 worldRight = WorldRightXZ();
		Vector2 worldUp = WorldUpXZ();

		Vector2 worldDelta = (worldRight * screenDelta.x + worldUp * screenDelta.y) * worldPerPixelY * sensitivity;
		panCenterXZ -= worldDelta; // drag the world under the finger, not the camera's look point
	}

	// Ground-plane projections of the camera's local right/up axes, for converting screen-space
	// drags into world-space XZ pan regardless of the level's authored camera rotation.
	private Vector2 WorldRightXZ()
	{
		Vector3 r = transform.right;
		Vector2 v = new Vector2(r.x, r.z);
		return v.sqrMagnitude > 0.0001f ? v.normalized : Vector2.right;
	}

	private Vector2 WorldUpXZ()
	{
		Vector3 u = transform.up;
		Vector2 v = new Vector2(u.x, u.z);
		return v.sqrMagnitude > 0.0001f ? v.normalized : Vector2.up;
	}

	private void ApplyToCamera()
	{
		if (cam == null) return;

		float groundDistance = Mathf.Lerp(maxZoomOutGroundDistance, maxZoomInGroundDistance, zoomLevel);

		if (cam.orthographic)
		{
			// Best-effort support for the debug 2D toggle / any future orthographic level data --
			// this project's levels are all perspective today (see Docs/CAMERA_READABILITY.md).
			float maxOrthoSize = cam.orthographicSize; // whatever CameraController last fit to
			float minOrthoSize = CameraReadabilityConfig.Active != null
				? CameraReadabilityConfig.Active.humanReferenceHeightWorldUnits /
					Mathf.Max(0.0001f, CameraReadabilityConfig.Active.targetHumanScreenHeightMm / Mathf.Max(1f, CameraReadabilityConfig.Active.referenceScreenHeightMm))
				: maxOrthoSize * 0.3f;
			cam.orthographicSize = Mathf.Lerp(maxOrthoSize, minOrthoSize, zoomLevel);
			transform.position = new Vector3(panCenterXZ.x, transform.position.y, panCenterXZ.y);
		}
		else
		{
			Vector3 groundTarget = new Vector3(panCenterXZ.x, 0f, panCenterXZ.y);
			transform.position = groundTarget - transform.forward * groundDistance;
		}
	}
}
