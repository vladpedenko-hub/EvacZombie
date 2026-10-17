using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

// feature/sandbox-big-city: procedurally rebuilds Level_Sandbox_Environment.prefab as a dense,
// AZS-style street grid roughly 3x the area of the old hand-placed sandbox city. Entirely an
// Editor-time tool -- run once via the menu item, inspect/tweak CityGenConfig, run again.
//
// Street-grid model: an (blocksX+1) x (blocksY+1) grid of intersections, always present (keeps the
// network connected even where an internal segment below is merged away). Each internal segment
// between two adjacent intersections independently rolls merge (skip entirely, fusing the two
// blocks it separated) / alley (narrower, and half-length "dead-end" or full-length "through") /
// normal. Column widths and row heights are rolled ONCE per column/row (not per block), so every
// internal street still runs straight across the whole grid -- only merge/alley interrupts it
// locally, never shifts it.
public static class CityGenerator
{
	private const string DefaultConfigPath = "Assets/Resources/CityGenConfig.asset";
	private const string BuildingPoolFolder = "Assets/ExternalAssets/SimpleTown/Prefabs/Buildings";
	private const string DefaultParkPropPath = "Assets/ExternalAssets/SimpleTown/Prefabs/Props/grass_square_mesh.prefab";
	private const string AsphaltMaterialPath = "Assets/Materials/SandboxCity_Asphalt.mat";
	private const string BlockBaseMaterialPath = "Assets/Materials/SandboxCity_BlockBase.mat";
	private const string ParkMaterialPath = "Assets/Materials/SandboxCity_Park.mat";
	private const float GroundThickness = 0.2f;
	private const float RoadThickness = 0.06f;
	private const float RoadTopY = 0.02f; // fix/sandbox-city-visuals: roads float slightly above the base plane
	private const float ParkTopY = 0.01f; // ...and park ground sits between the base plane and roads

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
		// immediately before block column/row bx/by (index blocksX/blocksY is the far ring road).
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

		// Vertical-segment merge/alley rolls, indexed [bx-1][by] for internal column boundaries
		// bx=1..blocksX-1 (bx=0 and bx=blocksX are the perimeter ring, always Normal/full).
		SegmentKind[,] vKind = new SegmentKind[Mathf.Max(1, blocksX - 1), blocksY];
		for (int bx = 1; bx < blocksX; bx++)
			for (int by = 0; by < blocksY; by++)
				vKind[bx - 1, by] = RollSegmentKind(config);

		SegmentKind[,] hKind = new SegmentKind[blocksX, Mathf.Max(1, blocksY - 1)];
		for (int by = 1; by < blocksY; by++)
			for (int bx = 0; bx < blocksX; bx++)
				hKind[bx, by - 1] = RollSegmentKind(config);

		// Parks: pick parkCount distinct blocks to leave building-free and walkable.
		var parkBlocks = new HashSet<(int, int)>();
		int totalBlocks = blocksX * blocksY;
		int parkCount = Mathf.Clamp(config.parkCount, 0, totalBlocks);
		while (parkBlocks.Count < parkCount)
			parkBlocks.Add((UnityEngine.Random.Range(0, blocksX), UnityEngine.Random.Range(0, blocksY)));

		Material asphaltMat = CreateOrUpdateFlatMaterial(AsphaltMaterialPath, config.asphaltColor);
		Material blockBaseMat = CreateOrUpdateFlatMaterial(BlockBaseMaterialPath, config.blockBaseColor);
		Material parkMat = CreateOrUpdateFlatMaterial(ParkMaterialPath, config.parkColor);

		GameObject root = new GameObject("Level_Sandbox_Environment");
		Transform groundParent = NewChild(root.transform, "Ground").transform;
		Transform buildingsParent = NewChild(root.transform, "Buildings").transform;
		Transform parksParent = NewChild(root.transform, "Parks").transform;
		Transform spawnersParent = NewChild(root.transform, "Spawners").transform;
		Transform waypointsParent = NewChild(root.transform, "Waypoints").transform;

		int groundLayer = LayerMask.NameToLayer("Ground");

		// ── Single base plane for the whole city (sidewalks/block ground), y=0. Everything else
		// (roads, parks) floats a hair above this one continuous mesh instead of being hundreds of
		// separate coplanar cubes touching edge-to-edge -- the latter z-fights under floating-point
		// seam error; a single mesh has no seams to fight.
		CreateGroundCube(groundParent, "BaseGround", new Vector3(cityWidth * 0.5f + offsetX, 0f, cityDepth * 0.5f + offsetZ),
			new Vector3(cityWidth, GroundThickness, cityDepth), groundLayer, blockBaseMat);

		// ── Intersections: always present, keeps the network connected regardless of merges ──
		for (int bx = 0; bx <= blocksX; bx++)
		{
			for (int by = 0; by <= blocksY; by++)
			{
				Vector3 center = new Vector3(offsetX + streetCenterX[bx], RoadTopY, offsetZ + streetCenterZ[by]);
				CreateGroundCube(groundParent, $"Intersection_{bx}_{by}", center, new Vector3(roadWidth, RoadThickness, roadWidth), groundLayer, asphaltMat);
			}
		}

		// ── Perimeter ring segments (always full-width, never merged/alley'd) ──
		for (int by = 0; by < blocksY; by++)
		{
			float z = offsetZ + (blockBottom[by] + blockTop[by]) * 0.5f;
			float len = blockTop[by] - blockBottom[by];
			CreateGroundCube(groundParent, $"Ring_W_{by}", new Vector3(offsetX + streetCenterX[0], RoadTopY, z), new Vector3(roadWidth, RoadThickness, len), groundLayer, asphaltMat);
			CreateGroundCube(groundParent, $"Ring_E_{by}", new Vector3(offsetX + streetCenterX[blocksX], RoadTopY, z), new Vector3(roadWidth, RoadThickness, len), groundLayer, asphaltMat);
		}
		for (int bx = 0; bx < blocksX; bx++)
		{
			float x = offsetX + (blockLeft[bx] + blockRight[bx]) * 0.5f;
			float len = blockRight[bx] - blockLeft[bx];
			CreateGroundCube(groundParent, $"Ring_S_{bx}", new Vector3(x, RoadTopY, offsetZ + streetCenterZ[0]), new Vector3(len, RoadThickness, roadWidth), groundLayer, asphaltMat);
			CreateGroundCube(groundParent, $"Ring_N_{bx}", new Vector3(x, RoadTopY, offsetZ + streetCenterZ[blocksY]), new Vector3(len, RoadThickness, roadWidth), groundLayer, asphaltMat);
		}

		// ── Internal vertical street segments (between column bx-1 and bx, at row by) ──
		for (int bx = 1; bx < blocksX; bx++)
		{
			for (int by = 0; by < blocksY; by++)
			{
				SegmentKind kind = vKind[bx - 1, by];
				if (kind == SegmentKind.Merged) continue;

				float fullBottom = blockBottom[by];
				float fullTop = blockTop[by];
				float width = (kind == SegmentKind.Normal) ? roadWidth : alleyWidth;
				float zBottom = fullBottom, zTop = fullTop;
				if (kind == SegmentKind.AlleyDeadEndNear) zTop = fullBottom + (fullTop - fullBottom) * 0.5f;
				else if (kind == SegmentKind.AlleyDeadEndFar) zBottom = fullTop - (fullTop - fullBottom) * 0.5f;

				float zCenter = offsetZ + (zBottom + zTop) * 0.5f;
				float len = zTop - zBottom;
				CreateGroundCube(groundParent, $"Street_V_{bx}_{by}", new Vector3(offsetX + streetCenterX[bx], RoadTopY, zCenter), new Vector3(width, RoadThickness, len), groundLayer, asphaltMat);
			}
		}

		// ── Internal horizontal street segments (between row by-1 and by, at column bx) ──
		for (int by = 1; by < blocksY; by++)
		{
			for (int bx = 0; bx < blocksX; bx++)
			{
				SegmentKind kind = hKind[bx, by - 1];
				if (kind == SegmentKind.Merged) continue;

				float fullLeft = blockLeft[bx];
				float fullRight = blockRight[bx];
				float width = (kind == SegmentKind.Normal) ? roadWidth : alleyWidth;
				float xLeft = fullLeft, xRight = fullRight;
				if (kind == SegmentKind.AlleyDeadEndNear) xRight = fullLeft + (fullRight - fullLeft) * 0.5f;
				else if (kind == SegmentKind.AlleyDeadEndFar) xLeft = fullRight - (fullRight - fullLeft) * 0.5f;

				float xCenter = offsetX + (xLeft + xRight) * 0.5f;
				float len = xRight - xLeft;
				CreateGroundCube(groundParent, $"Street_H_{bx}_{by}", new Vector3(xCenter, RoadTopY, offsetZ + streetCenterZ[by]), new Vector3(len, RoadThickness, width), groundLayer, asphaltMat);
			}
		}

		// ── Blocks: buildings, or a park ──
		GameObject parkProp = config.parkPropPrefab;
		for (int bx = 0; bx < blocksX; bx++)
		{
			for (int by = 0; by < blocksY; by++)
			{
				float left = offsetX + blockLeft[bx];
				float right = offsetX + blockRight[bx];
				float bottom = offsetZ + blockBottom[by];
				float top = offsetZ + blockTop[by];

				if (parkBlocks.Contains((bx, by)))
				{
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
					continue;
				}

				for (int cx = 0; cx < colSize[bx]; cx++)
				{
					for (int cz = 0; cz < rowSize[by]; cz++)
					{
						float centerX = left + (cx + 0.5f) * cellSize;
						float centerZ = bottom + (cz + 0.5f) * cellSize;
						GameObject prefab = config.buildingPrefabs[UnityEngine.Random.Range(0, config.buildingPrefabs.Count)];
						if (prefab == null) continue;
						CreateBuilding(buildingsParent, prefab, new Vector3(centerX, 0f, centerZ), cellSize * 0.85f, config.buildingHeightScale);
					}
				}
			}
		}

		// ── Edge spawn points: one per internal street where it reaches the perimeter ring ──
		var north = new List<Vector3>();
		var south = new List<Vector3>();
		var west = new List<Vector3>();
		var east = new List<Vector3>();
		for (int bx = 1; bx < blocksX; bx++)
		{
			south.Add(new Vector3(offsetX + streetCenterX[bx], 0f, offsetZ + streetCenterZ[0]));
			north.Add(new Vector3(offsetX + streetCenterX[bx], 0f, offsetZ + streetCenterZ[blocksY]));
		}
		for (int by = 1; by < blocksY; by++)
		{
			west.Add(new Vector3(offsetX + streetCenterX[0], 0f, offsetZ + streetCenterZ[by]));
			east.Add(new Vector3(offsetX + streetCenterX[blocksX], 0f, offsetZ + streetCenterZ[by]));
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
		CreateCarWaypoint(waypointsParent, "CarWaypoint_West", new Vector3(offsetX + streetCenterX[0], 0f, offsetZ + cityDepth * 0.5f));
		CreateCarWaypoint(waypointsParent, "CarWaypoint_East", new Vector3(offsetX + streetCenterX[blocksX], 0f, offsetZ + cityDepth * 0.5f));

		// ── Save prefab ──
		string path = string.IsNullOrEmpty(config.outputPrefabPath) ? "Assets/Prefabs/Levels/Level_Sandbox_Environment.prefab" : config.outputPrefabPath;
		PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
		UnityEngine.Object.DestroyImmediate(root);

		if (!success)
		{
			Debug.LogError("[CityGenerator] Failed to save prefab at " + path);
			return;
		}

		Debug.Log($"[CityGenerator] Generated {blocksX}x{blocksY} block city, {cityWidth:F1}x{cityDepth:F1}m, seed={config.seed}. North/South markers={north.Count} each, West/East markers={west.Count} each.");

		RetuneSandboxCamera(config, cityWidth, cityDepth);

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
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

	private static void CreateGroundCube(Transform parent, string name, Vector3 centerLocal, Vector3 size, int groundLayer, Material material)
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
	}

	// fix/sandbox-city-visuals: SimpleTown's Road.mat is an atlas texture authored for its own
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

	private static void CreateBuilding(Transform parent, GameObject prefab, Vector3 centerLocal, float targetFootprint, float heightScale)
	{
		GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
		instance.transform.SetParent(parent, false);
		instance.transform.localPosition = Vector3.zero;
		instance.transform.localRotation = Quaternion.identity;
		instance.transform.localScale = Vector3.one;

		Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
		if (renderers.Length == 0)
		{
			UnityEngine.Object.DestroyImmediate(instance);
			return;
		}
		Bounds localBounds = renderers[0].bounds;
		for (int i = 1; i < renderers.Length; i++) localBounds.Encapsulate(renderers[i].bounds);

		float maxHorizontal = Mathf.Max(localBounds.size.x, localBounds.size.z);
		float scale = maxHorizontal > 0.001f ? targetFootprint / maxHorizontal : 1f;

		instance.tag = "Building";
		NavMeshObstacle obstacle = instance.GetComponent<NavMeshObstacle>();
		if (obstacle == null) obstacle = instance.AddComponent<NavMeshObstacle>();
		obstacle.shape = NavMeshObstacleShape.Box;
		obstacle.center = localBounds.center;
		obstacle.size = localBounds.size;
		obstacle.carving = true;

		MeshFilter mf = instance.GetComponent<MeshFilter>();
		if (mf != null && mf.sharedMesh != null && instance.GetComponent<MeshCollider>() == null)
		{
			MeshCollider mc = instance.AddComponent<MeshCollider>();
			mc.sharedMesh = mf.sharedMesh;
			mc.convex = false;
		}

		// fix/sandbox-city-visuals: Y-only on top of the uniform footprint-fit scale -- the
		// NavMeshObstacle's size/center above were set from the unscaled mesh bounds and are scaled
		// by the transform like any NavMesh carving, so squashing only Y here shortens the building
		// visually without touching the X/Z footprint it carves out of the NavMesh.
		instance.transform.localScale = new Vector3(scale, scale * heightScale, scale);
		instance.transform.localRotation = Quaternion.Euler(0f, 90f * UnityEngine.Random.Range(0, 4), 0f);
		instance.transform.localPosition = centerLocal;
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
