using UnityEngine;
using System.Collections.Generic;

public enum TutorialStepType
{
	DialogOnly,
	ClickOnly,
	DialogAndClick,
	DragAndDrop,      // �����: ������ ������������� ��������
	DialogAndDrag     // �����: ������ + �������������
}

public enum DialogPosition
{
	Top,
	Center,
	Bottom
}

[System.Serializable]
public class TutorialStep
{
	[Header("��� ����")]
	public TutorialStepType stepType;

	[Tooltip("ID ���� (TutorialTarget), ������ �������� (��������, ��������)")]
	public string targetId;

	[Tooltip("��������� �� ��������� ����� ������ ��������� ����?")]
	public bool useDarkMask = true;

	[Header("Позиция пальца")]
	[Tooltip("Смещение пальца в пикселях относительно центра цели (например (0, 50) — выше центра кнопки)")]
	public Vector2 fingerOffset = Vector2.zero;

	[Header("��������� Drag & Drop")]
	[Tooltip("ID UI-����, ���� ����� �������� ����� (�����������, ��� �������� ������)")]
	public string dropTargetId;

	[Tooltip("���� dropTargetId ������, ����� ������ ������������ ����� �� ����� �������")]
	public Vector2 swipeOffset = new Vector2(0, 400);

	[Header("��������� �������")]
	[TextArea(2, 4)]
	public string dialogText;
	public Sprite characterIcon;
	public DialogPosition dialogPosition = DialogPosition.Bottom;
}

[CreateAssetMenu(fileName = "NewTutorial", menuName = "ZombieGame/TutorialSequence")]
public class TutorialSequence : ScriptableObject
{
	public string tutorialId = "Tutorial_1";
	public List<TutorialStep> steps = new List<TutorialStep>();
}