using UnityEngine;

// [WHERE IT LIVES]: Create an empty RectTransform inside the Canvas (call it SafeAreaContainer).
// Stretch it to fill the whole screen (Stretch/Stretch, all zeroes).
// Place ALL your UI (Map, Menus, Popups) INSIDE SafeAreaContainer.
// Attach this script to SafeAreaContainer.
[RequireComponent(typeof(RectTransform))]
public class SafeArea : MonoBehaviour
{
	private RectTransform rectTransform;
	private Rect lastSafeArea = new Rect(0, 0, 0, 0);

	private void Awake()
	{
		rectTransform = GetComponent<RectTransform>();
		ApplySafeArea();
	}

	private void Update()
	{
		// Check if the safe area changed at runtime (can happen on Android when rotating)
		if (Screen.safeArea != lastSafeArea)
		{
			ApplySafeArea();
		}
	}

	private void ApplySafeArea()
	{
		lastSafeArea = Screen.safeArea;

		// Convert safe area rect to normalized anchor coordinates
		Vector2 anchorMin = lastSafeArea.position;
		Vector2 anchorMax = lastSafeArea.position + lastSafeArea.size;

		anchorMin.x /= Screen.width;
		anchorMin.y /= Screen.height;
		anchorMax.x /= Screen.width;
		anchorMax.y /= Screen.height;

		rectTransform.anchorMin = anchorMin;
		rectTransform.anchorMax = anchorMax;
	}
}