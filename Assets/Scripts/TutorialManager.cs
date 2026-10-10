using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using TMPro;
using DG.Tweening;

public class TutorialManager : MonoBehaviour
{
	public static TutorialManager Instance;

	public static event System.Action OnTutorialStarted;
	public static event System.Action OnTutorialFinished;

	// Raised whenever the visible step changes or the screen is released. The side dock uses it to hide itself
	// during blocking steps.
	public static event System.Action OnStepChanged;

	// Raised when a resumable sequence is interrupted (app paused, scene changed). Progress is saved, not finished.
	public static event System.Action<string> OnTutorialAbandoned;

	public bool IsActive => isTutorialActive;
	public string CurrentTargetId => GetCurrentStep()?.targetId;

	[Header("UI Elements (Main)")]
	[Tooltip("FullScreenBlocker object")]
	public GameObject fullScreenBlocker;
	public GameObject dialogPanel;
	public TextMeshProUGUI dialogText;
	public Image dialogIcon;
	public RectTransform fingerPointer;

	[Header("Mask Settings (Cutout)")]
	public RectTransform maskContainer;
	public CanvasGroup maskCanvasGroup;
	public RectTransform topMask;
	public RectTransform bottomMask;
	public RectTransform leftMask;
	public RectTransform rightMask;
	public float maskPadding = 25f;

	[Header("Solid Dark Mask (No Cutout)")]
	public GameObject solidDarkMask;

	private Dictionary<string, RectTransform> activeTargets = new Dictionary<string, RectTransform>();
	private TutorialSequence currentSequence;
	private int currentStepIndex = 0;
	private bool isTutorialActive = false;

	private Tween fingerTween;
	private Button currentTrackedButton;
	private bool _pausedTimeForTutorial = false;

	private Button skipButton;
	private (Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax) dialogTextDefaults;
	private TMPro.TextAlignmentOptions dialogTextAlignmentDefault;
	private bool isWaitingForTarget = false;
	private float waitingSince;
	private float waitingTimeout;

	private void Awake()
	{
		if (Instance == null)
		{
			Instance = this;
			DontDestroyOnLoad(gameObject);
		}
		else
		{
			Destroy(gameObject);
			return;
		}

		SceneManager.sceneUnloaded += OnSceneUnloaded;

		// Remove the Button component from the blocker if it exists, and replace with a filter instead
		Button btn = fullScreenBlocker.GetComponent<Button>();
		if (btn != null) Destroy(btn);

		TutorialRaycastFilter filter = fullScreenBlocker.AddComponent<TutorialRaycastFilter>();
		filter.manager = this;

		SetupMaskRect(topMask);
		SetupMaskRect(bottomMask);
		SetupMaskRect(leftMask);
		SetupMaskRect(rightMask);

		// Built last so it sits above the blocker. Only shown for sequences with allowSkip.
		// Bottom right, above the tab bar, so it never covers the settings gear or the DEV button.
		skipButton = MetaUI.Button("SkipButton", transform, "SKIP", MetaUI.ButtonSecondary,
			new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-260f, 216f), new Vector2(-30f, 286f), out _);
		if (dialogText != null)
		{
			RectTransform textRect = dialogText.rectTransform;
			dialogTextDefaults = (textRect.anchorMin, textRect.anchorMax, textRect.offsetMin, textRect.offsetMax);
			dialogTextAlignmentDefault = dialogText.alignment;
		}
		skipButton.onClick.AddListener(SkipTutorial);

		CloseTutorialUI();
	}

	private void OnDestroy() => SceneManager.sceneUnloaded -= OnSceneUnloaded;

	private void OnSceneUnloaded(Scene scene)
	{
		activeTargets.Clear();
		CleanUpTrackedButton();

		// Leaving the scene must never leave the screen blocked. Resumable sequences save their step.
		if (isTutorialActive && currentSequence != null && currentSequence.resumable) AbandonTutorial();
	}

	// Backgrounding the app (mobile) interrupts a resumable sequence. The screen is released, progress is kept.
	private void OnApplicationPause(bool paused)
	{
		if (paused && isTutorialActive && currentSequence != null && currentSequence.resumable) AbandonTutorial();
	}

	// Missing-target timeout. Only runs while a step is waiting for a target that is not registered.
	private void Update()
	{
		if (!isWaitingForTarget || !isTutorialActive) return;
		if (Time.unscaledTime - waitingSince < waitingTimeout) return;

		isWaitingForTarget = false;
		Debug.Log("[Tutorial] Target missing for " + waitingTimeout + "s; skipping step.");
		NextStep();
	}

	public void RegisterTarget(string id, RectTransform rect)
	{
		activeTargets[id] = rect;
		if (isTutorialActive && GetCurrentStep() != null && GetCurrentStep().targetId == id)
			ShowStep(GetCurrentStep());
	}

	public void UnregisterTarget(string id)
	{
		if (activeTargets.ContainsKey(id)) activeTargets.Remove(id);
	}

	public void StartTutorial(TutorialSequence sequence)
	{
		if (sequence == null) return;
		if (TutorialProgress.IsDone(sequence.tutorialId)) return;

		currentSequence = sequence;
		currentStepIndex = 0;
		if (sequence.resumable)
			currentStepIndex = Mathf.Clamp(TutorialProgress.GetStep(sequence.tutorialId), 0, Mathf.Max(0, sequence.steps.Count - 1));
		isTutorialActive = true;

		// Pause timeScale only during active gameplay.
		// In Planning, the timer and zombies are already inactive, and a hard pause
		// blocks energy regeneration and makes cards unavailable.
		_pausedTimeForTutorial = GameManager.Instance != null &&
			(GameManager.Instance.State == GameManager.GameState.Playing ||
			 GameManager.Instance.State == GameManager.GameState.SuddenDeath);

		if (_pausedTimeForTutorial)
		{
			if (TimeManager.Instance != null)
				TimeManager.Instance.PauseTime();
			else
				Time.timeScale = 0f;
		}

		OnTutorialStarted?.Invoke();
		fullScreenBlocker.SetActive(true);
		skipButton.gameObject.SetActive(sequence.allowSkip);
		ShowStep(GetCurrentStep());
	}

	private TutorialStep GetCurrentStep()
	{
		if (currentSequence == null || currentStepIndex >= currentSequence.steps.Count) return null;
		return currentSequence.steps[currentStepIndex];
	}

	private void ShowStep(TutorialStep step)
	{
		if (step == null)
		{
			FinishTutorial();
			return;
		}

		CleanUpTrackedButton();
		if (fingerTween != null) fingerTween.Kill();
		isWaitingForTarget = false;
		OnStepChanged?.Invoke();

		fingerPointer.gameObject.SetActive(false);
		maskContainer.gameObject.SetActive(false);
		if (solidDarkMask != null) solidDarkMask.SetActive(false);
		dialogPanel.SetActive(false);

		// --- Mask ---
		if (step.useDarkMask)
		{
			if (step.stepType == TutorialStepType.DialogOnly)
			{
				if (solidDarkMask != null) solidDarkMask.SetActive(true);
			}
			else
			{
				maskContainer.gameObject.SetActive(true);
				if (maskCanvasGroup != null)
				{
					maskCanvasGroup.alpha = 0f;
					maskCanvasGroup.DOFade(1f, 0.3f).SetUpdate(true);
				}
			}
		}

		// --- Dialog ---
		if (step.stepType == TutorialStepType.DialogOnly || step.stepType == TutorialStepType.DialogAndClick || step.stepType == TutorialStepType.DialogAndDrag)
		{
			dialogPanel.SetActive(true);
			dialogText.text = step.dialogText;
			ApplyDialogTextLayout(step.wideDialogText);

			if (step.characterIcon != null)
			{
				dialogIcon.gameObject.SetActive(true);
				dialogIcon.sprite = step.characterIcon;
			}
			else dialogIcon.gameObject.SetActive(false);

			RectTransform dialogRect = dialogPanel.GetComponent<RectTransform>();
			(float topInset, float bottomInset) = GetVerticalSafeAreaInsetsInCanvasUnits(dialogRect);
			switch (step.dialogPosition)
			{
				case DialogPosition.Top:
					dialogRect.anchorMin = new Vector2(0, 1);
					dialogRect.anchorMax = new Vector2(1, 1);
					dialogRect.pivot = new Vector2(0.5f, 1);
					// -170 (was -100) clears the Gameplay HUD (timer/rescued counter sit ~0-62
					// units below the true top edge); the safe-area inset is added on top so this
					// also clears a notch/cutout on devices where Screen.safeArea shrinks the top.
					dialogRect.anchoredPosition = new Vector2(0, -170f - topInset);
					break;
				case DialogPosition.Center:
					dialogRect.anchorMin = new Vector2(0, 0.5f);
					dialogRect.anchorMax = new Vector2(1, 0.5f);
					dialogRect.pivot = new Vector2(0.5f, 0.5f);
					dialogRect.anchoredPosition = Vector2.zero;
					break;
				case DialogPosition.Bottom:
					dialogRect.anchorMin = new Vector2(0, 0);
					dialogRect.anchorMax = new Vector2(1, 0);
					dialogRect.pivot = new Vector2(0.5f, 0);
					dialogRect.anchoredPosition = new Vector2(0, 100f + bottomInset);
					break;
			}

			dialogPanel.transform.localScale = Vector3.zero;
			dialogPanel.transform.DOScale(1f, 0.4f).SetEase(Ease.OutBack).SetUpdate(true);
		}

		// --- Finger and Target ---
		if (step.stepType != TutorialStepType.DialogOnly)
		{
			if (TryGetTarget(step, out RectTransform targetRect))
			{
				if (step.useDarkMask) FocusMaskOnTarget(targetRect);
				fingerPointer.gameObject.SetActive(true);

				// 1. Click step type
				if (step.stepType == TutorialStepType.ClickOnly || step.stepType == TutorialStepType.DialogAndClick)
				{
					PlaceFingerAtTarget(targetRect, step.fingerOffset);
					fingerPointer.localScale = Vector3.one;
					fingerTween = fingerPointer.DOScale(1.15f, 0.4f).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine).SetUpdate(true);

					// Subscribe to the button click event
					currentTrackedButton = targetRect.GetComponent<Button>();
					if (currentTrackedButton != null) currentTrackedButton.onClick.AddListener(NextStep);
				}
				// 2. Drag step type
				else if (step.stepType == TutorialStepType.DragAndDrop || step.stepType == TutorialStepType.DialogAndDrag)
				{
					PlaceFingerAtTarget(targetRect, step.fingerOffset);
					Vector3 startPos = fingerPointer.position;
					Vector3 endPos = startPos + (Vector3)step.swipeOffset;

					if (!string.IsNullOrEmpty(step.dropTargetId) && activeTargets.TryGetValue(step.dropTargetId, out RectTransform dropRect))
					{
						endPos = GetWorldPositionOnFingerCanvas(dropRect);
					}

					// Animation: press -> drag -> release -> reset
					Sequence dragSeq = DOTween.Sequence().SetUpdate(true);
					dragSeq.Append(fingerPointer.DOScale(0.85f, 0.2f)); // press down
					dragSeq.Append(fingerPointer.DOMove(endPos, 1.2f).SetEase(Ease.InOutQuad)); // drag
					dragSeq.Append(fingerPointer.DOScale(1.1f, 0.2f)); // release
					dragSeq.AppendInterval(0.2f);
					dragSeq.Append(fingerPointer.DOMove(startPos, 0f)); // reset position
					dragSeq.SetLoops(-1, LoopType.Restart);
					fingerTween = dragSeq;

					// Note: DragAndDrop does not use Button click. NextStep() must be called externally from game logic.
				}
			}
			else
			{
				Debug.Log($"[Tutorial] Waiting for target: {step.targetId}...");
				dialogPanel.SetActive(false);

				if (step.missingTargetTimeout > 0f)
				{
					isWaitingForTarget = true;
					waitingSince = Time.unscaledTime;
					waitingTimeout = step.missingTargetTimeout;
				}
			}
		}
	}

	// Wide text spans the dialog panel; otherwise the original layout is restored exactly.
	private void ApplyDialogTextLayout(bool wide)
	{
		RectTransform textRect = dialogText.rectTransform;
		if (wide)
		{
			textRect.anchorMin = Vector2.zero;
			textRect.anchorMax = Vector2.one;
			textRect.offsetMin = new Vector2(40f, 20f);
			textRect.offsetMax = new Vector2(-40f, -20f);
			dialogText.alignment = TMPro.TextAlignmentOptions.Center;
			return;
		}

		textRect.anchorMin = dialogTextDefaults.anchorMin;
		textRect.anchorMax = dialogTextDefaults.anchorMax;
		textRect.offsetMin = dialogTextDefaults.offsetMin;
		textRect.offsetMax = dialogTextDefaults.offsetMax;
		dialogText.alignment = dialogTextAlignmentDefault;
	}

	// A target is present when it is registered. Steps that opt into a missing-target timeout also need the
	// target to be active, so a closed overlay counts as missing. Steps without a timeout keep the original rule.
	private bool TryGetTarget(TutorialStep step, out RectTransform rect)
	{
		rect = null;
		if (!activeTargets.TryGetValue(step.targetId, out rect) || rect == null) return false;
		if (step.missingTargetTimeout > 0f && !rect.gameObject.activeInHierarchy) return false;
		return true;
	}

	// Positions the fingerPointer exactly over targetRect via screen space conversion.
	// Works correctly even when target and fingerPointer are on different Canvases with different Canvas Scalers.
	private void PlaceFingerAtTarget(RectTransform targetRect, Vector2 offset = default)
	{
		Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(null, targetRect.position);
		if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
			fingerPointer.parent as RectTransform, screenPos, null, out Vector2 localPos))
		{
			fingerPointer.localPosition = localPos + offset;
		}
	}

	private Vector3 GetWorldPositionOnFingerCanvas(RectTransform targetRect)
	{
		Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(null, targetRect.position);
		if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
			fingerPointer.parent as RectTransform, screenPos, null, out Vector2 localPos))
		{
			return fingerPointer.parent.TransformPoint(localPos);
		}
		return targetRect.position;
	}

	// DialogPanel can't be wrapped in a SafeArea container (it's part of the TutorialCanvas
	// prefab instance, and Unity won't let a prefab-instance child be reparented outside it),
	// so the Top/Bottom cases above add this inset directly to their anchoredPosition instead.
	private (float top, float bottom) GetVerticalSafeAreaInsetsInCanvasUnits(RectTransform rect)
	{
		Canvas canvas = rect.GetComponentInParent<Canvas>();
		float scale = (canvas != null && canvas.scaleFactor > 0f) ? canvas.scaleFactor : 1f;
		Rect safe = Screen.safeArea;
		float topPixels = Screen.height - (safe.y + safe.height);
		float bottomPixels = safe.y;
		return (topPixels / scale, bottomPixels / scale);
	}

	private void SetupMaskRect(RectTransform rect)
	{
		if (rect == null) return;
		rect.anchorMin = new Vector2(0.5f, 0.5f);
		rect.anchorMax = new Vector2(0.5f, 0.5f);
		rect.pivot = new Vector2(0.5f, 0.5f);
	}

	private void FocusMaskOnTarget(RectTransform target)
	{
		Vector3[] corners = new Vector3[4];
		target.GetWorldCorners(corners);

		RectTransformUtility.ScreenPointToLocalPointInRectangle(maskContainer, RectTransformUtility.WorldToScreenPoint(null, corners[0]), null, out Vector2 bottomLeft);
		RectTransformUtility.ScreenPointToLocalPointInRectangle(maskContainer, RectTransformUtility.WorldToScreenPoint(null, corners[2]), null, out Vector2 topRight);

		float targetWidth = topRight.x - bottomLeft.x;
		float targetHeight = topRight.y - bottomLeft.y;

		float padding = maskPadding;
		targetWidth += padding * 2;
		targetHeight += padding * 2;
		bottomLeft.x -= padding;
		bottomLeft.y -= padding;
		topRight.x += padding;
		topRight.y += padding;

		Vector2 targetCenter = new Vector2(bottomLeft.x + targetWidth / 2f, bottomLeft.y + targetHeight / 2f);

		float maxScreenW = 4000f;
		float maxScreenH = 4000f;

		topMask.sizeDelta = new Vector2(maxScreenW, maxScreenH);
		topMask.anchoredPosition = new Vector2(targetCenter.x, topRight.y + (maxScreenH / 2f));

		bottomMask.sizeDelta = new Vector2(maxScreenW, maxScreenH);
		bottomMask.anchoredPosition = new Vector2(targetCenter.x, bottomLeft.y - (maxScreenH / 2f));

		leftMask.sizeDelta = new Vector2(maxScreenW, targetHeight);
		leftMask.anchoredPosition = new Vector2(bottomLeft.x - (maxScreenW / 2f), targetCenter.y);

		rightMask.sizeDelta = new Vector2(maxScreenW, targetHeight);
		rightMask.anchoredPosition = new Vector2(topRight.x + (maxScreenW / 2f), targetCenter.y);
	}

	private void CleanUpTrackedButton()
	{
		if (currentTrackedButton != null)
		{
			currentTrackedButton.onClick.RemoveListener(NextStep);
			currentTrackedButton = null;
		}
	}

	// This method is public! It must be callable from any card button.
	public void NextStep()
	{
		if (!isTutorialActive) return;

		currentStepIndex++;
		if (currentSequence.resumable) TutorialProgress.SetStep(currentSequence.tutorialId, currentStepIndex);

		if (currentStepIndex >= currentSequence.steps.Count) FinishTutorial();
		else ShowStep(GetCurrentStep());
	}

	public void OnBlockerClicked()
	{
		TutorialStep step = GetCurrentStep();
		if (step != null && step.stepType == TutorialStepType.DialogOnly)
		{
			NextStep();
		}
	}

	// Called from the raycast filter to determine whether a click should pass through the blocker
	public bool ShouldBlockRaycast(Vector2 screenPosition, Camera eventCamera)
	{
		TutorialStep step = GetCurrentStep();
		if (step == null || !isTutorialActive) return false;

		if (step.stepType == TutorialStepType.DialogOnly) return true; // Always block

		if (TryGetTarget(step, out RectTransform targetRect))
		{
			bool inHole = RectTransformUtility.RectangleContainsScreenPoint(targetRect, screenPosition, eventCamera);
			return !inHole; // Inside the hole — allow (false). Outside — block (true).
		}

		return true;
	}

	public void FinishTutorial()
	{
		if (currentSequence != null)
		{
			TutorialProgress.MarkDone(currentSequence.tutorialId);
			TutorialProgress.ClearStep(currentSequence.tutorialId);
		}
		ReleaseScreen();
		OnTutorialFinished?.Invoke();
	}

	// Skip ends the sequence for good (marked done). Only allowed when the sequence opts in.
	public void SkipTutorial()
	{
		if (!isTutorialActive || currentSequence == null || !currentSequence.allowSkip) return;
		FinishTutorial();
	}

	// Interruption: release the screen and keep the step, so the next start resumes there. Not marked done.
	public void AbandonTutorial()
	{
		if (!isTutorialActive || currentSequence == null) return;

		string id = currentSequence.tutorialId;
		TutorialProgress.SetStep(id, currentStepIndex);
		ReleaseScreen();
		OnTutorialAbandoned?.Invoke(id);
	}

	// Every exit path ends here, so the screen, the time pause and the skip button are always released.
	private void ReleaseScreen()
	{
		CloseTutorialUI();
		if (_pausedTimeForTutorial)
		{
			_pausedTimeForTutorial = false;
			if (TimeManager.Instance != null)
				TimeManager.Instance.ResumeTime();
			else
				Time.timeScale = 1f;
		}
	}

	private void CloseTutorialUI()
	{
		isTutorialActive = false;
		isWaitingForTarget = false;
		currentSequence = null;
		if (skipButton != null) skipButton.gameObject.SetActive(false);
		CleanUpTrackedButton();
		if (fullScreenBlocker != null) fullScreenBlocker.SetActive(false);
		dialogPanel.SetActive(false);
		fingerPointer.gameObject.SetActive(false);
		if (maskContainer) maskContainer.gameObject.SetActive(false);
		if (solidDarkMask) solidDarkMask.SetActive(false);

		if (fingerTween != null) fingerTween.Kill();
		OnStepChanged?.Invoke();
	}
}

// Auxiliary class: transparent clickable overlay
public class TutorialRaycastFilter : MonoBehaviour, ICanvasRaycastFilter, IPointerClickHandler
{
	public TutorialManager manager;

	public bool IsRaycastLocationValid(Vector2 sp, Camera eventCamera)
	{
		if (manager == null) return true;
		return manager.ShouldBlockRaycast(sp, eventCamera);
	}

	public void OnPointerClick(PointerEventData eventData)
	{
		if (manager != null) manager.OnBlockerClicked();
	}
}