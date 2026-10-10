using UnityEngine;

// [WHERE IT LIVES]: On the Main Camera in the gameplay scene.
// [REQUIREMENT]: The object must have a Camera component attached.
[RequireComponent(typeof(Camera))]
public class CameraController : MonoBehaviour
{
	public static CameraController Instance;
	private Camera cam;

	[Header("Fit-to-width")]
	[Tooltip("Extra breathing room added on top of the tight fit to the authored framing.")]
	[Range(0f, 0.3f)] public float fitMarginPercent = 0.08f;

	// Cached per-level so CameraSwitcher (toggled independently, any time) can reuse the same
	// HUD/card-hand reservation without needing its own LevelManager lookup.
	public float TopReservedScreenFraction { get; private set; }
	public float BottomReservedScreenFraction { get; private set; }

	private void Awake()
	{
		Instance = this;
		cam = GetComponent<Camera>();
	}

	// Called from LevelManager when setting up the level
	public void SetupCamera(LevelData data)
	{
		SetupCamera(data, 0f, 0f);
	}

	// topReservedScreenFraction/bottomReservedScreenFraction: fraction of the screen height
	// covered by the HUD / card hand (+ safe area), so the playable area isn't hidden under them.
	public void SetupCamera(LevelData data, float topReservedScreenFraction, float bottomReservedScreenFraction)
	{
		if (data == null || cam == null) return;

		transform.position = data.cameraPosition;
		transform.rotation = Quaternion.Euler(data.cameraRotation);
		cam.orthographic = (data.cameraType == LevelData.CameraType.Orthographic);

		TopReservedScreenFraction = topReservedScreenFraction;
		BottomReservedScreenFraction = bottomReservedScreenFraction;

		if (cam.orthographic)
		{
			cam.orthographicSize = CameraFraming.ComputeOrthographicSize(data.orthographicSize, cam.aspect,
				topReservedScreenFraction, bottomReservedScreenFraction, fitMarginPercent);
		}
		else
		{
			cam.fieldOfView = data.cameraFieldOfView;
			float? groundDistance = CameraFraming.GetGroundDistance(transform.position, transform.forward);
			if (groundDistance.HasValue)
			{
				float pullback = CameraFraming.ComputePerspectivePullback(groundDistance.Value, cam.aspect,
					topReservedScreenFraction, bottomReservedScreenFraction, fitMarginPercent);
				if (pullback > 0f) transform.position -= transform.forward * pullback;
			}
		}
	}
}
