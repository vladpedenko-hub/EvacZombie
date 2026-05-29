using UnityEngine;
using UnityEngine.UI;

// [WHERE IT LIVES]: On the DeckPanel object.
public class DeckMenuManager : MonoBehaviour
{
	[Header("Deck Slots")]
	[SerializeField] private MetaCardUI[] deckSlots; // Array of MetaCardUI slot references

	[Header("Inventory")]
	[SerializeField] private Transform inventoryPanel;
	[SerializeField] private Transform lockedCardsPanel;
	[SerializeField] private GameObject inventoryCardPrefab;

	private void Start()
	{
		RefreshUI();
	}

	private void OnEnable()
	{
		if (PlayerProfile.Instance != null) RefreshUI();
	}

	public void RefreshUI()
	{
		if (PlayerProfile.Instance == null) return;

		// 1. Fill deck slots with currently equipped cards
		for (int i = 0; i < deckSlots.Length; i++)
		{
			CardData cardInSlot = PlayerProfile.Instance.currentDeck[i];

			if (cardInSlot != null)
			{
				CardProgress prog = PlayerProfile.Instance.ownedCardsProgress.Find(p => p.cardId == cardInSlot.name);
				Transform slotTransform = deckSlots[i].transform;

				// Capture transform for the lambda callback
				deckSlots[i].Setup(cardInSlot, prog, () => CardPopupManager.Instance.OpenContextMenu(cardInSlot, prog, true, slotTransform));
			}
			else
			{
				// Empty slot - show empty state
				deckSlots[i].SetupEmpty();
			}
		}

		// 2. Clear existing cards in inventory
		foreach (Transform child in inventoryPanel) Destroy(child.gameObject);
		if (lockedCardsPanel != null)
		{
			foreach (Transform child in lockedCardsPanel) Destroy(child.gameObject);
		}

		// 3. Populate inventory with owned cards
		foreach (CardData cardData in PlayerProfile.Instance.allAvailableCards)
		{
			if (cardData == null) continue;

			CardProgress progress = PlayerProfile.Instance.ownedCardsProgress.Find(p => p.cardId == cardData.name);

			if (progress != null)
			{
				bool isEquipped = false;
				foreach (var deckCard in PlayerProfile.Instance.currentDeck)
				{
					if (deckCard != null && deckCard.name == cardData.name) isEquipped = true;
				}

				if (!isEquipped)
				{
					GameObject cardObj = Instantiate(inventoryCardPrefab, inventoryPanel);
					MetaCardUI cardUI = cardObj.GetComponent<MetaCardUI>();

					Transform cardTransform = cardObj.transform;
					cardUI.Setup(cardData, progress, () => CardPopupManager.Instance.OpenContextMenu(cardData, progress, false, cardTransform));
				}
			}
			else
			{
				if (lockedCardsPanel != null)
				{
					GameObject cardObj = Instantiate(inventoryCardPrefab, lockedCardsPanel);
					cardObj.GetComponent<MetaCardUI>().SetupLocked(cardData);
				}
			}
		}
	}
}