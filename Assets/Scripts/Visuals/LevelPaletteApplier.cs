using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// feature/readability-palette: applies a flat, palette-driven visual look to GENERATED cities only
// -- a CityGenerator-built Level_Sandbox_Environment. Detected by the "Buildings"/"Frame"/"Ground"
// child transforms CityGenerator.Generate() always creates on its root (see CityGenerator.cs),
// NOT just by StreetSegment presence -- a hand-built level's RoadZone also carries a StreetSegment
// (RoadZone.cs), so that alone isn't a safe signal. Hand-built levels (Level_1..10) never match
// this signature, so LevelPaletteService.Active stays null for them and nothing here ever runs.
//
// Runtime-only, no scene/prefab edits: LevelManager.LoadLevel calls OnLevelLoaded() right after
// instantiating the level prefab (same spot as SupplyManager/SupplyHud/PlanningControllerV2's own
// OnLevelLoaded hooks). SandboxDevPanel's palette-cycle button calls Apply() directly to re-paint
// the already-loaded city live, without reloading the level.
public static class LevelPaletteApplier
{
	private static Transform environmentRoot;
	private static readonly List<Material> roofMaterials = new List<Material>();
	private static readonly List<GameObject> dashLineQuads = new List<GameObject>();
	private static Material dashLineMaterial;

	// Support "Enter Play Mode without domain reload" -- don't leak stale references (destroyed
	// Materials/GameObjects from a previous play session) the way CameraReadabilityConfig and
	// LevelPaletteService already guard against.
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ResetStatics()
	{
		environmentRoot = null;
		roofMaterials.Clear();
		dashLineQuads.Clear();
		dashLineMaterial = null;
	}

	public static void OnLevelLoaded(GameObject levelEnvironment)
	{
		ClearDashLines();
		roofMaterials.Clear();
		environmentRoot = null;

		if (levelEnvironment == null)
		{
			LevelPaletteService.SetActive(null);
			return;
		}

		Transform root = levelEnvironment.transform;
		bool isGeneratedCity = root.Find("Buildings") != null && root.Find("Frame") != null && root.Find("Ground") != null;

		if (!isGeneratedCity)
		{
			LevelPaletteService.SetActive(null);
			return;
		}

		environmentRoot = root;
		Apply(LevelPaletteService.ResolveStartupPalette());
	}

	// Re-applies palette to the already-loaded generated city (environment + every currently
	// active actor) without reloading the level -- used at level load and by the sandbox
	// palette-cycle button alike.
	public static void Apply(LevelPalette palette)
	{
		LevelPaletteService.SetActive(palette);
		if (palette == null || environmentRoot == null) return;

		ApplyCamera(palette);
		ApplyEnvironment(palette);
		ApplyActors(palette);
	}

	private static void ApplyCamera(LevelPalette palette)
	{
		Camera cam = CameraController.Instance != null ? CameraController.Instance.Camera : null;
		if (cam == null) return;

		cam.clearFlags = CameraClearFlags.SolidColor;
		cam.backgroundColor = palette.backgroundColor;
	}

	private static void ApplyEnvironment(LevelPalette palette)
	{
		Transform ground = environmentRoot.Find("Ground");
		Transform parks = environmentRoot.Find("Parks");
		Transform frame = environmentRoot.Find("Frame");
		Transform buildings = environmentRoot.Find("Buildings");

		if (ground != null)
		{
			foreach (Transform child in ground)
			{
				Renderer r = child.GetComponent<Renderer>();
				if (r == null || r.sharedMaterial == null) continue;

				bool isStreet = child.GetComponent<StreetSegment>() != null;
				TintFlatMaterial(r.sharedMaterial, isStreet ? palette.streetColor : palette.groundColor);
			}
		}

		if (parks != null)
		{
			// Parks also holds decorative prop clones (CityGenConfig.parkPropPrefab, e.g. grass
			// tufts) as siblings of the "Park_<bx>_<by>" ground tiles -- only the ground tile
			// itself (CityGenerator.CreateGroundCube's exact naming) gets the flat park color;
			// the props keep their own look.
			foreach (Transform child in parks)
			{
				if (!child.name.StartsWith("Park_")) continue;
				Renderer r = child.GetComponent<Renderer>();
				if (r != null && r.sharedMaterial != null) TintFlatMaterial(r.sharedMaterial, palette.parkColor);
			}
		}

		if (frame != null)
		{
			foreach (Transform child in frame)
			{
				Renderer r = child.GetComponent<Renderer>();
				if (r != null && r.sharedMaterial != null) TintFlatMaterial(r.sharedMaterial, palette.frameColor);
			}
		}

		if (buildings != null)
		{
			BuildRoofMaterials(palette);
			foreach (Transform building in buildings)
			{
				Material roof = PickRoofMaterial(building.position);

				// Every renderer under the building -- not just a single "main" one -- so this
				// also removes the SimpleTown.mat shared window/trim submesh (saturated cyan
				// _Color tint, Universal Render Pipeline/Simple Lit) that most SimpleTown building
				// prefabs carry. That shared, highly saturated submaterial combined with Simple
				// Lit's specular response is the "colored outline/rim" look the brief asked to
				// remove -- no renderer feature or outline component exists in this project's URP
				// renderer assets (Mobile_Renderer/PC_Renderer both have an empty
				// m_RendererFeatures list), so the look is purely material-sourced.
				Renderer[] renderers = building.GetComponentsInChildren<Renderer>();
				for (int i = 0; i < renderers.Length; i++)
				{
					Renderer r = renderers[i];
					if (r == null) continue;
					r.sharedMaterial = roof;
					r.shadowCastingMode = ShadowCastingMode.Off;
					r.receiveShadows = false;
				}
			}
		}

		BuildDashLines(palette, ground);
	}

	// URP/Lit flat materials (no textures) -- Material.color is special-cased by Unity to resolve
	// to whichever property the shader marks [MainColor] (here _BaseColor), same as
	// CityGenerator.CreateOrUpdateFlatMaterial already relies on. Smoothness forced to 0 so the
	// readability pass's "flat, matte" world doesn't pick up specular highlights.
	private static void TintFlatMaterial(Material mat, Color color)
	{
		mat.color = color;
		if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0f);
		else if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0f);
	}

	// Stable per-building hash from world position (not a per-run random roll) -- the same
	// building footprint always gets the same roof tone across a palette switch or a level
	// reload with the same generated city.
	private static void BuildRoofMaterials(LevelPalette palette)
	{
		foreach (Material m in roofMaterials) Object.Destroy(m);
		roofMaterials.Clear();

		Color[] colors = palette.roofColors;
		int count = (colors != null && colors.Length > 0) ? colors.Length : 1;
		Shader shader = Shader.Find("Universal Render Pipeline/Lit");

		for (int i = 0; i < count; i++)
		{
			Material mat = new Material(shader);
			Color roofColor = (colors != null && colors.Length > 0) ? colors[i] : Color.gray;
			TintFlatMaterial(mat, roofColor);
			roofMaterials.Add(mat);
		}
	}

	private static Material PickRoofMaterial(Vector3 position)
	{
		if (roofMaterials.Count == 0) return null;

		int ix = Mathf.RoundToInt(position.x * 100f);
		int iz = Mathf.RoundToInt(position.z * 100f);

		int hash;
		unchecked
		{
			hash = 17;
			hash = hash * 31 + ix;
			hash = hash * 31 + iz;
		}

		int index = ((hash % roofMaterials.Count) + roofMaterials.Count) % roofMaterials.Count;
		return roofMaterials[index];
	}

	// Thin flat quads along the centerline of each non-intersection StreetSegment under Ground.
	// Rebuilt from scratch on every Apply() (level load, or a sandbox palette cycle) -- the
	// generated city is small enough (a couple dozen street pieces) that this is cheap, and it's
	// far simpler than tracking per-dash recolor state separately.
	private static void BuildDashLines(LevelPalette palette, Transform ground)
	{
		ClearDashLines();
		if (ground == null) return;

		if (dashLineMaterial == null)
		{
			dashLineMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
			dashLineMaterial.SetInt("_Cull", (int)CullMode.Off);
		}
		dashLineMaterial.color = palette.streetLineColor;

		const float dashLength = 1f;
		const float gapLength = 0.6f;
		const float step = dashLength + gapLength;

		foreach (Transform child in ground)
		{
			StreetSegment segment = child.GetComponent<StreetSegment>();
			if (segment == null || segment.IsIntersection) continue;

			Vector3 dir = segment.Direction.sqrMagnitude > 0.0001f ? segment.Direction.normalized : Vector3.forward;
			bool axisIsZ = Mathf.Abs(dir.z) >= Mathf.Abs(dir.x);
			float length = axisIsZ ? child.localScale.z : child.localScale.x;
			float thickness = Mathf.Max(0.02f, segment.Width * 0.06f);
			float topY = child.position.y + child.localScale.y * 0.5f + 0.005f;

			int dashCount = Mathf.Max(1, Mathf.FloorToInt(length / step));

			for (int i = 0; i < dashCount; i++)
			{
				float t = (i - (dashCount - 1) * 0.5f) * step;
				Vector3 center = child.position + dir * t;
				center.y = topY;

				GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
				quad.name = "DashLine";
				Object.Destroy(quad.GetComponent<Collider>());
				quad.transform.SetParent(child, true);
				quad.transform.position = center;
				quad.transform.rotation = Quaternion.LookRotation(Vector3.up, dir);
				quad.transform.localScale = new Vector3(thickness, dashLength, 1f);

				Renderer r = quad.GetComponent<Renderer>();
				r.sharedMaterial = dashLineMaterial;
				r.shadowCastingMode = ShadowCastingMode.Off;
				r.receiveShadows = false;

				dashLineQuads.Add(quad);
			}
		}
	}

	private static void ClearDashLines()
	{
		for (int i = 0; i < dashLineQuads.Count; i++)
			if (dashLineQuads[i] != null) Object.Destroy(dashLineQuads[i]);

		dashLineQuads.Clear();
	}

	private static void ApplyActors(LevelPalette palette)
	{
		foreach (Zombie z in Zombie.AllZombies) if (z != null) z.ApplyPaletteTint(palette.zombieColor);
		foreach (Human h in Human.AllHumans) if (h != null) h.ApplyPaletteTint(palette.civilianColor);
		foreach (Scientist s in Scientist.AllScientists) if (s != null) s.ApplyPaletteTint(palette.scientistColor);
		foreach (Barricade b in Barricade.AllBarricades) if (b != null) b.ApplyPaletteTint(palette.barricadeColor);
	}
}
