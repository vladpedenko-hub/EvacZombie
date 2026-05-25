using UnityEngine;
using UnityEngine.EventSystems;
using DG.Tweening;
using UnityEngine.UI;

// [ГДЕ ВИСИТ]: На объекте TabsContainer (внутри Container/SafeArea).
public class MainMenuSwipeController : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
	public static bool IsSwipeLocked = false;

	[Header("Ссылки")]
	[SerializeField] private RectTransform[] navButtons;
	[SerializeField] private RectTransform deckPanel;
	[SerializeField] private RectTransform mapPanel;
	[SerializeField] private RectTransform metaPanel;
	[SerializeField] private MapController mapController; // <-- сюда перетянешь MapPanel (MapController)

	[Header("Настройки пружины (горизонталь)")]
	[SerializeField] private float snapDuration = 0.4f;
	[SerializeField] private float swipeThreshold = 0.15f;
	[Range(0.05f, 0.4f)]
	[SerializeField] private float elasticity = 0.25f;

	[Header("Настройки вертикального свайпа карты")]
	[SerializeField] private float minVerticalSwipe = 120f;
	[SerializeField] private float verticalDominance = 1.5f; // вертикаль должна быть в X раз больше горизонтали

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

		// Собираем все вкладки
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
		GoToTab(1, true); // MapPanel по центру по умолчанию
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

		// Фикс: закрываем контекстное меню карт
		if (CardPopupManager.Instance != null)
			CardPopupManager.Instance.CloseContextMenu();
	}

	public void OnDrag(PointerEventData eventData)
	{
		if (!dragActive || IsSwipeLocked) return;

		Vector2 delta = eventData.position - dragStartPos;
		float absX = Mathf.Abs(delta.x);
		float absY = Mathf.Abs(delta.y);

		// 1) Если вертикаль явно доминирует и мы на вкладке карты — НЕ двигаем TabsContainer
		if (currentTab == 1 && absY > absX * verticalDominance && absY >= minVerticalSwipe * 0.3f)
		{
			// Ничего не двигаем – пусть решение примем в OnEndDrag (vertical swipe)
			return;
		}

		// 2) Иначе считаем жест горизонтальным и двигаем TabsContainer
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

		// 1) Проверяем вертикальный свайп карты (только когда мы на вкладке Map)
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

			// Позицию TabsContainer не меняем, остаёмся на MapPanel
			GoToTab(currentTab, false);
			return;
		}

		// 2) Иначе обрабатываем как горизонтальный свайп между панелями
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