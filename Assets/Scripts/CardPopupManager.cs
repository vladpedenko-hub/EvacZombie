using UnityEngine;
using UnityEngine.UI;
using TMPro;

// [ГДЕ ВИСИТ]: На объекте CardPopupManager (в корне Canvas).
// [ЧТО НАСТРОИТЬ]: Закинь ссылки на панельку и кнопки из Шага 1.
// Не забудь перетащить ссылку на DeckPanelManager!
public class CardPopupManager : MonoBehaviour
{
	public static CardPopupManager Instance;

	[Header("Ссылки на UI")]
	[SerializeField] private GameObject contextMenuPanel;
	[SerializeField] private Button infoUpgradeBtn;
	[SerializeField] private TextMeshProUGUI infoUpgradeText;
	[SerializeField] private Button equipRemoveBtn;
	[SerializeField] private TextMeshProUGUI equipRemoveText;

	[Header("Связь с колодой")]
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

	// Вызываем этот метод при клике на любую карту
	public void OpenContextMenu(CardData data, CardProgress progress, bool inDeck, Transform cardTransform)
	{
		selectedCard = data;
		selectedProgress = progress;
		isCardInDeck = inDeck;

		// 1. Включаем меню ДО расчетов координат, иначе Unity может криво посчитать размеры
		contextMenuPanel.SetActive(true);

		RectTransform cardRect = cardTransform.GetComponent<RectTransform>();
		RectTransform menuRect = contextMenuPanel.GetComponent<RectTransform>();

		// 2. Делаем ширину меню точно такой же, как у карты (стиль Clash Royale)
		// Высоту (y) оставляем как есть, чтобы кнопки влезли
		menuRect.sizeDelta = new Vector2(cardRect.rect.width, menuRect.sizeDelta.y);

		// 3. Достаем мировые координаты 4-х углов карточки
		Vector3[] cardCorners = new Vector3[4];
		cardRect.GetWorldCorners(cardCorners);

		// Индексы Unity: 0 - нижний левый, 1 - верхний левый, 2 - верхний правый, 3 - нижний правый.
		// Нам нужен центр нижней грани: складываем нижние углы и делим пополам.
		Vector3 bottomCenter = (cardCorners[0] + cardCorners[3]) / 2f;

		// 4. Ставим меню ровно под карту (спасибо нашему Pivot Y=1)
		// Добавим микро-отступ в 5 пикселей вверх (+Vector3.up * 5), чтобы оно прям визуально сливалось с картой
		menuRect.position = bottomCenter + (Vector3.up * 5f);

		// 5. Перерисовываем тексты и логику кнопок
		SetupButtons();
	}

	private void SetupButtons()
	{
		// 1. Сбрасываем старые подписки, чтобы кнопки не выполняли 10 действий за раз
		infoUpgradeBtn.onClick.RemoveAllListeners();
		equipRemoveBtn.onClick.RemoveAllListeners();

		// 2. Настраиваем кнопку "Use / Remove"
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

		// 3. Настраиваем кнопку "Info / Upgrade"
		int lvl = selectedProgress != null ? selectedProgress.currentLevel : 1;
		int shards = selectedProgress != null ? selectedProgress.collectedShards : 0;

		// Проверяем, не уперлись ли мы в максимальный уровень
		bool isMaxLevel = lvl >= selectedCard.maxLevel || selectedCard.upgradeCosts.Count == 0;

		if (!isMaxLevel)
		{
			// Берем требования для текущего уровня из твоего конфига
			int costIndex = Mathf.Clamp(lvl - 1, 0, selectedCard.upgradeCosts.Count - 1);
			int requiredShards = selectedCard.upgradeCosts[costIndex].duplicateCardsNeeded;
			int upgradeCost = selectedCard.upgradeCosts[costIndex].currencyCost;

			// Если хватает и карточек, и валюты:
			if (shards >= requiredShards && PlayerProfile.Instance.totalCurrency >= upgradeCost)
			{
				// Показываем цену прям на кнопке
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

		// Любой клик по кнопке закрывает меню
		infoUpgradeBtn.onClick.AddListener(() => contextMenuPanel.SetActive(false));
		equipRemoveBtn.onClick.AddListener(() => contextMenuPanel.SetActive(false));
	}

	// --- ЛОГИКА ДЕЙСТВИЙ ---

	private void EquipSelectedCard()
	{
		for (int i = 0; i < PlayerProfile.Instance.currentDeck.Length; i++)
		{
			if (PlayerProfile.Instance.currentDeck[i] == null)
			{
				PlayerProfile.Instance.currentDeck[i] = selectedCard;
				PlayerProfile.Instance.SaveProfile();
				deckManager.RefreshUI(); // Перерисовываем экран
				return;
			}
		}
		Debug.LogWarning("Колода заполнена! Сначала удали карту.");
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
		// Вместо того чтобы писать тут сложную логику списывания денег,
		// мы просто говорим скрипту: "А открой-ка нам большое окно с инфой!"
		OpenInfoPopup();
	}

	private void OpenInfoPopup()
	{
		// Прячем маленькое меню
		contextMenuPanel.SetActive(false);

		// Открываем большое окно!
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