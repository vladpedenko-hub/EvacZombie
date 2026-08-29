using UnityEngine;
using UnityEngine.UI;
using TMPro;

// [WHERE IT LIVES]: On the CardInfoPopup object (inside a Canvas).
public class CardInfoPopup : MonoBehaviour
{
	public static CardInfoPopup Instance;

	[Header("UI References")]
	[SerializeField] private GameObject windowPanel;
	[SerializeField] private Image cardIcon;
	[SerializeField] private TextMeshProUGUI cardNameText;
	[SerializeField] private TextMeshProUGUI descriptionText;
	[SerializeField] private TextMeshProUGUI levelText;
	[SerializeField] private TextMeshProUGUI statsText;
	[SerializeField] private Button upgradeBtn;
	[SerializeField] private TextMeshProUGUI upgradeCostText;
	[SerializeField] private Button closeBtn;
	[SerializeField] private Button backgroundCloseBtn;

	[Header("Rarity and Category")]
	[SerializeField] private TextMeshProUGUI rarityText;
	[SerializeField] private TextMeshProUGUI categoryText;

	[Header("Shard Progress Bar")]
	[SerializeField] private TextMeshProUGUI progressText;
	[SerializeField] private Image progressBarFill;
	[SerializeField] private Color progressColorEnough = Color.green;
	[SerializeField] private Color progressColorNotEnough = new Color(0.2f, 0.6f, 1f); // Blue

	[Header("Deck Manager")]
	[SerializeField] private DeckMenuManager deckManager;

	[Header("Upgrade Button Colors")]
	[SerializeField] private Color colorReady = new Color(0.2f, 0.8f, 0.2f);
	[SerializeField] private Color colorNoMoney = new Color(0.8f, 0.2f, 0.2f);
	[SerializeField] private Color colorNoShards = new Color(0.5f, 0.5f, 0.5f);

	private CardData currentData;
	private CardProgress currentProgress;

	private void Awake()
	{
		if (Instance == null) Instance = this;
		windowPanel.transform.parent.gameObject.SetActive(false);
		closeBtn.onClick.AddListener(Close);
		if (backgroundCloseBtn != null) backgroundCloseBtn.onClick.AddListener(Close);
	}

	public void Show(CardData data, CardProgress progress)
	{
		currentData = data;
		currentProgress = progress;

		cardIcon.sprite = data.icon;
		cardNameText.text = data.cardName;
		if (descriptionText != null) descriptionText.text = data.description;

		if (categoryText != null) categoryText.text = $"Type: {data.category}";

		if (rarityText != null)
		{
			rarityText.text = data.rarity.ToString();
			rarityText.color = CardVisuals.GetRarityColor(data.rarity);
		}

		int lvl = progress != null ? progress.currentLevel : 1;
		int shards = progress != null ? progress.collectedShards : 0;
		levelText.text = $"Level {lvl}";

		bool isMaxLevel = lvl >= data.maxLevel || data.upgradeCosts.Count == 0;

		// Build stats list
		statsText.text = "";
		foreach (var stat in data.stats)
		{
			float currentVal = stat.GetFloatValue(lvl);
			string valStr = currentVal % 1 == 0 ? currentVal.ToString("F0") : currentVal.ToString("F1");
			string statLine = $"{stat.statName}: {valStr}{stat.unitSuffix}";

			if (!isMaxLevel)
			{
				float nextVal = stat.GetFloatValue(lvl + 1);
				float diff = nextVal - currentVal;
				if (diff > 0)
				{
					string diffStr = diff % 1 == 0 ? diff.ToString("F0") : diff.ToString("F1");
					statLine += $" <color=#32CD32>+{diffStr}{stat.unitSuffix}</color>";
				}
			}
			statsText.text += statLine + "\n\n";
		}

		upgradeBtn.onClick.RemoveAllListeners();
		Image btnImage = upgradeBtn.GetComponent<Image>();

		if (!isMaxLevel)
		{
			int costIndex = Mathf.Clamp(lvl - 1, 0, data.upgradeCosts.Count - 1);
			int requiredShards = data.upgradeCosts[costIndex].duplicateCardsNeeded;
			int upgradeCost = data.upgradeCosts[costIndex].currencyCost;

			// --- Shard progress display ---
			if (progressText != null) progressText.text = $"{shards} / {requiredShards}";
			if (progressBarFill != null)
			{
				progressBarFill.fillAmount = Mathf.Clamp01((float)shards / requiredShards);
				progressBarFill.color = (shards >= requiredShards) ? progressColorEnough : progressColorNotEnough;
			}

			// --- Upgrade button state ---
			if (shards >= requiredShards)
			{
				if (PlayerProfile.Instance.totalCurrency >= upgradeCost)
				{
					upgradeCostText.text = $"Upgrade ({upgradeCost}$)";
					btnImage.color = colorReady;
					upgradeBtn.interactable = true;
					upgradeBtn.onClick.AddListener(() => PerformUpgrade(upgradeCost, requiredShards));
				}
				else
				{
					upgradeCostText.text = $"Need {upgradeCost}$";
					btnImage.color = colorNoMoney;
					upgradeBtn.interactable = false;
				}
			}
			else
			{
				upgradeCostText.text = $"Collect shards";
				btnImage.color = colorNoShards;
				upgradeBtn.interactable = false;
			}
			upgradeBtn.gameObject.SetActive(true);
		}
		else
		{
			// --- Max level reached ---
			if (progressText != null) progressText.text = "MAX";
			if (progressBarFill != null)
			{
				progressBarFill.fillAmount = 1f;
				progressBarFill.color = Color.yellow;
			}
			upgradeBtn.gameObject.SetActive(false);
		}

		windowPanel.transform.parent.gameObject.SetActive(true);
	}

	private void PerformUpgrade(int cost, int shardsToSpend)
	{
		PlayerProfile.Instance.totalCurrency -= cost;
		currentProgress.collectedShards -= shardsToSpend;
		currentProgress.currentLevel++;

		PlayerProfile.Instance.SaveProfile();
		Show(currentData, currentProgress);
		if (deckManager != null) deckManager.RefreshUI();
	}

	public void Close()
	{
		windowPanel.transform.parent.gameObject.SetActive(false);
	}
}