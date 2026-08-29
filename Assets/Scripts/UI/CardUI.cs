using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

[RequireComponent(typeof(CanvasGroup))]
public class CardUI : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
	[Header("Card & Settings")]
	public CardData myCardData; // Always assign CARD DATA here, not an ENUM

	[Header("Gameplay")]
	public float cost;
	public float cooldownTime = 3f;

	[Header("Visuals")]
	public Image cardImage;
	public Image cooldownFill;
	public TextMeshProUGUI timerText;
	public Image rarityBorder; // NEW — assign in prefab

	private CanvasGroup canvasGroup;
	private RectTransform rectTransform;
	private LayoutElement layoutElement;
	private Canvas dragSortCanvas;

	private bool isOnCooldown = false;
	private float currentCooldown = 0f;
	private bool isCurrentlyDragging = false;

	private void Awake()
	{
		canvasGroup = GetComponent<CanvasGroup>();
		rectTransform = GetComponent<RectTransform>();

		layoutElement = GetComponent<LayoutElement>();
		if (layoutElement == null) layoutElement = gameObject.AddComponent<LayoutElement>();

		dragSortCanvas = GetComponent<Canvas>();
		if (dragSortCanvas == null) dragSortCanvas = gameObject.AddComponent<Canvas>();
		dragSortCanvas.overrideSorting = false; // off by default, only on while dragging

		if (GetComponent<GraphicRaycaster>() == null)
			gameObject.AddComponent<GraphicRaycaster>();
	}

	private void Start()
	{
		if (cooldownFill != null) cooldownFill.fillAmount = 0;
		if (timerText != null) timerText.text = "";

		// Initialization: if card data is assigned and the image field exists, set the icon
		if (myCardData != null && cardImage != null)
		{
			cardImage.sprite = myCardData.icon;
		}

		if (myCardData != null && rarityBorder != null)
		{
			rarityBorder.color = CardVisuals.GetRarityColor(myCardData.rarity);
		}
	}

	private void Update()
	{
		if (isOnCooldown)
		{
			currentCooldown -= Time.deltaTime;

			if (cooldownFill != null)
			{
				cooldownFill.gameObject.SetActive(true);
				cooldownFill.fillAmount = currentCooldown / cooldownTime;
			}

			if (timerText != null)
			{
				timerText.gameObject.SetActive(true);
				timerText.text = currentCooldown.ToString("F1");
			}

			if (currentCooldown <= 0)
			{
				isOnCooldown = false;
				if (cooldownFill != null) cooldownFill.fillAmount = 0;
				if (timerText != null) timerText.text = "";
				if (cardImage != null) cardImage.color = Color.white;
			}
		}
	}

	public void OnBeginDrag(PointerEventData eventData)
	{
		// Verify that CardData is properly assigned
		if (isOnCooldown || myCardData == null) return;

		// Exclude from the Layout Group's arrangement WITHOUT leaving cardsPanel —
		// this is what stops the other cards from reflowing.
		layoutElement.ignoreLayout = true;

		// Render above siblings/HUD without reparenting.
		dragSortCanvas.overrideSorting = true;
		dragSortCanvas.sortingOrder = 1000;

		canvasGroup.blocksRaycasts = false;
		if (cardImage != null) cardImage.color = new Color(1, 1, 1, 0.5f);

		isCurrentlyDragging = true;

		// Pass CARD DATA to INPUT MANAGER!
		InputManager.Instance.StartDragging(myCardData);
	}

	public void OnDrag(PointerEventData eventData)
	{
		if (!isCurrentlyDragging) return;

		rectTransform.position = eventData.position;
		InputManager.Instance.UpdateDragging(eventData.position);
	}

	public void OnEndDrag(PointerEventData eventData)
	{
		if (!isCurrentlyDragging) return;
		isCurrentlyDragging = false;

		layoutElement.ignoreLayout = false;
		dragSortCanvas.overrideSorting = false;

		canvasGroup.blocksRaycasts = true;
		if (cardImage != null) cardImage.color = Color.white;

		bool success = InputManager.Instance.EndDragging();

		if (success)
		{
			// EnergyManager.Instance.TrySpendEnergy(cost); ← убрано
			StartCooldown();
		}
	}

	public void StartCooldown()
	{
		if (!isOnCooldown)
		{
			isOnCooldown = true;
			currentCooldown = cooldownTime;
			if (cardImage != null) cardImage.color = Color.gray;
		}
	}
}
