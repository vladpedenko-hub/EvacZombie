using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

// Off-screen threat indicators: pooled UI arrows at the screen edge pointing toward zombie
// clusters that are off-camera, with one dedicated, visually distinct indicator for a boss.
// Self-bootstrapping (same RuntimeInitializeOnLoadMethod pattern as Feedback/AudioManager) --
// no scene or prefab wiring needed. Idle no-op outside Gameplay (Zombie.AllZombies is empty in
// MainMenu, so Update() bails out immediately).
public class OffscreenThreatIndicatorManager : MonoBehaviour
{
	public static OffscreenThreatIndicatorManager Instance;

	private class Slot
	{
		public RectTransform rect;
		public float pulseTimer = -1f;
	}

	private RectTransform safeAreaContainer;
	private readonly List<Slot> regularSlots = new List<Slot>();
	private Slot bossSlot;

	private float pollTimer;
	private int lastOffscreenRegularCount;
	private bool lastBossOffscreen;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ResetStatics() => Instance = null;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Bootstrap()
	{
		if (Instance != null) return;
		var go = new GameObject("[OffscreenThreatIndicatorManager]");
		DontDestroyOnLoad(go);
		go.AddComponent<OffscreenThreatIndicatorManager>();
	}

	private void Awake()
	{
		Instance = this;
		BuildUi();
	}

	private void BuildUi()
	{
		Canvas canvas = RuntimeUiKit.CreateOverlayCanvas("ThreatIndicatorCanvas", 400, transform);
		// CreateOverlayCanvas sets a fixed matchWidthOrHeight -- AspectAwareCanvasScaler
		// (Awake fires immediately on AddComponent) corrects it per the brief's SafeArea/
		// aspect-aware-scaler constraint, same as every other Canvas in this project.
		canvas.gameObject.AddComponent<AspectAwareCanvasScaler>();

		RectTransform safeRoot = RuntimeUiKit.CreateRect("SafeAreaContainer", canvas.transform);
		RuntimeUiKit.Stretch(safeRoot);
		safeRoot.gameObject.AddComponent<SafeArea>();
		safeAreaContainer = safeRoot;

		CameraReadabilityConfig config = CameraReadabilityConfig.Active;
		int count = config != null ? Mathf.Max(1, config.maxIndicatorCount) : 8;

		for (int i = 0; i < count; i++)
			regularSlots.Add(CreateSlot("RegularIndicator", config, isBoss: false));

		bossSlot = CreateSlot("BossIndicator", config, isBoss: true);
	}

	private Slot CreateSlot(string name, CameraReadabilityConfig config, bool isBoss)
	{
		RectTransform rt = RuntimeUiKit.CreateRect(name, safeAreaContainer);

		float baseSize = config != null ? config.indicatorSize : 64f;
		float size = isBoss ? baseSize * (config != null ? config.bossIndicatorSizeMultiplier : 1.5f) : baseSize;
		rt.sizeDelta = new Vector2(size, size);
		rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
		rt.pivot = new Vector2(0.5f, 0.5f);

		Image img = rt.gameObject.AddComponent<Image>();
		img.sprite = RuntimeUiKit.Triangle();
		img.color = isBoss
			? (config != null ? config.bossIndicatorColor : new Color(0.65f, 0.1f, 0.85f, 0.95f))
			: (config != null ? config.regularIndicatorColor : new Color(0.85f, 0.15f, 0.15f, 0.9f));
		img.raycastTarget = false;

		rt.gameObject.SetActive(false);
		return new Slot { rect = rt };
	}

	private void Update()
	{
		pollTimer -= Time.deltaTime;
		if (pollTimer > 0f) return;

		CameraReadabilityConfig config = CameraReadabilityConfig.Active;
		pollTimer = config != null ? Mathf.Max(0.05f, config.indicatorPollInterval) : 0.25f;

		UnityEngine.Camera cam = UnityEngine.Camera.main;
		if (cam == null || Zombie.AllZombies.Count == 0)
		{
			HideAll();
		}
		else
		{
			UpdateIndicators(cam, config);
		}

		// Pulses tick every frame regardless of poll cadence, for a smooth settle animation.
	}

	private void LateUpdate()
	{
		CameraReadabilityConfig config = CameraReadabilityConfig.Active;
		TickPulse(bossSlot, config);
		foreach (Slot slot in regularSlots) TickPulse(slot, config);
	}

	private void HideAll()
	{
		foreach (Slot slot in regularSlots) if (slot.rect.gameObject.activeSelf) slot.rect.gameObject.SetActive(false);
		if (bossSlot.rect.gameObject.activeSelf) bossSlot.rect.gameObject.SetActive(false);
		lastOffscreenRegularCount = 0;
		lastBossOffscreen = false;
	}

	private void UpdateIndicators(UnityEngine.Camera cam, CameraReadabilityConfig config)
	{
		float clusterRadius = config != null ? config.indicatorClusterRadius : 15f;
		float insetFraction = config != null ? config.indicatorEdgeInset : 0.04f;

		List<Zombie> offscreenRegular = new List<Zombie>();
		Zombie offscreenBoss = null;

		foreach (Zombie z in Zombie.AllZombies)
		{
			if (z == null || z.IsDead) continue;
			if (!IsOffscreen(cam, z.transform.position)) continue;

			if (z is ZombieBoss) { if (offscreenBoss == null) offscreenBoss = z; }
			else offscreenRegular.Add(z);
		}

		bool bossNowOffscreen = offscreenBoss != null;
		if (bossNowOffscreen)
		{
			PlaceIndicator(bossSlot, cam, offscreenBoss.transform.position, insetFraction);
			if (!lastBossOffscreen) Pulse(bossSlot, config);
		}
		else if (bossSlot.rect.gameObject.activeSelf)
		{
			bossSlot.rect.gameObject.SetActive(false);
		}
		lastBossOffscreen = bossNowOffscreen;

		List<List<Zombie>> clusters = ClusterByProximity(offscreenRegular, clusterRadius);

		// Approximation (not per-spawn-event tracking, which would need a LevelManager hook):
		// pulse every visible cluster whenever the total off-screen count has grown since the
		// last poll. Good enough to read as "something new just showed up" without needing to
		// touch LevelManager.cs for a real spawn-event callback.
		bool isNewBatch = offscreenRegular.Count > lastOffscreenRegularCount;
		lastOffscreenRegularCount = offscreenRegular.Count;

		int shown = Mathf.Min(clusters.Count, regularSlots.Count);
		for (int i = 0; i < regularSlots.Count; i++)
		{
			if (i < shown)
			{
				Vector3 centroid = Vector3.zero;
				foreach (Zombie z in clusters[i]) centroid += z.transform.position;
				centroid /= clusters[i].Count;

				PlaceIndicator(regularSlots[i], cam, centroid, insetFraction);
				if (isNewBatch) Pulse(regularSlots[i], config);
			}
			else if (regularSlots[i].rect.gameObject.activeSelf)
			{
				regularSlots[i].rect.gameObject.SetActive(false);
			}
		}
	}

	private static List<List<Zombie>> ClusterByProximity(List<Zombie> zombies, float radius)
	{
		var clusters = new List<List<Zombie>>();
		foreach (Zombie z in zombies)
		{
			bool added = false;
			foreach (var cluster in clusters)
			{
				if (Vector3.Distance(cluster[0].transform.position, z.transform.position) < radius)
				{
					cluster.Add(z);
					added = true;
					break;
				}
			}
			if (!added) clusters.Add(new List<Zombie> { z });
		}
		return clusters;
	}

	private static bool IsOffscreen(UnityEngine.Camera cam, Vector3 worldPos)
	{
		Vector3 vp = cam.WorldToViewportPoint(worldPos);
		return vp.z < 0f || vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f;
	}

	// Standard screen-edge-indicator projection: direction from the safe-area center to the
	// (possibly off-screen) point, clamped to the inset rectangle's boundary -- rectangular, not
	// circular, so it correctly reaches into the corners on any aspect ratio.
	private void PlaceIndicator(Slot slot, UnityEngine.Camera cam, Vector3 worldPos, float insetFraction)
	{
		slot.rect.gameObject.SetActive(true);

		Rect safeArea = Screen.safeArea;
		Vector3 screenPoint = cam.WorldToScreenPoint(worldPos);
		Vector2 center = safeArea.center;
		Vector2 dir = new Vector2(screenPoint.x, screenPoint.y) - center;
		if (screenPoint.z < 0f) dir = -dir; // behind the camera (shouldn't happen for this top-down setup, kept for robustness)
		if (dir.sqrMagnitude < 0.0001f) dir = Vector2.up;
		dir.Normalize();

		float insetMargin = safeArea.height * insetFraction;
		float halfW = safeArea.width * 0.5f - insetMargin;
		float halfH = safeArea.height * 0.5f - insetMargin;
		float tx = Mathf.Abs(dir.x) > 0.0001f ? halfW / Mathf.Abs(dir.x) : float.PositiveInfinity;
		float ty = Mathf.Abs(dir.y) > 0.0001f ? halfH / Mathf.Abs(dir.y) : float.PositiveInfinity;
		float t = Mathf.Min(tx, ty);
		Vector2 edgeScreenPoint = center + dir * t;

		RectTransformUtility.ScreenPointToLocalPointInRectangle(safeAreaContainer, edgeScreenPoint, null, out Vector2 localPoint);
		slot.rect.anchoredPosition = localPoint;

		float angleDeg = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f; // Triangle() points up by default
		slot.rect.localRotation = Quaternion.Euler(0f, 0f, angleDeg);
	}

	private static void Pulse(Slot slot, CameraReadabilityConfig config)
	{
		slot.pulseTimer = config != null ? Mathf.Max(0.01f, config.indicatorPulseDuration) : 0.35f;
	}

	private static void TickPulse(Slot slot, CameraReadabilityConfig config)
	{
		if (slot.pulseTimer < 0f) return;

		float duration = config != null ? Mathf.Max(0.01f, config.indicatorPulseDuration) : 0.35f;
		float pulseScale = config != null ? config.indicatorPulseScale : 1.6f;

		slot.pulseTimer -= Time.deltaTime;
		float tNorm = 1f - Mathf.Clamp01(slot.pulseTimer / duration);
		slot.rect.localScale = Vector3.one * Mathf.Lerp(pulseScale, 1f, tNorm);

		if (slot.pulseTimer < 0f) slot.rect.localScale = Vector3.one;
	}
}
