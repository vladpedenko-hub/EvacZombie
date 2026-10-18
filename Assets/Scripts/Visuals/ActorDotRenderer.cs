using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// feature/readability-palette: a GPU-instanced minimum-size "dot" marker drawn on top of every
// active Zombie/Human/Scientist, so actors never shrink below a readable size when the camera
// zooms out over a generated city (Docs/CAMERA_READABILITY.md's existing scale pass handles the
// real models; this is the readability-palette brief's own floor on top of that). Generated
// cities only -- LevelPaletteService.Active is null on hand-built levels, so LateUpdate below
// draws nothing there.
//
// No per-actor GameObject: iterates Zombie.AllZombies/Human.AllHumans/Scientist.AllScientists
// fresh every frame (the exact same rosters pooling already maintains via OnEnable/OnDisable) and
// batches them through Graphics.DrawMeshInstanced in chunks of up to 1023 -- works with pooling
// for free, no extra bookkeeping needed when an actor is spawned/recycled.
public class ActorDotRenderer : MonoBehaviour
{
	public static ActorDotRenderer Instance { get; private set; }

	private const int MaxInstancesPerDrawCall = 1023;

	private Mesh dotMesh;
	private Material dotMaterial;
	private Texture2D dotTexture;
	private Quaternion dotRotation;

	private readonly Matrix4x4[] matrixBuffer = new Matrix4x4[MaxInstancesPerDrawCall];
	private readonly Vector4[] colorBuffer = new Vector4[MaxInstancesPerDrawCall];
	private MaterialPropertyBlock propertyBlock;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Bootstrap()
	{
		SceneManager.sceneLoaded -= OnSceneLoaded;
		SceneManager.sceneLoaded += OnSceneLoaded;
	}

	private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		if (scene.name != "Gameplay") return;

		GameObject host = new GameObject("[ActorDotRenderer]");
		SceneManager.MoveGameObjectToScene(host, scene); // dies with the Gameplay scene
		host.AddComponent<ActorDotRenderer>();
	}

	private void Awake()
	{
		Instance = this;

		GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Quad);
		dotMesh = temp.GetComponent<MeshFilter>().sharedMesh;
		Destroy(temp);

		dotTexture = BuildDotTexture();

		// URP's Unlit shader picks its blend-mode shader path from the _SURFACE_TYPE_TRANSPARENT
		// keyword, not just the _Surface float -- the editor's ShaderGUI normally flips both
		// together when you touch the Inspector's Surface Type dropdown; done by hand here since
		// nothing invokes that ShaderGUI at runtime.
		dotMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
		dotMaterial.mainTexture = dotTexture;
		dotMaterial.enableInstancing = true;
		dotMaterial.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
		dotMaterial.SetFloat("_Surface", 1f); // Transparent
		dotMaterial.SetFloat("_Blend", 0f); // Alpha
		dotMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
		dotMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
		dotMaterial.SetFloat("_ZWrite", 0f);
		dotMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
		dotMaterial.DisableKeyword("_ALPHATEST_ON");
		dotMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

		dotRotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
		propertyBlock = new MaterialPropertyBlock();
	}

	private void OnDestroy()
	{
		if (dotMaterial != null) Destroy(dotMaterial);
		if (dotTexture != null) Destroy(dotTexture);
	}

	private void LateUpdate()
	{
		LevelPalette palette = LevelPaletteService.Active;
		if (palette == null) return;

		Camera cam = CameraController.Instance != null ? CameraController.Instance.Camera : null;
		if (cam == null) return;

		float dotDiameter = ComputeDotWorldDiameter(cam, palette);

		DrawRole(Zombie.AllZombies, palette.zombieColor, dotDiameter);
		DrawRole(Human.AllHumans, palette.civilianColor, dotDiameter);
		DrawRole(Scientist.AllScientists, palette.scientistColor, dotDiameter);
	}

	// Dot diameter = max(the actor's real on-screen size, minActorPixelSize) -- only ever enlarges
	// a too-small actor, never shrinks one that's already big enough (zoomed-in close-ups keep
	// their real apparent size). All actors sit at roughly the same height above a (straight-down)
	// camera, so this is computed once per frame rather than per actor.
	private float ComputeDotWorldDiameter(Camera cam, LevelPalette palette)
	{
		float distance = Mathf.Max(0.01f, Mathf.Abs(cam.transform.position.y));
		float worldUnitsPerScreenPixel = cam.orthographic
			? (2f * cam.orthographicSize) / Mathf.Max(1, Screen.height)
			: (2f * distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad)) / Mathf.Max(1, Screen.height);

		float desiredScreenPixels = Mathf.Max(1f, palette.minActorPixelSize) * (Screen.width / 1080f);
		float desiredWorldDiameter = desiredScreenPixels * worldUnitsPerScreenPixel;

		CameraReadabilityConfig readability = CameraReadabilityConfig.Active;
		float actorWorldDiameter = readability != null
			? readability.humanReferenceHeightWorldUnits * Mathf.Max(0.01f, readability.actorVisualScaleMultiplier)
			: 0.3f;

		return Mathf.Max(actorWorldDiameter, desiredWorldDiameter);
	}

	private void DrawRole<T>(List<T> actors, Color color, float diameter) where T : Component
	{
		if (actors == null || actors.Count == 0) return;

		Vector4 colorVec = color;
		int count = 0;

		for (int i = 0; i < actors.Count; i++)
		{
			T actor = actors[i];
			if (actor == null) continue;

			Vector3 pos = actor.transform.position;
			pos.y += 0.03f; // slightly above the ground/street surface

			matrixBuffer[count] = Matrix4x4.TRS(pos, dotRotation, new Vector3(diameter, diameter, diameter));
			colorBuffer[count] = colorVec;
			count++;

			if (count == MaxInstancesPerDrawCall)
			{
				Flush(count);
				count = 0;
			}
		}

		if (count > 0) Flush(count);
	}

	private void Flush(int count)
	{
		propertyBlock.SetVectorArray("_BaseColor", colorBuffer);
		Graphics.DrawMeshInstanced(dotMesh, 0, dotMaterial, matrixBuffer, count, propertyBlock);
	}

	// A filled disc with a darker outer ring -- per-instance color tints both, so the ring reads
	// as a darker shade of the same role color (a cheap "outlined dot" with no extra draw call).
	private static Texture2D BuildDotTexture()
	{
		const int size = 32;
		Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
		tex.wrapMode = TextureWrapMode.Clamp;
		tex.filterMode = FilterMode.Bilinear;

		Color[] pixels = new Color[size * size];
		float center = (size - 1) * 0.5f;
		float radius = size * 0.5f;

		for (int y = 0; y < size; y++)
		{
			for (int x = 0; x < size; x++)
			{
				float dx = x - center;
				float dy = y - center;
				float dist = Mathf.Sqrt(dx * dx + dy * dy) / radius;

				Color c;
				if (dist > 1f) c = new Color(0f, 0f, 0f, 0f);
				else if (dist > 0.78f) c = new Color(0.22f, 0.22f, 0.22f, 1f);
				else c = Color.white;

				pixels[y * size + x] = c;
			}
		}

		tex.SetPixels(pixels);
		tex.Apply(false, true);
		return tex;
	}
}
