using UnityEngine;

// Stretches its RectTransform to Screen.safeArea so notches and home indicators never cover controls.
[RequireComponent(typeof(RectTransform))]
public class SafeAreaFitter : MonoBehaviour
{
	private RectTransform rect;
	private Rect appliedArea;

	private void Awake()
	{
		rect = (RectTransform)transform;
		Apply();
	}

	private void Update()
	{
		if (Screen.safeArea != appliedArea) Apply();
	}

	private void Apply()
	{
		appliedArea = Screen.safeArea;
		rect.anchorMin = new Vector2(appliedArea.xMin / Screen.width, appliedArea.yMin / Screen.height);
		rect.anchorMax = new Vector2(appliedArea.xMax / Screen.width, appliedArea.yMax / Screen.height);
		rect.offsetMin = Vector2.zero;
		rect.offsetMax = Vector2.zero;
	}
}
