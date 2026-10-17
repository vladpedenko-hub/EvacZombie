using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

// feature/city-gen-v2: rebuilds Level_Sandbox_Environment.prefab following Atom Zombie Smasher's
// street logic -- solid blocks (buildings touch each other and the street edge, no walkable gap
// inside a block), narrow streets (~1m, 2-3 actors abreast), a bounded city with a solid frame
// wall instead of a ring road (streets dead-end at the frame), and a square barricade that spans
// the full street width. Entirely an Editor-time tool -- run once via the menu item, inspect/tweak
// CityGenConfig, run again.
//
// Street-grid model: an (blocksX+1) x (blocksY+1) grid of intersections, always present (keeps the
// network connected even where an internal segment below is merged away). Internal streets now run
// the FULL blocksX+1 / blocksY+1 range (there is no separate ring-road model any more) -- the
// outermost streets (touching the frame) are always Normal; every other segment still rolls
// merge/alley independently. Column widths and row heights are rolled ONCE per column/row, so every
// street still runs straight across the whole grid -- only merge/alley interrupts it locally.
//
// Block fill: each block rectangle (a run of blocks fused by a merged street is treated as one
// bigger rectangle; parks are excluded from fusing) is recursively split (BSP) into building
// footprints with no gaps, instead of a uniform per-cell grid. Buildings carry no NavMeshObstacle
// any more -- one NavMeshModifierVolume per block rectangle marks the whole footprint (ground +
// building rooftops) Not Walkable at bake time, which is cheaper than carving ~100 obstacles live
// and makes walkable area = streets + parks + intersections only, by construction.
public static class CityGenerator
{
	private const string DefaultConfigPath = "Assets/Resources/CityGenConfig.asset";
	private const string BuildingPoolFolder = "Assets/ExternalAssets/SimpleTown/Prefabs/Buildings";
	private const string DefaultParkPropPath = "Assets/ExternalAssets/SimpleTown/Prefabs/Props/grass_square_mesh.prefab";
	private const string AsphaltMaterialPath = "Assets/Materials/SandboxCity_Asphalt.mat";
	private const string BlockBaseMaterialPath = "Assets/Materials/SandboxCity_BlockBase.mat";
	private const string ParkMaterialPath = "Assets/Materials/SandboxCity_Park.mat";
	private const string FrameMaterialPath = "Assets/Materials/SandboxCity_Frame.mat";
	private const float GroundThickness = 0.2f;
	private const float RoadThickness = 0.06f;
	private const float RoadTopY = 0.02f; // fix/sandbox-city-visuals: roads float slightly above the base plane
	private const float ParkTopY = 0.01f; // ...and park ground sits between the base plane and roads

	// feature/city-gen-v2: BSP block-fill tunables. Not exposed on CityGenConfig -- these shape the
	// subdivision algorithm itself (see step 4 of the brief), not a per-city balance knob.
	private const float BspMinLeafSide = 6f;
	private const float BspRandomStopChance = 0.2f;
	private const float BspSeamGap = 0.05f;
	private const float BspSplitFractionMin = 0.35f;
	private const float BspSplitFractionMax = 0.65f;

	// feature/city-gen-v2: solid perimeter wall replacing the old ring road. Streets dead-end here.
	private const float FrameThickness = 1f;
	private const float FrameHeight = 2.5f;

	// feature/city-gen-v2: NavMeshModifierVolume covering a block must reach above the tallest
	// building roof and below ground, so both the ground-level footprint AND any (disconnected,
	// but cheap to be safe about) rooftop surface the NavMesh bake sees get marked Not Walkable.
	private const float BlockVolumeBelowGround = 1f;
	private const float BlockVolumeAboveGround = 12f;

	private enum SegmentKind { Normal, Merged, AlleyThrough, AlleyDeadEndNear, AlleyDeadEndFar }

	[MenuItem("Tools/EvacZombie/Generate Sandbox City")]
	public static void Generate()
	{
		CityGenConfig config = LoadOrCreateDefaultConfig();
		if (config.buildingPrefabs == null || config.buildingPrefabs.Count == 0)
		{
			Debug.LogError("[CityGenerator] CityGenConfig has no buildingPrefabs and none could be auto-discovered under " + BuildingPoolFolder + ". Aborting.");
			return;
		}

		UnityEngine.Random.InitState(config.seed);

		int blocksX = Mathf.Max(1, config.blocksX);
		int blocksY = Mathf.Max(1, config.blocksY);
		int sizeMin = Mathf.Max(1, config.blockSizeMin);
		int sizeMax = Mathf.Max(sizeMin, config.blockSizeMax);
		float cellSize = Mathf.Max(0.5f, config.buildingCellSize);
		float roadWidth = Mathf.Max(0.5f, config.roadWidth);
		float alleyWidth = roadWidth * Mathf.Clamp(config.alleyWidthFactor, 0.1f, 1f);

		int[] colSize = new int[blocksX];
		int[] rowSize = new int[blocksY];
		for (int bx = 0; bx < blocksX; bx++) colSize[bx] = UnityEngine.Random.Range(sizeMin, sizeMax + 1);
		for (int by = 0; by < blocksY; by++) rowSize[by] = UnityEngine.Random.Range(sizeMin, sizeMax + 1);

		// streetCenterX[bx]/streetCenterZ[by]: centerline of the vertical/horizontal street
		// immediately before block column/row bx/by (index blocksX/blocksY is the outermost street,
		// right against the frame).
		float[] streetCenterX = new float[blocksX + 1];
		float[] blockLeft = new float[blocksX];
		float[] blockRight = new float[blocksX];
		float cursor = 0f;
		for (int bx = 0; bx < blocksX; bx++)
		{
			streetCenterX[bx] = cursor + roadWidth * 0.5f;
			cursor += roadWidth;
			blockLeft[bx] = cursor;
			cursor += colSize[bx] * cellSize;
			blockRight[bx] = cursor;
		}
		streetCenterX[blocksX] = cursor + roadWidth * 0.5f;
		cursor += roadWidth;
		float cityWidth = cursor;

		float[] streetCenterZ = new float[blocksY + 1];
		float[] blockBottom = new float[blocksY];
		float[] blockTop = new float[blocksY];
		cursor = 0f;
		for (int by = 0; by < blocksY; by++)
		{
			streetCenterZ[by] = cursor + roadWidth * 0.5f;
			cursor += roadWidth;
			blockBottom[by] = cursor;
			cursor += rowSize[by] * cellSize;
			blockTop[by] = cursor;
		}
		streetCenterZ[blocksY] = cursor + roadWidth * 0.5f;
		cursor += roadWidth;
		float cityDepth = cursor;

		// Centers the generated city on the prefab root's local origin -- LevelManager always
		// instantiates data.levelPrefab at world (0,0,0) (see LevelManager.LoadLevel), ignoring the
		// prefab asset's own authored root position, so centering has to happen on every child's
		// local position here, not on the root transform.
		float offsetX = -cityWidth * 0.5f;
		float offsetZ = -cityDepth * 0.5f;

		// feature/city-gen-v2: vKind/hKind now span the FULL grid (bx=0..blocksX, by=0..blocksY) --
		// there is no separate always-full ring any more. The outermost column/row on each side
		// touches the frame directly and is forced Normal below; every other segment still rolls.
		SegmentKind[,] vKind = new SegmentKind[blocksX + 1, blocksY];
		for (int bx = 0; bx <= blocksX; bx++)
			for (int by = 0; by < blocksY; by++)
				vKind[bx, by] = (bx == 0 || bx == blocksX) ? SegmentKind.Normal : RollSegmentKind(config);

		SegmentKind[,] hKind = new SegmentKind[blocksX, blocksY + 1];
		for (int by = 0; by <= blocksY; by++)
			for (int bx = 0; bx < blocksX; bx++)
				hKind[bx, by] = (by == 0 || by == blocksY) ? SegmentKind.Normal : RollSegmentKind(config);

		// Parks: pick parkCount distinct blocks to leave building-free and walkable.
		var parkBlocks = new HashSet<(int, int)>();
		int totalBlocks = blocksX * blocksY;
		int parkCount = Mathf.Clamp(config.parkCount, 0, totalBlocks);
		while (parkBlocks.Count < parkCount)
			parkBlocks.Add((UnityEngine.Random.Range(0, blocksX), UnityEngine.Random.Range(0, blocksY)));

		Material asphaltMat = CreateOrUpdateFlatMaterial(AsphaltMaterialPath, config.asphaltColor);
		Material blockBaseMat = CreateOrUpdateFlatMaterial(BlockBaseMaterialPath, config.blockBaseColor);
		Material parkMat = CreateOrUpdateFlatMaterial(ParkMaterialPath, config.parkColor);
		Material frameMat = CreateOrUpdateFlatMaterial(FrameMaterialPath, config.blockBaseColor * 0.6f);

		GameObject root = new GameObject("Level_Sandbox_Environment");
		Transform groundParent = NewChild(root.transform, "Ground").transform;
		Transform buildingsParent = NewChild(root.transform, "Buildings").transform;
		Transform parksParent = NewChild(root.transform, "Parks").transform;
		Transform spawnersParent = NewChild(root.transform, "Spawners").transform;
		Transform waypointsParent = NewChild(root.transform, "Waypoints").transform;
		Transform frameParent = NewChild(root.transform, "Frame").transform;
		Transform navVolumesParent = NewChild(root.transform, "NavVolumes").transform;

		int groundLayer = LayerMask.NameToLayer("Ground");

		// ── Single base plane for the whole city (sidewalks/block ground), y=0. Extended by the
		// frame thickness on every side so there's no gap visible under the frame walls.
		float totalWidth = cityWidth + FrameThickness * 2f;
		float totalDepth = cityDepth + FrameThickness * 2f;
		CreateGroundCube(groundParent, "BaseGround", new Vector3(cityWidth * 0.5f + offsetX, 0f, cityDepth * 0.5f + offsetZ),
			new Vector3(totalWidth, GroundThickness, totalDepth), groundLayer, blockBaseMat);

		// ── Intersections: always present, keeps the network connected regardless of merges ──
		for (int bx = 0; bx <= blocksX; bx++)
		{
			for (int by = 0; by <= blocksY; by++)
			{
				Vector3 center = new Vector3(offsetX + streetCenterX[bx], RoadTopY, offsetZ + streetCenterZ[by]);
				GameObject intersectionGo = CreateGroundCube(groundParent, $"Intersection_{bx}_{by}", center, new Vector3(roadWidth, RoadThickness, roadWidth), groundLayer, asphaltMat);
				AddStreetSegment(intersectionGo, Vector3.forward, roadWidth, true);
			}
		}

		// ── Internal vertical street segments -- now the FULL range bx=0..blocksX (no ring road) ──
		for (int bx = 0; bx <= blocksX; bx++)
		{
			for (int by = 0; by < blocksY; by++)
			{
				SegmentKind kind = vKind[bx, by];
				if (kind == SegmentKind.Merged) continue;

				float fullBottom = blockBottom[by];
				float fullTop = blockTop[by];
				float width = (kind == SegmentKind.Normal) ? roadWidth : alleyWidth;
				float zBottom = fullBottom, zTop = fullTop;
				if (kind == SegmentKind.AlleyDeadEndNear) zTop = fullBottom + (fullTop - fullBottom) * 0.5f;
				else if (kind == SegmentKind.AlleyDeadEndFar) zBottom = fullTop - (fullTop - fullBottom) * 0.5f;

				float zCenter = offsetZ + (zBottom + zTop) * 0.5f;
				float len = zTop - zBottom;
				GameObject streetVGo = CreateGroundCube(groundParent, $"Street_V_{bx}_{by}", new Vector3(offsetX + streetCenterX[bx], RoadTopY, zCenter), new Vector3(width, RoadThickness, len), groundLayer, asphaltMat);
				AddStreetSegment(streetVGo, Vector3.forward, width, false);
			}
		}

		// ── Internal horizontal street segments -- full range by=0..blocksY ──
		for (int by = 0; by <= blocksY; by++)
		{
			for (int bx = 0; bx < blocksX; bx++)
			{
				SegmentKind kind = hKind[bx, by];
				if (kind == SegmentKind.Merged) continue;

				float fullLeft = blockLeft[bx];
				float fullRight = blockRight[bx];
				float width = (kind == SegmentKind.Normal) ? roadWidth : alleyWidth;
				float xLeft = fullLeft, xRight = fullRight;
				if (kind == SegmentKind.AlleyDeadEndNear) xRight = fullLeft + (fullRight - fullLeft) * 0.5f;
				else if (kind == SegmentKind.AlleyDeadEndFar) xLeft = fullRight - (fullRight - fullLeft) * 0.5f;

				float xCenter = offsetX + (xLeft + xRight) * 0.5f;
				float len = xRight - xLeft;
				GameObject streetHGo = CreateGroundCube(groundParent, $"Street_H_{bx}_{by}", new Vector3(xCenter, RoadTopY, offsetZ + streetCenterZ[by]), new Vector3(len, RoadThickness, width), groundLayer, asphaltMat);
				AddStreetSegment(streetHGo, Vector3.right, width, false);
			}
		}

		// ── Frame: solid, non-walkable perimeter wall. Streets dead-end here -- no ring road. ──
		CreateFrameWall(frameParent, "Frame_South", new Vector3(offsetX + cityWidth * 0.5f, 0f, offsetZ - FrameThickness * 0.5f), new Vector3(totalWidth, FrameHeight, FrameThickness), frameMat);
		CreateFrameWall(frameParent, "Frame_North", new Vector3(offsetX + cityWidth * 0.5f, 0f, offsetZ + cityDepth + FrameThickness * 0.5f), new Vector3(totalWidth, FrameHeight, FrameThickness), frameMat);
		CreateFrameWall(frameParent, "Frame_West", new Vector3(offsetX - FrameThickness * 0.5f, 0f, offsetZ + cityDepth * 0.5f), new Vector3(FrameThickness, FrameHeight, cityDepth), frameMat);
		CreateFrameWall(frameParent, "Frame_East", new Vector3(offsetX + cityWidth + FrameThickness * 0.5f, 0f, offsetZ + cityDepth * 0.5f), new Vector3(FrameThickness, FrameHeight, cityDepth), frameMat);

		// ── Merge-group bounding rectangles: a run of blocks fused by a Merged street becomes one
		// rectangle for BSP purposes. Parks never fuse with a neighbor (excluded from the union). ──
		int[] parent = new int[blocksX * blocksY];
		for (int i = 0; i < parent.Length; i++) parent[i] = i;
		int Index(int bx, int by) => by * blocksX + bx;
		int Find(int i) { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; }
		void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[a] = b; }

		for (int bx = 1; bx < blocksX; bx++)
			for (int by = 0; by < blocksY; by++)
				if (vKind[bx, by] == SegmentKind.Merged && !parkBlocks.Contains((bx - 1, by)) && !parkBlocks.Contains((bx, by)))
					Union(Index(bx - 1, by), Index(bx, by));

		for (int by = 1; by < blocksY; by++)
			for (int bx = 0; bx < blocksX; bx++)
				if (hKind[bx, by] == SegmentKind.Merged && !parkBlocks.Contains((bx, by - 1)) && !parkBlocks.Contains((bx, by)))
					Union(Index(bx, by - 1), Index(bx, by));

		// feature/city-gen-v2: a merge chain can connect two blocks INDIRECTLY (A-B merged, B-C
		// merged, so A and C share a union-find root) without the direct A-C edge itself ever being
		// merged -- if A and C's bounding box happens to be a clean rectangle anyway (e.g. a full
		// 2x2 group fused only around three of its four internal edges), a naive area check would
		// wrongly accept it even though the un-merged fourth edge is a real, drawn street running
		// through the middle. The only fully correct check is: every internal boundary strictly
		// inside the group's grid-index bounding box must itself be Merged. Track grid-index extents
		// (not just world-space bounds) so that check can be done directly against vKind/hKind.
		var groupMinBx = new Dictionary<int, int>();
		var groupMaxBx = new Dictionary<int, int>();
		var groupMinBy = new Dictionary<int, int>();
		var groupMaxBy = new Dictionary<int, int>();
		var groupMembers = new Dictionary<int, List<(int bx, int by)>>();
		for (int bx = 0; bx < blocksX; bx++)
		{
			for (int by = 0; by < blocksY; by++)
			{
				if (parkBlocks.Contains((bx, by))) continue;

				int root2 = Find(Index(bx, by));
				if (groupMembers.TryGetValue(root2, out var members))
				{
					groupMinBx[root2] = Mathf.Min(groupMinBx[root2], bx);
					groupMaxBx[root2] = Mathf.Max(groupMaxBx[root2], bx);
					groupMinBy[root2] = Mathf.Min(groupMinBy[root2], by);
					groupMaxBy[root2] = Mathf.Max(groupMaxBy[root2], by);
					members.Add((bx, by));
				}
				else
				{
					groupMinBx[root2] = bx; groupMaxBx[root2] = bx;
					groupMinBy[root2] = by; groupMaxBy[root2] = by;
					groupMembers[root2] = new List<(int bx, int by)> { (bx, by) };
				}
			}
		}

		// ── Blocks: parks, or a BSP-subdivided building footprint per merge-group rectangle ──
		GameObject parkProp = config.parkPropPrefab;
		for (int bx = 0; bx < blocksX; bx++)
		{
			for (int by = 0; by < blocksY; by++)
			{
				if (!parkBlocks.Contains((bx, by))) continue;

				float left = offsetX + blockLeft[bx];
				float right = offsetX + blockRight[bx];
				float bottom = offsetZ + blockBottom[by];
				float top = offsetZ + blockTop[by];

				Vector3 center = new Vector3((left + right) * 0.5f, ParkTopY, (bottom + top) * 0.5f);
				CreateGroundCube(parksParent, $"Park_{bx}_{by}", center, new Vector3(right - left, RoadThickness, top - bottom), groundLayer, parkMat);

				if (parkProp != null)
				{
					int propCount = Mathf.Max(1, (colSize[bx] * rowSize[by]) / 2);
					for (int p = 0; p < propCount; p++)
					{
						float px = UnityEngine.Random.Range(left + 1f, right - 1f);
						float pz = UnityEngine.Random.Range(bottom + 1f, top - 1f);
						GameObject propGo = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(parkProp);
						propGo.transform.SetParent(parksParent, false);
						propGo.transform.localPosition = new Vector3(px, 0f, pz);
						propGo.transform.localRotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
					}
				}
			}
		}

		int buildingCount = 0;
		int blockRectCount = 0;
		int splitGroupCount = 0;
		var processedGroups = new HashSet<int>();
		for (int bx = 0; bx < blocksX; bx++)
		{
			for (int by = 0; by < blocksY; by++)
			{
				if (parkBlocks.Contains((bx, by))) continue;

				int root2 = Find(Index(bx, by));
				if (!processedGroups.Add(root2)) continue; // already handled this merge-group

				int minBx = groupMinBx[root2], maxBx = groupMaxBx[root2];
				int minBy = groupMinBy[root2], maxBy = groupMaxBy[root2];

				// Every internal boundary strictly inside the group's grid-index box must itself be
				// Merged -- not just "same union-find root" (reachable via some OTHER edge) -- or
				// there's a real, drawn street inside this box that building footprints must not
				// cover. A single member (minBx==maxBx && minBy==maxBy) trivially has none to check.
				bool isCleanRect = true;
				for (int ix = minBx; ix < maxBx && isCleanRect; ix++)
					for (int iy = minBy; iy <= maxBy; iy++)
						if (vKind[ix + 1, iy] != SegmentKind.Merged) { isCleanRect = false; break; }
				for (int iy = minBy; iy < maxBy && isCleanRect; iy++)
					for (int ix = minBx; ix <= maxBx; ix++)
						if (hKind[ix, iy + 1] != SegmentKind.Merged) { isCleanRect = false; break; }

				var rects = new List<(float left, float bottom, float right, float top)>();
				if (isCleanRect)
				{
					rects.Add((offsetX + blockLeft[minBx], offsetZ + blockBottom[minBy], offsetX + blockRight[maxBx], offsetZ + blockTop[maxBy]));
				}
				else
				{
					// Not a clean rectangle (some internal boundary inside the box was never actually
					// merged -- a real street runs through it) -- fall back to each member's own
					// exact block rectangle. They still touch with no street between them wherever a
					// pair actually did merge; this only gives up fusing them into one BSP footprint.
					splitGroupCount++;
					foreach (var member in groupMembers[root2])
					{
						rects.Add((offsetX + blockLeft[member.bx], offsetZ + blockBottom[member.by], offsetX + blockRight[member.bx], offsetZ + blockTop[member.by]));
					}
				}

				foreach (var rect in rects)
				{
					blockRectCount++;

					var leaves = new List<(float left, float bottom, float right, float top)>();
					SubdivideBsp(rect.left, rect.bottom, rect.right, rect.top, leaves);

					foreach (var leaf in leaves)
					{
						GameObject prefab = config.buildingPrefabs[UnityEngine.Random.Range(0, config.buildingPrefabs.Count)];
						if (prefab == null) continue;
						CreateBspBuilding(buildingsParent, prefab, leaf.left, leaf.bottom, leaf.right, leaf.top, config.buildingHeightScale, cellSize);
						buildingCount++;
					}

					// One NavMeshModifierVolume per block rectangle (not per building): marks the
					// whole footprint -- ground-level gaps between buildings AND rooftops -- Not
					// Walkable at bake time. Replaces per-building NavMeshObstacle carving entirely.
					CreateBlockNotWalkableVolume(navVolumesParent, $"BlockVolume_{bx}_{by}_{blockRectCount}", rect.left, rect.bottom, rect.right, rect.top);
				}
			}
		}

		if (splitGroupCount > 0)
			Debug.Log($"[CityGenerator] {splitGroupCount} merge group(s) formed a non-rectangular (L/cross) shape and were split back into independent block rectangles to avoid swallowing nearby streets.");

		// ── Spawn markers: one per internal street where it meets the frame, offset inward by one
		// street width so actors don't spawn flush against the wall or on top of the corner
		// intersection. Every vertical street (bx=0..blocksX) now reaches the frame at both ends
		// (there's no separate ring any more), same for every horizontal street. ──
		var north = new List<Vector3>();
		var south = new List<Vector3>();
		var west = new List<Vector3>();
		var east = new List<Vector3>();
		for (int bx = 0; bx <= blocksX; bx++)
		{
			south.Add(new Vector3(offsetX + streetCenterX[bx], 0f, offsetZ + streetCenterZ[0] + roadWidth));
			north.Add(new Vector3(offsetX + streetCenterX[bx], 0f, offsetZ + streetCenterZ[blocksY] - roadWidth));
		}
		for (int by = 0; by <= blocksY; by++)
		{
			west.Add(new Vector3(offsetX + streetCenterX[0] + roadWidth, 0f, offsetZ + streetCenterZ[by]));
			east.Add(new Vector3(offsetX + streetCenterX[blocksX] - roadWidth, 0f, offsetZ + streetCenterZ[by]));
		}

		CreateEdgeMarkers(spawnersParent, "North", north, SpawnGroup.North);
		CreateEdgeMarkers(spawnersParent, "South", south, SpawnGroup.South);
		CreateEdgeMarkers(spawnersParent, "West", west, SpawnGroup.West);
		CreateEdgeMarkers(spawnersParent, "East", east, SpawnGroup.East);

		// Special_1/Special_2: duplicate markers at two already-placed edge points (own groups
		// are independent of the cardinal-direction marker already sitting there).
		if (south.Count > 1) CreateMarker(spawnersParent, "SpawnPoint_Special_1", south[1], SpawnGroup.Special_1, false);
		if (east.Count > 1) CreateMarker(spawnersParent, "SpawnPoint_Special_2", east[1], SpawnGroup.Special_2, false);

		// CarWaypoints: two reachable points on opposite sides of the city, for the Car card's
		// entry/exit (CarController.cs picks the two tagged "CarWaypoint" farthest/nearest from the
		// tap position -- see CarController.LoadPassengers).
		CreateCarWaypoint(waypointsParent, "CarWaypoint_West", new Vector3(offsetX + streetCenterX[0] + roadWidth, 0f, offsetZ + cityDepth * 0.5f));
		CreateCarWaypoint(waypointsParent, "CarWaypoint_East", new Vector3(offsetX + streetCenterX[blocksX] - roadWidth, 0f, offsetZ + cityDepth * 0.5f));

		// ── Save prefab ──
		string path = string.IsNullOrEmpty(config.outputPrefabPath) ? "Assets/Prefabs/Levels/Level_Sandbox_Environment.prefab" : config.outputPrefabPath;
		PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
		UnityEngine.Object.DestroyImmediate(root);

		if (!success)
		{
			Debug.LogError("[CityGenerator] Failed to save prefab at " + path);
			return;
		}

		Debug.Log($"[CityGenerator] Generated {blocksX}x{blocksY} block city, {cityWidth:F1}x{cityDepth:F1}m (+{FrameThickness:F1}m frame), seed={config.seed}, roadWidth={roadWidth:F2}m, alleyWidth={alleyWidth:F2}m. {blockRectCount} block rectangles, {buildingCount} buildings. North/South markers={north.Count} each, West/East markers={west.Count} each.");

		RetuneSandboxCamera(config, totalWidth, totalDepth);

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
	}

	// feature/city-gen-v2: recursively splits a block rectangle into building-footprint leaves.
	// Splits the longer side at a random 35-65% fraction until a leaf's longer side is <= ~6m, or a
	// random early stop (only once the rect is already reasonably small, so a single huge block
	// doesn't skip subdivision entirely).
	private static void SubdivideBsp(float left, float bottom, float right, float top, List<(float left, float bottom, float right, float top)> leaves)
	{
		float width = right - left;
		float depth = top - bottom;
		float maxSide = Mathf.Max(width, depth);

		bool stopBySize = maxSide <= BspMinLeafSide;
		bool stopByRandom = maxSide <= BspMinLeafSide * 2f && UnityEngine.Random.value < BspRandomStopChance;

		if (stopBySize || stopByRandom || width < 1f || depth < 1f)
		{
			leaves.Add((left, bottom, right, top));
			return;
		}

		float splitFraction = UnityEngine.Random.Range(BspSplitFractionMin, BspSplitFractionMax);

		if (width >= depth)
		{
			float splitX = left + width * splitFraction;
			SubdivideBsp(left, bottom, splitX, top, leaves);
			SubdivideBsp(splitX, bottom, right, top, leaves);
		}
		else
		{
			float splitZ = bottom + depth * splitFraction;
			SubdivideBsp(left, bottom, right, splitZ, leaves);
			SubdivideBsp(left, splitZ, right, top, leaves);
		}
	}

	private static SegmentKind RollSegmentKind(CityGenConfig config)
	{
		if (UnityEngine.Random.value < config.mergeBlockChance) return SegmentKind.Merged;
		if (UnityEngine.Random.value < config.alleyChance)
		{
			float r = UnityEngine.Random.value;
			if (r < 0.5f) return SegmentKind.AlleyThrough;
			return UnityEngine.Random.value < 0.5f ? SegmentKind.AlleyDeadEndNear : SegmentKind.AlleyDeadEndFar;
		}
		return SegmentKind.Normal;
	}

	private static GameObject NewChild(Transform parent, string name)
	{
		GameObject go = new GameObject(name);
		go.transform.SetParent(parent, false);
		return go;
	}

	private static GameObject CreateGroundCube(Transform parent, string name, Vector3 centerLocal, Vector3 size, int groundLayer, Material material)
	{
		GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
		go.name = name;
		go.layer = groundLayer;
		go.transform.SetParent(parent, false);
		go.transform.localPosition = centerLocal - new Vector3(0f, size.y * 0.5f, 0f); // top face at y=centerLocal.y
		go.transform.localScale = size;
		if (material != null)
		{
			Renderer r = go.GetComponent<Renderer>();
			if (r != null) r.sharedMaterial = material;
		}
		return go;
	}

	// feature/street-segments: tags a road piece with its travel direction/width so barricade
	// placement (InputManager.cs) can snap to the centerline instead of guessing from the hit
	// collider's (always-identity) transform. Only road pieces get one -- BaseGround/Park_* are
	// not streets.
	private static void AddStreetSegment(GameObject go, Vector3 direction, float width, bool isIntersection)
	{
		StreetSegment segment = go.AddComponent<StreetSegment>();
		segment.Direction = direction;
		segment.Width = width;
		segment.IsIntersection = isIntersection;
	}

	// feature/city-gen-v2: solid perimeter wall. Left Untagged (not "Building") so Sniper placement
	// and CameraReadabilityConfig's Building-tag-based pan bounds don't pick it up; sits on top of
	// BaseGround (y=0) rather than centered on it like a road strip.
	private static void CreateFrameWall(Transform parent, string name, Vector3 centerXZAtGround, Vector3 size, Material material)
	{
		GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
		go.name = name;
		go.transform.SetParent(parent, false);
		go.transform.localPosition = new Vector3(centerXZAtGround.x, size.y * 0.5f, centerXZAtGround.z);
		go.transform.localScale = size;
		Renderer r = go.GetComponent<Renderer>();
		if (r != null && material != null) r.sharedMaterial = material;
	}

	// feature/city-gen-v2: replaces per-building NavMeshObstacle carving. One Not-Walkable volume
	// per block rectangle, tall enough to cover both the ground-level footprint and any building
	// rooftop the NavMesh bake might otherwise treat as a (harmless but unnecessary) walkable island.
	private static void CreateBlockNotWalkableVolume(Transform parent, string name, float left, float bottom, float right, float top)
	{
		GameObject go = new GameObject(name);
		go.transform.SetParent(parent, false);
		float centerX = (left + right) * 0.5f;
		float centerZ = (bottom + top) * 0.5f;
		float centerY = (BlockVolumeAboveGround - BlockVolumeBelowGround) * 0.5f;
		go.transform.localPosition = new Vector3(centerX, centerY, centerZ);

		NavMeshModifierVolume volume = go.AddComponent<NavMeshModifierVolume>();
		volume.size = new Vector3(right - left, BlockVolumeBelowGround + BlockVolumeAboveGround, top - bottom);
		volume.center = Vector3.zero;
		volume.area = NavMesh.GetAreaFromName("Not Walkable");
	}

	// feature/city-gen-v2: SimpleTown's Road.mat is an atlas texture authored for its own
	// fixed-size road meshes -- stretching its UVs across an arbitrarily large/small generated cube
	// reads as rainbow stripes. A flat, untextured color has no UVs to stretch.
	private static Material CreateOrUpdateFlatMaterial(string path, Color color)
	{
		Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
		if (mat == null)
		{
			Shader shader = Shader.Find("Universal Render Pipeline/Lit");
			mat = new Material(shader);
			AssetDatabase.CreateAsset(mat, path);
		}
		mat.color = color;
		EditorUtility.SetDirty(mat);
		return mat;
	}

	// feature/city-gen-v2: places one building per BSP leaf, scaling X/Z independently (non-uniform)
	// to fill the leaf with no gaps, instead of the old uniform footprint-fit scale. Rotation is
	// rolled and applied FIRST, then the footprint is re-measured from the rotated bounds -- so a
	// 90-degree facing still gets an exact non-uniform fit instead of swapping which scale axis
	// applies to which world axis. No NavMeshObstacle: the block's own NavMeshModifierVolume (see
	// CreateBlockNotWalkableVolume) already makes this whole rectangle Not Walkable.
	private static void CreateBspBuilding(Transform parent, GameObject prefab, float left, float bottom, float right, float top, float heightScaleBase, float referenceCellSize)
	{
		GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
		instance.transform.SetParent(parent, false);
		instance.transform.localPosition = Vector3.zero;
		instance.transform.localScale = Vector3.one;
		instance.transform.localRotation = Quaternion.identity;

		Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
		if (renderers.Length == 0)
		{
			UnityEngine.Object.DestroyImmediate(instance);
			return;
		}
		// Measured BEFORE rotating -- these are the mesh's own unrotated local-space extents.
		Bounds localBounds = renderers[0].bounds;
		for (int i = 1; i < renderers.Length; i++) localBounds.Encapsulate(renderers[i].bounds);

		float targetWidth = Mathf.Max(0.5f, (right - left) - BspSeamGap);
		float targetDepth = Mathf.Max(0.5f, (top - bottom) - BspSeamGap);

		// A 90/270 rotation swaps which world axis the object's local X/Z end up on. Scale is
		// applied in LOCAL space (before rotation), so for a swapping rotation the local-X scale
		// factor must target the DEPTH (it's local X that ends up spanning world Z), not the width --
		// getting this backwards is what silently produced wildly oversized buildings before.
		int rotationSteps = UnityEngine.Random.Range(0, 4);
		bool swapsAxes = (rotationSteps % 2) == 1; // 90 or 270 degrees

		float worldTargetForLocalX = swapsAxes ? targetDepth : targetWidth;
		float worldTargetForLocalZ = swapsAxes ? targetWidth : targetDepth;

		float scaleX = localBounds.size.x > 0.001f ? worldTargetForLocalX / localBounds.size.x : 1f;
		float scaleZ = localBounds.size.z > 0.001f ? worldTargetForLocalZ / localBounds.size.z : 1f;

		instance.transform.localRotation = Quaternion.Euler(0f, 90f * rotationSteps, 0f);

		// Tall slabs on a narrow leaf read wrong -- damp height by how small the leaf's narrower
		// side is relative to a "normal" building slot (buildingCellSize), on top of the usual
		// footprint-scale/heightScale relationship the old uniform-fit code used.
		float avgFootprintScale = (scaleX + scaleZ) * 0.5f;
		float narrowDamp = Mathf.Clamp01(Mathf.Min(targetWidth, targetDepth) / Mathf.Max(0.5f, referenceCellSize));
		float scaleY = avgFootprintScale * heightScaleBase * narrowDamp;

		instance.tag = "Building";

		MeshFilter mf = instance.GetComponent<MeshFilter>();
		if (mf != null && mf.sharedMesh != null && instance.GetComponent<MeshCollider>() == null)
		{
			MeshCollider mc = instance.AddComponent<MeshCollider>();
			mc.sharedMesh = mf.sharedMesh;
			mc.convex = false;
		}

		instance.transform.localScale = new Vector3(scaleX, Mathf.Max(0.05f, scaleY), scaleZ);
		instance.transform.localPosition = new Vector3((left + right) * 0.5f, 0f, (bottom + top) * 0.5f);
	}

	private static void CreateEdgeMarkers(Transform parent, string groupName, List<Vector3> positions, SpawnGroup group)
	{
		for (int i = 0; i < positions.Count; i++)
		{
			bool isNight = i == 0; // one of each direction's markers also serves Sudden Death, per brief ("4 of them also isNightSpawn")
			CreateMarker(parent, $"SpawnPoint_{groupName}_{i}", positions[i], group, isNight);
		}
	}

	private static void CreateMarker(Transform parent, string name, Vector3 pos, SpawnGroup group, bool isNight)
	{
		GameObject go = new GameObject(name);
		go.transform.SetParent(parent, false);
		go.transform.localPosition = pos;
		SpawnPointMarker marker = go.AddComponent<SpawnPointMarker>();
		marker.group = group;
		marker.isNightSpawn = isNight;
	}

	private static void CreateCarWaypoint(Transform parent, string name, Vector3 pos)
	{
		GameObject go = new GameObject(name);
		go.tag = "CarWaypoint";
		go.transform.SetParent(parent, false);
		go.transform.localPosition = pos;
	}

	// fix/sandbox-city-visuals: Level_Sandbox now matches Level_1's top-down convention (Perspective,
	// rotation (90,0,0) -- straight down, so ground distance == camera height exactly, no
	// perspective skew the way the old 60-degree pitch had). CameraController.SetupCamera applies
	// data.cameraPosition directly, then CameraZoomPanController.OnLevelCameraSetup treats the
	// result as the max zoom-OUT ceiling (CameraReadabilityConfig.defaultZoomFraction, shared/
	// global, is the OTHER end -- the resting zoom level after the level-intro shot).
	//
	// initialViewFraction asks for two things that are normally in tension against a single
	// authored value (the ceiling): the *resting* view should show roughly initialViewFraction of
	// the city, AND pinching/scrolling all the way out (ceiling) should always still reach the
	// *whole* city. Solved properly instead of picked by feel: back-solve the ceiling from
	// Lerp(ceiling, zoomInDistance, defaultZoomFraction) == target-rest-distance (duplicating
	// CameraZoomPanController.ComputeMaxZoomInGroundDistance's math read-only, since that method is
	// private and this must not edit that shared file), then clamp up to a tight full-city fit so
	// the second requirement always holds even if the solve would otherwise undershoot.
	//
	// feature/city-gen-v2: takes the FRAME-inclusive city dimensions now, so the whole bounded city
	// (including the perimeter wall) always fits at max zoom-out, not just the street/block area.
	private static void RetuneSandboxCamera(CityGenConfig config, float cityWidth, float cityDepth)
	{
		LevelData data = AssetDatabase.LoadAssetAtPath<LevelData>("Assets/Data/Level_Sandbox.asset");
		if (data == null)
		{
			Debug.LogWarning("[CityGenerator] Level_Sandbox.asset not found -- skipped camera retune.");
			return;
		}

		data.cameraType = LevelData.CameraType.Perspective; // matches Level_1
		data.cameraRotation = new Vector3(90f, 0f, 0f); // straight down, matches Level_1

		const float referenceAspect = 1080f / 1920f; // CameraFraming.ReferenceAspect
		const float margin = 1.15f; // breathing room on top of a tight fit (HUD reservations, pan padding)

		float fovRad = Mathf.Max(1f, data.cameraFieldOfView) * Mathf.Deg2Rad;
		float tanHalfFov = Mathf.Tan(fovRad * 0.5f);

		float cityDimForHeight = cityDepth;
		float cityDimForWidth = cityWidth / referenceAspect;
		float fullCityDim = Mathf.Max(cityDimForHeight, cityDimForWidth);
		float fullCityDistance = (fullCityDim / (2f * tanHalfFov)) * margin;

		CameraReadabilityConfig readability = CameraReadabilityConfig.Active;
		float zoomInDistance;
		float defaultZoomFraction;
		if (readability != null)
		{
			float targetFraction = readability.targetHumanScreenHeightMm / Mathf.Max(1f, readability.referenceScreenHeightMm);
			float requiredVisibleWorldHeight = readability.humanReferenceHeightWorldUnits / Mathf.Max(0.0001f, targetFraction);
			zoomInDistance = requiredVisibleWorldHeight / (2f * tanHalfFov);
			defaultZoomFraction = readability.defaultZoomFraction;
		}
		else
		{
			zoomInDistance = 4f;
			defaultZoomFraction = 0.475f;
		}

		float initialViewFraction = Mathf.Clamp(config.initialViewFraction, 0.1f, 1f);
		float targetRestDistance = (initialViewFraction * fullCityDim) / (2f * tanHalfFov);
		float solvedCeiling = (targetRestDistance - zoomInDistance * defaultZoomFraction) / Mathf.Max(0.01f, 1f - defaultZoomFraction);

		float ceiling = Mathf.Max(solvedCeiling, fullCityDistance);
		data.cameraPosition = new Vector3(0f, ceiling, 0f); // city is generated centered on local (0,0,0)

		EditorUtility.SetDirty(data);

		Debug.Log($"[CityGenerator] Retuned Level_Sandbox camera: top-down, height={ceiling:F1}m (full-city fit={fullCityDistance:F1}m, solved-for-{initialViewFraction:F2}-rest={solvedCeiling:F1}m, zoomInDistance={zoomInDistance:F1}m).");
	}

	private static CityGenConfig LoadOrCreateDefaultConfig()
	{
		CityGenConfig config = AssetDatabase.LoadAssetAtPath<CityGenConfig>(DefaultConfigPath);
		if (config == null)
		{
			config = ScriptableObject.CreateInstance<CityGenConfig>();
			AssetDatabase.CreateAsset(config, DefaultConfigPath);
		}

		bool dirty = false;
		if (config.buildingPrefabs == null) config.buildingPrefabs = new List<GameObject>();
		if (config.buildingPrefabs.Count == 0)
		{
			string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { BuildingPoolFolder });
			foreach (string guid in guids)
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				if (!System.IO.Path.GetFileName(path).StartsWith("Building_")) continue;
				GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
				if (prefab != null) config.buildingPrefabs.Add(prefab);
			}
			dirty = true;
		}
		if (config.parkPropPrefab == null)
		{
			config.parkPropPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultParkPropPath);
			dirty = true;
		}

		if (dirty)
		{
			EditorUtility.SetDirty(config);
			AssetDatabase.SaveAssets();
		}

		return config;
	}
}
