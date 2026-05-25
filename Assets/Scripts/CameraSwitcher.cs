using UnityEngine;

[RequireComponent(typeof(Camera))]
public class CameraSwitcher : MonoBehaviour
{
	private Camera cam;
	private bool isOrtho = true;

	[Header("Настройки 2D (Ортография)")]
	public float orthoSize = 10f;
	public Vector3 orthoPos = new Vector3(0, 30f, 0);
	public Vector3 orthoRot = new Vector3(75f, 0, 0);

	[Header("Настройки Псевдо-3D (Перспектива)")]
	public float perspFOV = 35f;
	public Vector3 perspPos = new Vector3(0, 45f, -15f);
	public Vector3 perspRot = new Vector3(65f, 0, 0);

	private void Start()
	{
		cam = GetComponent<Camera>();
		ApplyOrtho(); // При старте включаем 2D
	}

	private void Update()
	{
		// Переключение по нажатию пробела в редакторе (или тапом 3 пальцами на телефоне)
		if (Input.GetKeyDown(KeyCode.Space) || (Input.touchCount == 3 && Input.GetTouch(0).phase == TouchPhase.Began))
		{
			isOrtho = !isOrtho;

			if (isOrtho) ApplyOrtho();
			else ApplyPersp();

			Debug.Log(isOrtho ? "<color=yellow>Камера: 2D ОРТОГРАФИЯ</color>" : "<color=green>Камера: 3D ПЕРСПЕКТИВА</color>");
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