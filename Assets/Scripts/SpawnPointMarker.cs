using UnityEngine;

public enum SpawnGroup
{
	Any,       // Zombies can spawn from any group point
	All,       // All group points are used simultaneously
	North,
	South,
	East,
	West,
	Special_1,
	Special_2
}

public class SpawnPointMarker : MonoBehaviour
{
	[Tooltip("Which group this spawn point belongs to. LevelManager selects points by group.")]
	public SpawnGroup group = SpawnGroup.Any;

	[Tooltip("Is this point only used during Sudden Death (night phase)?")]
	public bool isNightSpawn = false;

	private void OnDrawGizmos()
	{
		Gizmos.color = isNightSpawn ? Color.red : Color.yellow;
		Gizmos.DrawWireSphere(transform.position, 1f);
	}
}