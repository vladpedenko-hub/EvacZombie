using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

[RequireComponent(typeof(CanvasGroup))]
public class CardUI : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
	// Dev-only PlaytestLog's one unavoidable hook (Docs/CLAUDE_CODE_TASK_LevelProgression.md §7)
	// -- placement is a discrete, fast event polling could miss or double-count. No gameplay code
	// subscribes to this; it's purely observational.
	public static event System.Action<CardManager.CardType> OnCardPlaced;

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
	private float activeCooldownDuration = 3f;
	private bool isCurrentlyDragging = false;

	// Core Loop v2 (Docs/CORE_LOOP_V2.md Phase 1/2): built lazily in code the first time a v2
	// level is active, so legacy levels never pay for or show any of this.
	private Image v2CostBadgeBg;
	private TextMeshProUGUI v2CostBadgeText;
	private Image v2LockOverlay;

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
				cooldownFill.fillAmount = currentCooldown / activeCooldownDuration;
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
				Feedback.Play(FeedbackEvent.CardReady);
			}
		}

		UpdateV2State();
	}

	// Core Loop v2 (Docs/CORE_LOOP_V2.md Phase 1/2): Supply cost badge, unaffordable dim, and the
	// Planning-phase lock overlay for Evacuation-category cards. No-op (and nothing built) on
	// legacy levels.
	private void UpdateV2State()
	{
		if (myCardData == null) return;

		if (!CoreLoopV2.IsActive)
		{
			if (v2CostBadgeBg != null) v2CostBadgeBg.gameObject.SetActive(false);
			if (v2LockOverlay != null) v2LockOverlay.gameObject.SetActive(false);
			return;
		}

		BuildV2VisualsIfNeeded();

		bool hasCost = myCardData.supplyCost > 0 && !myCardData.isLastTransport;
		v2CostBadgeBg.gameObject.SetActive(hasCost);
		if (hasCost) v2CostBadgeText.text = myCardData.supplyCost.ToString();

		bool unaffordable = hasCost && SupplyManager.Instance != null && !SupplyManager.Instance.CanAfford(myCardData.supplyCost);
		if (cardImage != null && !isOnCooldown && !isCurrentlyDragging)
			cardImage.color = unaffordable ? new Color(1f, 1f, 1f, 0.35f) : Color.white;

		bool isPlanningLocked = IsLockedDuringPlanning();
		v2LockOverlay.gameObject.SetActive(isPlanningLocked);
	}

	private bool IsLockedDuringPlanning()
	{
		return myCardData != null
			&& myCardData.category == CardCategory.Evacuation
			&& !myCardData.isLastTransport
			&& GameManager.Instance != null
			&& GameManager.Instance.State == GameManager.GameState.Planning;
	}

	private void BuildV2VisualsIfNeeded()
	{
		if (v2CostBadgeBg != null) return;

		RectTransform badgeRt = RuntimeUiKit.CreateRect("SupplyCostBadge", transform);
		RuntimeUiKit.SetAnchors(badgeRt, 0.60f, 0.62f, 0.98f, 0.98f);
		v2CostBadgeBg = badgeRt.gameObject.AddComponent<Image>();
		v2CostBadgeBg.sprite = RuntimeUiKit.Rounded();
		v2CostBadgeBg.type = Image.Type.Sliced;
		v2CostBadgeBg.color = new Color(0.05f, 0.08f, 0.12f, 0.85f);
		v2CostBadgeBg.raycastTarget = false;

		v2CostBadgeText = RuntimeUiKit.CreateText("Text", badgeRt, "", 24f, new Color(0.65f, 1f, 0.75f, 1f), TextAlignmentOptions.Center, FontStyles.Bold);
		RuntimeUiKit.Stretch(v2CostBadgeText.rectTransform);
		v2CostBadgeBg.gameObject.SetActive(false);

		RectTransform lockRt = RuntimeUiKit.CreateRect("LockOverlay", transform);
		RuntimeUiKit.Stretch(lockRt);
		v2LockOverlay = lockRt.gameObject.AddComponent<Image>();
		v2LockOverlay.color = new Color(0f, 0f, 0f, 0.6f);
		v2LockOverlay.raycastTarget = false;
		TextMeshProUGUI lockLabel = RuntimeUiKit.CreateText("Text", lockRt, "LOCKED", 20f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
		RuntimeUiKit.Stretch(lockLabel.rectTransform);
		v2LockOverlay.gameObject.SetActive(false);
	}

	private void ShakeUnaffordable()
	{
		if (rectTransform == null) return;
		rectTransform.DOKill();
		rectTransform.DOShakeAnchorPos(0.3f, strength: 14f, vibrato: 14, randomness: 90f, snapping: false, fadeOut: true).SetUpdate(true);
	}

	public void OnBeginDrag(PointerEventData eventData)
	{
		// Verify that CardData is properly assigned
		if (isOnCooldown || myCardData == null) return;

		// Core Loop v2 (Docs/CORE_LOOP_V2.md Phase 1/2): Evacuation cards are locked during
		// Planning, and an unaffordable card can't be picked up at all.
		if (CoreLoopV2.IsActive)
		{
			if (IsLockedDuringPlanning())
			{
				Feedback.Play(FeedbackEvent.CardPlaceInvalid);
				return;
			}

			if (myCardData.supplyCost > 0 && !myCardData.isLastTransport
				&& SupplyManager.Instance != null && !SupplyManager.Instance.CanAfford(myCardData.supplyCost))
			{
				Feedback.Play(FeedbackEvent.CardPlaceInvalid);
				ShakeUnaffordable();
				return;
			}
		}

		// Exclude from the Layout Group's arrangement WITHOUT leaving cardsPanel —
		// this is what stops the other cards from reflowing.
		layoutElement.ignoreLayout = true;

		// Render above siblings/HUD without reparenting.
		dragSortCanvas.overrideSorting = true;
		dragSortCanvas.sortingOrder = 1000;

		canvasGroup.blocksRaycasts = false;
		if (cardImage != null) cardImage.color = new Color(1, 1, 1, 0.5f);

		isCurrentlyDragging = true;
		Feedback.Play(FeedbackEvent.CardPickUp);

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
			// Core Loop v2 (Docs/CORE_LOOP_V2.md Phase 1): Supply is spent only on a successful
			// placement, never on a rejected one.
			if (CoreLoopV2.IsActive && myCardData.supplyCost > 0 && !myCardData.isLastTransport)
				SupplyManager.Instance?.TrySpend(myCardData.supplyCost);

			StartCooldown();
			OnCardPlaced?.Invoke(myCardData.cardType);
		}
		else if (CardManager.Instance == null || !CardManager.Instance.IsScreenPointOverCardsPanel(Input.mousePosition))
		{
			// Released in the play area but placement was rejected (released in the card tray = plain cancel, stay silent)
			Feedback.Play(FeedbackEvent.CardPlaceInvalid);
		}
	}

	public void StartCooldown()
	{
		if (!isOnCooldown)
		{
			isOnCooldown = true;

			// Core Loop v2 (Docs/CORE_LOOP_V2.md Phase 1): a flat anti-double-tap cooldown
			// replaces each card's own prefab cooldownTime. Legacy levels are unaffected
			// (activeCooldownDuration == cooldownTime, identical to before this field existed).
			activeCooldownDuration = (CoreLoopV2.IsActive && myCardData != null && !myCardData.isLastTransport && CoreLoopV2Config.Active != null)
				? CoreLoopV2Config.Active.v2CardCooldown
				: cooldownTime;

			currentCooldown = activeCooldownDuration;
			if (cardImage != null) cardImage.color = Color.gray;
		}
	}
}
