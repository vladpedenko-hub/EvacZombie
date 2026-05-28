using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

// Attached to the root object of the energy shortage popup (EnergyStorePopup)
public class EnergyStorePopupUI : MonoBehaviour
{
	[Header("Window")]
	[SerializeField] private GameObject root;
	[SerializeField] private Button closeButton;

	[Header("Timer")]
	[SerializeField] private TextMeshProUGUI timerText;

	[Header("Watch Ad Button")]
	[SerializeField] private Button watchAdButton;
	[SerializeField] private int energyPerAd = 10;

	[Header("Buy with Scientists (Hard Currency)")]
	[SerializeField] private Button buyWithHardButton;
	[SerializeField] private int hardCurrencyCost = 50;
	[SerializeField] private int energyPerBuy = 20;

	private void Awake()
	{
		// Wire up buttons
		if (closeButton != null) closeButton.onClick.AddListener(Hide);
		if (watchAdButton != null) watchAdButton.onClick.AddListener(OnWatchAdClicked);
		if (buyWithHardButton != null) buyWithHardButton.onClick.AddListener(OnBuyWithHardClicked);
	}

	private void Update()
	{
		// Update timer only when the window is open and energy is not full
		if (root != null && root.activeSelf)
		{
			if (PlayerProfile.Instance.currentEnergy < PlayerProfile.Instance.maxEnergy)
			{
				float secondsLeft = PlayerProfile.Instance.GetSecondsToNextEnergy();
				int minutes = Mathf.FloorToInt(secondsLeft / 60);
				int seconds = Mathf.FloorToInt(secondsLeft % 60);
				timerText.text = $"Next ⚡ in: {minutes:00}:{seconds:00}";
			}
			else
			{
				timerText.text = "Energy full!";
			}
		}
	}

	public void Show()
	{
		if (root != null) root.SetActive(true);
		RefreshUI();

		// TODO: Analytics event for popup open (example for GameAnalytics/Firebase)
		// AnalyticsManager.LogEvent("energy_popup_opened");
	}

	public void Hide()
	{
		if (root != null) root.SetActive(false);
	}

	private void RefreshUI()
	{
		// Enable/disable the hard currency buy button based on Scientist balance
		if (buyWithHardButton != null)
		{
			bool hasEnoughHard = PlayerProfile.Instance.totalScientistsCurrency >= hardCurrencyCost;
			buyWithHardButton.interactable = hasEnoughHard;
		}
	}

	private void OnBuyWithHardClicked()
	{
		if (PlayerProfile.Instance.totalScientistsCurrency >= hardCurrencyCost)
		{
			// Deduct scientists, grant energy
			PlayerProfile.Instance.totalScientistsCurrency -= hardCurrencyCost;
			PlayerProfile.Instance.AddEnergy(energyPerBuy);

			// TODO: Analytics event for hard currency spend
			// AnalyticsManager.LogResourceSink("scientists", hardCurrencyCost, "buy_energy");

			RefreshUI();

			// If energy is now sufficient to play (or full) — auto-close the popup
			Hide();
		}
	}

	private void OnWatchAdClicked()
	{
		// TODO: Call ad SDK (AppLovin MAX or AdMob)
		// For MVP: show a stub, then grant the reward in the successful view callback.

		/* APPLOVIN MAX INTEGRATION EXAMPLE:
		if (MaxSdk.IsRewardedAdReady("YOUR_AD_UNIT_ID")) {
			MaxSdk.ShowRewardedAd("YOUR_AD_UNIT_ID");
			// The callback should live in a separate AdManager that calls the method below:
		}
		*/

		// FOR TESTING (No SDK yet — grant reward immediately on click):
		Debug.Log($"[Ads SDK Mock] Ad shown. Granting {energyPerAd} energy.");
		GrantAdReward();
	}

	// This method will be called from the ad SDK callback
	public void GrantAdReward()
	{
		PlayerProfile.Instance.AddEnergy(energyPerAd);

		// TODO: Analytics event for ad view
		// AnalyticsManager.LogEvent("rv_energy_watched");

		Hide();
	}
}