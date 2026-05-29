using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using UnityEngine.UI;

// [WHERE IT LIVES]: On the MenuManager object in the main menu scene
public class MenuManager : MonoBehaviour
{
	[Header("UI References")]
	[SerializeField] private TextMeshProUGUI globalCurrencyText;   // People (soft currency)
	[SerializeField] private TextMeshProUGUI scientistsCurrencyText; // Scientists (hard currency)

	[Header("UI Energy")]
	[SerializeField] private TextMeshProUGUI energyAmountText;
	[SerializeField] private TextMeshProUGUI energyTimerText;
	[SerializeField] private Slider energyProgressBar;

	[Header("Popup")]
	[SerializeField] private EnergyStorePopupUI energyStorePopup;

	[Header("Tutorial")]
	[Tooltip("Assign a TutorialSequence to play at the start of the main menu")]
	[SerializeField] private TutorialSequence mainMenuTutorial;

	private void Start()
	{
		// 1. Subscribe to profile update events
		if (PlayerProfile.Instance != null)
		{
			PlayerProfile.Instance.OnProfileUpdated += RefreshUI;
			PlayerProfile.Instance.OnEnergyUpdated += RefreshUI;
			RefreshUI();
		}

		// 2. Launch the main menu tutorial
		// Done in Start because PlayerProfile initializes in Awake.
		if (mainMenuTutorial != null && TutorialManager.Instance != null)
		{
			TutorialManager.Instance.StartTutorial(mainMenuTutorial);
		}
	}

	private void Update()
	{
		if (PlayerProfile.Instance != null && PlayerProfile.Instance.currentEnergy < PlayerProfile.Instance.maxEnergy)
		{
			if (energyTimerText != null)
			{
				float secondsLeft = PlayerProfile.Instance.GetSecondsToNextEnergy();
				int minutes = Mathf.FloorToInt(secondsLeft / 60);
				int seconds = Mathf.FloorToInt(secondsLeft % 60);
				energyTimerText.text = string.Format("{0:00}:{1:00}", minutes, seconds);
				if (!energyTimerText.gameObject.activeSelf) energyTimerText.gameObject.SetActive(true);
			}
		}
		else if (energyTimerText != null && energyTimerText.gameObject.activeSelf)
		{
			energyTimerText.gameObject.SetActive(false);
		}
	}

	private void OnDestroy()
	{
		if (PlayerProfile.Instance != null)
		{
			PlayerProfile.Instance.OnProfileUpdated -= RefreshUI;
			PlayerProfile.Instance.OnEnergyUpdated -= RefreshUI;
		}
	}

	private void RefreshUI()
	{
		if (PlayerProfile.Instance != null)
		{
			if (globalCurrencyText != null)
				globalCurrencyText.text = PlayerProfile.Instance.totalCurrency.ToString();

			if (scientistsCurrencyText != null)
				scientistsCurrencyText.text = PlayerProfile.Instance.totalScientistsCurrency.ToString();

			if (energyAmountText != null)
				energyAmountText.text = $"{PlayerProfile.Instance.currentEnergy}/{PlayerProfile.Instance.maxEnergy}";

			if (energyProgressBar != null)
			{
				energyProgressBar.maxValue = PlayerProfile.Instance.maxEnergy;
				energyProgressBar.value = PlayerProfile.Instance.currentEnergy;
			}
		}
	}

	public void PlayGame()
	{
		SceneManager.LoadScene("Gameplay");
	}

	public void OnEnergyUI_Clicked()
	{
		if (energyStorePopup != null)
		{
			energyStorePopup.Show();
		}
		else
		{
			Debug.LogWarning("Energy store popup is not assigned in MenuManager!");
		}
	}
}