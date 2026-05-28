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

		CloseTutorialUI();
	}

	private void OnDestroy() => SceneManager.sceneUnloaded -= OnSceneUnloaded;

	private void OnSceneUnloaded(Scene scene)
	{
		activeTargets.Clear();
		CleanUpTrackedButton();
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
		if (PlayerPrefs.GetInt("TUTORIAL_DONE_" + sequence.tutorialId, 0) == 1) return;

		currentSequence = sequence;
		currentStepIndex = 0;
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

			if (step.characterIcon != null)
			{
				dialogIcon.gameObject.SetActive(true);
				dialogIcon.sprite = step.characterIcon;
			}
			else dialogIcon.gameObject.SetActive(false);

			RectTransform dialogRect = dialogPanel.GetComponent<RectTransform>();
			switch (step.dialogPosition)
			{
				case DialogPosition.Top:
					dialogRect.anchorMin = new Vector2(0, 1);
					dialogRect.anchorMax = new Vector2(1, 1);
					dialogRect.pivot = new Vector2(0.5f, 1);
					dialogRect.anchoredPosition = new Vector2(0, -100);
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
					dialogRect.anchoredPosition = new Vector2(0, 100);
					break;
			}

			dialogPanel.transform.localScale = Vector3.zero;
			dialogPanel.transform.DOScale(1f, 0.4f).SetEase(Ease.OutBack).SetUpdate(true);
		}

		// --- Finger and Target ---
		if (step.stepType != TutorialStepType.DialogOnly)
		{
			if (activeTargets.TryGetValue(step.targetId, out RectTransform targetRect) && targetRect != null)
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
			}
		}
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

		if (activeTargets.TryGetValue(step.targetId, out RectTransform targetRect) && targetRect != null)
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
			PlayerPrefs.SetInt("TUTORIAL_DONE_" + currentSequence.tutorialId, 1);
			PlayerPrefs.Save();
		}
		CloseTutorialUI();
		if (_pausedTimeForTutorial)
		{
			_pausedTimeForTutorial = false;
			if (TimeManager.Instance != null)
				TimeManager.Instance.ResumeTime();
			else
				Time.timeScale = 1f;
		}
		OnTutorialFinished?.Invoke();
	}

	private void CloseTutorialUI()
	{
		isTutorialActive = false;
		CleanUpTrackedButton();
		if (fullScreenBlocker != null) fullScreenBlocker.SetActive(false);
		dialogPanel.SetActive(false);
		fingerPointer.gameObject.SetActive(false);
		if (maskContainer) maskContainer.gameObject.SetActive(false);
		if (solidDarkMask) solidDarkMask.SetActive(false);

		if (fingerTween != null) fingerTween.Kill();
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