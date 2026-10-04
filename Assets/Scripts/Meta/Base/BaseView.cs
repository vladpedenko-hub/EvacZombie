using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The Base tab: a top-down city built from uGUI primitives inside the MainMenu Base panel.
// Houses come from MetaContentDatabase, so adding a house is a data change only.
// One instance lives in MainMenu (MetaPanel). Events are subscribed only while enabled.
public class BaseView : MonoBehaviour
{
	[Header("Tab wiring (set on the scene instance)")]
	[Tooltip("The tab controller. Found automatically when empty.")]
	[SerializeField] private MainMenuSwipeController tabs;
	[Tooltip("The Base tab button. Gets the attention badge.")]
	[SerializeField] private RectTransform baseTabButton;
	[SerializeField] private int baseTabIndex = 2;

	[Header("Layout (canvas pixels)")]
	[Tooltip("Height of the Deck / Map / Base bar at the bottom. The city and info sheet stay above it.")]
	[SerializeField] private float tabBarHeight = 200f;
	[Tooltip("Empty space around the houses, so the city can be panned past the outermost house.")]
	[SerializeField] private float cityMargin = 200f;
	[SerializeField] private float sheetGap = 24f;

	private MetaService service;
	private RectTransform content;
	private readonly Dictionary<string, BaseHouseView> houses = new Dictionary<string, BaseHouseView>();
	private MetaBuildingPanel panel;
	private MetaSkillTreeScreen research;
	private Image badge;
	private bool built;
	private bool visible;

	private void Start()
	{
		MetaRuntime.EnsureInitialized();
		service = MetaRuntime.Service;
		if (service == null)
		{
			Debug.LogError("[Base] Meta data is unavailable. The Base tab stays empty.");
			enabled = false;
			return;
		}

		if (tabs == null) tabs = FindAnyObjectByType<MainMenuSwipeController>();

		Build();
		built = true;

		Subscribe();
		SetVisible(tabs != null && tabs.CurrentTab == baseTabIndex);
		Refresh();
	}

	private void OnEnable()
	{
		if (built) Subscribe();
	}

	private void OnDisable()
	{
		Unsubscribe();
		SetVisible(false);
	}

	private void Subscribe()
	{
		if (service != null) service.OnChanged += Refresh;
		if (tabs != null) tabs.OnTabChanged += OnTabChanged;
	}

	private void Unsubscribe()
	{
		if (service != null) service.OnChanged -= Refresh;
		if (tabs != null) tabs.OnTabChanged -= OnTabChanged;
	}

	private void OnTabChanged(int tab) => SetVisible(tab == baseTabIndex);

	// Animations run only while the tab is shown. Leaving the tab stops the pulses; returning refreshes everything.
	private void SetVisible(bool value)
	{
		if (visible == value) return;
		visible = value;
		Refresh();
	}

	private void Refresh()
	{
		if (service == null || !built) return;

		foreach (KeyValuePair<string, BaseHouseView> entry in houses)
			entry.Value.Refresh(service.GetBuildingState(entry.Key), visible);

		if (panel.IsOpen) panel.Refresh();
		if (research.IsOpen) research.Refresh();

		if (badge != null) badge.gameObject.SetActive(service.HasAttentionItems());
	}

	private void Build()
	{
		RectTransform self = (RectTransform)transform;
		Rect extents = ComputeExtents(service.Buildings);
		Vector2 contentSize = extents.size + Vector2.one * cityMargin * 2f;

		// Viewport stops above the tab bar, so the city never slides under the buttons.
		RectTransform viewport = MetaUI.Rect("CityViewport", self,
			Vector2.zero, Vector2.one, new Vector2(0f, tabBarHeight), Vector2.zero);
		Image viewportImage = viewport.gameObject.AddComponent<Image>();
		viewportImage.color = Color.clear;
		viewport.gameObject.AddComponent<RectMask2D>();

		content = MetaUI.Rect("CityContent", viewport, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
			Vector2.zero, Vector2.zero);
		content.sizeDelta = contentSize;
		content.anchoredPosition = Vector2.zero;
		MetaUI.ImageBox("Ground", content, new Color(0.24f, 0.3f, 0.22f), Vector2.zero, Vector2.one,
			Vector2.zero, Vector2.zero);

		// Drag-pan only. Clamped movement stops at the content edge, so there is no overshoot.
		var scroll = viewport.gameObject.AddComponent<ScrollRect>();
		scroll.viewport = viewport;
		scroll.content = content;
		scroll.horizontal = true;
		scroll.vertical = true;
		scroll.movementType = ScrollRect.MovementType.Clamped;

		Vector2 center = extents.center;
		foreach (BuildingDefinition building in service.Buildings)
		{
			if (building == null) continue;

			var go = new GameObject("House_" + building.id, typeof(RectTransform));
			go.transform.SetParent(content, false);
			var house = go.AddComponent<BaseHouseView>();
			house.Initialize(building, building.cityPosition - center, OnHouseTapped);
			houses[building.id] = house;
		}

		panel = gameObject.AddComponent<MetaBuildingPanel>();
		panel.Build(self, service, tabBarHeight + sheetGap);
		panel.OnOpenResearch += OpenResearch;

		// Research is the top overlay, so it is built last.
		research = gameObject.AddComponent<MetaSkillTreeScreen>();
		research.Build(self, service, MetaRuntime.Modifiers);

		BuildBadge();
	}

	private void OnHouseTapped(BuildingDefinition building)
	{
		panel.Show(building);
	}

	private void OpenResearch(BuildingDefinition building)
	{
		SkillTreeDefinition tree = service.FindTreeForBuilding(building.id);
		if (tree == null) return;

		panel.Hide();
		research.Open(tree);
	}

	// A "!" on the Base tab button, so the player sees an action even when the Base panel is not open.
	private void BuildBadge()
	{
		if (baseTabButton == null)
		{
			Debug.LogWarning("[Base] Base tab button is not assigned; the attention badge is disabled.");
			return;
		}

		badge = MetaUI.ImageBox("AttentionBadge", baseTabButton, MetaUI.ButtonPrimary,
			new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-70f, -70f), new Vector2(-10f, -10f));
		badge.raycastTarget = false;
		MetaUI.Text("AttentionText", badge.transform, "!", 40, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
		badge.gameObject.SetActive(false);
	}

	// Bounding box of every house and its fog, in city pixels.
	private Rect ComputeExtents(IReadOnlyList<BuildingDefinition> buildings)
	{
		if (buildings == null || buildings.Count == 0) return new Rect(-500f, -500f, 1000f, 1000f);

		bool first = true;
		Vector2 min = Vector2.zero;
		Vector2 max = Vector2.zero;
		foreach (BuildingDefinition b in buildings)
		{
			if (b == null) continue;
			Vector2 half = Vector2.Max(b.cityLayoutSize * 0.5f, Vector2.one * b.fogRadius);
			Vector2 lo = b.cityPosition - half;
			Vector2 hi = b.cityPosition + half;
			if (first)
			{
				min = lo;
				max = hi;
				first = false;
			}
			else
			{
				min = Vector2.Min(min, lo);
				max = Vector2.Max(max, hi);
			}
		}

		return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
	}
}
