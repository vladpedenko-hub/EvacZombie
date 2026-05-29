using UnityEngine;

// [WHERE IT LIVES]: On the Main Camera in the gameplay scene.
// [REQUIREMENT]: The object must have a Camera component attached.
[RequireComponent(typeof(Camera))]
public class CameraController : MonoBehaviour
{
	public static CameraController Instance;
	private Camera cam;

	private void Awake()
	{
		Instance = this;
		cam = GetComponent<Camera>();
	}

	// Called from LevelManager when setting up the level
	public void SetupCamera(LevelData data)
	{
		if (data == null || cam == null) return;

		// Set position and rotation
		transform.position = data.cameraPosition;
		transform.rotation = Quaternion.Euler(data.cameraRotation);

		// Set projection mode
		cam.orthographic = (data.cameraType == LevelData.CameraType.Orthographic);

		// Apply size or FOV depending on projection
		if (cam.orthographic)
		{
			cam.orthographicSize = data.orthographicSize;
		}
		else
		{
			cam.fieldOfView = data.cameraFieldOfView;
		}
	}
}