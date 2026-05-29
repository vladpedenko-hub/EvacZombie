using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Events;

// [WHERE IT LIVES]: On each MetaCardPrefab instance (in the Prefabs folder).
public class MetaCardUI : MonoBehaviour
{
	[Header("UI References")]
	[SerializeField] private Image cardIcon;
	[SerializeField] private TextMeshProUGUI levelText;
	[SerializeField] private TextMeshProUGUI progressText;
	[SerializeField] private Image progressBarFill;

	[Header("Progress Colors")]
	[SerializeField] private Color colorNotEnough = new Color(0.2f, 0.6f, 1f); // Blue
	[SerializeField] private Color colorEnough = Color.green; // Green

	private Button btn;
	private Image cardBackground;

	public void Setup(CardData data, CardProgress progress, UnityAction onClickAction)
	{
		if (btn == null) btn = GetComponent<Button>();
		if (cardBackground == null) cardBackground = GetComponent<Image>();

		// Restore card to normal state
		cardIcon.color = Color.white;
		cardBackground.color = Color.white;
		btn.interactable = true;

		cardIcon.sprite = data.icon;

		int lvl = progress != null ? progress.currentLevel : 1;
		int shards = progress != null ? progress.collectedShards : 0;
		levelText.text = $"Lvl {lvl}";

		bool isMaxLevel = lvl >= data.maxLevel || data.upgradeCosts.Count == 0;
		if (!isMaxLevel)
		{
			int costIndex = Mathf.Clamp(lvl - 1, 0, data.upgradeCosts.Count - 1);
			int required = data.upgradeCosts[costIndex].duplicateCardsNeeded;

			progressText.text = $"{shards}/{required}";
			progressBarFill.fillAmount = Mathf.Clamp01((float)shards / required);
			progressBarFill.color = (shards >= required) ? colorEnough : colorNotEnough;
		}
		else
		{
			progressText.text = "MAX";
			progressBarFill.fillAmount = 1f;
			progressBarFill.color = Color.yellow;
		}

		btn.onClick.RemoveAllListeners();
		btn.onClick.AddListener(onClickAction);
	}

	public void SetupLocked(CardData data)
	{
		if (btn == null) btn = GetComponent<Button>();
		if (cardBackground == null) cardBackground = GetComponent<Image>();

		cardIcon.sprite = data.icon;
		cardIcon.color = Color.black;
		cardBackground.color = new Color(0.5f, 0.5f, 0.5f, 0.8f);

		levelText.text = "???";
		progressText.text = "LOCKED";
		progressBarFill.fillAmount = 0;

		btn.interactable = false;
	}

	// --- Empty: shown when a deck slot is empty ---
	public void SetupEmpty()
	{
		if (btn == null) btn = GetComponent<Button>();
		if (cardBackground == null) cardBackground = GetComponent<Image>();

		cardIcon.sprite = null;
		cardIcon.color = new Color(0, 0, 0, 0f); // Fully transparent icon
		cardBackground.color = new Color(0, 0, 0, 0.3f); // Dim background for empty slot

		levelText.text = "";
		progressText.text = "";
		progressBarFill.fillAmount = 0;

		btn.interactable = false;
		btn.onClick.RemoveAllListeners();
	}
}