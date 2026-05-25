using UnityEngine;

// [ГДЕ ВИСИТ]: Прямо на объекте Main Camera на игровой сцене.
// [НАСТРОЙКИ]: Никаких ссылок перетаскивать не нужно, он сам возьмет компонент Camera.
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

	// Этот метод мы вызовем из LevelManager при загрузке уровня
	public void SetupCamera(LevelData data)
	{
		if (data == null || cam == null) return;

		// Ставим позицию и поворот
		transform.position = data.cameraPosition;
		transform.rotation = Quaternion.Euler(data.cameraRotation);

		// Включаем нужный режим
		cam.orthographic = (data.cameraType == LevelData.CameraType.Orthographic);

		// Применяем зум в зависимости от режима
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