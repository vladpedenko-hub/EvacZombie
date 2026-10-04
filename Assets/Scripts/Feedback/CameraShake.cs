using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Screen shake that does NOT fight CameraController / LevelManager: the offset is applied only for the
/// duration of rendering (URP begin/endCameraRendering) and removed right after, so gameplay code never
/// sees a shifted camera. Auto-created before the first scene.
/// </summary>
public class CameraShake : MonoBehaviour
{
	private static CameraShake instance;

	private float amplitude;
	private float duration;
	private float elapsed;
	private Vector3 offset;

	private Camera appliedCam;
	private Vector3 appliedOffset;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ResetStatics() => instance = null;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Bootstrap()
	{
		if (instance != null) return;
		var go = new GameObject("[CameraShake]");
		DontDestroyOnLoad(go);
		instance = go.AddComponent<CameraShake>();
	}

	private void OnEnable()
	{
		RenderPipelineManager.beginCameraRendering += OnBeginCamera;
		RenderPipelineManager.endCameraRendering += OnEndCamera;
	}

	private void OnDisable()
	{
		RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
		RenderPipelineManager.endCameraRendering -= OnEndCamera;
	}

	/// <summary>amp in world units (0.2–0.6 is a good range), duration in seconds.</summary>
	public static void Shake(float amp, float seconds)
	{
		if (instance == null) return;
		if (amp >= instance.CurrentAmplitude())
		{
			instance.amplitude = amp;
			instance.duration = Mathf.Max(0.05f, seconds);
			instance.elapsed = 0f;
		}
	}

	private float CurrentAmplitude()
	{
		if (duration <= 0f || elapsed >= duration) return 0f;
		return amplitude * (1f - elapsed / duration);
	}

	private void Update()
	{
		float amp = CurrentAmplitude();
		if (amp <= 0.0001f)
		{
			offset = Vector3.zero;
			duration = 0f;
			return;
		}

		elapsed += Time.unscaledDeltaTime;

		Camera cam = Camera.main;
		if (cam == null) { offset = Vector3.zero; return; }

		Vector2 r = Random.insideUnitCircle * amp;
		offset = cam.transform.right * r.x + cam.transform.up * r.y;
	}

	private void OnBeginCamera(ScriptableRenderContext context, Camera cam)
	{
		if (offset == Vector3.zero) return;
		if (cam != Camera.main) return;

		appliedCam = cam;
		appliedOffset = offset;
		cam.transform.position += appliedOffset;
	}

	private void OnEndCamera(ScriptableRenderContext context, Camera cam)
	{
		if (appliedCam == null || cam != appliedCam) return;

		cam.transform.position -= appliedOffset;
		appliedCam = null;
	}
}
