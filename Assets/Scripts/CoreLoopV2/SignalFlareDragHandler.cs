using UnityEngine;
using UnityEngine.EventSystems;

// Core Loop v2 Signal Flare drag input (Docs/CORE_LOOP_V2.md Phase 6). Added to the ability
// button GameObject only when a v2 level with the ability enabled loads (by AbilityManager.
// Initialize -> SetEnabledOnButton), so legacy levels' button never gets this component at all --
// no risk of it changing Unity's click-vs-drag disambiguation on a level that must stay byte-
// identical. Draws its own radius circle: a small, deliberate duplicate of InputManager's (its
// drag pipeline is typed around CardData, which doesn't fit a non-card ability).
public class SignalFlareDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
	private Camera mainCam;
	private LineRenderer radiusCircle;
	private bool isDragging;

	public static void SetEnabledOnButton(bool want)
	{
		AbilityButtonUI button = Object.FindFirstObjectByType<AbilityButtonUI>();
		if (button == null) return;

		SignalFlareDragHandler existing = button.GetComponent<SignalFlareDragHandler>();

		if (want && existing == null)
			button.gameObject.AddComponent<SignalFlareDragHandler>();
		else if (!want && existing != null)
			Destroy(existing);
	}

	private void Start()
	{
		mainCam = Camera.main;

		GameObject circleObj = new GameObject("FlareRadiusVisual");
		radiusCircle = circleObj.AddComponent<LineRenderer>();
		radiusCircle.startWidth = 0.25f;
		radiusCircle.endWidth = 0.25f;
		radiusCircle.material = SharedMaterials.SpritesDefaultMaterial;
		radiusCircle.loop = true;
		radiusCircle.startColor = new Color(1f, 0.75f, 0.2f, 0.7f);
		radiusCircle.endColor = new Color(1f, 0.75f, 0.2f, 0.7f);
		radiusCircle.enabled = false;
	}

	public void OnBeginDrag(PointerEventData eventData)
	{
		if (AbilityManager.Instance == null || !AbilityManager.Instance.IsReady) return;

		isDragging = true;
		TimeManager.Instance?.StartSlowMo();
	}

	public void OnDrag(PointerEventData eventData)
	{
		if (!isDragging || mainCam == null) return;

		Ray ray = mainCam.ScreenPointToRay(eventData.position);
		if (Physics.Raycast(ray, out RaycastHit hit))
			DrawRadiusCircle(hit.point);
		else if (radiusCircle != null)
			radiusCircle.enabled = false;
	}

	public void OnEndDrag(PointerEventData eventData)
	{
		if (!isDragging) return;
		isDragging = false;

		if (radiusCircle != null) radiusCircle.enabled = false;
		TimeManager.Instance?.StopSlowMo();

		if (AbilityManager.Instance == null || !AbilityManager.Instance.IsReady || mainCam == null) return;
		if (CardManager.Instance != null && CardManager.Instance.IsScreenPointOverCardsPanel(eventData.position)) return;

		Ray ray = mainCam.ScreenPointToRay(eventData.position);
		if (Physics.Raycast(ray, out RaycastHit hit))
			AbilityManager.Instance.ActivateFlareAt(hit.point);
	}

	private void DrawRadiusCircle(Vector3 center)
	{
		CoreLoopV2Config cfg = CoreLoopV2Config.Active;
		float radius = cfg != null ? cfg.flareRadius : 12f;

		radiusCircle.enabled = true;
		radiusCircle.positionCount = 32;

		float angle = 0f;
		for (int i = 0; i < 32; i++)
		{
			float x = Mathf.Sin(Mathf.Deg2Rad * angle) * radius;
			float z = Mathf.Cos(Mathf.Deg2Rad * angle) * radius;
			radiusCircle.SetPosition(i, center + new Vector3(x, 0.2f, z));
			angle += 360f / 32f;
		}
	}

	private void OnDestroy()
	{
		if (radiusCircle != null) Destroy(radiusCircle.gameObject);
	}
}
