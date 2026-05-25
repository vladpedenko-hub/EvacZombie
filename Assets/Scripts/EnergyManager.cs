using UnityEngine;
using System;

public class EnergyManager : MonoBehaviour
{
	public static EnergyManager Instance;

	[Header("Настройки Энергии")]
	public float maxEnergy = 10f;
	public float energyRegenPerSecond = 1f; // +1 мана в секунду
	public float startingEnergy = 3f; // Даем чуть-чуть маны на старте уровня

	public float CurrentEnergy { get; private set; }

	// Событие для обновления UI
	public Action<float, float> OnEnergyChanged;

	private void Awake()
	{
		Instance = this;
	}

	private void Start()
	{
		CurrentEnergy = startingEnergy;
		UpdateUI();
	}

	private void Update()
	{
		// Энергия копится только когда идет игра (или стадия планирования)
		if (GameManager.Instance.State == GameManager.GameState.Playing ||
			GameManager.Instance.State == GameManager.GameState.SuddenDeath ||
			GameManager.Instance.State == GameManager.GameState.Planning)
		{
			if (CurrentEnergy < maxEnergy)
			{
				CurrentEnergy += energyRegenPerSecond * Time.deltaTime;
				CurrentEnergy = Mathf.Clamp(CurrentEnergy, 0, maxEnergy);
				UpdateUI();
			}
		}
	}

	// Метод для покупки карты
	public bool TrySpendEnergy(float cost)
	{
		if (CurrentEnergy >= cost)
		{
			CurrentEnergy -= cost;
			UpdateUI();
			return true;
		}
		return false; // Не хватает маны!
	}

	private void UpdateUI()
	{
		OnEnergyChanged?.Invoke(CurrentEnergy, maxEnergy);
	}

	public void CheatFillEnergy()
	{
		CurrentEnergy = 100f; // Если у тебя есть переменная максимальной маны (типа maxEnergy), напиши её сюда вместо 100f

		// Дергаем событие, чтобы UI-полоска маны сразу обновилась на экране!
		OnEnergyChanged?.Invoke(CurrentEnergy, 100f);
	}

}