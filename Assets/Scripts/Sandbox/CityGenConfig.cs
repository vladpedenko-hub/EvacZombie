using UnityEngine;
using System.Collections.Generic;

// feature/sandbox-big-city: tunables for CityGenerator (Tools/EvacZombie/Generate Sandbox City).
// Lives under Resources/ so the Editor tool can find a default instance without requiring the
// user to assign one by hand. Entirely a dev/Editor-time concern -- nothing here is read at
// runtime (unlike SandboxConfig, which the dev panel reads in a build).
[CreateAssetMenu(fileName = "CityGenConfig", menuName = "Game/City Gen Config")]
public class CityGenConfig : ScriptableObject
{
	[Header("Grid")]
	[Tooltip("Seed for the procedural layout -- same seed + same config always produces the same city.")]
	public int seed = 12345;

	[Tooltip("Number of block columns (X axis).")]
	public int blocksX = 10;

	[Tooltip("Number of block rows (Z axis). 14 vs 10 columns keeps the city portrait-shaped, matching the game's portrait-only orientation.")]
	public int blocksY = 14;

	[Tooltip("Street width, meters. Also the width every alley's 'through'/'dead-end' variant is a fraction of (see alleyWidthFactor). CrowdTuning (Sandbox Default.asset) derives its road-grid cell size from this value when cellSizeFromRoadWidth is on -- keep the two in sync if you change this.")]
	public float roadWidth = 6f;

	[Tooltip("Alley width as a fraction of roadWidth.")]
	[Range(0.1f, 1f)]
	public float alleyWidthFactor = 0.5f;

	[Tooltip("World-space footprint reserved per building slot inside a block, meters. Each building prefab is uniformly scaled to fit inside this.")]
	public float buildingCellSize = 4f;

	[Tooltip("Buildings per side of a block (min/max, inclusive). Rolled once per column (width) and once per row (height), so streets stay straight through the whole grid -- only merge/alley gaps interrupt them.")]
	public int blockSizeMin = 2;
	public int blockSizeMax = 4;

	[Range(0f, 1f)]
	[Tooltip("Per internal street segment, chance that segment is skipped (no road strip drawn there), fusing its two neighboring blocks into one bigger un-streeted area.")]
	public float mergeBlockChance = 0.2f;

	[Range(0f, 1f)]
	[Tooltip("Per internal street segment (that wasn't merged away), chance it's narrowed into a 1-lane alley instead of a full street. Half of alleys are 'through' (full length, same as a normal street); half are 'dead-end' (half length, stub from one side, doesn't reach the far block).")]
	public float alleyChance = 0.25f;

	[Tooltip("Number of blocks converted into open, walkable plazas (no buildings, decorated with park props) instead of a building grid.")]
	public int parkCount = 2;

	[Tooltip("fix/sandbox-city-visuals: uniform Y-only scale applied on top of each building's footprint-fit scale, so buildings read shorter from the new top-down camera instead of dominating/occluding the view. X/Z scale (and therefore the NavMeshObstacle footprint, which is set from the unscaled mesh bounds and scaled by the transform like any other NavMesh carving) is untouched -- only how tall buildings look changes, not what they block.")]
	[Range(0.1f, 1f)]
	public float buildingHeightScale = 0.5f;

	[Header("Prefabs")]
	[Tooltip("Building prefab pool -- reused as-is from existing levels (SimpleTown pack). Each placement picks one at random, scales it to buildingCellSize, and adds the same MeshCollider + NavMeshObstacle + 'Building' tag convention used by hand-built levels (see Level_01's building instances).")]
	public List<GameObject> buildingPrefabs = new List<GameObject>();

	[Tooltip("Decorative prop scattered over park blocks. Purely visual -- no collider, no NavMeshObstacle, doesn't affect the road grid.")]
	public GameObject parkPropPrefab;

	[Header("Colors (fix/sandbox-city-visuals)")]
	[Tooltip("Generator-created, untextured URP Lit material color for roads/streets/alleys/intersections. Previously this reused SimpleTown's Road.mat, whose atlas UVs stretch into rainbow stripes when painted across an arbitrarily-sized cube -- a flat color has no UVs to stretch.")]
	public Color asphaltColor = new Color(0.16f, 0.16f, 0.17f, 1f);

	[Tooltip("Generator-created, untextured material color for the single city-wide ground/base plane under everything (sidewalks, block interiors, the ground visible around/under buildings).")]
	public Color blockBaseColor = new Color(0.42f, 0.42f, 0.4f, 1f);

	[Tooltip("Generator-created, untextured material color for park-block ground.")]
	public Color parkColor = new Color(0.27f, 0.38f, 0.24f, 1f);

	[Header("Camera (fix/sandbox-city-visuals)")]
	[Tooltip("Fraction of the city's longer ground dimension the camera should show at the existing CameraZoomPanController's resting zoom level (CameraReadabilityConfig.defaultZoomFraction, ~0.475 -- shared/global, not edited by this tool). The generator solves the authored camera height backwards from this target, then clamps it to never end up narrower than a full-city fit -- so pinching/scrolling out via that same controller still always reaches the whole city.")]
	[Range(0.1f, 1f)]
	public float initialViewFraction = 0.5f;

	[Header("Output")]
	[Tooltip("Prefab asset path CityGenerator overwrites.")]
	public string outputPrefabPath = "Assets/Prefabs/Levels/Level_Sandbox_Environment.prefab";

	[Tooltip("feature/sandbox-two-levels: LevelData asset whose camera this run retunes (RetuneSandboxCamera). Lets Small/Large share one generator while each only ever writes to its own LevelData -- never the other's.")]
	public string targetLevelDataPath = "Assets/Data/Level_Sandbox.asset";

	[Header("Spawn Markers")]
	[Tooltip("feature/sandbox-two-levels: when on, only North/South edge markers + one Special_1 marker are created (no West/East, no Special_2) -- a smaller spawn set for a one-screen sandbox that doesn't need every direction.")]
	public bool minimalSpawnMarkers = false;
}
