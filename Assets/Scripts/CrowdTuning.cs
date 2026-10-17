using UnityEngine;

// feature/crowd-tuning-pass: single shared config for every zombie/civilian street-AI gameplay
// number (speeds, radii, durations, shares, siege, infection, grid, debug). Supersedes
// ChaosSettings and the per-prefab duplicates that used to live on Zombie/EvacueeBase (moveSpeed,
// attackDistance, barricadeCheckRadius, walkSpeed, runSpeed, panicRadius, etc.).
//
// Zombie/EvacueeBase/CrowdFieldManager read every field here directly through a property each
// time it's needed (no caching into local fields at spawn time), so edits in the Inspector take
// effect immediately in Play Mode. The one deliberate exception is the handful of already-cached
// WaitForSeconds objects (attack windup/cooldown, hit-flash duration) that predate this pass for
// GC reasons (Docs/REFACTOR_AUDIT.md Phase B) -- those pick up an edit on the next spawn/pool
// reuse, not mid-attack. That's an existing, unrelated tradeoff this pass doesn't revisit.
//
// Speeds (zombieSpeed, civCalmSpeed, civPanicSpeed) are ABSOLUTE world units/second, not
// multipliers on some other base speed field.
[CreateAssetMenu(fileName = "CrowdTuning", menuName = "Game/Crowd Tuning")]
public class CrowdTuning : ScriptableObject
{
	// ───────────────────────────── Zombie ─────────────────────────────
	[Header("Zombie")]
	[Range(0.2f, 4f)]
	[Tooltip("Zombie base speed, m/s (absolute value, not a multiplier).")]
	public float zombieSpeed = 1.0f;

	[Range(0.5f, 4f)]
	[Tooltip("Zombie bite/attack distance, meters. (Code defaulted to 1.4, but the regular zombie prefab actually had 1.0 -- ZombieBoss had 1.4. Merging into one shared config inevitably loses one of those distinct settings; kept the regular zombie's value since it's the more numerous one.)")]
	public float attackDistance = 1.0f;

	[Range(1f, 8f)]
	[Tooltip("Radius within which a zombie switches to attacking a barricade instead of going around it.")]
	public float barricadeCheckRadius = 3.5f;

	[Range(1, 999)]
	[Tooltip("Damage dealt to a barricade per hit.")]
	public int barricadeDamage = 25;

	[Range(0.1f, 5f)]
	[Tooltip("Cooldown between barricade hits, seconds.")]
	public float attackCooldown = 1.0f;

	[Range(0f, 2f)]
	[Tooltip("Radius within which zombies spread out around a Bait.")]
	public float crowdRadius = 0.8f;

	[Range(0f, 1f)]
	[Tooltip("Share of a normal wave's zombies that become Hunters (the rest are Wanderers).")]
	public float hunterShare = 0.7f;

	[Range(0f, 1f)]
	[Tooltip("hunterShare at night (during SuddenDeath) -- normally all zombies become Hunters.")]
	public float nightHunterShare = 1.0f;

	[Range(0f, 1f)]
	[Tooltip("If the share of living civilians drops below this value -- all zombies become Hunters.")]
	public float lateWaveHunterThreshold = 0.2f;

	[Range(0.2f, 5f)]
	[Tooltip("Minimum interval between navigation decisions (zombies and civilians), seconds.")]
	public float retargetCooldown = 1.5f;

	[Range(1, 10)]
	[Tooltip("How many nearest (straight-line) civilians a Hunter considers before randomly picking a target.")]
	public int hunterCandidateCount = 4;

	[Range(1, 5)]
	[Tooltip("Maximum number of zombies that can chase the same civilian at once.")]
	public int maxZombiesPerTarget = 2;

	[Range(0f, 0.5f)]
	[Tooltip("Random spread of zombie speed on spawn, as a fraction of base speed (e.g. 0.15 = ±15%).")]
	public float speedVariance = 0.15f;

	[Tooltip("A/B toggle: forces ObstacleAvoidanceType.LowQuality on every zombie instead of the prefab's value.")]
	public bool zombieAvoidanceLow = false;

	// ───────────────────────────── Opportunity & Bait (fix/bite-and-bait) ─────────────────────────────
	[Header("Opportunity & Bait")]
	[Range(0, 10)]
	[Tooltip("fix/bite-and-bait: how many steps of the F_civ field at the zombie's own cell are allowed before it bothers with the expensive \"bite any civilian in melee range\" check (cheaply skips ticks when nobody is anywhere close).")]
	public int biteFieldGateSteps = 2;

	[Range(1, 15)]
	[Tooltip("fix/bite-and-bait: grid cells. A civilian reachable by road (NavMesh path, not a straight line) within this radius becomes an \"opportunity target\" -- higher priority than far Hunt, with no maxZombiesPerTarget cap.")]
	public int opportunityRadiusCells = 4;

	[Range(1f, 5f)]
	[Tooltip("fix/bite-and-bait: switching the current \"opportunity target\" to a new one requires the new one to be at least this many times closer by path -- otherwise the zombie keeps its current target (without this, two nearby civilians would make a zombie flip between them every tick).")]
	public float opportunitySwitchRatio = 1.5f;

	[Tooltip("fix/bite-and-bait: if enabled, Bait outranks an available \"opportunity target\" (normally the other way around -- Bait waits until no civilians are nearby).")]
	public bool baitOverridesOpportunity = false;

	[Tooltip("fix/bite-and-bait: show a small marker above a zombie currently latched onto a Bait -- not only while Bait is actually the active target this tick, but the whole time the latch is alive.")]
	public bool showBaitedMarker = true;

	// ───────────────────────────── Civilian ─────────────────────────────
	[Header("Civilian")]
	[Range(0.1f, 3f)]
	[Tooltip("Civilian speed while calm, m/s (absolute value).")]
	public float civCalmSpeed = 0.45f;

	[Range(0.2f, 6f)]
	[Tooltip("Civilian speed while panicking, m/s (absolute value). Must be greater than zombieSpeed.")]
	public float civPanicSpeed = 1.4f;

	[Range(1f, 10f)]
	[Tooltip("Grid cells. A zombie within this radius (straight-line) triggers panic in a civilian.")]
	public float fearRadiusCells = 4f;

	[Range(1, 10)]
	[Tooltip("Panic: the flee target is chosen several steps ahead along the F_zed field (away from the zombie) per SetDestination, rather than one cell at a time.")]
	public int panicLookaheadCells = 6;

	[Range(0.5f, 5f)]
	[Tooltip("Seconds for which the panic direction (flee the zombie, or head to evacuation) is locked in before re-evaluating the threat.")]
	public float panicDuration = 2.5f;

	[Range(0f, 8f)]
	[Tooltip("Grid cells. A calm civilian within this radius of one already panicking also starts panicking (chain reaction).")]
	public float panicContagionCells = 2f;

	[Range(1f, 15f)]
	[Tooltip("Grid cells. A calm civilian only wanders between intersections where the F_zed field is at least this value (a safe distance from zombies).")]
	public float calmSafeDistanceCells = 6f;

	[Range(1f, 15f)]
	[Tooltip("Grid cells (F_evac field steps, not meters). Within this radius of an active evacuation point, a civilian heads to it instead of wandering.")]
	public float evacRadiusCells = 8f;

	// ───────────────────────────── Siege ─────────────────────────────
	[Header("Siege")]
	[Range(1, 10)]
	[Tooltip("In Siege mode, a zombie follows the F_civ field forward this many cells (or until an intersection) per SetDestination.")]
	public int lookaheadCells = 6;

	[Range(1f, 4f)]
	[Tooltip("A Hunter chases a civilian directly only if the NavMesh path is no longer than (the zombie's F_civ field distance, in meters) × this factor. Otherwise -- Siege.")]
	public float maxDetourRatio = 1.5f;

	[Range(0f, 10f)]
	[Tooltip("Grid cells. Standing still within this radius of a barricade in Siege mode is normal (queueing at the breach) -- the stall guard doesn't trigger.")]
	public float siegeQueueRadiusCells = 3f;

	[Range(0.1f, 5f)]
	[Tooltip("Seconds a Hunter can stand nearly motionless (not attacking) before dropping its target and switching to Siege.")]
	public float stallTime = 1f;

	[Range(1f, 30f)]
	[Tooltip("fix/barricade-jerk-wave1 (Bug 1). Grid cells. When a barricade is placed or destroyed, every zombie within this radius of it immediately recomputes its decision the same frame (instead of waiting for the usual tick/cooldown) -- otherwise there was a ~0.3s jerk backward while the NavMesh/field hadn't caught up yet.")]
	public float barricadeReactRadiusCells = 12f;

	[Range(3f, 20f)]
	[Tooltip("fix/barricade-jerk-wave1 (Bug 2). Radius (meters) for NavMesh.SamplePosition when placing a zombie on the NavMesh after spawning. A spawn point farther than 3m from the NavMesh (the old radius) left the agent off-mesh -- the zombie never moved at all.")]
	public float spawnSnapRadius = 10f;

	// ───────────────────────────── Infection ─────────────────────────────
	[Header("Infection")]
	[Range(0f, 2f)]
	[Tooltip("Delay before the bite/hit lands after closing in on the target, seconds.")]
	public float infectDelay = 0.15f;

	[Range(0f, 5f)]
	[Tooltip("Seconds between the bite and the civilian turning into a zombie. Killing the infected civilian before then prevents the turn.")]
	public float turnDelay = 0.7f;

	// ───────────────────────────── Grid ─────────────────────────────
	[Header("Grid")]
	[Range(0.05f, 2f)]
	[Tooltip("How often (seconds) the shared F_civ/F_zed/F_evac fields are recomputed.")]
	public float fieldRefresh = 0.3f;

	[Range(0.5f, 10f)]
	[Tooltip("Minimum size of a road-grid cell, meters.")]
	public float roadGridMinCellSize = 2f;

	[Range(8, 256)]
	[Tooltip("Target number of grid cells per axis -- on larger levels the cell grows to stay around this number. Ignored if cellSizeFromRoadWidth is enabled.")]
	public int roadGridTargetCellsPerAxis = 48;

	[Tooltip("feature/sandbox-big-city: instead of dividing the level's span by roadGridTargetCellsPerAxis, cell size = roadWidth * 0.5 -- guarantees a cell is never wider than the narrowest street/alley in the procedural city (otherwise a walkable road strip would collapse in the grid).")]
	public bool cellSizeFromRoadWidth = false;

	[Range(0.5f, 20f)]
	[Tooltip("feature/sandbox-big-city: procedural city street width, meters -- used to compute cell size, only when cellSizeFromRoadWidth is enabled. Should match the CityGenConfig.roadWidth the current level was generated with.")]
	public float roadWidth = 6f;

	[Range(0.05f, 1f)]
	[Tooltip("A cell is walkable only if the NavMesh is found within this fraction of the cell size around its center.")]
	public float roadGridSnapTolerance = 0.35f;

	[Range(1f, 50f)]
	[Tooltip("Cost for a zombie to pass through a cell with an active barricade (instead of 1 for open road). Finite -- the zombie will still pass through/attack if it's the best option.")]
	public float barricadePathCost = 15f;

	// ───────────────────────────── Debug ─────────────────────────────
	[Header("Debug")]
	[Tooltip("Show the debug road grid and F_civ field direction in the Scene View (Editor only).")]
	public bool drawGridGizmos = false;

	[Tooltip("Color of walkable cells in the debug grid.")]
	public Color gizmoWalkableColor = new Color(0f, 1f, 0f, 0.15f);

	[Tooltip("Color of F_civ field direction arrows in the debug grid.")]
	public Color gizmoDescentArrowColor = new Color(1f, 0.5f, 0f, 0.85f);

	[Tooltip("Show each zombie's mode (Hunt/Wander/Siege), a line to its current target, and a stuck-zombie counter (Editor and development builds).")]
	public bool showZombieSiegeOverlay = false;

	// ───────────────────────────── Legacy ─────────────────────────────
	[Header("Legacy")]
	[Tooltip("Enables the old random \"chaos\" zombie behavior (randomly losing the current target). Off by default -- the fix/zombie-siege-mode paths are enough for current behavior.")]
	public bool legacyChaosEnabled = false;

	[Range(0f, 1f)]
	[Tooltip("(legacyChaosEnabled) Chance that a zombie randomly loses its current target.")]
	public float zombieLoseTargetChance = 0.03f;

	[Range(0.1f, 5f)]
	[Tooltip("(legacyChaosEnabled) How often (seconds) the chance to lose the target is checked.")]
	public float zombieLoseTargetCheckInterval = 0.75f;

	[Range(0.1f, 5f)]
	[Tooltip("(legacyChaosEnabled) How many seconds a zombie wanders aimlessly after losing its target.")]
	public float zombieLoseTargetDuration = 0.6f;

	[Range(0f, 1f)]
	[Tooltip("DEPRECATED, read nowhere -- old chance of human panic, from before the switch to the deterministic system (fearRadiusCells).")]
	public float humanPanicChance = 0.05f;

	[Range(0.1f, 5f)]
	[Tooltip("DEPRECATED, read nowhere -- old panic-check interval.")]
	public float humanPanicCheckInterval = 1.0f;

	[Range(0.1f, 5f)]
	[Tooltip("DEPRECATED, read nowhere -- duration of the old panic sprint.")]
	public float humanPanicDuration = 0.4f;

	[Range(0f, 10f)]
	[Tooltip("DEPRECATED, read nowhere -- distance of the old panic sprint.")]
	public float humanPanicMoveDistance = 3.0f;

	[Range(0f, 10f)]
	[Tooltip("DEPRECATED, read nowhere -- old panic trigger radius.")]
	public float humanPanicTriggerRadius = 4.5f;

	private void OnValidate()
	{
		if (civPanicSpeed <= zombieSpeed)
		{
			Debug.LogWarning($"[CrowdTuning] civPanicSpeed ({civPanicSpeed}) <= zombieSpeed ({zombieSpeed}) -- panicking civilians won't be able to outrun zombies.", this);
		}
	}
}
