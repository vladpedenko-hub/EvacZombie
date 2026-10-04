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

	[Header("Missing Target")]
	[Tooltip("Seconds to wait for the target before this step is skipped. 0 = wait forever (original behaviour).")]
	public float missingTargetTimeout = 0f;

	[Header("Layout")]
	[Tooltip("Stretch the dialog text across the dialog panel. Off keeps the original narrow text box.")]
	public bool wideDialogText = false;
}

[CreateAssetMenu(fileName = "NewTutorial", menuName = "ZombieGame/TutorialSequence")]
public class TutorialSequence : ScriptableObject
{
	public string tutorialId = "Tutorial_1";
	public List<TutorialStep> steps = new List<TutorialStep>();

	[Header("Interruption (defaults keep the original behaviour)")]
	[Tooltip("Show a Skip button. Skip ends the sequence and marks it done.")]
	public bool allowSkip = false;

	[Tooltip("When the app is paused or the scene changes, release the screen and save the step. The next start resumes there.")]
	public bool resumable = false;
}