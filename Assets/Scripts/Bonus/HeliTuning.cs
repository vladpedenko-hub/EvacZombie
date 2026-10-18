using UnityEngine;

// feature/heli-rework: single tunables asset for the base Helicopter evac card's new feel (inbound
// timing, live-magnet rescan, heat pressure, parallel boarding throughput). Lives at
// Assets/Resources/HeliTuning.asset so HelicopterController/InputManager/SandboxDevPanel can all
// reach it via the static .Active accessor without any scene/prefab wiring -- same pattern as
// CoreLoopV2Config/CameraReadabilityConfig. Per-card stats (capacity/speed/attractRadius/loadTime/
// boardingCooldown) stay on CardData as before -- this asset only holds the new feel-tuning knobs
// that aren't per-card-level stats.
[CreateAssetMenu(fileName = "HeliTuning", menuName = "ZombieGame/Heli Tuning")]
public class HeliTuning : ScriptableObject
{
	private static HeliTuning cached;

	public static HeliTuning Active
	{
		get
		{
			if (cached == null) cached = Resources.Load<HeliTuning>("HeliTuning");
			return cached;
		}
	}

	// Support "Enter Play Mode without domain reload" -- don't leak a stale cached reference
	// between play sessions.
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ResetStatics() => cached = null;

	[Header("Inbound (not used by the Last Transport, which keeps its old instant descent)")]
	[Tooltip("Seconds from the tap/call to touchdown. The descent eases out over exactly this duration.")]
	public float inboundTime = 2.8f;

	[Header("Live magnet")]
	[Tooltip("Master toggle for the periodic rescan below (SandboxDevPanel's 'Old feel' preset turns this off).")]
	public bool enableLiveRescan = true;

	[Tooltip("Seconds between rescans of Human.AllHumans/Scientist.AllScientists for stragglers who wandered into attractRadius after the initial call-time scan.")]
	public float rescanInterval = 0.5f;

	[Header("Throughput")]
	[Tooltip("Max units boarding in parallel (each as its own coroutine).")]
	public int boardingSlots = 3;

	[Tooltip("Minimum gap, in seconds, between consecutive parallel boarding starts within the same slot-fill round.")]
	public float boardingStagger = 0.2f;

	[Header("Heat (replaces the old instant-panic-abort; disabled whenever panicRadius <= 0, e.g. the heli_no_panic run flag)")]
	[Tooltip("Master toggle for the heat multiplier below (SandboxDevPanel's 'Old feel' preset turns this off; hardAbortRadius still applies regardless of this toggle).")]
	public bool enableHeat = true;

	[Tooltip("Zombies within this radius of the heli contribute to heat.")]
	public float heatRadius = 5f;

	[Tooltip("Heat multiplier added per zombie within heatRadius, per second of exposure -- heatMultiplier = clamp(1 + count * this, 1, heatMaxMultiplier).")]
	public float heatPerZombiePerSec = 1.0f;

	[Tooltip("Upper clamp on the heat multiplier.")]
	public float heatMaxMultiplier = 3.5f;

	[Tooltip("At max heat, the boarding interval is multiplied by 1/this (e.g. 0.5 => boarding takes twice as long at max heat).")]
	public float heatBoardSlowFactor = 0.5f;

	[Tooltip("A zombie this close still forces an immediate takeoff (the old hard panic-abort), independent of the heat multiplier.")]
	public float hardAbortRadius = 1.5f;

	[Header("Full load")]
	[Tooltip("Supply granted (via the existing Supply API) when the heli takes off because it reached maxCapacity.")]
	public int fullLoadSupplyBonus = 1;

	[Header("Landing point")]
	[Tooltip("If true, the Helicopter card's placement point snaps to the nearest NavMesh point within snapMaxDistance instead of using the raw tap point.")]
	public bool snapToWalkable = true;

	[Tooltip("Max distance (NavMesh.SamplePosition) the call position may snap. No point found within this range rejects the placement, same as any other invalid placement.")]
	public float snapMaxDistance = 4f;
}
