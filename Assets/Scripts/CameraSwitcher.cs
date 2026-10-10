using UnityEngine;

[RequireComponent(typeof(Camera))]
public class CameraSwitcher : MonoBehaviour
{
	private Camera cam;
	private bool isOrtho = true;

	[Header("2D Settings (Orthographic)")]
	public float orthoSize = 10f;
	public Vector3 orthoPos = new Vector3(0, 30f, 0);
	public Vector3 orthoRot = new Vector3(75f, 0, 0);

	[Header("Quasi-3D Settings (Perspective)")]
	public float perspFOV = 35f;
	public Vector3 perspPos = new Vector3(0, 45f, -15f);
	public Vector3 perspRot = new Vector3(65f, 0, 0);

	private void Start()
	{
		cam = GetComponent<Camera>();
		ApplyOrtho(); // Start in 2D mode
	}

	private void Update()
	{
		// Toggle on Space key or 3-finger tap (mobile)
		if (Input.GetKeyDown(KeyCode.Space) || (Input.touchCount == 3 && Input.GetTouch(0).phase == TouchPhase.Began))
		{
			isOrtho = !isOrtho;

			if (isOrtho) ApplyOrtho();
			else ApplyPersp();

			Debug.Log(isOrtho ? "<color=yellow>Mode: 2D Orthographic</color>" : "<color=green>Mode: 3D Perspective</color>");
		}
	}

	private void ApplyOrtho()
	{
		cam.orthographic = true;
		transform.position = orthoPos;
		transform.rotation = Quaternion.Euler(orthoRot);

		// Reuse the current level's cached HUD/card-hand reservation (computed by
		// CameraController.SetupCamera) so this debug toggle also fits the city width/HUD.
		CameraController cc = CameraController.Instance;
		cam.orthographicSize = cc != null
			? CameraFraming.ComputeOrthographicSize(orthoSize, cam.aspect, cc.TopReservedScreenFraction, cc.BottomReservedScreenFraction, cc.fitMarginPercent)
			: orthoSize;
	}

	private void ApplyPersp()
	{
		cam.orthographic = false;
		transform.position = perspPos;
		transform.rotation = Quaternion.Euler(perspRot);
		cam.fieldOfView = perspFOV;

		CameraController cc = CameraController.Instance;
		float? groundDistance = CameraFraming.GetGroundDistance(transform.position, transform.forward);
		if (cc != null && groundDistance.HasValue)
		{
			float pullback = CameraFraming.ComputePerspectivePullback(groundDistance.Value, cam.aspect, cc.TopReservedScreenFraction, cc.BottomReservedScreenFraction, cc.fitMarginPercent);
			if (pullback > 0f) transform.position -= transform.forward * pullback;
		}
	}
}
