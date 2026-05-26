using UnityEngine;
using System.Collections.Generic;

public class CardManager : MonoBehaviour
{
	// ��������� CardType ����� ��� ����� ������ �������� �������� (���� �� ��� ������������)
	public enum CardType { None, Helicopter, Soldier, Bait, Bomb, Car, Sniper, CombatHelicopter, Barricade };

	public static CardManager Instance;

	[Header("���� �������� �����?")]
	public Transform cardsPanel;

	private void Awake() => Instance = this;

	private void Start()
	{
		SpawnDeck();
	}

	private void SpawnDeck()
	{
		// 1. ������� ������
		foreach (Transform child in cardsPanel)
		{
			Destroy(child.gameObject);
		}

		if (PlayerProfile.Instance == null)
		{
			Debug.LogError("PlayerProfile �� ������! ������� ��������� ������� ����.");
			return;
		}

		// 2. ������� ����� �� ������ (������ �� ���������� CardData)
		foreach (CardData card in PlayerProfile.Instance.currentDeck)
		{
			if (card != null && card.uiButtonPrefab != null)
			{
				Instantiate(card.uiButtonPrefab, cardsPanel);
			}
			else if (card != null && card.uiButtonPrefab == null)
			{
				Debug.LogWarning($"� ����� {card.cardName} �� �������� UI ������ ������!");
			}
		}
	}
}