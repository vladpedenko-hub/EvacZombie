using UnityEngine;
using UnityEngine.EventSystems;
using DG.Tweening;
using UnityEngine.UI;

// [WHERE IT LIVES]: On the TabsContainer object (child of Container/SafeArea).
public class MainMenuSwipeController : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
	public static bool IsSwipeLocked = false;

	[Header("Tabs")]
	[SerializeField] private RectTransform[] navButtons;
	[SerializeField] private RectTransform deckPanel;
	[SerializeField] private RectTransform mapPanel;
	[SerializeField] private RectTransform metaPanel;
	[SerializeField] private MapController mapController; // <-- Reference to MapPanel (MapController)

	[Header("Snap Settings (Horizontal)")]
	[SerializeField] private float snapDuration = 0.4f;
	[SerializeField] private float swipeThreshold = 0.15f;
	[Range(0.05f, 0.4f)]
	[SerializeField] private float elasticity = 0.25f;

	[Header("Vertical Swipe Detection Settings")]
	[SerializeField] private float minVerticalSwipe = 120f;
	[SerializeField] private float verticalDominance = 1.5f; // vertical must exceed X times horizontal to count as vertical

	private int currentTab = 1;               // 0 = Deck, 1 = Map, 2 = Meta
	private RectTransform rectTransform;
	private RectTransform parentRect;
	private RectTransform[] tabs;
	private float startPosition;

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

		rectTransform.DOKill();
		startPosition = rectTransform.anchoredPosition.x;

		// Close card popup when drag starts
		if (CardPopupManager.Instance != null)
			CardPopupManager.Instance.CloseContextMenu();
	}

	public void OnDrag(PointerEventData eventData)
	{
		if (!dragActive || IsSwipeLocked) return;

		Vector2 delta = eventData.position - dragStartPos;
		float absX = Mathf.Abs(delta.x);
		float absY = Mathf.Abs(delta.y);

		// 1) If vertical clearly dominates and we're on Map tab - do NOT move TabsContainer
		if (currentTab == 1 && absY > absX * verticalDominance && absY >= minVerticalSwipe * 0.3f)
		{
			// Do not drag tabs - handle vertical swipe in OnEndDrag
			return;
		}

		// 2) Otherwise treat as horizontal and drag TabsContainer
		float width = parentRect.rect.width;
		float dragDelta = delta.x;

		float maxX = width;
		float minX = -width;

		float targetX = startPosition + dragDelta;

		if (targetX > maxX)
		{
			float overshot = targetX - maxX;
			targetX = maxX + (overshot * elasticity);
			targetX = Mathf.Min(targetX, maxX + (width * 0.3f));
		}
		else if (targetX < minX)
		{
			float overshot = targetX - minX;
			targetX = minX + (overshot * elasticity);
			targetX = Mathf.Max(targetX, minX - (width * 0.3f));
		}

		rectTransform.anchoredPosition = new Vector2(targetX, rectTransform.anchoredPosition.y);
	}

	public void OnEndDrag(PointerEventData eventData)
	{
		if (!dragActive || IsSwipeLocked) return;
		dragActive = false;

		if (parentRect == null) return;

		Vector2 delta = eventData.position - dragStartPos;
		float absX = Mathf.Abs(delta.x);
		float absY = Mathf.Abs(delta.y);
		float width = parentRect.rect.width;

		// 1) Check for vertical region swipe (only when on the Map tab)
		bool isVerticalSwipe = currentTab == 1
							   && mapController != null
							   && absY >= minVerticalSwipe
							   && absY > absX * verticalDominance;

		if (isVerticalSwipe)
		{
			if (delta.y > 0f)
				mapController.TryPreviewPreviousRegion();
			else
				mapController.TryPreviewNextRegion();

			// Snap TabsContainer back so we stay on MapPanel
			GoToTab(currentTab, false);
			return;
		}

		// 2) Otherwise handle as horizontal swipe between panels
		float dragDelta = delta.x;

		if (Mathf.Abs(dragDelta) > width * swipeThreshold)
		{
			if (dragDelta > 0 && currentTab > 0) currentTab--;
			else if (dragDelta < 0 && currentTab < tabs.Length - 1) currentTab++;
		}

		GoToTab(currentTab, false);
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

		if (instant)
			rectTransform.anchoredPosition = new Vector2(targetX, rectTransform.anchoredPosition.y);
		else
			rectTransform.DOAnchorPosX(targetX, snapDuration).SetEase(Ease.OutBack, 0.6f);

		UpdateButtonsUI();
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