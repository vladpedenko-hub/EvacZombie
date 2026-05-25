using UnityEngine;
using System.Collections.Generic;

// Паттерны появления толпы
public enum SpawnPattern
{
	Linear,         // Строго друг за другом с равным интервалом
	Burst,          // Все появляются почти мгновенно (взрыв толпы)
	RandomInterval  // Хаотичные паузы между зомби (эффект неровной орды)
}

[System.Serializable]
public class WaveAction
{
	[Header("Хореография")]
	[Tooltip("Задержка перед стартом именно этой пачки (относительно старта самой волны)")]
	public float delayBeforeStart = 0f;

	[Tooltip("Префаб зомби. Если пусто - возьмется дефолтный из LevelManager")]
	public GameObject zombiePrefab;

	[Header("Количество (Рандом)")]
	[Tooltip("Минимальное количество зомби")]
	public int minCount = 5;

	[Tooltip("Максимальное количество зомби (если равно minCount - рандома нет)")]
	public int maxCount = 5;

	[Header("Настройки поведения")]
	[Tooltip("Как именно будет выходить эта толпа")]
	public SpawnPattern pattern = SpawnPattern.Linear;

	[Tooltip("Базовый интервал (для Linear) или максимальный интервал (для RandomInterval)")]
	public float spawnInterval = 0.5f;

	[Tooltip("Из какой группы точек спавнить эту пачку")]
	public SpawnGroup spawnGroup = SpawnGroup.Any;

	// Помощник для получения финального числа зомби
	public int GetRandomCount()
	{
		return Random.Range(minCount, maxCount + 1);
	}
}

[CreateAssetMenu(fileName = "NewWave", menuName = "ZombieGame/WaveData")]
public class WaveData : ScriptableObject
{
	[Header("Тайминг Волны")]
	[Tooltip("Секунда от начала уровня, когда начнется спавн")]
	public float startTime = 10f;

	[Tooltip("За сколько секунд до старта показать UI-предупреждение над точкой спавна")]
	public float warningDuration = 3f;

	[Header("Действия волны")]
	[Tooltip("Список пачек зомби, которые появятся в эту волну")]
	public List<WaveAction> actions = new List<WaveAction>();
}