using UnityEngine;

// feature/barricade-feel: single tunables asset for the Barricade "feel" pass -- placement
// drop/squash, hit punch, damage-stage darkening/cracks, and the repair-by-placing-on-top
// interaction. Lives at Assets/Resources/BarricadeFeelTuning.asset so Barricade.cs/InputManager.cs
// reach it via the static .Active accessor with no scene/prefab wiring -- same pattern as
// CoreLoopV2Config/CameraReadabilityConfig.
[CreateAssetMenu(fileName = "BarricadeFeelTuning", menuName = "ZombieGame/Barricade Feel Tuning")]
public class BarricadeFeelTuning : ScriptableObject
{
	private static BarricadeFeelTuning cached;

	public static BarricadeFeelTuning Active
	{
		get
		{
			if (cached == null) cached = Resources.Load<BarricadeFeelTuning>("BarricadeFeelTuning");
			return cached;
		}
	}

	// Support "Enter Play Mode without domain reload" -- don't leak a stale cached reference
	// between play sessions.
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ResetStatics() => cached = null;

	[Header("Damage stages")]
	[Tooltip("HP fraction at/below which a barricade enters stage 2 (first cracks, slight darkening).")]
	public float stage2Threshold = 0.66f;

	[Tooltip("HP fraction at/below which a barricade enters stage 3 (heaviest cracks, darkest).")]
	public float stage3Threshold = 0.33f;

	[Tooltip("Color value (HSV) reduction applied per stage past stage 1 -- hue/saturation kept, only darkened.")]
	public float stageDarken = 0.12f;

	[Header("Placement")]
	[Tooltip("World units the visual drops from on spawn before settling to its resting position.")]
	public float placeDropHeight = 0.6f;

	[Tooltip("Seconds for the drop-and-settle animation.")]
	public float placeDropDuration = 0.12f;

	[Tooltip("Squash/stretch strength on landing, as a fraction of scale.")]
	public float placeSquash = 0.18f;

	[Tooltip("Camera shake amplitude on placement, world units (CameraShake.Shake). Kept here rather " +
		"than in the Feedback table so it's tunable alongside the rest of the placement feel.")]
	public float placeShake = 0.05f;

	[Header("Hit feedback")]
	[Tooltip("Punch-scale strength per hit, as a fraction of scale.")]
	public float hitPunchScale = 0.06f;

	[Tooltip("Seconds for the hit punch-scale to recover.")]
	public float hitPunchDuration = 0.08f;

	[Header("Repair")]
	[Tooltip("Fraction of maxHealth restored when a new Barricade card is placed on top of an existing, same-street Barricade instead of spawning a duplicate.")]
	public float repairFraction = 0.5f;

	[Tooltip("Dev toggle: placing a Barricade card on an existing, same-street Barricade repairs it (if below max HP) or rejects the placement (if at max HP), instead of stacking a duplicate.")]
	public bool repairOnPlace = true;

	[Header("Dev")]
	[Tooltip("SandboxDevPanel 'Barricade feel: Old/New' toggle. True = pre-feature-pass behavior: no drop/squash, no damage stages/cracks, no hit punch, no repair-on-place.")]
	public bool legacyFeelEnabled = false;
}
