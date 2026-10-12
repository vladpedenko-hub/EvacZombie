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

// Core Loop v2 tutorial rewrite (Docs/CORE_LOOP_V2.md Phase 3): generic, data-driven step
// completion, not hardcoded per level. "Default" keeps the original behaviour (Button.onClick for
// ClickOnly/DialogAndClick, an external NextStep() call for DragAndDrop/DialogAndDrag, a tap on
// the blocker for DialogOnly). The others are driven by TutorialManager subscribing to the named
// game event only while a step with that trigger is the active one.
public enum TutorialCompletionTrigger
{
	Default,
	CardPlaced,
	WaveStarted,
	FirstRescueCredited,
	NightStarted,
	UltimateReady,
	UltimateUsed
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

	[Header("Completion Trigger (Core Loop v2, Docs/CORE_LOOP_V2.md Phase 3)")]
	[Tooltip("How this step completes. Default keeps the original per-stepType behaviour (button click / an external NextStep() call for drag steps / a blocker tap for DialogOnly). The others complete the instant the named game event fires while this step is the active one.")]
	public TutorialCompletionTrigger completionTrigger = TutorialCompletionTrigger.Default;

	[Tooltip("CardPlaced only: which card type must be placed.")]
	public CardManager.CardType requiredCardType = CardManager.CardType.None;

	[Tooltip("CardPlaced only: how many placements of that type are needed to complete the step.")]
	public int requiredCount = 1;

	[Tooltip("CardPlaced only: require specifically the free Night 'LAST TRANSPORT' card (CardData.isLastTransport), not a regular card of requiredCardType.")]
	public bool requireLastTransportCard = false;
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