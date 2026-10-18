using UnityEngine;

// feature/bait-rework: single tunables asset for the Bait "crowd steering" pass -- the street-grid
// snap distance, the duration/radius multipliers stacked on top of CardData, crowd-heat ring
// readability, and life readability. Lives at Assets/Resources/BaitTuning.asset so Bait.cs/
// InputManager.cs reach it via the static .Active accessor with no scene/prefab wiring -- same
// pattern as CoreLoopV2Config.
[CreateAssetMenu(fileName = "BaitTuning", menuName = "ZombieGame/Bait Tuning")]
public class BaitTuning : ScriptableObject
{
	private static BaitTuning cached;

	public static BaitTuning Active
	{
		get
		{
			if (cached == null) cached = Resources.Load<BaitTuning>("BaitTuning");
			return cached;
		}
	}

	// Support "Enter Play Mode without domain reload" -- don't leak a stale cached reference
	// between play sessions.
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ResetStatics() => cached = null;

	[Header("Stats (stacked ON TOP of CardData, never replacing it)")]
	[Tooltip("Multiplies the CardData-derived Duration (roguelite modifiers, if any, apply to CardData's result first, same as every other card).")]
	public float durationMult = 2.5f;

	[Tooltip("Multiplies the CardData-derived Radius (same stacking order as durationMult).")]
	public float radiusMult = 1.4f;

	[Header("Street-grid snap")]
	[Tooltip("Max distance (meters) NavMesh.SamplePosition searches from the tap/drop point. Streets are ~1.2-1.6m wide -- the old hardcoded 6 could snap clean across several blocks.")]
	public float navSnapMaxDistance = 2f;

	[Tooltip("If true, a placement with no NavMesh point within navSnapMaxDistance is rejected (same as any other invalid placement: no Supply spent, same rejection feedback). If false, falls back to the raw tap point off-mesh, as before.")]
	public bool requireNavMeshPoint = true;

	[Header("Crowd heat")]
	[Tooltip("LatchedCount at/above which the ring reaches its maximum width/intensity.")]
	public int heatZombiesForMax = 10;

	[Tooltip("Ring width at LatchedCount = 0.")]
	public float ringBaseWidth = 0.15f;

	[Tooltip("Ring width at LatchedCount >= heatZombiesForMax, as a multiple of ringBaseWidth.")]
	public float ringMaxWidthMult = 2.2f;

	[Header("Life readability")]
	[Tooltip("Ring radius/alpha shrink and fade with remaining life (on top of the existing pulse).")]
	public bool shrinkWithLife = true;

	[Tooltip("Fraction of remaining life (0..1) at which the ring starts blinking instead of just fading.")]
	public float lowLifeBlinkFraction = 0.25f;

	[Header("Placement")]
	[Tooltip("One expanding ring ping out to the final radius over ~0.4s on placement, on top of the attract-point pop-in.")]
	public bool placementRipple = true;

	[Header("Reach zone (optional -- see Bait.cs step 8 note)")]
	[Tooltip("NOT IMPLEMENTED this pass: CrowdFieldManager doesn't expose a cheap bounded flood-fill/cell " +
		"enumeration (only single-point field samples and single-cell directional lookups), so a correct " +
		"walkable-cells-within-path-distance overlay would need either per-cell NavMesh.CalculatePath calls " +
		"(expensive at a 0.1s refresh) or new CrowdFieldManager-internal grid-walking, both past this pass's " +
		"scope gate. Left here, always false, so a future pass can wire it without another asset migration.")]
	public bool showReachZone = false;

	[Header("Dev")]
	[Tooltip("SandboxDevPanel 'Bait: Old/New' toggle. True = pre-feature-pass behavior: multipliers at 1, 6m snap distance, no heat, no shrink, no ripple.")]
	public bool legacyFeelEnabled = false;
}
