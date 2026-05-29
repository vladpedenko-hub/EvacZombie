using UnityEngine;

public class UI_Pulsator : MonoBehaviour
{
	[Header("Pulse Settings")]
	public float pulseSpeed = 2f;      // pulse speed
	public float maxScaleMulti = 1.2f; // max scale multiplier (1.2 means 20% bigger)
	public float minScaleMulti = 0.9f; // min scale multiplier

	private Vector3 baseScale;

	private void Start()
	{
		// Store the starting scale, used as the baseline for pulsing
		baseScale = transform.localScale;
	}

	private void Update()
	{
		// Use Sin to get a smooth value cycling between -1 and 1
		float sinValue = Mathf.Sin(Time.time * pulseSpeed);

		// Remap to get a value between minScale and maxScale
		float t = Mathf.InverseLerp(-1f, 1f, sinValue);
		float currentScaleMulti = Mathf.Lerp(minScaleMulti, maxScaleMulti, t);

		// Apply the new scale to the transform
		transform.localScale = baseScale * currentScaleMulti;
	}
}