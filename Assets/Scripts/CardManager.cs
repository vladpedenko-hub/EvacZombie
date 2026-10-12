using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class CardManager : MonoBehaviour
{
	// Note: CardType enum is here so that upgrade definitions can reference specific card types
	public enum CardType { None, Helicopter, Soldier, Bait, Bomb, Car, Sniper, CombatHelicopter, Barricade };

	public static CardManager Instance;

	[Header("Where to spawn cards?")]
	public Transform cardsPanel;

	private void Awake() => Instance = this;

	// Real hit-test against the actual card-hand panel, used in place of a hardcoded
	// "bottom N% of screen is UI" assumption so this works on any aspect ratio / safe area.
	public bool IsScreenPointOverCardsPanel(Vector2 screenPoint)
	{
		RectTransform panelRect = cardsPanel as RectTransform;
		if (panelRect == null) return false;

		Canvas canvas = panelRect.GetComponentInParent<Canvas>();
		Camera uiCamera = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;
		return RectTransformUtility.RectangleContainsScreenPoint(panelRect, screenPoint, uiCamera);
	}

	private void Start()
	{
		SpawnDeck();
	}

	// Core Loop v2 (Docs/CORE_LOOP_V2.md Phase 4): injects a single card UI instance into the
	// hand without going through currentDeck/SpawnDeck -- used for the free, one-shot Night
	// "LAST TRANSPORT" card. uiPrefab's CardUI.myCardData is overridden to the given CardData.
	public CardUI SpawnTemporaryCard(CardData card, GameObject uiPrefab)
	{
		if (card == null || uiPrefab == null || cardsPanel == null) return null;

		GameObject go = Instantiate(uiPrefab, cardsPanel);
		SetEqualSlotWidth(go);
		CardUI ui = go.GetComponent<CardUI>();
		if (ui != null) ui.myCardData = card;
		return ui;
	}

	private void SpawnDeck()
	{
		// 1. Clear existing cards
		foreach (Transform child in cardsPanel)
		{
			Destroy(child.gameObject);
		}

		// Core Loop v2 HUD rebuild (Docs/CORE_LOOP_V2.md section 3.3): "Always 5 equal-width
		// slots spanning the full safe width." childControlWidth was off (cards kept their own
		// prefab width and bunched instead of spanning the row); flexibleWidth=1 on every slot
		// (set below) is what actually makes them equal, this just lets the layout group apply it.
		HorizontalLayoutGroup hlg = cardsPanel.GetComponent<HorizontalLayoutGroup>();
		if (hlg != null)
		{
			hlg.childControlWidth = true;
			hlg.childForceExpandWidth = true;
		}

		if (PlayerProfile.Instance == null)
		{
			Debug.LogError("PlayerProfile not found! Cannot spawn cards for the deck.");
			return;
		}

		// 2. Spawn cards from the player's deck (using CardData references). Always 5 slots --
		// an empty deck entry is a dimmed placeholder instead of being skipped, so owning fewer
		// than 5 cards no longer bunches the real ones instead of spanning the row.
		foreach (CardData card in PlayerProfile.Instance.currentDeck)
		{
			if (card != null && card.uiButtonPrefab != null)
			{
				GameObject go = Instantiate(card.uiButtonPrefab, cardsPanel);
				SetEqualSlotWidth(go);
			}
			else
			{
				if (card != null && card.uiButtonPrefab == null)
					Debug.LogWarning($"Card {card.cardName} has no UI button prefab assigned!");

				SpawnEmptySlot();
			}
		}
	}

	private void SetEqualSlotWidth(GameObject go)
	{
		LayoutElement le = go.GetComponent<LayoutElement>();
		if (le == null) le = go.AddComponent<LayoutElement>();
		le.flexibleWidth = 1f;
	}

	private void SpawnEmptySlot()
	{
		var go = new GameObject("EmptyCardSlot", typeof(RectTransform));
		go.transform.SetParent(cardsPanel, false);

		Image img = go.AddComponent<Image>();
		img.sprite = RuntimeUiKit.Rounded();
		img.type = Image.Type.Sliced;
		img.color = new Color(1f, 1f, 1f, 0.08f);
		img.raycastTarget = false;

		LayoutElement le = go.AddComponent<LayoutElement>();
		le.flexibleWidth = 1f;
	}
}