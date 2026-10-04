using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Put on any UI object (or a parent) whose clicks must NOT trigger the automatic click sound.</summary>
public class SuppressClickSfx : MonoBehaviour { }

/// <summary>
/// Automatic click sound for every Button / Toggle in every scene — no per-button wiring.
/// Decides on pointer-DOWN which selectable was hit, and plays on pointer-UP if the pointer barely moved
/// (so swipes/scrolls that start on a button stay silent). Buttons named like close/back/cancel/no thanks
/// get the "back" sound instead.
/// </summary>
public class UiClickFeedback : MonoBehaviour
{
	private const float MaxTapMovePixels = 30f;

	private readonly List<RaycastResult> results = new List<RaycastResult>();
	private Selectable downTarget;
	private Vector2 downPos;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Bootstrap()
	{
		var go = new GameObject("[UiClickFeedback]");
		DontDestroyOnLoad(go);
		go.AddComponent<UiClickFeedback>();
	}

	private void Update()
	{
		if (EventSystem.current == null) return;

		if (Input.GetMouseButtonDown(0))
		{
			downPos = Input.mousePosition;
			downTarget = FindSelectableAt(downPos);
		}
		else if (Input.GetMouseButtonUp(0))
		{
			if (downTarget != null && (((Vector2)Input.mousePosition) - downPos).sqrMagnitude <= MaxTapMovePixels * MaxTapMovePixels)
			{
				string n = downTarget.name.ToLowerInvariant();
				bool isBack = n.Contains("close") || n.Contains("back") || n.Contains("cancel") || n.Contains("nothanks") || n.Contains("no_thanks");
				Feedback.Play(isBack ? FeedbackEvent.UiBack : FeedbackEvent.UiClick);
			}
			downTarget = null;
		}
	}

	private Selectable FindSelectableAt(Vector2 screenPos)
	{
		var pointer = new PointerEventData(EventSystem.current) { position = screenPos };
		results.Clear();
		EventSystem.current.RaycastAll(pointer, results);
		if (results.Count == 0) return null;

		// Only the top-most hit counts: a panel blocking a button means the button wasn't tapped.
		GameObject top = results[0].gameObject;
		if (top == null) return null;

		if (top.GetComponentInParent<SuppressClickSfx>() != null) return null;

		Selectable sel = top.GetComponentInParent<Selectable>();
		if (sel == null || !sel.IsInteractable()) return null;
		if (!(sel is Button) && !(sel is Toggle)) return null;

		return sel;
	}
}
