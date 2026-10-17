using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Shared road-grid + distance-field crowd system (feature/zombie-street-ai). Builds one walkable
// grid from the baked NavMesh per level, then every chaosSettings.fieldRefresh seconds recomputes
// three multi-source Dijkstra distance fields over that grid:
//   F_civ  — distance-to-nearest-civilian, consumed by Zombie (Hunter descent)
//   F_zed  — distance-to-nearest-zombie,   consumed by EvacueeBase (Panic ascent)
//   F_evac — distance-to-nearest-active-evac-point, consumed by EvacueeBase (Evacuating descent)
// F_civ/F_zed share the "zombie" cost structure (a tile with an active Barricade costs
// barricadePathCost instead of 1 — barricades slow zombies down but don't block them outright).
// F_evac uses uniform cost 1: civilians pass through barricades freely.
//
// Individual agents never run their own per-frame pathfinding against these fields — they just
// read the precomputed value at their own cell and its 4 neighbors (O(1)) to pick a direction,
// at most once per chaosSettings.retargetCooldown. This is what actually removes the old
// per-tick-shortlist-and-CalculatePath jitter in Zombie.cs, not a patch on top of it.
//
// Auto-created the same way AudioManager/etc. are (RuntimeInitializeOnLoadMethod) — no scene setup.
public enum CrowdFieldType { Civilian, Zombie, Evac }

public class CrowdFieldManager : MonoBehaviour
{
	public static CrowdFieldManager Instance { get; private set; }

	public bool GridReady { get; private set; }
	public float CellSize => cellSize;

	// Whether every zombie on the level should currently be forced into the Hunter role
	// (aliveCivilians dropped below chaosSettings.lateWaveHunterThreshold of the level's peak).
	public bool ForceAllHunters { get; private set; }

	private CrowdTuning cachedSettings;

	private float cellSize;
	private int gridW;
	private int gridH;
	private Vector3 originWorld;

	private bool[] walkable;
	private Vector3[] cellWorldPos;
	private readonly List<int> intersectionCells = new List<int>();
	private bool[] isIntersectionCell; // parallel to walkable — O(1) membership test for lookahead stepping

	private float[] fieldCiv;
	private float[] fieldZed;
	private float[] fieldEvac;

	private bool pendingRebuild;
	private float nextRefreshTime;

	// feature/sandbox-big-city: logs grid size once per level load, and the first field-recompute's
	// wall-clock cost once per level load -- a quick sanity read on how the much bigger procedural
	// sandbox city scales, without spamming every fieldRefresh tick.
	private bool loggedFirstRecompute;

	private int initialCivilianCountSeen;

	private readonly List<IEvacPoint> evacPoints = new List<IEvacPoint>();

	private readonly List<int> civSources = new List<int>();
	private readonly List<int> zedSources = new List<int>();
	private readonly List<int> evacSources = new List<int>();
	private readonly HashSet<int> barricadeCellsThisRefresh = new HashSet<int>();
	private float barricadePathCostCached = 15f;

	// Reusable binary min-heap for Dijkstra — sized to the grid's cell count once, cleared (not
	// reallocated) on every refresh, so recomputing the three fields every 0.3s does not allocate.
	private float[] heapKey;
	private int[] heapNode;
	private int[] heapIndexOf;
	private int heapSize;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ResetStatics() => Instance = null;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Bootstrap()
	{
		if (Instance != null) return;
		var go = new GameObject("[CrowdFieldManager]");
		DontDestroyOnLoad(go);
		Instance = go.AddComponent<CrowdFieldManager>();
	}

	private void Awake()
	{
		if (Instance != null && Instance != this) { Destroy(gameObject); return; }
		Instance = this;

		// fix/barricade-jerk-wave1 (Bug 1): react to a barricade placement/destruction the same
		// frame it happens, instead of waiting for the next fieldRefresh (up to 0.3s) during
		// which NavMeshAgent's own auto-repath (triggered instantly by the carving) was visibly
		// jerking chasing zombies backward with nothing correcting it.
		Barricade.OnBarricadeChanged += HandleBarricadeChanged;
	}

	private void Update()
	{
		if (pendingRebuild)
		{
			pendingRebuild = false;
			BuildGrid();
		}

		if (!GridReady) return;

		float refresh = cachedSettings != null ? Mathf.Max(0.05f, cachedSettings.fieldRefresh) : 0.3f;
		if (Time.time < nextRefreshTime) return;

		nextRefreshTime = Time.time + refresh;
		RecomputeFields();
	}

	// fix/barricade-jerk-wave1 (Bug 1): recomputes the fields right now (not on the usual
	// fieldRefresh cadence), then has every zombie within barricadeReactRadiusCells of the
	// change immediately re-run its own decision logic this same frame.
	private void HandleBarricadeChanged(Vector3 position)
	{
		if (!GridReady) return;

		RecomputeFields();
		nextRefreshTime = Time.time + (cachedSettings != null ? Mathf.Max(0.05f, cachedSettings.fieldRefresh) : 0.3f);

		float radiusCells = cachedSettings != null ? cachedSettings.barricadeReactRadiusCells : 12f;
		float radiusWorld = radiusCells * cellSize;
		float radiusSqr = radiusWorld * radiusWorld;

		foreach (Zombie z in Zombie.AllZombies)
		{
			if (z == null) continue;
			if ((z.transform.position - position).sqrMagnitude <= radiusSqr)
			{
				z.ForceImmediateRedecision();
			}
		}
	}

	// Called from LevelManager right after navSurface.BuildNavMesh(). The actual grid build is
	// deferred to this object's own next Update() rather than done synchronously here: at this
	// exact point in LoadLevel() no civilian/zombie has spawned yet, so EnsureConfigured() hasn't
	// cached a CrowdTuning reference for grid-resolution tuning. Humans/Scientists spawn later in
	// the same LoadLevel() call and their Start() runs before this object's next Update(), so by
	// rebuild time a settings reference is already cached in the common case (deliberately NOT
	// reset here — the same CrowdTuning asset is shared project-wide, so keeping the last-known
	// reference across level loads means even a level with zero initial civilians gets correctly
	// tuned grid resolution instead of falling back to field defaults).
	public void OnLevelLoaded()
	{
		GridReady = false;
		pendingRebuild = true;
		initialCivilianCountSeen = 0;
		ForceAllHunters = false;
		loggedFirstRecompute = false;
	}

	public void EnsureConfigured(CrowdTuning settings)
	{
		if (cachedSettings == null && settings != null) cachedSettings = settings;
	}

	// feature/sandbox-level: lets the dev panel's preset dropdown force-switch the active
	// CrowdTuning asset at runtime (unlike EnsureConfigured, which only ever sets it once).
	public CrowdTuning ActiveTuning => cachedSettings;

	public void OverrideTuning(CrowdTuning tuning)
	{
		if (tuning != null) cachedSettings = tuning;
	}

	public void RegisterEvacPoint(IEvacPoint point)
	{
		if (!evacPoints.Contains(point)) evacPoints.Add(point);
	}

	public void UnregisterEvacPoint(IEvacPoint point)
	{
		evacPoints.Remove(point);
	}

	// ── Grid build ──────────────────────────────────────────────────────

	private void BuildGrid()
	{
		NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
		if (tri.vertices == null || tri.vertices.Length == 0)
		{
			GridReady = false;
			return;
		}

		float minX = float.MaxValue, maxX = float.MinValue;
		float minZ = float.MaxValue, maxZ = float.MinValue;
		double sumY = 0d;

		for (int i = 0; i < tri.vertices.Length; i++)
		{
			Vector3 v = tri.vertices[i];
			if (v.x < minX) minX = v.x;
			if (v.x > maxX) maxX = v.x;
			if (v.z < minZ) minZ = v.z;
			if (v.z > maxZ) maxZ = v.z;
			sumY += v.y;
		}

		float avgY = (float)(sumY / tri.vertices.Length);
		float spanX = Mathf.Max(1f, maxX - minX);
		float spanZ = Mathf.Max(1f, maxZ - minZ);

		float minCell = cachedSettings != null ? Mathf.Max(0.25f, cachedSettings.roadGridMinCellSize) : 2f;
		float neededCell;
		// feature/sandbox-big-city: the procedural sandbox city's streets/alleys have a known
		// physical width -- basing the cell size on that directly (instead of span/targetPerAxis)
		// guarantees a cell never ends up wider than the narrowest alley, which would otherwise
		// collapse a walkable lane into zero passable cells regardless of how high targetPerAxis is set.
		if (cachedSettings != null && cachedSettings.cellSizeFromRoadWidth && cachedSettings.roadWidth > 0f)
		{
			neededCell = cachedSettings.roadWidth * 0.5f;
		}
		else
		{
			int targetPerAxis = cachedSettings != null ? Mathf.Max(4, cachedSettings.roadGridTargetCellsPerAxis) : 48;
			neededCell = Mathf.Max(spanX, spanZ) / targetPerAxis;
		}
		cellSize = Mathf.Max(minCell, neededCell);

		originWorld = new Vector3(minX, avgY, minZ);
		gridW = Mathf.Max(1, Mathf.CeilToInt(spanX / cellSize) + 1);
		gridH = Mathf.Max(1, Mathf.CeilToInt(spanZ / cellSize) + 1);

		int count = gridW * gridH;
		walkable = new bool[count];
		cellWorldPos = new Vector3[count];

		// fix/zombie-corner-stuck: a wide acceptance radius here (previously 0.6*cellSize) let a
		// cell snap to a NavMesh point near a building corner that was really outside this cell's
		// footprint, making the walkable region cut diagonally through corners -- the field then
		// pointed zombies through geometry that wasn't actually open, and NavMeshAgent fighting the
		// field's intent every decision tick looked like "stuck at the corner." Tightening to
		// roadGridSnapTolerance (default 0.35) means a cell is only walkable if NavMesh is genuinely
		// close to ITS OWN center.
		float snapTolerance = cachedSettings != null ? Mathf.Clamp(cachedSettings.roadGridSnapTolerance, 0.05f, 1f) : 0.35f;
		float sampleRadius = cellSize * snapTolerance;
		for (int y = 0; y < gridH; y++)
		{
			for (int x = 0; x < gridW; x++)
			{
				int idx = y * gridW + x;
				Vector3 center = new Vector3(originWorld.x + x * cellSize, avgY, originWorld.z + y * cellSize);
				if (NavMesh.SamplePosition(center, out NavMeshHit hit, sampleRadius, NavMesh.AllAreas))
				{
					walkable[idx] = true;
					cellWorldPos[idx] = hit.position;
				}
			}
		}

		BuildIntersections();

		heapKey = new float[count];
		heapNode = new int[count];
		heapIndexOf = new int[count];

		fieldCiv = new float[count];
		fieldZed = new float[count];
		fieldEvac = new float[count];

		GridReady = true;
		nextRefreshTime = 0f; // recompute immediately on the next Update

		Debug.Log($"[CrowdFieldManager] Grid built: {gridW}x{gridH} cells ({count} total), cellSize={cellSize:F2}m, span={spanX:F1}x{spanZ:F1}m");
	}

	private void BuildIntersections()
	{
		intersectionCells.Clear();
		isIntersectionCell = new bool[walkable.Length];

		for (int idx = 0; idx < walkable.Length; idx++)
		{
			if (!walkable[idx]) continue;

			int x = idx % gridW;
			int y = idx / gridW;
			bool n = IsWalkableAt(x, y + 1);
			bool s = IsWalkableAt(x, y - 1);
			bool e = IsWalkableAt(x + 1, y);
			bool w = IsWalkableAt(x - 1, y);
			int degree = (n ? 1 : 0) + (s ? 1 : 0) + (e ? 1 : 0) + (w ? 1 : 0);
			bool corner = degree == 2 && !((n && s) || (e && w));

			if (degree >= 3 || corner)
			{
				intersectionCells.Add(idx);
				isIntersectionCell[idx] = true;
			}
		}

		// Sparse/degenerate road shapes (no true junction) — fall back to any walkable cell so
		// Wanderers always have somewhere to pick. Deliberately NOT marked in isIntersectionCell:
		// that array means "a real junction", used to stop the Hunter lookahead early (fix 3) —
		// treating every walkable cell as a junction there would collapse the lookahead back to
		// single-cell steps, the exact stop-and-go this branch exists to avoid.
		if (intersectionCells.Count == 0)
		{
			for (int idx = 0; idx < walkable.Length; idx++)
				if (walkable[idx]) intersectionCells.Add(idx);
		}
	}

	private bool IsWalkableAt(int x, int y)
	{
		if (x < 0 || x >= gridW || y < 0 || y >= gridH) return false;
		return walkable[y * gridW + x];
	}

	// ── Field recompute ─────────────────────────────────────────────────

	private void RecomputeFields()
	{
		System.Diagnostics.Stopwatch sw = !loggedFirstRecompute ? System.Diagnostics.Stopwatch.StartNew() : null;

		// feature/azs-crowd-feel (3): infected civilians stop seeding F_civ the instant
		// BeginInfection runs, even though they stay in AllHumans/AllScientists until they
		// actually turn (other systems, e.g. the car/heli siren loops, still iterate that list).
		civSources.Clear();
		foreach (var h in Human.AllHumans) if (h != null && !h.IsInfected) AddSource(civSources, h.transform.position);
		foreach (var s in Scientist.AllScientists) if (s != null && !s.IsInfected) AddSource(civSources, s.transform.position);

		zedSources.Clear();
		foreach (var z in Zombie.AllZombies) if (z != null && !z.IsDead) AddSource(zedSources, z.transform.position);

		evacSources.Clear();
		for (int i = 0; i < evacPoints.Count; i++)
		{
			IEvacPoint e = evacPoints[i];
			if (e != null && e.IsAcceptingPassengers) AddSource(evacSources, e.EvacPosition);
		}

		barricadeCellsThisRefresh.Clear();
		for (int i = 0; i < Barricade.AllBarricades.Count; i++)
		{
			Barricade b = Barricade.AllBarricades[i];
			if (b == null) continue;
			int idx = WorldToCellIndex(b.transform.position);
			if (idx >= 0) barricadeCellsThisRefresh.Add(idx);
		}

		barricadePathCostCached = cachedSettings != null ? Mathf.Max(1f, cachedSettings.barricadePathCost) : 15f;

		ComputeField(civSources, true, fieldCiv);
		ComputeField(zedSources, true, fieldZed);
		ComputeField(evacSources, false, fieldEvac);

		int aliveCivilians = Human.AllHumans.Count + Scientist.AllScientists.Count;
		if (aliveCivilians > initialCivilianCountSeen) initialCivilianCountSeen = aliveCivilians;

		float threshold = cachedSettings != null ? cachedSettings.lateWaveHunterThreshold : 0.2f;
		ForceAllHunters = initialCivilianCountSeen > 0 && aliveCivilians < initialCivilianCountSeen * threshold;

		if (sw != null)
		{
			sw.Stop();
			loggedFirstRecompute = true;
			Debug.Log($"[CrowdFieldManager] First field recompute after load: {sw.Elapsed.TotalMilliseconds:F2}ms ({gridW}x{gridH} cells)");
		}
	}

	private void AddSource(List<int> list, Vector3 worldPos)
	{
		int idx = WorldToCellIndex(worldPos);
		if (idx >= 0) list.Add(idx);
	}

	private void ComputeField(List<int> sources, bool useBarricadeCost, float[] outField)
	{
		for (int i = 0; i < outField.Length; i++) outField[i] = float.PositiveInfinity;

		heapSize = 0;
		for (int i = 0; i < heapIndexOf.Length; i++) heapIndexOf[i] = -1;

		for (int i = 0; i < sources.Count; i++)
		{
			int s = sources[i];
			if (s < 0 || s >= outField.Length || !walkable[s]) continue;
			if (outField[s] <= 0f) continue; // already seeded at 0 by an earlier source in this same call
			outField[s] = 0f;
			HeapPush(s, 0f);
		}

		while (heapSize > 0)
		{
			int node = HeapPopMin();
			float d = outField[node];
			int x = node % gridW;
			int y = node / gridW;

			TryRelax(x + 1, y, d, useBarricadeCost, outField);
			TryRelax(x - 1, y, d, useBarricadeCost, outField);
			TryRelax(x, y + 1, d, useBarricadeCost, outField);
			TryRelax(x, y - 1, d, useBarricadeCost, outField);
		}
	}

	private void TryRelax(int nx, int ny, float dFrom, bool useBarricadeCost, float[] outField)
	{
		if (nx < 0 || nx >= gridW || ny < 0 || ny >= gridH) return;
		int nIdx = ny * gridW + nx;
		if (!walkable[nIdx]) return;

		float cost = (useBarricadeCost && barricadeCellsThisRefresh.Contains(nIdx)) ? barricadePathCostCached : 1f;
		float nd = dFrom + cost;

		if (nd < outField[nIdx])
		{
			outField[nIdx] = nd;
			HeapDecreaseOrPush(nIdx, nd);
		}
	}

	// ── Queries used by Zombie / EvacueeBase ───────────────────────────

	public float SampleField(Vector3 worldPos, CrowdFieldType type)
	{
		if (!GridReady) return float.PositiveInfinity;

		// fix/barricade-jerk-wave1 (Bug 2b): a straight round-to-nearest-cell lookup reads
		// Infinity for a perfectly valid position whose nearest cell happens to be non-walkable
		// (common at edges/corners) -- every "is anyone reachable from here" check funnels
		// through this method, so resolving to the nearest ACTUALLY walkable cell here fixes it
		// everywhere at once instead of needing each call site to remember to do it.
		int idx = ResolveWalkableCell(worldPos);
		if (idx < 0) return float.PositiveInfinity;
		return SelectField(type)[idx];
	}

	// fix/barricade-jerk-wave1 (diagnostic): whether worldPos's own nearest grid cell (no
	// ResolveWalkableCell fallback) is walkable -- deliberately the raw/unresolved reading, so
	// the stall diagnostic log can tell "own cell is the problem" apart from "own cell is fine,
	// something else produced no destination".
	public bool IsOwnCellWalkable(Vector3 worldPos)
	{
		if (!GridReady) return false;
		int idx = WorldToCellIndex(worldPos);
		return idx >= 0 && walkable[idx];
	}

	public bool TryGetDescentDirection(Vector3 fromWorld, CrowdFieldType type, out Vector3 targetWorldPos)
	{
		return TryGetDirectionalStep(fromWorld, type, steepestDescent: true, out targetWorldPos);
	}

	public bool TryGetAscentDirection(Vector3 fromWorld, CrowdFieldType type, out Vector3 targetWorldPos)
	{
		return TryGetDirectionalStep(fromWorld, type, steepestDescent: false, out targetWorldPos);
	}

	// fix/zombie-corner-stuck (3): follows the gradient for up to maxCells steps (or until it
	// reaches an intersection cell), returning the furthest point reached instead of just the
	// immediate neighbor. One SetDestination call then covers several cells of smooth NavMeshAgent
	// travel instead of a decision firing every single cell — which, combined with the
	// retargetCooldown, was producing a stop-and-go walk.
	public bool TryGetLookaheadDescent(Vector3 fromWorld, CrowdFieldType type, int maxCells, out Vector3 targetWorldPos)
	{
		return TryGetLookahead(fromWorld, type, maxCells, steepestDescent: true, out targetWorldPos);
	}

	public bool TryGetLookaheadAscent(Vector3 fromWorld, CrowdFieldType type, int maxCells, out Vector3 targetWorldPos)
	{
		return TryGetLookahead(fromWorld, type, maxCells, steepestDescent: false, out targetWorldPos);
	}

	private bool TryGetLookahead(Vector3 fromWorld, CrowdFieldType type, int maxCells, bool steepestDescent, out Vector3 targetWorldPos)
	{
		targetWorldPos = fromWorld;
		if (!GridReady) return false;

		int current = ResolveWalkableCell(fromWorld);
		if (current < 0) return false;

		float[] field = SelectField(type);
		int reached = current;

		for (int steps = 0; steps < Mathf.Max(1, maxCells); steps++)
		{
			int x = reached % gridW, y = reached / gridW;
			int best = reached;
			float bestVal = field[reached];

			CheckNeighbor(x + 1, y, field, steepestDescent, ref best, ref bestVal);
			CheckNeighbor(x - 1, y, field, steepestDescent, ref best, ref bestVal);
			CheckNeighbor(x, y + 1, field, steepestDescent, ref best, ref bestVal);
			CheckNeighbor(x, y - 1, field, steepestDescent, ref best, ref bestVal);

			if (best == reached) break; // local optimum — can't go any further this direction

			reached = best;

			if (isIntersectionCell[reached]) break;
		}

		if (reached == current) return false;
		targetWorldPos = cellWorldPos[reached];
		return true;
	}

	public bool TryGetRandomIntersection(out Vector3 worldPos)
	{
		worldPos = Vector3.zero;
		if (!GridReady || intersectionCells.Count == 0) return false;
		int pick = intersectionCells[Random.Range(0, intersectionCells.Count)];
		worldPos = cellWorldPos[pick];
		return true;
	}

	// feature/crowd-tuning-pass (Part B.5): same as TryGetRandomIntersection, but only considers
	// intersections whose dangerField reading is at least minFieldValue (field STEPS, e.g. F_zed
	// for "far enough from zombies") — uniform random pick via reservoir sampling, so this stays
	// allocation-free regardless of how many intersections qualify.
	public bool TryGetRandomSafeIntersection(CrowdFieldType dangerField, float minFieldValue, out Vector3 worldPos)
	{
		worldPos = Vector3.zero;
		if (!GridReady) return false;

		float[] field = SelectField(dangerField);
		int chosen = -1;
		int seen = 0;

		for (int i = 0; i < intersectionCells.Count; i++)
		{
			int cell = intersectionCells[i];
			float v = field[cell];
			if (float.IsPositiveInfinity(v) || v < minFieldValue) continue;

			seen++;
			if (Random.Range(0, seen) == 0) chosen = cell;
		}

		if (chosen < 0) return false;
		worldPos = cellWorldPos[chosen];
		return true;
	}

	// True if a and b sit on the same grid row/column with every intervening cell walkable — the
	// "same straight street" check a Wanderer uses to notice a civilian and become a Hunter.
	public bool IsSameStreetVisible(Vector3 a, Vector3 b)
	{
		if (!GridReady) return false;
		int ia = WorldToCellIndex(a);
		int ib = WorldToCellIndex(b);
		if (ia < 0 || ib < 0) return false;

		int ax = ia % gridW, ay = ia / gridW;
		int bx = ib % gridW, by = ib / gridW;

		if (ax == bx)
		{
			int lo = Mathf.Min(ay, by), hi = Mathf.Max(ay, by);
			for (int y = lo; y <= hi; y++) if (!walkable[y * gridW + ax]) return false;
			return true;
		}

		if (ay == by)
		{
			int lo = Mathf.Min(ax, bx), hi = Mathf.Max(ax, bx);
			for (int x = lo; x <= hi; x++) if (!walkable[ay * gridW + x]) return false;
			return true;
		}

		return false;
	}

	private bool TryGetDirectionalStep(Vector3 fromWorld, CrowdFieldType type, bool steepestDescent, out Vector3 targetWorldPos)
	{
		targetWorldPos = fromWorld;
		if (!GridReady) return false;

		int idx = ResolveWalkableCell(fromWorld);
		if (idx < 0) return false;

		float[] field = SelectField(type);
		int x = idx % gridW, y = idx / gridW;

		int best = idx;
		float bestVal = field[idx];

		CheckNeighbor(x + 1, y, field, steepestDescent, ref best, ref bestVal);
		CheckNeighbor(x - 1, y, field, steepestDescent, ref best, ref bestVal);
		CheckNeighbor(x, y + 1, field, steepestDescent, ref best, ref bestVal);
		CheckNeighbor(x, y - 1, field, steepestDescent, ref best, ref bestVal);

		if (best == idx) return false;
		targetWorldPos = cellWorldPos[best];
		return true;
	}

	private void CheckNeighbor(int nx, int ny, float[] field, bool steepestDescent, ref int best, ref float bestVal)
	{
		if (nx < 0 || nx >= gridW || ny < 0 || ny >= gridH) return;
		int nIdx = ny * gridW + nx;
		if (!walkable[nIdx]) return;

		float v = field[nIdx];
		if (float.IsPositiveInfinity(v)) return;

		bool better = steepestDescent ? v < bestVal : v > bestVal;
		if (better) { bestVal = v; best = nIdx; }
	}

	private float[] SelectField(CrowdFieldType type)
	{
		switch (type)
		{
			case CrowdFieldType.Civilian: return fieldCiv;
			case CrowdFieldType.Zombie: return fieldZed;
			default: return fieldEvac;
		}
	}

	private int WorldToCellIndex(Vector3 pos)
	{
		if (!GridReady) return -1;
		int x = Mathf.RoundToInt((pos.x - originWorld.x) / cellSize);
		int y = Mathf.RoundToInt((pos.z - originWorld.z) / cellSize);
		if (x < 0 || x >= gridW || y < 0 || y >= gridH) return -1;
		return y * gridW + x;
	}

	// fix/zombie-corner-stuck (1): the cell nearest a world position (via rounding) is commonly
	// non-walkable right at a building corner — e.g. a civilian/zombie standing just off the
	// corner of a road tile. Previously that made every directional query fail outright, and
	// Zombie.Brain() treated "no step" as "nothing to do" and called agent.ResetPath() every tick,
	// a permanent stall. This walks outward (8 neighbors, then a radius-2 ring) and returns the
	// walkable cell nearest the actual position instead of giving up immediately.
	private int ResolveWalkableCell(Vector3 worldPos)
	{
		int idx = WorldToCellIndex(worldPos);
		if (idx < 0) return -1;
		if (walkable[idx]) return idx;

		int cx = idx % gridW;
		int cy = idx / gridW;

		for (int radius = 1; radius <= 2; radius++)
		{
			int best = -1;
			float bestSqr = float.MaxValue;

			for (int dy = -radius; dy <= radius; dy++)
			{
				for (int dx = -radius; dx <= radius; dx++)
				{
					if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != radius) continue; // this radius's ring only

					int nx = cx + dx, ny = cy + dy;
					if (nx < 0 || nx >= gridW || ny < 0 || ny >= gridH) continue;

					int nIdx = ny * gridW + nx;
					if (!walkable[nIdx]) continue;

					float sqr = (cellWorldPos[nIdx] - worldPos).sqrMagnitude;
					if (sqr < bestSqr) { bestSqr = sqr; best = nIdx; }
				}
			}

			if (best >= 0) return best;
		}

		return -1;
	}

	// ── Binary min-heap (Dijkstra) — parallel arrays, decrease-key via heapIndexOf ──────────────

	private void HeapPush(int node, float key)
	{
		int i = heapSize++;
		heapNode[i] = node;
		heapKey[i] = key;
		heapIndexOf[node] = i;

		while (i > 0)
		{
			int parent = (i - 1) / 2;
			if (heapKey[parent] <= heapKey[i]) break;
			HeapSwap(parent, i);
			i = parent;
		}
	}

	private void HeapDecreaseOrPush(int node, float key)
	{
		int i = heapIndexOf[node];
		if (i < 0) { HeapPush(node, key); return; }
		if (key >= heapKey[i]) return;

		heapKey[i] = key;
		while (i > 0)
		{
			int parent = (i - 1) / 2;
			if (heapKey[parent] <= heapKey[i]) break;
			HeapSwap(parent, i);
			i = parent;
		}
	}

	private int HeapPopMin()
	{
		int rootNode = heapNode[0];
		heapIndexOf[rootNode] = -1;
		heapSize--;

		if (heapSize > 0)
		{
			heapNode[0] = heapNode[heapSize];
			heapKey[0] = heapKey[heapSize];
			heapIndexOf[heapNode[0]] = 0;

			int i = 0;
			while (true)
			{
				int l = i * 2 + 1, r = i * 2 + 2, smallest = i;
				if (l < heapSize && heapKey[l] < heapKey[smallest]) smallest = l;
				if (r < heapSize && heapKey[r] < heapKey[smallest]) smallest = r;
				if (smallest == i) break;
				HeapSwap(i, smallest);
				i = smallest;
			}
		}

		return rootNode;
	}

	private void HeapSwap(int a, int b)
	{
		int na = heapNode[a], nb = heapNode[b];
		float ka = heapKey[a], kb = heapKey[b];

		heapNode[a] = nb; heapKey[a] = kb; heapIndexOf[nb] = a;
		heapNode[b] = na; heapKey[b] = ka; heapIndexOf[na] = b;
	}

#if UNITY_EDITOR
	// fix/zombie-corner-stuck (5): visual sanity check for the grid/field bugs above — walkable
	// cells and each cell's F_civ descent direction, so a bad corner (non-walkable gap, wrong
	// arrow) is visible in the Scene view instead of only inferred from zombie behavior.
	// feature/crowd-tuning-pass: the toggle/colors moved to CrowdTuning (Debug section) — read
	// from cachedSettings like every other tunable, null-guarded since no zombie/civilian may
	// have spawned yet to populate it.
	private void OnDrawGizmos()
	{
		if (cachedSettings == null || !cachedSettings.drawGridGizmos) return;
		if (!GridReady || walkable == null || fieldCiv == null) return;

		for (int i = 0; i < walkable.Length; i++)
		{
			if (!walkable[i]) continue;

			Gizmos.color = cachedSettings.gizmoWalkableColor;
			Gizmos.DrawCube(cellWorldPos[i] + Vector3.up * 0.02f, new Vector3(cellSize * 0.85f, 0.02f, cellSize * 0.85f));

			float val = fieldCiv[i];
			if (float.IsPositiveInfinity(val)) continue;

			int x = i % gridW, y = i / gridW;
			int best = i;
			float bestVal = val;
			CheckNeighbor(x + 1, y, fieldCiv, true, ref best, ref bestVal);
			CheckNeighbor(x - 1, y, fieldCiv, true, ref best, ref bestVal);
			CheckNeighbor(x, y + 1, fieldCiv, true, ref best, ref bestVal);
			CheckNeighbor(x, y - 1, fieldCiv, true, ref best, ref bestVal);
			if (best == i) continue;

			Gizmos.color = cachedSettings.gizmoDescentArrowColor;
			Vector3 from = cellWorldPos[i] + Vector3.up * 0.05f;
			Vector3 to = Vector3.Lerp(cellWorldPos[i], cellWorldPos[best], 0.6f) + Vector3.up * 0.05f;
			Gizmos.DrawLine(from, to);
			Gizmos.DrawSphere(to, cellSize * 0.08f);
		}

		if (cachedSettings.showZombieSiegeOverlay) DrawZombieSiegeGizmos();
	}

	// fix/zombie-siege-mode (5): per-zombie line to its current NavMeshAgent destination
	// (green = PathComplete, red = anything else -- most commonly Partial, which is exactly the
	// "walked to a sidewalk instead of the barricade" bug this branch fixes) plus a Hunt/Wander/
	// Siege label, so the fix (and any regression of it) is visible directly in the Scene view.
	private void DrawZombieSiegeGizmos()
	{
		foreach (Zombie z in Zombie.AllZombies)
		{
			if (z == null) continue;

			NavMeshAgent a = z.Agent;
			if (a == null || !a.enabled || !a.isOnNavMesh) continue;

			if (a.hasPath)
			{
				Gizmos.color = a.pathStatus == NavMeshPathStatus.PathComplete ? Color.green : Color.red;
				Gizmos.DrawLine(z.transform.position + Vector3.up * 0.3f, a.destination + Vector3.up * 0.3f);
			}

			UnityEditor.Handles.color = ZombieModeColor(z.CurrentMode);
			UnityEditor.Handles.Label(z.transform.position + Vector3.up * 1.4f, z.CurrentMode.ToString());
		}
	}

	private static Color ZombieModeColor(Zombie.ZombieMode mode)
	{
		switch (mode)
		{
			case Zombie.ZombieMode.Hunt: return Color.yellow;
			case Zombie.ZombieMode.Siege: return new Color(1f, 0.4f, 0f);
			default: return Color.cyan;
		}
	}
#endif

	// fix/zombie-siege-mode (5): on-screen counter, works in the Editor AND development builds.
	// DEVELOPMENT_BUILD is a deprecated preprocessor directive in this Unity version (UAC0009) —
	// Debug.isDebugBuild is the supported runtime equivalent (true in the Editor and in
	// development player builds, false in release builds), so the gating happens inside OnGUI
	// instead of via #if. Shares the showZombieSiegeOverlay toggle (CrowdTuning, Debug section)
	// with the Editor-only gizmos above.
	private void OnGUI()
	{
		if (cachedSettings == null || !cachedSettings.showZombieSiegeOverlay || !Debug.isDebugBuild) return;

		int stalled = 0;
		foreach (Zombie z in Zombie.AllZombies)
			if (z != null && z.IsStalled) stalled++;

		GUI.Label(new Rect(10, 10, 300, 24), $"stalled zombies: {stalled}");
	}
}
