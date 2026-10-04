using UnityEngine;
using UnityEngine.EventSystems;
using DG.Tweening;
using UnityEngine.UI;

// [WHERE IT LIVES]: On the TabsContainer object (child of Container/SafeArea).
// Tabs (Deck / Map / Base) change only by tapping the bottom tab bar buttons (GoToTabFromButton).
// The only drag gesture left is the vertical region swipe on the Map tab, which belongs to the map.
public class MainMenuSwipeController : MonoBehaviour, IBeginDragHandler, IEndDragHandler
{
	public static bool IsSwipeLocked = false;

	[Header("Tabs")]
	[SerializeField] private RectTransform[] navButtons;
	[SerializeField] private MapController mapController; // <-- Reference to MapPanel (MapController)

	[Header("Tab Animation")]
	[SerializeField] private float snapDuration = 0.4f;

	[Header("Map Region Swipe (vertical)")]
	[SerializeField] private float minVerticalSwipe = 120f;
	[SerializeField] private float verticalDominance = 1.5f; // vertical must exceed X times horizontal to count as vertical

	private int currentTab = 1;               // 0 = Deck, 1 = Map, 2 = Meta

	// Raised whenever the tab settles on a new position (button or programmatic).
	public event System.Action<int> OnTabChanged;
	public int CurrentTab => currentTab;

	private RectTransform rectTransform;
	private RectTransform parentRect;
	private RectTransform[] tabs;

	private Vector2 dragStartPos;
	private bool dragActive = false;

	private void Start()
	{
		rectTransform = GetComponent<RectTransform>();
		parentRect = transform.parent.GetComponent<RectTransform>();

		// Collect all child tabs
		tabs = new RectTransform[transform.childCount];
		for (int i = 0; i < transform.childCount; i++)
		{
			tabs[i] = transform.GetChild(i).GetComponent<RectTransform>();
			tabs[i].anchorMin = Vector2.zero;
			tabs[i].anchorMax = Vector2.one;
			tabs[i].sizeDelta = Vector2.zero;
			tabs[i].localScale = Vector3.one;
		}

		AlignTabs();
		GoToTab(1, true); // MapPanel is in the center by default
	}

	private void AlignTabs()
	{
		float width = parentRect.rect.width;
		float overlapBuffer = 2f;

		for (int i = 0; i < tabs.Length; i++)
		{
			tabs[i].anchoredPosition = new Vector2((i - 1) * (width - overlapBuffer / 2f), 0);
		}
	}

	public void OnBeginDrag(PointerEventData eventData)
	{
		if (IsSwipeLocked) return;

		dragActive = true;
		dragStartPos = eventData.position;

		// Close card popup when drag starts
		if (CardPopupManager.Instance != null)
			CardPopupManager.Instance.CloseContextMenu();
	}

	public void OnEndDrag(PointerEventData eventData)
	{
		if (!dragActive || IsSwipeLocked) return;
		dragActive = false;

		Vector2 delta = eventData.position - dragStartPos;
		float absX = Mathf.Abs(delta.x);
		float absY = Mathf.Abs(delta.y);

		// Vertical region swipe, only on the Map tab. Tabs never move.
		bool isVerticalSwipe = currentTab == 1
							   && mapController != null
							   && absY >= minVerticalSwipe
							   && absY > absX * verticalDominance;

		if (!isVerticalSwipe) return;

		if (delta.y > 0f)
			mapController.TryPreviewPreviousRegion();
		else
			mapController.TryPreviewNextRegion();
	}

	public void GoToTabFromButton(int tabIndex)
	{
		if (IsSwipeLocked) return;

		if (CardPopupManager.Instance != null)
			CardPopupManager.Instance.CloseContextMenu();

		currentTab = Mathf.Clamp(tabIndex, 0, tabs.Length - 1);
		GoToTab(currentTab, false);
	}

	private void GoToTab(int tabIndex, bool instant)
	{
		if (parentRect == null) return;

		currentTab = Mathf.Clamp(tabIndex, 0, tabs.Length - 1);
		float width = parentRect.rect.width;
		float targetX = (1 - currentTab) * width;

		rectTransform.DOKill();
		if (instant)
			rectTransform.anchoredPosition = new Vector2(targetX, rectTransform.anchoredPosition.y);
		else
			rectTransform.DOAnchorPosX(targetX, snapDuration).SetEase(Ease.OutBack, 0.6f);

		UpdateButtonsUI();
		OnTabChanged?.Invoke(currentTab);
	}

	private void UpdateButtonsUI()
	{
		for (int i = 0; i < navButtons.Length; i++)
		{
			if (navButtons[i] == null) continue;

			Image btnImg = navButtons[i].GetComponent<Image>();
			if (btnImg != null)
				btnImg.color = (i == currentTab) ? Color.green : Color.gray;
		}
	}
}
