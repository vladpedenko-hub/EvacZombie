using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

// Вешается на корневой объект попапа нехватки энергии (EnergyStorePopup)
public class EnergyStorePopupUI : MonoBehaviour
{
	[Header("Окно")]
	[SerializeField] private GameObject root;
	[SerializeField] private Button closeButton;

	[Header("Таймер")]
	[SerializeField] private TextMeshProUGUI timerText;

	[Header("Кнопка Рекламы")]
	[SerializeField] private Button watchAdButton;
	[SerializeField] private int energyPerAd = 10;

	[Header("Кнопка Покупки (Ученые)")]
	[SerializeField] private Button buyWithHardButton;
	[SerializeField] private int hardCurrencyCost = 50;
	[SerializeField] private int energyPerBuy = 20;

	private void Awake()
	{
		// Подписываем кнопки
		if (closeButton != null) closeButton.onClick.AddListener(Hide);
		if (watchAdButton != null) watchAdButton.onClick.AddListener(OnWatchAdClicked);
		if (buyWithHardButton != null) buyWithHardButton.onClick.AddListener(OnBuyWithHardClicked);
	}

	private void Update()
	{
		// Обновляем таймер только если окно открыто и энергия не полная
		if (root != null && root.activeSelf)
		{
			if (PlayerProfile.Instance.currentEnergy < PlayerProfile.Instance.maxEnergy)
			{
				float secondsLeft = PlayerProfile.Instance.GetSecondsToNextEnergy();
				int minutes = Mathf.FloorToInt(secondsLeft / 60);
				int seconds = Mathf.FloorToInt(secondsLeft % 60);
				timerText.text = $"До +1 ⚡: {minutes:00}:{seconds:00}";
			}
			else
			{
				timerText.text = "Энергия полная!";
			}
		}
	}

	public void Show()
	{
		if (root != null) root.SetActive(true);
		RefreshUI();

		// TODO: Аналитика открытия попапа (пример для GameAnalytics/Firebase)
		// AnalyticsManager.LogEvent("energy_popup_opened");
	}

	public void Hide()
	{
		if (root != null) root.SetActive(false);
	}

	private void RefreshUI()
	{
		// Включаем/выключаем кнопку покупки за харду в зависимости от баланса Ученых
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
			// Списываем ученых, начисляем энергию
			PlayerProfile.Instance.totalScientistsCurrency -= hardCurrencyCost;
			PlayerProfile.Instance.AddEnergy(energyPerBuy);

			// TODO: Аналитика траты харды
			// AnalyticsManager.LogResourceSink("scientists", hardCurrencyCost, "buy_energy");

			RefreshUI();

			// Если энергии теперь хватает на игру (или фулл) — можем автоматически закрыть окно
			Hide();
		}
	}

	private void OnWatchAdClicked()
	{
		// TODO: Вызов SDK рекламы (AppLovin MAX или AdMob)
		// В MVP делаем так: показываем заглушку, а в коллбеке успешного просмотра выдаем награду.

		/* ПРИМЕР ИНТЕГРАЦИИ APPLOVIN MAX:
		if (MaxSdk.IsRewardedAdReady("YOUR_AD_UNIT_ID")) {
			MaxSdk.ShowRewardedAd("YOUR_AD_UNIT_ID");
			// Обработчик должен висеть в отдельном AdManager, который вызовет метод ниже:
		}
		*/

		// ДЛЯ ТЕСТОВ (Пока нет SDK, выдаем награду сразу по клику):
		Debug.Log($"[Ads SDK Mock] Показали рекламу. Выдаем {energyPerAd} энергии.");
		GrantAdReward();
	}

	// Этот метод будем вызывать из коллбека SDK рекламы
	public void GrantAdReward()
	{
		PlayerProfile.Instance.AddEnergy(energyPerAd);

		// TODO: Аналитика просмотра рекламы
		// AnalyticsManager.LogEvent("rv_energy_watched");

		Hide();
	}
}