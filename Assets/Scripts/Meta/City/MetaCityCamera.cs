using UnityEngine;
using UnityEngine.EventSystems;

// Orthographic top-down camera. Drag to pan, clamped to the city bounds. No zoom.
[RequireComponent(typeof(Camera))]
public class MetaCityCamera : MonoBehaviour
{
	private Camera cam;
	private Rect panBounds;
	private bool panning;
	private Vector3 lastMouse;

	public Camera Cam => cam;

	public void Configure(Rect bounds, float orthoSize)
	{
		cam = GetComponent<Camera>();
		cam.orthographic = true;
		cam.orthographicSize = orthoSize;
		cam.clearFlags = CameraClearFlags.SolidColor;
		cam.backgroundColor = new Color(0.12f, 0.14f, 0.12f);

		transform.rotation = Quaternion.Euler(90f, 0f, 0f);
		panBounds = bounds;
		transform.position = new Vector3(bounds.center.x, 50f, bounds.center.y);
	}

	private void LateUpdate()
	{
		if (cam == null) return;

		if (Input.GetMouseButtonDown(0))
		{
			// A drag that starts on UI (panel, buttons) must not move the city.
			panning = EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject();
			lastMouse = Input.mousePosition;
		}

		if (Input.GetMouseButtonUp(0)) panning = false;

		if (panning && Input.GetMouseButton(0))
		{
			Vector3 delta = Input.mousePosition - lastMouse;
			lastMouse = Input.mousePosition;

			float unitsPerPixel = cam.orthographicSize * 2f / Screen.height;
			transform.position += new Vector3(-delta.x, 0f, -delta.y) * unitsPerPixel;
		}

		Vector3 p = transform.position;
		p.x = Mathf.Clamp(p.x, panBounds.xMin, panBounds.xMax);
		p.z = Mathf.Clamp(p.z, panBounds.yMin, panBounds.yMax);
		transform.position = p;
	}

	// Pans the camera so the given city point is centered. Used for routing focus.
	public void CenterOn(Vector2 cityPoint)
	{
		transform.position = new Vector3(cityPoint.x, transform.position.y, cityPoint.y);
	}
}
