using UnityEngine;
using System.Collections.Generic;

public class CardManager : MonoBehaviour
{
	// Оставляем CardType чисто для связи внутри префабов карточек (если он там используется)
	public enum CardType { None, Helicopter, Soldier, Bait, Bomb, Car, Sniper, CombatHelicopter };

	public static CardManager Instance;

	[Header("Куда спавнить карты?")]
	public Transform cardsPanel;

	private void Awake() => Instance = this;

	private void Start()
	{
		SpawnDeck();
	}

	private void SpawnDeck()
	{
		// 1. Очищаем панель
		foreach (Transform child in cardsPanel)
		{
			Destroy(child.gameObject);
		}

		if (PlayerProfile.Instance == null)
		{
			Debug.LogError("PlayerProfile не найден! Сначала запустите Главное Меню.");
			return;
		}

		// 2. Спавним карты из колоды (теперь мы перебираем CardData)
		foreach (CardData card in PlayerProfile.Instance.currentDeck)
		{
			if (card != null && card.uiButtonPrefab != null)
			{
				Instantiate(card.uiButtonPrefab, cardsPanel);
			}
			else if (card != null && card.uiButtonPrefab == null)
			{
				Debug.LogWarning($"У карты {card.cardName} не назначен UI префаб кнопки!");
			}
		}
	}
}