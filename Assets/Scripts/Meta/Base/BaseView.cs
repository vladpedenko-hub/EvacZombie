using System.Collections;
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

	// Raised when the research overlay opens or closes. The side dock hides itself while it is open.
	public event System.Action ResearchStateChanged;
	public bool IsResearchOpen => research != null && research.IsOpen;

	// Tutorial targets inside the research overlay (null while the overlay has not been built).
	public RectTransform ResearchNodeRect(int index) => research != null ? research.NodeRect(index) : null;
	public RectTransform ResearchBuyRect => research != null ? research.BuyRect : null;

	private MetaService service;
	private RectTransform content;
	private ScrollRect scroll;
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

		StartCoroutine(RouteOnEntry());
	}

	// MainMenu opens after the result popup is done, so this is the right moment to route.
	// One frame of delay lets MainMenuSwipeController.Start place the tabs first.
	private IEnumerator RouteOnEntry()
	{
		yield return null;
		if (tabs == null) yield break;

		string focusId = service.ConsumePendingFocus();
		bool introDue = !service.State.metaIntroSeen && service.State.highestClearedLevel >= 1;
		if (focusId == null && !introDue) yield break;

		if (introDue) service.MarkMetaIntroSeen();
		tabs.GoToTabFromButton(baseTabIndex);
		if (focusId != null) FocusHouse(focusId);
	}

	// Scrolls the city so the house is centered. Houses that need attention already pulse.
	private void FocusHouse(string buildingId)
	{
		if (!houses.TryGetValue(buildingId, out BaseHouseView house) || scroll == null) return;

		content.anchoredPosition = -house.Rect.anchoredPosition;
	}

	// Android Back (Escape): close the topmost overlay first. Research works from any tab, so it is checked first.
	// With nothing open, the key is left for the app.
	private void Update()
	{
		if (!Input.GetKeyDown(KeyCode.Escape)) return;

		if (IsResearchOpen) research.Close();
		else if (visible && panel != null && panel.IsOpen) panel.Hide();
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

	// Currency is listened to as well: People and Scientists change house and node availability,
	// and those changes do not raise MetaService.OnChanged.
	private void Subscribe()
	{
		if (service != null) service.OnChanged += Refresh;
		if (MetaRuntime.Currency != null) MetaRuntime.Currency.OnChanged += OnCurrencyChanged;
		if (tabs != null) tabs.OnTabChanged += OnTabChanged;
	}

	private void Unsubscribe()
	{
		if (service != null) service.OnChanged -= Refresh;
		if (MetaRuntime.Currency != null) MetaRuntime.Currency.OnChanged -= OnCurrencyChanged;
		if (tabs != null) tabs.OnTabChanged -= OnTabChanged;
	}

	private void OnCurrencyChanged(CurrencyType type, int value) => Refresh();

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
		// Full-width ground behind the viewport, so the reserved dock margin matches the city.
		MetaUI.ImageBox("CityBackground", self, new Color(0.24f, 0.3f, 0.22f), Vector2.zero, Vector2.one,
			Vector2.zero, Vector2.zero);

		// The right margin is reserved for the side dock, so houses are never hidden under its icon.
		RectTransform viewport = MetaUI.Rect("CityViewport", self,
			Vector2.zero, Vector2.one, new Vector2(0f, tabBarHeight), new Vector2(-SideDock.IconSize - SideDock.RightInset - 12f, 0f));
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
		scroll = viewport.gameObject.AddComponent<ScrollRect>();
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
		panel.OnOpenResearch += OpenResearchForHouse;

		// Research lives on its own overlay canvas, so it opens from any tab (the side dock).
		// Its bottom inset keeps the tab bar visible.
		Canvas researchCanvas = MetaUI.CreateOverlayCanvas("[MetaResearch]", 90); // under the tutorial canvas (100), so tutorial masks cover it
		research = gameObject.AddComponent<MetaSkillTreeScreen>();
		research.Build(researchCanvas.transform, service, MetaRuntime.Modifiers, tabBarHeight);
		research.Closed += () => ResearchStateChanged?.Invoke();

		BuildBadge();
	}

	private void OnHouseTapped(BuildingDefinition building)
	{
		panel.Show(building);
	}

	// The house sheet's "Open Research" button. Same path as the dock.
	private void OpenResearchForHouse(BuildingDefinition building)
	{
		OpenResearch(service.FindTreeForBuilding(building.id));
	}

	// The one code path that opens research. The house button and the side dock both call this.
	// Opening does not change tabs, so Back returns to whichever tab the player was on.
	public void OpenResearch(SkillTreeDefinition tree)
	{
		if (tree == null || research == null) return;

		panel.Hide();
		research.Open(tree);
		ResearchStateChanged?.Invoke();
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
