using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class LevelMissionPopupUI : MonoBehaviour
{
	[Header("Корень popup")]
	[SerializeField] private GameObject root;

	[Header("Тексты")]
	[SerializeField] private TextMeshProUGUI titleText;
	[SerializeField] private TextMeshProUGUI descriptionText;

	[Header("Иконка уровня")]
	[SerializeField] private Image missionIcon;

	[Header("Тексты условий звезд")]
	[SerializeField] private TextMeshProUGUI star1Text;
	[SerializeField] private TextMeshProUGUI star2Text;
	[SerializeField] private TextMeshProUGUI star3Text;

	[Header("Иконки звезд условий")]
	[SerializeField] private Image star1Icon;
	[SerializeField] private Image star2Icon;
	[SerializeField] private Image star3Icon;

	[Header("Спрайты звезд")]
	[SerializeField] private Sprite emptyStarSprite;
	[SerializeField] private Sprite filledStarSprite;

	[Header("First-time rewards")]
	[SerializeField] private GameObject rewardsRoot;
	[SerializeField] private TextMeshProUGUI softRewardText;
	[SerializeField] private Image lootboxIcon;
	[SerializeField] private GameObject lootboxBlock;

	[Header("Кнопки")]
	[SerializeField] private Button startButton;
	[SerializeField] private Button closeButton;

	[Header("Энергия")]
	[SerializeField] private int levelEnergyCost = 5;
	[SerializeField] private Image startButtonImage;
	[SerializeField] private TextMeshProUGUI energyCostText; // НОВОЕ: Сюда закинь текст на кнопке "Play" (например "5 ⚡")
	[SerializeField] private Color enoughEnergyColor = Color.green;
	[SerializeField] private Color notEnoughEnergyColor = Color.gray;
	[SerializeField] private EnergyStorePopupUI energyStorePopup;

	private int selectedRegionIndex;
	private int selectedLevelIndex;
	private LevelData selectedLevelData;

	private void Awake()
	{
		if (startButton != null)
		{
			startButton.onClick.RemoveAllListeners();
			startButton.onClick.AddListener(OnStartPressed);
		}

		if (closeButton != null)
		{
			closeButton.onClick.RemoveAllListeners();
			closeButton.onClick.AddListener(Hide);
		}
	}

	private void Start()
	{
		if (root == null) root = gameObject;
	}

	public void Show(int regionIndex, int levelIndex, LevelData levelData)
	{
		selectedRegionIndex = regionIndex;
		selectedLevelIndex = levelIndex;
		selectedLevelData = levelData;

		if (selectedLevelData == null) return;

		GameObject targetRoot = root != null ? root : gameObject;
		targetRoot.SetActive(true);

		// Визуал кнопки и цены энергии
		bool hasEnoughEnergy = PlayerProfile.Instance.currentEnergy >= levelEnergyCost;
		if (startButtonImage != null)
			startButtonImage.color = hasEnoughEnergy ? enoughEnergyColor : notEnoughEnergyColor;

		if (energyCostText != null)
			energyCostText.text = $"-{levelEnergyCost}"; // Покажет "-5"

		// Оригинальный код заполнения UI
		if (titleText != null) titleText.text = selectedLevelData.GetMissionTitle(regionIndex, levelIndex);
		if (descriptionText != null) descriptionText.text = selectedLevelData.missionDescription;

		if (missionIcon != null)
		{
			if (selectedLevelData.missionIcon != null)
			{
				missionIcon.sprite = selectedLevelData.missionIcon;
				missionIcon.gameObject.SetActive(true);
			}
			else missionIcon.gameObject.SetActive(false);
		}

		int savedStars = 0;
		if (PlayerProfile.Instance != null) savedStars = PlayerProfile.Instance.GetSavedStarsForLevel(selectedLevelData);

		if (star1Text != null) star1Text.text = $"Спасти {selectedLevelData.GetStar1Target()} людей";
		if (star2Text != null) star2Text.text = $"Спасти {selectedLevelData.GetStar2Target()} людей";
		if (star3Text != null) star3Text.text = selectedLevelData.star3Description;

		ApplyStarState(star1Icon, savedStars >= 1);
		ApplyStarState(star2Icon, savedStars >= 2);
		ApplyStarState(star3Icon, savedStars >= 3);

		string firstPassKey = "LevelPassed_" + selectedLevelData.name;
		bool passedBefore = PlayerPrefs.GetInt(firstPassKey, 0) == 1;

		if (rewardsRoot != null) rewardsRoot.SetActive(!passedBefore);
		if (softRewardText != null) softRewardText.text = selectedLevelData.currencyReward.ToString();
		if (lootboxBlock != null) lootboxBlock.SetActive(selectedLevelData.levelRewardLootbox != null && !passedBefore);

		if (lootboxIcon != null && selectedLevelData.levelRewardLootbox != null && selectedLevelData.levelRewardLootbox.boxIcon != null)
			lootboxIcon.sprite = selectedLevelData.levelRewardLootbox.boxIcon;
	}

	private void ApplyStarState(Image starIcon, bool isFilled)
	{
		if (starIcon == null) return;
		if (isFilled && filledStarSprite != null) starIcon.sprite = filledStarSprite;
		else if (!isFilled && emptyStarSprite != null) starIcon.sprite = emptyStarSprite;
	}

	private void OnStartPressed()
	{
		if (PlayerProfile.Instance.TryConsumeEnergy(levelEnergyCost))
		{
			PlayerPrefs.SetInt("SelectedLevelToPlay", selectedLevelIndex);
			SceneManager.LoadScene("Gameplay");
		}
		else
		{
			if (energyStorePopup != null)
				energyStorePopup.Show();
		}
	}

	public void Hide()
	{
		GameObject targetRoot = root != null ? root : gameObject;
		targetRoot.SetActive(false);
	}
}