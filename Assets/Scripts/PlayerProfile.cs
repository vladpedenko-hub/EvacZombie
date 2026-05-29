using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;

public class PlayerProfile : MonoBehaviour
{
	public static PlayerProfile Instance;

	public Action OnProfileUpdated;
	public Action OnEnergyUpdated; // Event for UI energy widgets

	[Header("Currency")]
	public int totalCurrency = 0;
	public int totalScientistsCurrency = 0;

	[Header("Energy (Meta)")]
	public int currentEnergy = 20;
	public int maxEnergy = 20;
	public int secondsToRegenOneEnergy = 300; // 5 minutes per 1 energy
	private DateTime lastEnergyUpdateTime;

	[Header("Map Progress (Saga)")]
	public int currentRegionIndex = 0;
	public int currentLevelIndex = 0;
	public bool hasPendingMapAnimation = false;
	public bool hasPendingRegionAnimation = false;

	[Header("Pending Star Animation")]
	public bool hasPendingStarAnimation = false;
	public string pendingStarLevelId = "";
	public int pendingStarOldValue = 0;
	public int pendingStarNewValue = 0;

	[Header("Region Data")]
	public List<RegionConfig> allRegions = new List<RegionConfig>();

	[Header("Cards and Deck")]
	public List<CardData> allAvailableCards = new List<CardData>();
	public List<CardData> starterCards = new List<CardData>();
	public List<CardProgress> ownedCardsProgress = new List<CardProgress>();
	public CardData[] currentDeck = new CardData[5];

	[Header("Level Stars Progress")]
	public List<LevelStarsProgress> levelStarsProgress = new List<LevelStarsProgress>();

	[Header("Region Reward Claims")]
	public List<RegionRewardClaimState> regionRewardClaims = new List<RegionRewardClaimState>();

	private void Awake()
	{
		if (Instance == null)
		{
			Instance = this;
			DontDestroyOnLoad(gameObject);
			LoadProfile();
		}
		else Destroy(gameObject);
	}

	private void Update()
	{
		// Real-time energy regeneration
		if (currentEnergy < maxEnergy)
		{
			TimeSpan timePassed = DateTime.UtcNow - lastEnergyUpdateTime;
			if (timePassed.TotalSeconds >= secondsToRegenOneEnergy)
			{
				int energyToAdd = (int)(timePassed.TotalSeconds / secondsToRegenOneEnergy);
				currentEnergy = Mathf.Min(currentEnergy + energyToAdd, maxEnergy);
				lastEnergyUpdateTime = lastEnergyUpdateTime.AddSeconds(energyToAdd * secondsToRegenOneEnergy);

				SaveProfile();
				OnEnergyUpdated?.Invoke();
			}
		}
	}

	public void LoadProfile()
	{
		totalCurrency = PlayerPrefs.GetInt("TotalCurrency", 0);
		totalScientistsCurrency = PlayerPrefs.GetInt("TotalScientistsCurrency", 0);

		// Load energy
		currentEnergy = PlayerPrefs.GetInt("CurrentEnergy", maxEnergy);
		long tempTime = Convert.ToInt64(PlayerPrefs.GetString("LastEnergyTime", DateTime.UtcNow.ToBinary().ToString()));
		lastEnergyUpdateTime = DateTime.FromBinary(tempTime);
		RecalculateOfflineEnergy();

		currentRegionIndex = PlayerPrefs.GetInt("CurrentRegion", 0);
		currentLevelIndex = PlayerPrefs.GetInt("CurrentLevel", 0);
		hasPendingMapAnimation = PlayerPrefs.GetInt("PendingMapAnim", 0) == 1;
		hasPendingRegionAnimation = PlayerPrefs.GetInt("PendingRegionAnim", 0) == 1;

		hasPendingStarAnimation = PlayerPrefs.GetInt("PendingStarAnim", 0) == 1;
		pendingStarLevelId = PlayerPrefs.GetString("PendingStarLevelId", "");
		pendingStarOldValue = PlayerPrefs.GetInt("PendingStarOldValue", 0);
		pendingStarNewValue = PlayerPrefs.GetInt("PendingStarNewValue", 0);

		if (PlayerPrefs.HasKey("CardsProgress"))
		{
			string json = PlayerPrefs.GetString("CardsProgress");
			var wrapper = JsonUtility.FromJson<SerializationWrapper<CardProgress>>(json);
			if (wrapper != null && wrapper.target != null) ownedCardsProgress = wrapper.target;
		}

		if (PlayerPrefs.HasKey("LevelStarsProgress"))
		{
			string json = PlayerPrefs.GetString("LevelStarsProgress");
			var wrapper = JsonUtility.FromJson<SerializationWrapper<LevelStarsProgress>>(json);
			if (wrapper != null && wrapper.target != null) levelStarsProgress = wrapper.target;
		}

		if (PlayerPrefs.HasKey("RegionRewardClaims"))
		{
			string json = PlayerPrefs.GetString("RegionRewardClaims");
			var wrapper = JsonUtility.FromJson<SerializationWrapper<RegionRewardClaimState>>(json);
			if (wrapper != null && wrapper.target != null) regionRewardClaims = wrapper.target;
		}

		string deckStr = PlayerPrefs.GetString("CurrentDeck", "");
		if (!string.IsNullOrEmpty(deckStr))
		{
			string[] deckIds = deckStr.Split(',');
			for (int i = 0; i < 5; i++)
			{
				currentDeck[i] = (i < deckIds.Length && deckIds[i] != "None")
					? allAvailableCards.Find(c => c.name == deckIds[i])
					: null;
			}
		}
		else GiveStarterCards();
	}

	public void SaveProfile()
	{
		PlayerPrefs.SetInt("TotalCurrency", totalCurrency);
		PlayerPrefs.SetInt("TotalScientistsCurrency", totalScientistsCurrency);

		// Save energy
		PlayerPrefs.SetInt("CurrentEnergy", currentEnergy);
		PlayerPrefs.SetString("LastEnergyTime", lastEnergyUpdateTime.ToBinary().ToString());

		PlayerPrefs.SetInt("CurrentRegion", currentRegionIndex);
		PlayerPrefs.SetInt("CurrentLevel", currentLevelIndex);
		PlayerPrefs.SetInt("PendingMapAnim", hasPendingMapAnimation ? 1 : 0);
		PlayerPrefs.SetInt("PendingRegionAnim", hasPendingRegionAnimation ? 1 : 0);

		PlayerPrefs.SetInt("PendingStarAnim", hasPendingStarAnimation ? 1 : 0);
		PlayerPrefs.SetString("PendingStarLevelId", pendingStarLevelId);
		PlayerPrefs.SetInt("PendingStarOldValue", pendingStarOldValue);
		PlayerPrefs.SetInt("PendingStarNewValue", pendingStarNewValue);

		string progressJson = JsonUtility.ToJson(new SerializationWrapper<CardProgress>(ownedCardsProgress));
		PlayerPrefs.SetString("CardsProgress", progressJson);

		string starsJson = JsonUtility.ToJson(new SerializationWrapper<LevelStarsProgress>(levelStarsProgress));
		PlayerPrefs.SetString("LevelStarsProgress", starsJson);

		string rewardClaimsJson = JsonUtility.ToJson(new SerializationWrapper<RegionRewardClaimState>(regionRewardClaims));
		PlayerPrefs.SetString("RegionRewardClaims", rewardClaimsJson);

		string[] deckIds = new string[5];
		for (int i = 0; i < 5; i++)
			deckIds[i] = currentDeck[i] != null ? currentDeck[i].name : "None";

		PlayerPrefs.SetString("CurrentDeck", string.Join(",", deckIds));
		PlayerPrefs.Save();

		OnProfileUpdated?.Invoke();
	}

	// --- Helpers for offline regeneration ---
	private void RecalculateOfflineEnergy()
	{
		if (currentEnergy >= maxEnergy) return;

		TimeSpan timePassed = DateTime.UtcNow - lastEnergyUpdateTime;
		int energyToAdd = (int)(timePassed.TotalSeconds / secondsToRegenOneEnergy);

		if (energyToAdd > 0)
		{
			currentEnergy = Mathf.Min(currentEnergy + energyToAdd, maxEnergy);
			lastEnergyUpdateTime = lastEnergyUpdateTime.AddSeconds(energyToAdd * secondsToRegenOneEnergy);
			SaveProfile();
		}
	}

	public bool TryConsumeEnergy(int amount)
	{
		if (currentEnergy >= amount)
		{
			if (currentEnergy == maxEnergy)
				lastEnergyUpdateTime = DateTime.UtcNow;

			currentEnergy -= amount;
			SaveProfile();
			OnEnergyUpdated?.Invoke();
			return true;
		}
		return false;
	}

	public void AddEnergy(int amount)
	{
		currentEnergy = Mathf.Min(currentEnergy + amount, maxEnergy);
		SaveProfile();
		OnEnergyUpdated?.Invoke();
	}

	public float GetSecondsToNextEnergy()
	{
		if (currentEnergy >= maxEnergy) return 0;
		TimeSpan timePassed = DateTime.UtcNow - lastEnergyUpdateTime;
		return Mathf.Max(0, secondsToRegenOneEnergy - (float)timePassed.TotalSeconds);
	}

	// --- Saga map progression (level completion) ---
	public void CompleteCurrentLevel()
	{
		if (currentRegionIndex >= allRegions.Count) return;

		currentLevelIndex++;
		int levelsInCurrentRegion = allRegions[currentRegionIndex].levels.Count;

		if (currentLevelIndex >= levelsInCurrentRegion)
		{
			currentRegionIndex++;
			currentLevelIndex = 0;

			if (currentRegionIndex < allRegions.Count)
			{
				hasPendingRegionAnimation = true;
				hasPendingMapAnimation = false;
			}
			else
			{
				currentRegionIndex = allRegions.Count - 1;
				currentLevelIndex = levelsInCurrentRegion;
				hasPendingMapAnimation = true;
			}
		}
		else
		{
			hasPendingMapAnimation = true;
		}

		SaveProfile();
	}

	public void AddCardReward(CardData newCard)
	{
		if (newCard == null) return;

		CardProgress progress = ownedCardsProgress.Find(p => p.cardId == newCard.name);
		if (progress == null)
		{
			progress = new CardProgress(newCard.name);
			ownedCardsProgress.Add(progress);

			for (int i = 0; i < 5; i++)
			{
				if (currentDeck[i] == null)
				{
					currentDeck[i] = newCard;
					break;
				}
			}
		}
		else progress.collectedShards++;

		SaveProfile();
	}

	public int GetSavedStarsForLevel(string levelId)
	{
		LevelStarsProgress data = levelStarsProgress.Find(x => x.levelId == levelId);
		return data != null ? Mathf.Clamp(data.stars, 0, 3) : 0;
	}

	public int GetSavedStarsForLevel(LevelData levelData)
	{
		if (levelData == null) return 0;
		return GetSavedStarsForLevel(levelData.name);
	}

	public bool TrySetLevelStars(string levelId, int newStars, out int oldStars)
	{
		oldStars = 0;
		if (string.IsNullOrEmpty(levelId)) return false;

		newStars = Mathf.Clamp(newStars, 0, 3);

		LevelStarsProgress data = levelStarsProgress.Find(x => x.levelId == levelId);
		if (data == null)
		{
			data = new LevelStarsProgress
			{
				levelId = levelId,
				stars = newStars
			};
			levelStarsProgress.Add(data);
			oldStars = 0;
		}
		else
		{
			oldStars = Mathf.Clamp(data.stars, 0, 3);
			if (newStars <= oldStars)
				return false;

			data.stars = newStars;
		}

		hasPendingStarAnimation = true;
		pendingStarLevelId = levelId;
		pendingStarOldValue = oldStars;
		pendingStarNewValue = newStars;

		SaveProfile();
		return true;
	}

	public void ClearPendingStarAnimation()
	{
		hasPendingStarAnimation = false;
		pendingStarLevelId = "";
		pendingStarOldValue = 0;
		pendingStarNewValue = 0;
		SaveProfile();
	}

	public int GetRegionTotalEarnedStars(int regionIndex)
	{
		if (regionIndex < 0 || regionIndex >= allRegions.Count) return 0;

		int total = 0;
		var region = allRegions[regionIndex];
		if (region == null || region.levels == null) return 0;

		for (int i = 0; i < region.levels.Count; i++)
		{
			if (region.levels[i] == null) continue;
			total += GetSavedStarsForLevel(region.levels[i].name);
		}

		return total;
	}

	public int GetRegionMaxStars(int regionIndex)
	{
		if (regionIndex < 0 || regionIndex >= allRegions.Count) return 0;
		var region = allRegions[regionIndex];
		if (region == null || region.levels == null) return 0;

		return region.levels.Count * 3;
	}

	public float GetRegionProgress01(int regionIndex)
	{
		int max = GetRegionMaxStars(regionIndex);
		if (max <= 0) return 0f;

		int current = GetRegionTotalEarnedStars(regionIndex);
		return Mathf.Clamp01((float)current / max);
	}

	public bool IsRegionRewardClaimed(int regionIndex, int rewardIndex)
	{
		RegionRewardClaimState state = regionRewardClaims.Find(x => x.regionIndex == regionIndex);
		if (state == null) return false;

		return state.claimedRewardIndices.Contains(rewardIndex);
	}

	public void MarkRegionRewardClaimed(int regionIndex, int rewardIndex)
	{
		RegionRewardClaimState state = regionRewardClaims.Find(x => x.regionIndex == regionIndex);
		if (state == null)
		{
			state = new RegionRewardClaimState
			{
				regionIndex = regionIndex,
				claimedRewardIndices = new List<int>()
			};
			regionRewardClaims.Add(state);
		}

		if (!state.claimedRewardIndices.Contains(rewardIndex))
		{
			state.claimedRewardIndices.Add(rewardIndex);
			SaveProfile();
		}
	}

	private void GiveStarterCards()
	{
		foreach (CardData card in starterCards)
			if (card != null) AddCardReward(card);

		SaveProfile();
	}

	[ContextMenu("Reset All Progress (WARNING: deletes all saves)")]
	public void ResetProfile()
	{
		PlayerPrefs.DeleteAll();
		ownedCardsProgress.Clear();
		levelStarsProgress.Clear();
		regionRewardClaims.Clear();

		for (int i = 0; i < 5; i++)
			currentDeck[i] = null;

		totalCurrency = 0;
		totalScientistsCurrency = 0;
		currentEnergy = maxEnergy; // Restore full energy

		currentRegionIndex = 0;
		currentLevelIndex = 0;
		hasPendingMapAnimation = false;
		hasPendingRegionAnimation = false;

		hasPendingStarAnimation = false;
		pendingStarLevelId = "";
		pendingStarOldValue = 0;
		pendingStarNewValue = 0;
	}
}

// Helper wrapper for serializing lists via JsonUtility
[System.Serializable]
public class SerializationWrapper<T>
{
	public List<T> target;
	public SerializationWrapper(List<T> target) => this.target = target;
}

[System.Serializable]
public class LevelStarsProgress
{
	public string levelId;
	public int stars;
}

[System.Serializable]
public class RegionRewardClaimState
{
	public int regionIndex;
	public List<int> claimedRewardIndices = new List<int>();
}