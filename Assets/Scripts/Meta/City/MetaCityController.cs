using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

// [WHERE IT LIVES]: root object of the MetaCity scene. Builds the city at runtime from MetaContentDatabase,
// so adding a building is a data change only.
public class MetaCityController : MonoBehaviour
{
	[SerializeField] private float cameraOrthoSize = 16f;
	[SerializeField] private float tapTolerancePixels = 14f;
	[SerializeField] private float cityMargin = 8f;

	private readonly Dictionary<string, MetaHouseView> houses = new Dictionary<string, MetaHouseView>();
	private MetaService service;
	private MetaCityCamera cityCamera;
	private MetaBuildingPanel panel;
	private MetaTopBar topBar;
	private Vector3 pressPosition;
	private bool pressOverUi;

	private void Start()
	{
		MetaRuntime.EnsureInitialized();
		service = MetaRuntime.Service;
		if (service == null)
		{
			Debug.LogError("[MetaCity] Meta data is unavailable. Start the game from MainMenu.");
			enabled = false;
			return;
		}

		EnsureEventSystem();

		Rect extents = ComputeExtents(service.Buildings);
		BuildGround(extents);

		cityCamera = Camera.main.gameObject.AddComponent<MetaCityCamera>();
		cityCamera.Configure(extents, cameraOrthoSize);

		foreach (BuildingDefinition building in service.Buildings)
		{
			if (building == null) continue;

			var go = new GameObject("House_" + building.id);
			go.transform.SetParent(transform, false);
			var view = go.AddComponent<MetaHouseView>();
			view.Initialize(building);
			houses[building.id] = view;
		}

		Canvas canvas = MetaUI.CreateOverlayCanvas("MetaCityCanvas", 10);
		RectTransform safeRoot = MetaUI.Rect("SafeRoot", canvas.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
		safeRoot.gameObject.AddComponent<SafeAreaFitter>();

		topBar = new MetaTopBar();
		topBar.Build(safeRoot);

		panel = gameObject.AddComponent<MetaBuildingPanel>();
		panel.Build(safeRoot, service);

		service.OnChanged += Refresh;
		Refresh();
	}

	private void OnDestroy()
	{
		if (service != null) service.OnChanged -= Refresh;
	}

	private void Update()
	{
		if (service == null) return;

		if (Input.GetMouseButtonDown(0))
		{
			pressPosition = Input.mousePosition;
			pressOverUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
		}

		if (Input.GetMouseButtonUp(0) && !pressOverUi &&
			Vector2.Distance(pressPosition, Input.mousePosition) <= tapTolerancePixels)
		{
			HandleTap(Input.mousePosition);
		}
	}

	private void HandleTap(Vector3 screenPosition)
	{
		Ray ray = cityCamera.Cam.ScreenPointToRay(screenPosition);
		if (Physics.Raycast(ray, out RaycastHit hit, 200f))
		{
			MetaHouseView house = hit.collider.GetComponentInParent<MetaHouseView>();
			if (house != null)
			{
				panel.Show(house.Building);
				return;
			}
		}

		panel.Hide();
	}

	private void Refresh()
	{
		foreach (KeyValuePair<string, MetaHouseView> entry in houses)
			entry.Value.Refresh(service.GetBuildingState(entry.Key));

		topBar.Refresh(MetaRuntime.Currency);
		if (panel.IsOpen) panel.Refresh();
	}

	private static void EnsureEventSystem()
	{
		if (FindAnyObjectByType<EventSystem>() != null) return;
		new GameObject("EventSystem", typeof(EventSystem), typeof(UnityEngine.EventSystems.StandaloneInputModule));
	}

	// Bounding box of all building slots, expanded by a margin. Used for the camera pan limits and the ground.
	private Rect ComputeExtents(IReadOnlyList<BuildingDefinition> buildings)
	{
		if (buildings.Count == 0) return new Rect(-10f, -10f, 20f, 20f);

		Vector2 min = buildings[0].worldPosition;
		Vector2 max = min;
		foreach (BuildingDefinition b in buildings)
		{
			min = Vector2.Min(min, b.worldPosition);
			max = Vector2.Max(max, b.worldPosition);
		}
		return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
	}

	private void BuildGround(Rect extents)
	{
		GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
		ground.name = "Ground";
		ground.transform.SetParent(transform, false);

		float width = extents.width + cityMargin * 2f;
		float depth = extents.height + cityMargin * 2f;
		ground.transform.localPosition = new Vector3(extents.center.x, 0f, extents.center.y);
		ground.transform.localScale = new Vector3(width / 10f, 1f, depth / 10f);
		ground.GetComponent<Renderer>().material.SetColor("_BaseColor", new Color(0.24f, 0.3f, 0.22f));
	}
}
