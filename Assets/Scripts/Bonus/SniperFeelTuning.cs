using UnityEngine;

// feature/sniper-feel: single tunables asset for the Sniper "feel" pass -- aim timing, the
// pixel-width-scaled laser telegraph, the lock-on ring, shot tracer/recoil/kill-kick, and the
// lifetime arc. Lives at Assets/Resources/SniperFeelTuning.asset so Sniper.cs reaches it via the
// static .Active accessor with no scene/prefab wiring -- same pattern as CoreLoopV2Config.
[CreateAssetMenu(fileName = "SniperFeelTuning", menuName = "ZombieGame/Sniper Feel Tuning")]
public class SniperFeelTuning : ScriptableObject
{
	private static SniperFeelTuning cached;

	public static SniperFeelTuning Active
	{
		get
		{
			if (cached == null) cached = Resources.Load<SniperFeelTuning>("SniperFeelTuning");
			return cached;
		}
	}

	// Support "Enter Play Mode without domain reload" -- don't leak a stale cached reference
	// between play sessions.
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ResetStatics() => cached = null;

	[Header("Aim timing")]
	[Tooltip("Seconds spent aiming before firing (subsequent shots).")]
	public float aimDuration = 1.1f;

	[Tooltip("Fraction of aimDuration used for the very first shot (e.g. 0.35 = 65% faster).")]
	public float firstShotAimFactor = 0.35f;

	[Header("Aim telegraph")]
	[Tooltip("The laser is never thinner than this on screen, in pixels -- recomputed every frame " +
		"from the camera's ortho size/FOV and Screen.height so it stays readable at any zoom.")]
	public float laserMinPixelWidth = 3f;

	[Tooltip("Show a lock-on ring on the target that grows/brightens over the aim, blinking in the final 0.15s.")]
	public bool lockOnRing = true;

	[Header("Shot feedback")]
	[Tooltip("Seconds for the tracer streak to travel from the muzzle to the farthest pierced target.")]
	public float tracerDuration = 0.12f;

	[Tooltip("Recoil punch-scale strength on the sniper's visual child, as a fraction of scale.")]
	public float recoilKick = 0.12f;

	[Tooltip("Seconds for the recoil punch-scale to recover.")]
	public float recoilDuration = 0.1f;

	[Tooltip("Camera shake amplitude when a shot kills at least one zombie, world units (CameraShake.Shake). Throttled to once per 0.3s.")]
	public float killCameraKick = 0.04f;

	[Header("Lifetime")]
	[Tooltip("Show a thin arc around the sniper that depletes with remaining lifespan, hidden on extract.")]
	public bool lifetimeArc = true;

	[Header("Targeting")]
	[Tooltip("Dev toggle: direction-agnostic priority targeting (civilian threat > barricade siege > best pierce line > nearest), replacing the old Z-axis-progress fallback. Off = old targeting.")]
	public bool useTargetingV2 = true;
}
