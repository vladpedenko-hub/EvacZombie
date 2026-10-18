using UnityEngine;

// feature/card-scale: tunables for the global card-radius adaptation (see CardScale.cs). Lives at
// Assets/Resources/CardScaleTuning.asset so CardScale/LevelManager/InputManager reach it via the
// static .Active accessor with no scene/prefab wiring -- same pattern as CoreLoopV2Config.
[CreateAssetMenu(fileName = "CardScaleTuning", menuName = "ZombieGame/Card Scale Tuning")]
public class CardScaleTuning : ScriptableObject
{
	private static CardScaleTuning cached;

	public static CardScaleTuning Active
	{
		get
		{
			if (cached == null) cached = Resources.Load<CardScaleTuning>("CardScaleTuning");
			return cached;
		}
	}

	// Support "Enter Play Mode without domain reload" -- don't leak a stale cached reference
	// between play sessions.
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ResetStatics() => cached = null;

	[Header("Auto (from the loaded level's street width)")]
	[Tooltip("Street width (meters) every card radius was originally tuned for (hand-built levels, ~6m streets). CardScale.RadiusMult = clamp(level street width / this, minMult, 1).")]
	public float referenceStreetWidth = 6f;

	[Tooltip("Floor for the auto-computed multiplier -- a generated city's streets never shrink card ranges below this fraction, however narrow they get.")]
	public float minMult = 0.3f;

	[Tooltip("If true, CardScale computes RadiusMult from the loaded level's street width (1 on hand-built levels). If false, manualMult is used as-is.")]
	public bool autoFromStreets = true;

	[Header("Manual override (autoFromStreets = false, or the SandboxDevPanel 'Card scale' cycle)")]
	public float manualMult = 1f;

	[Header("Hardcoded ranges (step 4 -- see CardScale.cs audit note)")]
	[Tooltip("If true, the hardcoded metre fields listed in CardScale's audit (heli landingRadius/boardingRadius/panicRadius, Bait navMeshSearchRadius, Sniper civilianThreatRadius, CombatHelicopter pickupRadius) are also multiplied by RadiusMult, at the point each is read. If false, only StatType.Radius (every card's main range) is scaled.")]
	public bool scaleHardcodedRanges = true;
}
