using UnityEngine;

// This script lives on a building object (house).
// The object needs a BoxCollider and MeshRenderer.
// The material must use Rendering Mode: Transparent or Fade.
public class BuildingFader : MonoBehaviour
{
	[SerializeField] private float transparentAlpha = 0.3f; // Target alpha when faded (0 = invisible, 1 = opaque)
	[SerializeField] private float fadeSpeed = 5f;          // Speed of fade in/out

	private MeshRenderer meshRenderer;
	private Color originalColor;
	private Color targetColor;
	private bool isBlocking = false;

	void Start()
	{
		meshRenderer = GetComponent<MeshRenderer>();
		if (meshRenderer != null)
		{
			originalColor = meshRenderer.material.color;
			targetColor = originalColor;
		}
	}

	void Update()
	{
		if (meshRenderer == null) return;

		// Smoothly interpolate toward target color (transparent or opaque)
		meshRenderer.material.color = Color.Lerp(meshRenderer.material.color, targetColor, Time.deltaTime * fadeSpeed);

		// Reset every frame; FadeOut() must be called again to keep faded
		isBlocking = false;
		targetColor = originalColor;
	}

	// Call this method every frame while the building is blocking the camera view
	public void FadeOut()
	{
		isBlocking = true;
		targetColor = new Color(originalColor.r, originalColor.g, originalColor.b, transparentAlpha);
	}
}