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
	private const string DefaultRoadMaterialPath = "Assets/ExternalAssets/SimpleTown/Materials/Road.mat";
	private const string DefaultParkPropPath = "Assets/ExternalAssets/SimpleTown/Prefabs/Props/grass_square_mesh.prefab";
	private const float GroundThickness = 0.2f;

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

		GameObject root = new GameObject("Level_Sandbox_Environment");
		Transform groundParent = NewChild(root.transform, "Ground").transform;
		Transform buildingsParent = NewChild(root.transform, "Buildings").transform;
		Transform parksParent = NewChild(root.transform, "Parks").transform;
		Transform spawnersParent = NewChild(root.transform, "Spawners").transform;
		Transform waypointsParent = NewChild(root.transform, "Waypoints").transform;

		int groundLayer = LayerMask.NameToLayer("Ground");

		// ── Intersections: always present, keeps the network connected regardless of merges ──
		for (int bx = 0; bx <= blocksX; bx++)
		{
			for (int by = 0; by <= blocksY; by++)
			{
				Vector3 center = new Vector3(offsetX + streetCenterX[bx], 0f, offsetZ + streetCenterZ[by]);
				CreateGroundCube(groundParent, $"Intersection_{bx}_{by}", center, new Vector3(roadWidth, GroundThickness, roadWidth), groundLayer, config.roadMaterial);
			}
		}

		// ── Perimeter ring segments (always full-width, never merged/alley'd) ──
		for (int by = 0; by < blocksY; by++)
		{
			float z = offsetZ + (blockBottom[by] + blockTop[by]) * 0.5f;
			float len = blockTop[by] - blockBottom[by];
			CreateGroundCube(groundParent, $"Ring_W_{by}", new Vector3(offsetX + streetCenterX[0], 0f, z), new Vector3(roadWidth, GroundThickness, len), groundLayer, config.roadMaterial);
			CreateGroundCube(groundParent, $"Ring_E_{by}", new Vector3(offsetX + streetCenterX[blocksX], 0f, z), new Vector3(roadWidth, GroundThickness, len), groundLayer, config.roadMaterial);
		}
		for (int bx = 0; bx < blocksX; bx++)
		{
			float x = offsetX + (blockLeft[bx] + blockRight[bx]) * 0.5f;
			float len = blockRight[bx] - blockLeft[bx];
			CreateGroundCube(groundParent, $"Ring_S_{bx}", new Vector3(x, 0f, offsetZ + streetCenterZ[0]), new Vector3(len, GroundThickness, roadWidth), groundLayer, config.roadMaterial);
			CreateGroundCube(groundParent, $"Ring_N_{bx}", new Vector3(x, 0f, offsetZ + streetCenterZ[blocksY]), new Vector3(len, GroundThickness, roadWidth), groundLayer, config.roadMaterial);
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
				CreateGroundCube(groundParent, $"Street_V_{bx}_{by}", new Vector3(offsetX + streetCenterX[bx], 0f, zCenter), new Vector3(width, GroundThickness, len), groundLayer, config.roadMaterial);
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
				CreateGroundCube(groundParent, $"Street_H_{bx}_{by}", new Vector3(xCenter, 0f, offsetZ + streetCenterZ[by]), new Vector3(len, GroundThickness, width), groundLayer, config.roadMaterial);
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
					Vector3 center = new Vector3((left + right) * 0.5f, 0f, (bottom + top) * 0.5f);
					CreateGroundCube(parksParent, $"Park_{bx}_{by}", center, new Vector3(right - left, GroundThickness, top - bottom), groundLayer, config.roadMaterial);

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
						CreateBuilding(buildingsParent, prefab, new Vector3(centerX, 0f, centerZ), cellSize * 0.85f);
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

		RetuneSandboxCamera(cityWidth, cityDepth);

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

	private static void CreateBuilding(Transform parent, GameObject prefab, Vector3 centerLocal, float targetFootprint)
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

		instance.transform.localScale = Vector3.one * scale;
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

	// Retunes Level_Sandbox's authored camera (the CameraController.SetupCamera fit-to-width max
	// zoom-out limit, see CameraFraming.cs / Docs/CAMERA_READABILITY.md) so the new, much bigger
	// city still fits when fully zoomed out. Keeps the authored rotation/FOV -- only dollies the
	// position back along the same forward axis, the same way ComputePerspectivePullback already
	// treats "more area needed" (never re-aims the shot, just moves it back).
	private static void RetuneSandboxCamera(float cityWidth, float cityDepth)
	{
		LevelData data = AssetDatabase.LoadAssetAtPath<LevelData>("Assets/Data/Level_Sandbox.asset");
		if (data == null)
		{
			Debug.LogWarning("[CityGenerator] Level_Sandbox.asset not found -- skipped camera retune.");
			return;
		}

		const float referenceAspect = 1080f / 1920f; // CameraFraming.ReferenceAspect
		const float margin = 1.2f; // extra breathing room on top of a tight fit (HUD reservations, pan padding)

		Vector3 forward = Quaternion.Euler(data.cameraRotation) * Vector3.forward;
		if (Mathf.Abs(forward.y) < 0.0001f)
		{
			Debug.LogWarning("[CityGenerator] Sandbox camera rotation is not pitched downward -- skipped camera retune.");
			return;
		}

		float fovRad = Mathf.Max(1f, data.cameraFieldOfView) * Mathf.Deg2Rad;
		float tanHalfFov = Mathf.Tan(fovRad * 0.5f);

		float depthForHeight = cityDepth / (2f * tanHalfFov);
		float depthForWidth = cityWidth / (2f * tanHalfFov * referenceAspect);
		float dolly = Mathf.Max(depthForHeight, depthForWidth) * margin;

		Vector3 groundHit = Vector3.zero; // city is generated centered on local (0,0,0)
		data.cameraPosition = groundHit - forward * dolly;
		EditorUtility.SetDirty(data);

		Debug.Log($"[CityGenerator] Retuned Level_Sandbox camera: position={data.cameraPosition}, dolly={dolly:F1}m.");
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
		if (config.roadMaterial == null)
		{
			config.roadMaterial = AssetDatabase.LoadAssetAtPath<Material>(DefaultRoadMaterialPath);
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
