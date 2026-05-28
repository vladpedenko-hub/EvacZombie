using UnityEngine;
using System.Collections.Generic;

public enum TutorialStepType
{
	DialogOnly,
	ClickOnly,
	DialogAndClick,
	DragAndDrop,      // Drag only: finger shows drag animation
	DialogAndDrag     // Dialog + drag animation
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
	[Header("Step Type")]
	public TutorialStepType stepType;

	[Tooltip("ID of the target (TutorialTarget) that the finger points to (e.g. a button, a card)")]
	public string targetId;

	[Tooltip("Should the dark mask be shown around the target element?")]
	public bool useDarkMask = true;

	[Header("Finger Position")]
	[Tooltip("Finger offset in pixels relative to the target center (e.g. (0, 50) — above the button center)")]
	public Vector2 fingerOffset = Vector2.zero;

	[Header("Drag & Drop Settings")]
	[Tooltip("ID of the UI target to drag to (optional, overrides swipe direction)")]
	public string dropTargetId;

	[Tooltip("If dropTargetId is empty, the finger will swipe by this offset from the start")]
	public Vector2 swipeOffset = new Vector2(0, 400);

	[Header("Dialog Settings")]
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