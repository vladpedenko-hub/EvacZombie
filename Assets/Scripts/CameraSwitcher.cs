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
		cam.orthographicSize = orthoSize;
		transform.position = orthoPos;
		transform.rotation = Quaternion.Euler(orthoRot);
	}

	private void ApplyPersp()
	{
		cam.orthographic = false;
		cam.fieldOfView = perspFOV;
		transform.position = perspPos;
		transform.rotation = Quaternion.Euler(perspRot);
	}
}