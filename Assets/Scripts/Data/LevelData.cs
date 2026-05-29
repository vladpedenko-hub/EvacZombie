using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewLevel", menuName = "ZombieGame/LevelData")]
public class LevelData : ScriptableObject
{
	public enum CameraType { Perspective, Orthographic }

	[Header("Basic Settings")]
	public GameObject levelPrefab;
	public int humanCount = 25;
	public float levelTimer = 60f;
	public float suddenDeathSpawnRate = 0.3f;

	[Header("Zombie Multipliers (Optional)")]
	[Tooltip("Speed multiplier for all zombies in this level (1 = normal)")]
	public float zombieSpeedMultiplier = 1.0f;

	[Tooltip("Health multiplier for all zombies in this level (1 = normal)")]
	public float zombieHealthMultiplier = 1.0f;

	[Header("Wave Data")]
	[Tooltip("Wave data defining what spawns and when in this level")]
	public List<WaveData> waves = new List<WaveData>();

	[Header("Victory Condition")]
	[Tooltip("Minimum number of humans that must be rescued to win")]
	public int requiredRescuedHumans = 10;

	[Header("3 Stars")]
	[Tooltip("1 star. If 0, uses requiredRescuedHumans")]
	public int star1RequiredHumans = 0;

	[Tooltip("2 stars. If 0, uses requiredRescuedHumans + 5")]
	public int star2RequiredHumans = 0;

	[Tooltip("Condition for 3 stars. If empty = perfect clear")]
	public string star3Description = "Save everyone";

	[Header("Display on Mission Popup")]
	public string missionTitleOverride = "";
	public Sprite missionIcon;

	[TextArea(2, 4)]
	public string missionDescription;

	[Header("Camera Settings")]
	public CameraType cameraType = CameraType.Perspective;
	public Vector3 cameraPosition = new Vector3(0, 20, -15);
	public Vector3 cameraRotation = new Vector3(60, 0, 0);
	public float cameraFieldOfView = 60f;
	public float orthographicSize = 10f;

	[Header("Reward for Level Completion")]
	public int currencyReward = 50;
	public LootboxData levelRewardLootbox;

	[Header("Scientist on Level")]
	[Tooltip("Number of scientists placed on the level")]
	public int scientistCount = 0;

	[Tooltip("Scientist prefab to spawn (if not placed in the scene by LevelManager)")]
	public GameObject scientistPrefab;

	[Header("Ability")]
	[Tooltip("Whether the ability is available during this level")]
	public bool abilityEnabled = false;

	[Header("Tutorial Sequence")]
	[Tooltip("If a sequence is assigned, it will play at the start of the level")]
	public TutorialSequence onStartTutorial;

	public int GetStar1Target()
	{
		return star1RequiredHumans > 0 ? star1RequiredHumans : requiredRescuedHumans;
	}

	public int GetStar2Target()
	{
		return star2RequiredHumans > 0 ? star2RequiredHumans : requiredRescuedHumans + 5;
	}

	public string GetMissionTitle(int regionIndex, int levelIndex)
	{
		if (!string.IsNullOrEmpty(missionTitleOverride))
			return missionTitleOverride;

		return $"MISSION {regionIndex + 1}-{levelIndex + 1}";
	}
}