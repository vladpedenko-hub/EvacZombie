using System.Collections.Generic;
using UnityEngine;

public enum VfxId
{
	None = 0,
	HitSpark,
	DeathPuff,
	InfectBurst,
	CivilianLost,
	MuzzleFlash,
	PlacePop,
	BarricadeDust,
	BarricadeDebris,
	RescueSparkle
}

/// <summary>
/// Tiny pooled particle bursts built entirely in code (no prefabs / assets) — placeholders you can tune
/// right in the Spec table below, or replace by prefabs later (see Play()).
/// Auto-created before the first scene.
/// </summary>
public class VfxManager : MonoBehaviour
{
	public static VfxManager Instance { get; private set; }

	private enum Shape { Sphere, ConeUp, Ring }

	private struct Spec
	{
		public Color c0, c1;
		public int count;
		public float life, speed, size, gravity;
		public Shape shape;
		public float shapeRadius;

		public Spec(Color c0, Color c1, int count, float life, float speed, float size, float gravity, Shape shape, float shapeRadius)
		{
			this.c0 = c0; this.c1 = c1; this.count = count; this.life = life; this.speed = speed;
			this.size = size; this.gravity = gravity; this.shape = shape; this.shapeRadius = shapeRadius;
		}
	}

	private const int MaxActive = 60;

	// ------------------------------------------------------------------ TUNE HERE
	private static readonly Dictionary<VfxId, Spec> specs = new Dictionary<VfxId, Spec>
	{
		{ VfxId.HitSpark,        new Spec(new Color(1f, 0.30f, 0.20f), new Color(1f, 0.80f, 0.30f), 6,  0.30f, 3.0f, 0.25f,  0.5f, Shape.Sphere, 0.20f) },
		{ VfxId.DeathPuff,       new Spec(new Color(0.45f, 0.60f, 0.40f), new Color(0.30f, 0.40f, 0.30f), 10, 0.60f, 2.0f, 0.50f, -0.05f, Shape.ConeUp, 0.25f) },
		{ VfxId.InfectBurst,     new Spec(new Color(0.40f, 1f, 0.30f), new Color(0.70f, 1f, 0.40f), 14, 0.70f, 3.0f, 0.35f,  0.3f, Shape.Sphere, 0.25f) },
		{ VfxId.CivilianLost,    new Spec(new Color(1f, 0.20f, 0.20f), new Color(1f, 0.50f, 0.20f), 12, 0.60f, 3.0f, 0.35f,  0.4f, Shape.Sphere, 0.25f) },
		{ VfxId.MuzzleFlash,     new Spec(new Color(1f, 0.95f, 0.50f), new Color(1f, 0.70f, 0.20f), 4,  0.12f, 1.0f, 0.50f,  0.0f, Shape.Sphere, 0.05f) },
		{ VfxId.PlacePop,        new Spec(new Color(0.50f, 1f, 0.60f), new Color(1f, 1f, 1f),       20, 0.45f, 4.0f, 0.25f,  0.0f, Shape.Ring,   0.30f) },
		{ VfxId.BarricadeDust,   new Spec(new Color(0.75f, 0.70f, 0.60f), new Color(0.60f, 0.55f, 0.45f), 5, 0.40f, 1.5f, 0.40f, -0.1f, Shape.Sphere, 0.20f) },
		{ VfxId.BarricadeDebris, new Spec(new Color(0.50f, 0.35f, 0.20f), new Color(0.70f, 0.50f, 0.30f), 10, 0.60f, 4.0f, 0.22f, 1.5f, Shape.ConeUp, 0.30f) },
		{ VfxId.RescueSparkle,   new Spec(new Color(0.60f, 1f, 1f), new Color(1f, 1f, 1f),          12, 0.70f, 2.5f, 0.30f, -0.2f, Shape.ConeUp, 0.40f) },
	};
	// ------------------------------------------------------------------

	private struct ActiveFx { public VfxId id; public ParticleSystem ps; public float endTime; }

	private readonly Dictionary<VfxId, Stack<ParticleSystem>> pools = new Dictionary<VfxId, Stack<ParticleSystem>>();
	private readonly List<ActiveFx> active = new List<ActiveFx>();
	private Material material;
	private Texture2D softCircle;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ResetStatics() => Instance = null;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Bootstrap()
	{
		if (Instance != null) return;
		var go = new GameObject("[VfxManager]");
		DontDestroyOnLoad(go);
		Instance = go.AddComponent<VfxManager>();
	}

	private void Awake()
	{
		if (Instance != null && Instance != this) { Destroy(gameObject); return; }
		Instance = this;
	}

	private void OnDestroy()
	{
		if (Instance == this) Instance = null;
	}

	/// <summary>Plays a particle burst at a world position. Safe to call from anywhere.</summary>
	public static void Play(VfxId id, Vector3 position, float scale = 1f)
	{
		if (id == VfxId.None || Instance == null) return;
		Instance.PlayInternal(id, position, scale);
	}

	private void PlayInternal(VfxId id, Vector3 position, float scale)
	{
		if (active.Count >= MaxActive) return;
		if (!specs.TryGetValue(id, out Spec spec)) return;

		ParticleSystem ps = GetFromPool(id, spec);
		if (ps == null) return;

		Transform t = ps.transform;
		t.position = position;
		t.localScale = Vector3.one * Mathf.Max(0.1f, scale);

		ps.gameObject.SetActive(true);
		ps.Clear(true);
		ps.Play(true);

		active.Add(new ActiveFx { id = id, ps = ps, endTime = Time.time + spec.life + 0.1f });
	}

	private void Update()
	{
		for (int i = active.Count - 1; i >= 0; i--)
		{
			ActiveFx fx = active[i];

			if (fx.ps == null) { active.RemoveAt(i); continue; }
			if (Time.time < fx.endTime) continue;

			fx.ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
			fx.ps.gameObject.SetActive(false);
			pools[fx.id].Push(fx.ps);
			active.RemoveAt(i);
		}
	}

	private ParticleSystem GetFromPool(VfxId id, Spec spec)
	{
		if (!pools.TryGetValue(id, out Stack<ParticleSystem> stack))
		{
			stack = new Stack<ParticleSystem>();
			pools[id] = stack;
		}

		while (stack.Count > 0)
		{
			ParticleSystem pooled = stack.Pop();
			if (pooled != null) return pooled;
		}

		return Create(id, spec);
	}

	private ParticleSystem Create(VfxId id, Spec spec)
	{
		EnsureMaterial();

		// Build while inactive so nothing auto-plays and Unity doesn't warn about changing a playing system.
		var go = new GameObject("Vfx_" + id);
		go.SetActive(false);
		go.transform.SetParent(transform, false);

		var ps = go.AddComponent<ParticleSystem>();

		var main = ps.main;
		main.duration = spec.life;
		main.loop = false;
		main.playOnAwake = false;
		main.startLifetime = new ParticleSystem.MinMaxCurve(spec.life * 0.6f, spec.life);
		main.startSpeed = new ParticleSystem.MinMaxCurve(spec.speed * 0.5f, spec.speed);
		main.startSize = new ParticleSystem.MinMaxCurve(spec.size * 0.6f, spec.size);
		main.startColor = new ParticleSystem.MinMaxGradient(spec.c0, spec.c1);
		main.gravityModifier = spec.gravity;
		main.simulationSpace = ParticleSystemSimulationSpace.World;
		main.maxParticles = 48;

		var emission = ps.emission;
		emission.rateOverTime = 0f;
		emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)spec.count) });

		var shape = ps.shape;
		shape.enabled = true;
		switch (spec.shape)
		{
			case Shape.Sphere:
				shape.shapeType = ParticleSystemShapeType.Sphere;
				shape.radius = spec.shapeRadius;
				break;
			case Shape.ConeUp:
				shape.shapeType = ParticleSystemShapeType.Cone;
				shape.angle = 25f;
				shape.radius = spec.shapeRadius;
				shape.rotation = new Vector3(-90f, 0f, 0f); // cone axis +Z -> +Y (up)
				break;
			case Shape.Ring:
				shape.shapeType = ParticleSystemShapeType.Circle;
				shape.radius = spec.shapeRadius;
				shape.radiusThickness = 0f;               // emit from the rim only
				shape.rotation = new Vector3(-90f, 0f, 0f); // circle XY -> XZ (lies on the ground)
				break;
		}

		// Fade out and shrink over lifetime
		var col = ps.colorOverLifetime;
		col.enabled = true;
		var grad = new Gradient();
		grad.SetKeys(
			new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
			new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
		col.color = new ParticleSystem.MinMaxGradient(grad);

		var sol = ps.sizeOverLifetime;
		sol.enabled = true;
		sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));

		var psRenderer = go.GetComponent<ParticleSystemRenderer>();
		psRenderer.material = material;
		psRenderer.sortingOrder = 50;

		return ps;
	}

	private void EnsureMaterial()
	{
		if (material != null) return;

		if (softCircle == null)
		{
			const int size = 32;
			softCircle = new Texture2D(size, size, TextureFormat.RGBA32, false);
			softCircle.wrapMode = TextureWrapMode.Clamp;
			float c = (size - 1) * 0.5f;
			for (int y = 0; y < size; y++)
			{
				for (int x = 0; x < size; x++)
				{
					float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
					float a = Mathf.Clamp01(1f - d);
					softCircle.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
				}
			}
			softCircle.Apply();
		}

		// Same shader the project already relies on for runtime FX (Bomb.cs).
		material = new Material(Shader.Find("Sprites/Default"));
		material.mainTexture = softCircle;
	}
}
