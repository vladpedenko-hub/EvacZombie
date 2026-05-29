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

	private CanvasGroup canvasGroup;
	private Transform originalParent;
	private int originalSiblingIndex;
	private Vector2 originalAnchoredPos;
	private RectTransform rectTransform;

	private bool isOnCooldown = false;
	private float currentCooldown = 0f;
	private bool isCurrentlyDragging = false;

	private void Awake()
	{
		canvasGroup = GetComponent<CanvasGroup>();
		rectTransform = GetComponent<RectTransform>();
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
		if (isOnCooldown || EnergyManager.Instance.CurrentEnergy < cost || myCardData == null) return;

		originalParent = transform.parent;
		originalSiblingIndex = transform.GetSiblingIndex();
		originalAnchoredPos = rectTransform.anchoredPosition;

		transform.SetParent(transform.root, false);
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

		transform.SetParent(originalParent, false);
		transform.SetSiblingIndex(originalSiblingIndex);
		rectTransform.anchoredPosition = originalAnchoredPos;

		canvasGroup.blocksRaycasts = true;
		if (cardImage != null) cardImage.color = Color.white;

		bool success = InputManager.Instance.EndDragging();

		if (success)
		{
			EnergyManager.Instance.TrySpendEnergy(cost);
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