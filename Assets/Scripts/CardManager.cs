using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class CardManager : MonoBehaviour
{
	// Note: CardType enum is here so that upgrade definitions can reference specific card types
	public enum CardType { None, Helicopter, Soldier, Bait, Bomb, Car, Sniper, CombatHelicopter, Barricade };

	public static CardManager Instance;

	// feature/sandbox-level: when true, SpawnDeck() shows every card in SandboxAllCards instead
	// of the player's real PlayerProfile.currentDeck. Set/cleared only by SandboxDevPanel, and
	// only while the active level has LevelData.isSandboxLevel == true -- never true otherwise.
	public static bool SandboxAllCardsActive = false;
	public static List<CardData> SandboxAllCards = null;

	[Header("Where to spawn cards?")]
	public Transform cardsPanel;

	private void Awake() => Instance = this;

	// feature/sandbox-level: lets SandboxDevPanel force a rebuild after setting the statics
	// above, regardless of Awake/Start ordering between the two components.
	public void RefreshDeck() => SpawnDeck();

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

		// The hand shows only the cards the player actually owns, at their own authored width,
		// centered as a group -- not 5 force-expanded equal-width slots with dimmed placeholders
		// filling the gaps for an incomplete deck.
		HorizontalLayoutGroup hlg = cardsPanel.GetComponent<HorizontalLayoutGroup>();
		if (hlg != null)
		{
			hlg.childControlWidth = false;
			hlg.childForceExpandWidth = false;
			hlg.childAlignment = TextAnchor.MiddleCenter;
		}

		// feature/sandbox-level: all 8 cards, bypassing the player's real 5-slot deck entirely.
		if (SandboxAllCardsActive && SandboxAllCards != null)
		{
			foreach (CardData card in SandboxAllCards)
			{
				if (card != null && card.uiButtonPrefab != null)
					Instantiate(card.uiButtonPrefab, cardsPanel);
			}
			return;
		}

		if (PlayerProfile.Instance == null)
		{
			Debug.LogError("PlayerProfile not found! Cannot spawn cards for the deck.");
			return;
		}

		// 2. Spawn cards from the player's deck (using CardData references). An empty/misconfigured
		// deck entry is simply skipped -- no placeholder slot.
		foreach (CardData card in PlayerProfile.Instance.currentDeck)
		{
			if (card != null && card.uiButtonPrefab != null)
			{
				Instantiate(card.uiButtonPrefab, cardsPanel);
			}
			else if (card != null && card.uiButtonPrefab == null)
			{
				Debug.LogWarning($"Card {card.cardName} has no UI button prefab assigned!");
			}
		}
	}
}