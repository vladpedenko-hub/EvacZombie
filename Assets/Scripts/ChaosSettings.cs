using UnityEngine;

[CreateAssetMenu(fileName = "ChaosSettings", menuName = "Game/Chaos Settings")]
public class ChaosSettings : ScriptableObject
{
	[Header("General")]
	public bool chaosEnabled = true;

	[Header("Zombies")]
	[Range(0f, 1f)]
	public float zombieLoseTargetChance = 0.03f;

	[Tooltip("How often a zombie checks whether to lose its target (in seconds)")]
	public float zombieLoseTargetCheckInterval = 0.75f;

	[Tooltip("How long a zombie wanders aimlessly after losing its target")]
	public float zombieLoseTargetDuration = 0.6f;

	[Header("Humans")]
	[Range(0f, 1f)]
	public float humanPanicChance = 0.05f;

	[Tooltip("How often a human checks whether to panic (in seconds)")]
	public float humanPanicCheckInterval = 1.0f;

	[Tooltip("How long a human runs in a random direction during panic")]
	public float humanPanicDuration = 0.4f;

	[Tooltip("Distance a human moves during a panic sprint")]
	public float humanPanicMoveDistance = 3.0f;

	[Tooltip("Zombie must be within this radius to trigger human panic")]
	public float humanPanicTriggerRadius = 4.5f;

	// ── Street AI (feature/zombie-street-ai): shared road-grid + distance-field crowd system.
	// Supersedes the probabilistic panic trigger above for the zombie-proximity check specifically
	// (humanPanicChance/humanPanicTriggerRadius are left in place, unread by the new path, so
	// existing asset values aren't lost if this feature is ever reverted).
	[Header("Street AI — Shared Grid/Fields")]
	[Tooltip("How often (seconds) the shared F_civ/F_zed/F_evac distance fields are recalculated. No per-agent pathing happens between refreshes.")]
	public float fieldRefresh = 0.3f;

	[Tooltip("Base/minimum road-grid cell size (world units). The grid auto-grows cell size on large levels to keep the node count near roadGridTargetCellsPerAxis.")]
	public float roadGridMinCellSize = 2f;

	[Tooltip("Target number of grid cells per axis — the actual cell size scales up on large levels so width/height stay roughly at this value.")]
	public int roadGridTargetCellsPerAxis = 48;

	[Tooltip("fix/zombie-corner-stuck (4): a grid cell is only walkable if the baked NavMesh is within this fraction of cellSize from the cell's own center. Too high lets the walkable region cut through building corners.")]
	public float roadGridSnapTolerance = 0.35f;

	[Header("Street AI — Zombies")]
	[Tooltip("Path cost (vs. 1 for open road) a zombie's distance field assigns to stepping into a tile occupied by an active barricade. Finite — zombies will route through/attack it if that's still the best option.")]
	public float barricadePathCost = 15f;

	[Range(0f, 1f)]
	[Tooltip("Fraction of a normal-wave zombie's spawns that roll as Hunter (descend F_civ) vs Wanderer (patrol intersections).")]
	public float hunterShare = 0.7f;

	[Range(0f, 1f)]
	[Tooltip("hunterShare used instead, for zombies spawned while GameManager.State == SuddenDeath (the Night phase).")]
	public float nightHunterShare = 1.0f;

	[Tooltip("Grid tiles. A Wanderer that has a civilian within this range on the same straight street becomes a Hunter (one-way; it never reverts to Wanderer).")]
	public float aggroRadiusTiles = 5f;

	[Range(0f, 1f)]
	[Tooltip("If aliveCivilians / initialCivilians drops below this fraction, every zombie on the level becomes a Hunter regardless of hunterShare.")]
	public float lateWaveHunterThreshold = 0.2f;

	[Tooltip("Minimum time between a zombie's street-AI navigation decisions (picking a new grid direction/intersection) -- unless it arrives at its current destination first (within ~1 cell), which also forces an early re-decide.")]
	public float retargetCooldown = 1.5f;

	[Tooltip("fix/zombie-corner-stuck (3): a Hunter's gradient-descent destination is chosen by walking up to this many grid cells ahead (or until an intersection), instead of one cell at a time -- avoids a decision (and a stop) every single cell.")]
	public int lookaheadCells = 6;

	[Tooltip("fix/zombie-corner-stuck (2): a civilian only becomes a direct opportunistic-chase target if the zombie's NavMesh path to it is at most this long (world units) -- straight-line 'nearby' alone can point through a wall.")]
	public float closeChaseMaxPath = 6f;

	[Tooltip("fix/zombie-corner-stuck (2): once a close-range victim is picked, it's held for this long before re-evaluating, instead of re-scanning (and potentially flip-flopping against the field direction) every Brain tick.")]
	public float victimLockTime = 1f;

	[Header("Street AI — Civilians")]
	[Tooltip("Multiplier applied to a civilian's own walkSpeed while Calm (wandering intersection to intersection).")]
	public float civCalmSpeedMult = 0.6f;

	[Tooltip("Grid tiles. A zombie within this range triggers civilian Panic.")]
	public float fearRadiusTiles = 4f;

	[Tooltip("Multiplier applied to walkSpeed for the Panic sprint.")]
	public float panicSpeedMult = 1.7f;

	[Tooltip("Seconds a Panic direction is locked before the civilian re-evaluates threats.")]
	public float panicDuration = 2.5f;

	[Tooltip("Grid tiles. Within this range of an active evac point (car/helicopter present and accepting passengers), a Calm civilian walks toward it (F_evac) instead of wandering.")]
	public float evacRadiusTiles = 8f;

	[Tooltip("Seconds between zombie contact and actually turning into a zombie. Killing the infected civilian before this elapses prevents the turn.")]
	public float turnDelay = 0.7f;

	// ── feature/azs-crowd-feel: AZS-style individual-goal chasing + bite-on-contact. ──────────
	[Header("Street AI — Crowd Feel")]
	[Tooltip("A Hunter picks its chase target at random among this many nearest reachable civilians, instead of always the single closest one — spreads zombies onto individual targets instead of one shared gradient.")]
	public int hunterCandidateCount = 4;

	[Tooltip("At most this many zombies may have the same civilian as their current hunt target; others pick a different candidate.")]
	public int maxZombiesPerTarget = 2;

	[Tooltip("Per-zombie move-speed variance rolled at spawn, as a fraction of base speed (e.g. 0.15 = ±15%) -- stops a wave from moving as one uniform blob.")]
	public float speedVariance = 0.15f;

	[Tooltip("A/B toggle: sets every zombie's NavMeshAgent.obstacleAvoidanceType to LowQuality instead of the prefab's authored value.")]
	public bool zombieAvoidanceLow = false;
}