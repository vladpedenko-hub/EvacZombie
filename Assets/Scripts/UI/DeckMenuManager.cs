using UnityEngine;
using UnityEngine.UI;

// [ГДЕ ВИСИТ]: На объекте DeckPanel.
public class DeckMenuManager : MonoBehaviour
{
	[Header("Колода")]
	[SerializeField] private MetaCardUI[] deckSlots; // ИЗМЕНЕНО: Теперь здесь MetaCardUI

	[Header("Инвентарь")]
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

		// 1. Рисуем активную колоду через наши полноценные префабы
		for (int i = 0; i < deckSlots.Length; i++)
		{
			CardData cardInSlot = PlayerProfile.Instance.currentDeck[i];

			if (cardInSlot != null)
			{
				CardProgress prog = PlayerProfile.Instance.ownedCardsProgress.Find(p => p.cardId == cardInSlot.name);
				Transform slotTransform = deckSlots[i].transform;

				// Настраиваем слот как полноценную карту
				deckSlots[i].Setup(cardInSlot, prog, () => CardPopupManager.Instance.OpenContextMenu(cardInSlot, prog, true, slotTransform));
			}
			else
			{
				// Если пусто - превращаем в серый слот
				deckSlots[i].SetupEmpty();
			}
		}

		// 2. Очищаем старые карты в инвентаре
		foreach (Transform child in inventoryPanel) Destroy(child.gameObject);
		if (lockedCardsPanel != null)
		{
			foreach (Transform child in lockedCardsPanel) Destroy(child.gameObject);
		}

		// 3. Рисуем карты в инвентаре
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