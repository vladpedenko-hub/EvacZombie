using UnityEngine;
using UnityEngine.UI;

// Picks CanvasScaler.matchWidthOrHeight from the current screen aspect instead of a single
// fixed value, so one CanvasScaler setup stays correct from 16:9 phones through 20:9 phones
// and 4:3 tablets without per-device tuning.
[RequireComponent(typeof(CanvasScaler))]
public class AspectAwareCanvasScaler : MonoBehaviour
{
	private CanvasScaler scaler;
	private int lastWidth;
	private int lastHeight;

	private void Awake()
	{
		scaler = GetComponent<CanvasScaler>();
		Apply();
	}

	private void Update()
	{
		// Screen size changes on rotation (shouldn't happen, portrait-locked) and on some
		// Android devices when the on-screen nav bar / cutout area changes at runtime.
		if (Screen.width == lastWidth && Screen.height == lastHeight) return;
		Apply();
	}

	private void Apply()
	{
		lastWidth = Screen.width;
		lastHeight = Screen.height;

		float referenceAspect = scaler.referenceResolution.y / scaler.referenceResolution.x;
		float currentAspect = (float)Screen.height / Screen.width;

		// Taller than reference (19:9-20:9 phones): match height so the vertical rhythm
		// (HUD / card-hand offsets) stays exact and the extra space shows up as width.
		// Reference or wider (4:3 tablets): match width so edge-anchored panels stay
		// full-bleed and the extra space shows up as height instead of squeezing width.
		scaler.matchWidthOrHeight = currentAspect > referenceAspect ? 1f : 0f;
	}
}
