using UnityEngine;
using System.Collections.Generic;

// Zombie spawn patterns
public enum SpawnPattern
{
	Linear,         // One zombie at a time at a fixed interval
	Burst,          // All zombies spawn at once (mini-wave)
	RandomInterval  // Random delay between spawns (irregular spawn feel)
}

[System.Serializable]
public class WaveAction
{
	[Header("Timing")]
	[Tooltip("Delay before this action starts (measured from wave start time)")]
	public float delayBeforeStart = 0f;

	[Tooltip("Zombie prefab. If null, uses the default prefab from LevelManager")]
	public GameObject zombiePrefab;

	[Header("Count (range)")]
	[Tooltip("Minimum number of zombies to spawn")]
	public int minCount = 5;

	[Tooltip("Maximum number of zombies to spawn (if equal to minCount, exact count)")]
	public int maxCount = 5;

	[Header("Spawn Pattern")]
	[Tooltip("How zombies are spawned in this action")]
	public SpawnPattern pattern = SpawnPattern.Linear;

	[Tooltip("Fixed interval (for Linear) or max random interval (for RandomInterval)")]
	public float spawnInterval = 0.5f;

	[Tooltip("Which spawn group point to use for this action")]
	public SpawnGroup spawnGroup = SpawnGroup.Any;

	// Returns a random count between min and max (inclusive)
	public int GetRandomCount()
	{
		return Random.Range(minCount, maxCount + 1);
	}
}

[CreateAssetMenu(fileName = "NewWave", menuName = "ZombieGame/WaveData")]
public class WaveData : ScriptableObject
{
	[Header("Wave Timing")]
	[Tooltip("Seconds from level start when this wave triggers")]
	public float startTime = 10f;

	[Tooltip("How many seconds before the wave starts to show the UI warning")]
	public float warningDuration = 3f;

	[Header("Wave Actions")]
	[Tooltip("List of spawn actions that make up this wave")]
	public List<WaveAction> actions = new List<WaveAction>();
}