using UnityEngine;

// Single tunables asset for the whole camera-readability pass (zoom/pan range, edge auto-pan,
// level-intro shot, off-screen threat indicators, actor visual scale). Lives at
// Assets/Resources/CameraReadabilityConfig.asset so it's reachable by static accessor from
// anywhere (actor scripts, the camera controller, the indicator manager) without any scene or
// prefab wiring -- same spirit as this project's other self-bootstrapping managers.
[CreateAssetMenu(fileName = "CameraReadabilityConfig", menuName = "ZombieGame/Camera Readability Config")]
public class CameraReadabilityConfig : ScriptableObject
{
	private static CameraReadabilityConfig cached;

	public static CameraReadabilityConfig Active
	{
		get
		{
			if (cached == null) cached = Resources.Load<CameraReadabilityConfig>("CameraReadabilityConfig");
			return cached;
		}
	}

	// Support "Enter Play Mode without domain reload" -- don't leak a stale cached reference
	// between play sessions.
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ResetStatics() => cached = null;

	[Header("Zoom range")]
	[Tooltip("World-space height of a Human prefab's rendered body, before any scale multiplier below. Measured once from Human.prefab's MeshRenderer bounds (~0.3). Only used to derive the zoom-in limit below -- doesn't affect anything else. Retune if the Human model/scale ever changes.")]
	public float humanReferenceHeightWorldUnits = 0.3f;

	[Tooltip("How tall a Human should read on screen at maximum zoom-in, in millimetres on the reference device below. The brief's target: 8-10mm.")]
	public float targetHumanScreenHeightMm = 9f;

	[Tooltip("Visible screen height, in millimetres, of the reference device this is tuned against (a ~6.5\" phone in portrait).")]
	public float referenceScreenHeightMm = 150f;

	[Tooltip("Where the level starts, as a fraction between max zoom-out (0 = the existing fit-to-width framing, untouched) and max zoom-in (1 = the Human-readability limit derived above). Brief target: ~0.45-0.50.")]
	[Range(0f, 1f)] public float defaultZoomFraction = 0.475f;

	[Header("Pan")]
	[Tooltip("Extra world-space margin added around the computed playable-area bounds (spawn points + Building-tagged colliders + the level's initial human/scientist cluster) before clamping pan.")]
	public float panBoundsPadding = 4f;

	[Tooltip("One-finger drag (or left-mouse-drag in the Editor) responsiveness. 1 = the ground point under your finger stays under your finger; lower/higher under- or over-shoots.")]
	public float panSensitivity = 1f;

	[Header("Pinch / scroll zoom")]
	[Tooltip("Screen-pixel pinch-distance change needed to move zoomLevel across the full 0-1 range. Lower = more sensitive pinch.")]
	public float pinchDistanceForFullZoom = 800f;

	[Tooltip("Editor/desktop testing only: mouse-scroll-wheel zoom speed (zoomLevel change per scroll tick). Touch devices use pinch instead.")]
	public float scrollZoomSpeed = 0.08f;

	[Header("Edge auto-pan (while dragging a card)")]
	[Tooltip("How close to a screen edge (fraction of screen width/height) a dragged card needs to be before the camera starts auto-panning that way.")]
	[Range(0.01f, 0.3f)] public float edgeAutoPanScreenFraction = 0.1f;

	[Tooltip("Camera pan speed, world units/second, while edge auto-pan is active.")]
	public float edgeAutoPanSpeed = 18f;

	[Header("Level-intro shot")]
	[Tooltip("Seconds to hold the full zoom-out (fit-to-width) view at level start before zooming in.")]
	public float introHoldDuration = 1.2f;

	[Tooltip("Seconds to ease from the zoom-out intro shot to the default zoom/pan.")]
	public float introZoomDuration = 0.6f;

	[Tooltip("If true, any tap/click during the intro shot skips straight to the default view.")]
	public bool introSkippable = true;

	[Header("Off-screen threat indicators")]
	[Tooltip("How often (seconds) the off-screen-zombie scan re-runs. Lower = more responsive, more CPU.")]
	public float indicatorPollInterval = 0.25f;

	[Tooltip("World-space radius used to group nearby off-screen zombies into a single indicator, so a big wave doesn't spam one arrow per zombie.")]
	public float indicatorClusterRadius = 15f;

	[Tooltip("Max simultaneous regular-zombie indicators on screen. A boss always gets its own dedicated indicator on top of this cap.")]
	public int maxIndicatorCount = 8;

	[Tooltip("How far the indicator sits inset from the safe-area edge, as a fraction of screen height.")]
	[Range(0f, 0.15f)] public float indicatorEdgeInset = 0.04f;

	[Tooltip("Indicator icon size, in reference-resolution (1080x1920) pixels.")]
	public float indicatorSize = 64f;

	[Tooltip("Boss indicator icon size, as a multiplier over the regular indicator size above.")]
	public float bossIndicatorSizeMultiplier = 1.5f;

	[Tooltip("Scale pulse applied the moment an indicator appears (1 = no pulse).")]
	public float indicatorPulseScale = 1.6f;

	[Tooltip("Seconds for a newly-appeared indicator's pulse to settle back to 1x.")]
	public float indicatorPulseDuration = 0.35f;

	[Tooltip("Tint for regular zombie-cluster indicators.")]
	public Color regularIndicatorColor = new Color(0.85f, 0.15f, 0.15f, 0.9f);

	[Tooltip("Tint for the dedicated boss indicator.")]
	public Color bossIndicatorColor = new Color(0.65f, 0.1f, 0.85f, 0.95f);

	[Header("Actor visual scale (Human / Scientist / Zombie / ZombieBoss)")]
	[Tooltip("Global visual-only scale multiplier for all four actor types. 1 = unchanged. Colliders and NavMeshAgent radii are compensated/unaffected by this -- it only changes render size. Tune this in Editor to taste.")]
	public float actorVisualScaleMultiplier = 1f;
}
