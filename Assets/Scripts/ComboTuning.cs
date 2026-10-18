using UnityEngine;

// feature/bomb-combo: tunables for the Bomb "street-reach blast" + combo moment pass. Lives at
// Assets/Resources/ComboTuning.asset so Bomb.cs/ComboFeedback.cs/InputManager.cs reach it via the
// static .Active accessor with no scene/prefab wiring -- same pattern as CoreLoopV2Config.
[CreateAssetMenu(fileName = "ComboTuning", menuName = "ZombieGame/Combo Tuning")]
public class ComboTuning : ScriptableObject
{
	private static ComboTuning cached;

	public static ComboTuning Active
	{
		get
		{
			if (cached == null) cached = Resources.Load<ComboTuning>("ComboTuning");
			return cached;
		}
	}

	// Support "Enter Play Mode without domain reload" -- don't leak a stale cached reference
	// between play sessions.
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ResetStatics() => cached = null;

	[Header("Combo trigger")]
	[Tooltip("Number of Bait-gathered zombies a single Bomb blast must kill to trigger the combo moment.")]
	public int gatheredKillThreshold = 6;

	[Header("Combo feedback")]
	[Tooltip("Time.timeScale during the combo slowmo window.")]
	public float slowmoScale = 0.25f;

	[Tooltip("Slowmo duration, REAL (unscaled) seconds.")]
	public float slowmoDuration = 0.3f;

	[Tooltip("Camera punch amplitude (CameraShake.Shake), world units.")]
	public float zoomPunch = 0.06f;

	[Tooltip("Seconds the 'TRAP x{count}' world-space label stays visible (scale-in + float + fade).")]
	public float labelDuration = 1.0f;

	[Header("Blast shape")]
	[Tooltip("If true, the blast only reaches targets with an unobstructed NavMesh path from the epicenter (no killing through a building wall). If false, plain sphere overlap, as before.")]
	public bool useStreetBlast = true;

	[Tooltip("If true, the blast never destroys the player's own Soldier/Sniper units.")]
	public bool sparesOwnUnits = true;

	[Header("Supply refund (combo only)")]
	public bool supplyRefundOnCombo = false;
	public int supplyRefundAmount = 1;
	public int supplyRefundMax = 2;

	[Header("Dev")]
	[Tooltip("SandboxDevPanel 'Bomb: Old/New' toggle. True = pre-feature-pass behavior: plain sphere blast, destroys buildings and own units, no placement-snap requirement, no combo detection, no friendly-fire preview.")]
	public bool legacyFeelEnabled = false;
}
