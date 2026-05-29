using UnityEngine;
using UnityEngine.UI;
using TMPro;

// [WHERE IT LIVES]: On the CardPopupManager object (inside a Canvas).
// [HOW IT WORKS]: A context menu pops up when a card is tapped in the collection UI.
// Do not confuse this with DeckPanelManager!
public class CardPopupManager : MonoBehaviour
{
	public static CardPopupManager Instance;

	[Header("UI References")]
	[SerializeField] private GameObject contextMenuPanel;
	[SerializeField] private Button infoUpgradeBtn;
	[SerializeField] private TextMeshProUGUI infoUpgradeText;
	[SerializeField] private Button equipRemoveBtn;
	[SerializeField] private TextMeshProUGUI equipRemoveText;

	[Header("Manager Reference")]
	[SerializeField] private DeckMenuManager deckManager;

	private CardData selectedCard;
	private CardProgress selectedProgress;
	private bool isCardInDeck;

	private void Awake()
	{
		if (Instance == null) Instance = this;
		else Destroy(gameObject);

		contextMenuPanel.SetActive(false);
	}

	// Open the context menu when a card is tapped in the collection
	public void OpenContextMenu(CardData data, CardProgress progress, bool inDeck, Transform cardTransform)
	{
		selectedCard = data;
		selectedProgress = progress;
		isCardInDeck = inDeck;

		// 1. Activate the panel so Unity can compute layout sizes
		contextMenuPanel.SetActive(true);

		RectTransform cardRect = cardTransform.GetComponent<RectTransform>();
		RectTransform menuRect = contextMenuPanel.GetComponent<RectTransform>();

		// 2. Match the menu width to the card width (similar to Clash Royale style)
		// Height (y) is left unchanged so the buttons fit
		menuRect.sizeDelta = new Vector2(cardRect.rect.width, menuRect.sizeDelta.y);

		// 3. Get all 4 world corners of the card
		Vector3[] cardCorners = new Vector3[4];
		cardRect.GetWorldCorners(cardCorners);

		// Unity corner order: 0 = bottom-left, 1 = top-left, 2 = top-right, 3 = bottom-right.
		// We want the bottom-center: average of the two bottom corners.
		Vector3 bottomCenter = (cardCorners[0] + cardCorners[3]) / 2f;

		// 4. Place the menu just below the card (Pivot Y=1 means top of the menu anchors here)
		// Shift slightly upward by 5 pixels so the menu overlaps the bottom edge of the card
		menuRect.position = bottomCenter + (Vector3.up * 5f);

		// 5. Configure button listeners and labels
		SetupButtons();
	}

	private void SetupButtons()
	{
		// 1. Clear listeners first to avoid duplicate calls accumulating over time
		infoUpgradeBtn.onClick.RemoveAllListeners();
		equipRemoveBtn.onClick.RemoveAllListeners();

		// 2. Configure the "Use / Remove" button
		if (isCardInDeck)
		{
			equipRemoveText.text = "Remove";
			equipRemoveBtn.onClick.AddListener(RemoveSelectedCard);
		}
		else
		{
			equipRemoveText.text = "Use";
			equipRemoveBtn.onClick.AddListener(EquipSelectedCard);
		}

		// 3. Configure the "Info / Upgrade" button
		int lvl = selectedProgress != null ? selectedProgress.currentLevel : 1;
		int shards = selectedProgress != null ? selectedProgress.collectedShards : 0;

		// Check whether the card is already at maximum level
		bool isMaxLevel = lvl >= selectedCard.maxLevel || selectedCard.upgradeCosts.Count == 0;

		if (!isMaxLevel)
		{
			// Get upgrade cost for the current level
			int costIndex = Mathf.Clamp(lvl - 1, 0, selectedCard.upgradeCosts.Count - 1);
			int requiredShards = selectedCard.upgradeCosts[costIndex].duplicateCardsNeeded;
			int upgradeCost = selectedCard.upgradeCosts[costIndex].currencyCost;

			// If enough shards and currency, show the upgrade button:
			if (shards >= requiredShards && PlayerProfile.Instance.totalCurrency >= upgradeCost)
			{
				// Show the upgrade cost on the button
				infoUpgradeText.text = $"Upgrade\n{upgradeCost}$";
				infoUpgradeBtn.onClick.AddListener(UpgradeSelectedCard);
			}
			else
			{
				infoUpgradeText.text = "Info";
				infoUpgradeBtn.onClick.AddListener(OpenInfoPopup);
			}
		}
		else
		{
			infoUpgradeText.text = "Info (MAX)";
			infoUpgradeBtn.onClick.AddListener(OpenInfoPopup);
		}

		// Always close the menu after either button is pressed
		infoUpgradeBtn.onClick.AddListener(() => contextMenuPanel.SetActive(false));
		equipRemoveBtn.onClick.AddListener(() => contextMenuPanel.SetActive(false));
	}

	// --- Action handlers ---

	private void EquipSelectedCard()
	{
		for (int i = 0; i < PlayerProfile.Instance.currentDeck.Length; i++)
		{
			if (PlayerProfile.Instance.currentDeck[i] == null)
			{
				PlayerProfile.Instance.currentDeck[i] = selectedCard;
				PlayerProfile.Instance.SaveProfile();
				deckManager.RefreshUI(); // Refresh deck UI
				return;
			}
		}
		Debug.LogWarning("Deck is full! Remove a card first.");
	}

	private void RemoveSelectedCard()
	{
		for (int i = 0; i < PlayerProfile.Instance.currentDeck.Length; i++)
		{
			if (PlayerProfile.Instance.currentDeck[i] != null && PlayerProfile.Instance.currentDeck[i].name == selectedCard.name)
			{
				PlayerProfile.Instance.currentDeck[i] = null;
				PlayerProfile.Instance.SaveProfile();
				deckManager.RefreshUI();
				return;
			}
		}
	}

	private void UpgradeSelectedCard()
	{
		// We could upgrade here directly, but it's cleaner to open the full info popup
		// so the player can see the upgrade details: "Oh, so I already had enough shards!"
		OpenInfoPopup();
	}

	private void OpenInfoPopup()
	{
		// Close the context menu first
		contextMenuPanel.SetActive(false);

		// Open the full card info popup
		CardInfoPopup.Instance.Show(selectedCard, selectedProgress);
	}
	public void CloseContextMenu()
	{
		if (contextMenuPanel != null)
		{
			contextMenuPanel.SetActive(false);
		}
	}
}