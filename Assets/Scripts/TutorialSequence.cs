using UnityEngine;
using System.Collections.Generic;

public enum TutorialStepType
{
	DialogOnly,
	ClickOnly,
	DialogAndClick,
	DragAndDrop,      // НОВОЕ: Только перетягивание карточки
	DialogAndDrag     // НОВОЕ: Диалог + перетягивание
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
	[Header("Тип шага")]
	public TutorialStepType stepType;

	[Tooltip("ID цели (TutorialTarget), откуда начинаем (например, Карточка)")]
	public string targetId;

	[Tooltip("Затемнять ли остальной экран вокруг стартовой цели?")]
	public bool useDarkMask = true;

	[Header("Настройки Drag & Drop")]
	[Tooltip("ID UI-зоны, куда нужно дотянуть карту (опционально, для анимации пальца)")]
	public string dropTargetId;

	[Tooltip("Если dropTargetId пустой, палец просто проанимирует свайп по этому вектору")]
	public Vector2 swipeOffset = new Vector2(0, 400);

	[Header("Настройки Диалога")]
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