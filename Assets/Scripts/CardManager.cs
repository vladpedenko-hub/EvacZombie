using UnityEngine;
using System.Collections.Generic;

public class CardManager : MonoBehaviour
{
	// Note: CardType enum is here so that upgrade definitions can reference specific card types
	public enum CardType { None, Helicopter, Soldier, Bait, Bomb, Car, Sniper, CombatHelicopter, Barricade };

	public static CardManager Instance;

	[Header("Where to spawn cards?")]
	public Transform cardsPanel;

	private void Awake() => Instance = this;

	private void Start()
	{
		SpawnDeck();
	}

	private void SpawnDeck()
	{
		// 1. Clear existing cards
		foreach (Transform child in cardsPanel)
		{
			Destroy(child.gameObject);
		}

		if (PlayerProfile.Instance == null)
		{
			Debug.LogError("PlayerProfile not found! Cannot spawn cards for the deck.");
			return;
		}

		// 2. Spawn cards from the player's deck (using CardData references)
		foreach (CardData card in PlayerProfile.Instance.currentDeck)
		{
			if (card != null && card.uiButtonPrefab != null)
			{
				Instantiate(card.uiButtonPrefab, cardsPanel);
			}
			else if (card != null && card.uiButtonPrefab == null)
			{
				Debug.LogWarning($"Card {card.cardName} has no UI button prefab assigned!");
			}
		}
	}
}