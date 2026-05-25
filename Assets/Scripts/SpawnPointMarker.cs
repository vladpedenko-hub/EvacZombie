using UnityEngine;

public enum SpawnGroup
{
	Any,       // Выберет ровно ОДНУ случайную точку
	All,       // Задействует ВСЕ доступные дневные точки
	North,
	South,
	East,
	West,
	Special_1,
	Special_2
}

public class SpawnPointMarker : MonoBehaviour
{
	[Tooltip("К какой группе относится эта точка. Позволяет направлять волны с конкретных сторон.")]
	public SpawnGroup group = SpawnGroup.Any;

	[Tooltip("Использовать ли эту точку для Судного Дня (внезапная смерть)?")]
	public bool isNightSpawn = false;

	private void OnDrawGizmos()
	{
		Gizmos.color = isNightSpawn ? Color.red : Color.yellow;
		Gizmos.DrawWireSphere(transform.position, 1f);
	}
}