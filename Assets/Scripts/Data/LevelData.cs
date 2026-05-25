using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewLevel", menuName = "ZombieGame/LevelData")]
public class LevelData : ScriptableObject
{
	public enum CameraType { Perspective, Orthographic }

	[Header("Базовые настройки")]
	public GameObject levelPrefab;
	public int humanCount = 25;
	public float levelTimer = 60f;
	public float suddenDeathSpawnRate = 0.3f;

	[Header("Модификаторы уровня (Мутаторы)")]
	[Tooltip("Множитель скорости всех зомби на этом уровне (1 = стандартная)")]
	public float zombieSpeedMultiplier = 1.0f;

	[Tooltip("Множитель здоровья всех зомби на этом уровне (1 = стандартное)")]
	public float zombieHealthMultiplier = 1.0f;

	[Header("Таймлайн Волн")]
	[Tooltip("Перетащи сюда модули волн в том порядке, в котором они должны сработать")]
	public List<WaveData> waves = new List<WaveData>();

	[Header("Условия победы")]
	[Tooltip("Минимальное количество спасённых людей для победы")]
	public int requiredRescuedHumans = 10;

	[Header("3 звезды")]
	[Tooltip("1 звезда. Если 0, будет использоваться requiredRescuedHumans")]
	public int star1RequiredHumans = 0;

	[Tooltip("2 звезды. Если 0, будет использоваться requiredRescuedHumans + 5")]
	public int star2RequiredHumans = 0;

	[Tooltip("Описание 3 звезды. Сама логика = perfect clear")]
	public string star3Description = "Пройти идеально";

	[Header("Отображение на Mission Popup")]
	public string missionTitleOverride = "";
	public Sprite missionIcon;

	[TextArea(2, 4)]
	public string missionDescription;

	[Header("Настройки Камеры Уровня")]
	public CameraType cameraType = CameraType.Perspective;
	public Vector3 cameraPosition = new Vector3(0, 20, -15);
	public Vector3 cameraRotation = new Vector3(60, 0, 0);
	public float cameraFieldOfView = 60f;
	public float orthographicSize = 10f;

	[Header("Награда за первое прохождение")]
	public int currencyReward = 50;
	public LootboxData levelRewardLootbox;

	[Header("Учёные на уровне")]
	[Tooltip("Сколько учёных будет на уровне")]
	public int scientistCount = 0;

	[Tooltip("Префаб учёного (Scientist) для этого уровня (если не задан — возьмём из LevelManager)")]
	public GameObject scientistPrefab;

	[Header("Туториал уровня")]
	[Tooltip("Этот туториал запустится АВТОМАТИЧЕСКИ, когда игрок начнет этот уровень")]
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