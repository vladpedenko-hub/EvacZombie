using UnityEngine;

// Shared, cached Material/Shader for runtime-drawn LineRenderers (lasers, tracers, radius rings,
// warning circles) that only ever use LineRenderer's own per-vertex startColor/endColor — never
// material.color/mainTexture directly. Each of these used to call
// `new Material(Shader.Find("Sprites/Default"))` per unit placed: one unique Material instance +
// one Shader.Find string lookup every time, for a visually identical result. One shared instance
// removes both costs.
//
// Do NOT point a renderer that mutates `.material.color` (not a LineRenderer's startColor/endColor)
// at SpritesDefaultMaterial — that would leak the color change to every other user of the shared
// instance. Use SpritesDefaultShader instead for those (still avoids the repeated Shader.Find lookup).
public static class SharedMaterials
{
	private static Shader spritesDefaultShader;
	private static Material spritesDefaultMaterial;

	public static Shader SpritesDefaultShader
	{
		get
		{
			if (spritesDefaultShader == null) spritesDefaultShader = Shader.Find("Sprites/Default");
			return spritesDefaultShader;
		}
	}

	public static Material SpritesDefaultMaterial
	{
		get
		{
			if (spritesDefaultMaterial == null) spritesDefaultMaterial = new Material(SpritesDefaultShader);
			return spritesDefaultMaterial;
		}
	}

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ResetStatics()
	{
		spritesDefaultShader = null;
		spritesDefaultMaterial = null;
	}
}
