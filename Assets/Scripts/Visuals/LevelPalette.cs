using UnityEngine;

// feature/readability-palette: a flat, palette-driven look for generated cities only
// (Docs/mechanics -- see LevelPaletteApplier for how/when this gets applied). The world
// (streets/buildings/ground/frame/park) stays neutral and value-separated; saturated color is
// reserved for gameplay entities (zombie/civilian/scientist/barricade/spawn/evac markers), so the
// player's eye is drawn to what matters.
[CreateAssetMenu(fileName = "LevelPalette", menuName = "ZombieGame/Level Palette")]
public class LevelPalette : ScriptableObject
{
	[Header("Camera / World (neutral, value-separated)")]
	public Color backgroundColor = new Color(0.117647f, 0.129412f, 0.149020f);
	public Color groundColor = new Color(0.168627f, 0.180392f, 0.200000f);
	public Color frameColor = new Color(0.227451f, 0.227451f, 0.219608f);
	public Color streetColor = new Color(0.078431f, 0.086275f, 0.101961f);
	public Color streetLineColor = new Color(0.419608f, 0.435294f, 0.462745f);
	public Color parkColor = new Color(0.247059f, 0.309804f, 0.215686f);

	[Tooltip("5 flat, nearly-desaturated roof tones -- picked per building by a stable position hash, never per-run random.")]
	public Color[] roofColors = new Color[5]
	{
		new Color(0.545098f, 0.560784f, 0.580392f),
		new Color(0.603922f, 0.584314f, 0.533333f),
		new Color(0.494118f, 0.521569f, 0.549020f),
		new Color(0.639216f, 0.619608f, 0.572549f),
		new Color(0.529412f, 0.545098f, 0.501961f),
	};

	[Tooltip("Reserved for a future building-seam/grout line pass between adjacent BSP footprints -- not consumed by LevelPaletteApplier yet.")]
	public Color buildingSeamColor = new Color(0.419608f, 0.435294f, 0.462745f);

	[Header("Gameplay entities (saturated -- the only thing allowed to pop)")]
	public Color zombieColor = new Color(1.000000f, 0.184314f, 0.627451f);
	public Color civilianColor = new Color(0.949020f, 0.964706f, 0.972549f);
	public Color scientistColor = new Color(1.000000f, 0.823529f, 0.247059f);
	public Color barricadeColor = new Color(1.000000f, 0.541176f, 0.121569f);
	public Color spawnMarkerColor = new Color(0.878431f, 0.141176f, 0.231373f);
	public Color evacMarkerColor = new Color(0.239216f, 0.862745f, 1.000000f);

	[Header("Readability")]
	[Tooltip("Minimum on-screen diameter (in pixels, at a 1080px-wide reference resolution) an actor's dot marker is drawn at, so actors never disappear when zoomed out. Scaled to the real device by Screen.width/1080.")]
	public float minActorPixelSize = 9f;
}
