using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewLevel", menuName = "ZombieGame/LevelData")]
public class LevelData : ScriptableObject
{
	public enum CameraType { Perspective, Orthographic }

	[Header("������� ���������")]
	public GameObject levelPrefab;
	public int humanCount = 25;
	public float levelTimer = 60f;
	public float suddenDeathSpawnRate = 0.3f;

	[Header("������������ ������ (��������)")]
	[Tooltip("��������� �������� ���� ����� �� ���� ������ (1 = �����������)")]
	public float zombieSpeedMultiplier = 1.0f;

	[Tooltip("��������� �������� ���� ����� �� ���� ������ (1 = �����������)")]
	public float zombieHealthMultiplier = 1.0f;

	[Header("�������� ����")]
	[Tooltip("�������� ���� ������ ���� � ��� �������, � ������� ��� ������ ���������")]
	public List<WaveData> waves = new List<WaveData>();

	[Header("������� ������")]
	[Tooltip("����������� ���������� �������� ����� ��� ������")]
	public int requiredRescuedHumans = 10;

	[Header("3 ������")]
	[Tooltip("1 ������. ���� 0, ����� �������������� requiredRescuedHumans")]
	public int star1RequiredHumans = 0;

	[Tooltip("2 ������. ���� 0, ����� �������������� requiredRescuedHumans + 5")]
	public int star2RequiredHumans = 0;

	[Tooltip("�������� 3 ������. ���� ������ = perfect clear")]
	public string star3Description = "������ ��������";

	[Header("����������� �� Mission Popup")]
	public string missionTitleOverride = "";
	public Sprite missionIcon;

	[TextArea(2, 4)]
	public string missionDescription;

	[Header("��������� ������ ������")]
	public CameraType cameraType = CameraType.Perspective;
	public Vector3 cameraPosition = new Vector3(0, 20, -15);
	public Vector3 cameraRotation = new Vector3(60, 0, 0);
	public float cameraFieldOfView = 60f;
	public float orthographicSize = 10f;

	[Header("������� �� ������ �����������")]
	public int currencyReward = 50;
	public LootboxData levelRewardLootbox;

	[Header("������ �� ������")]
	[Tooltip("������� ������ ����� �� ������")]
	public int scientistCount = 0;

	[Tooltip("������ ������� (Scientist) ��� ����� ������ (���� �� ����� � ������ �� LevelManager)")]
	public GameObject scientistPrefab;

	[Header("�������")]
	[Tooltip("���������, ���������� �� ������ ������ ������ �������")]
	public bool abilityEnabled = false;

	[Header("�������� ������")]
	[Tooltip("���� �������� ���������� �������������, ����� ����� ������ ���� �������")]
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