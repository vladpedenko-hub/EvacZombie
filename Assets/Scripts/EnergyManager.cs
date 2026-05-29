using UnityEngine;
using System;

public class EnergyManager : MonoBehaviour
{
	public static EnergyManager Instance;

	[Header("Energy Settings")]
	public float maxEnergy = 10f;
	public float energyRegenPerSecond = 1f; // +1 energy per second
	public float startingEnergy = 3f; // Energy at the start of each level

	public float CurrentEnergy { get; private set; }

	// Event for UI updates
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
		// Regenerate energy only while the game is in an active state (not during menus)
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

	// Try to spend energy; returns false if insufficient
	public bool TrySpendEnergy(float cost)
	{
		if (CurrentEnergy >= cost)
		{
			CurrentEnergy -= cost;
			UpdateUI();
			return true;
		}
		return false; // Not enough energy!
	}

	private void UpdateUI()
	{
		OnEnergyChanged?.Invoke(CurrentEnergy, maxEnergy);
	}

	public void CheatFillEnergy()
	{
		CurrentEnergy = 100f; // Note: intentionally ignores maxEnergy for cheat purposes

		// Invoke manually so UI widgets can display the cheat value correctly!
		OnEnergyChanged?.Invoke(CurrentEnergy, 100f);
	}

}