using DG.Tweening;
using UnityEngine;

// Semi-transparent zombie fog over a building's zone. Fades out when the building is restored.
public class MetaFogZone : MonoBehaviour
{
	private const float FadeDuration = 1.2f;
	private static readonly Color FogColor = new Color(0.05f, 0.08f, 0.05f, 0.55f);

	private Renderer fogRenderer;
	private bool fading;

	public static MetaFogZone Create(Transform parent, float radius)
	{
		GameObject go = GameObject.CreatePrimitive(PrimitiveType.Plane);
		go.name = "ZombieFog";
		go.transform.SetParent(parent, false);
		go.transform.localPosition = new Vector3(0f, 0.05f, 0f);
		// Plane is 10x10 units at scale 1.
		go.transform.localScale = new Vector3(radius * 2f / 10f, 1f, radius * 2f / 10f);
		Destroy(go.GetComponent<Collider>());

		var zone = go.AddComponent<MetaFogZone>();
		zone.fogRenderer = go.GetComponent<Renderer>();
		zone.fogRenderer.material = CreateFogMaterial();
		return zone;
	}

	public void FadeOut()
	{
		if (fading || fogRenderer == null) return;
		fading = true;

		Color clear = FogColor;
		clear.a = 0f;
		fogRenderer.material.DOColor(clear, "_BaseColor", FadeDuration)
			.OnComplete(() => gameObject.SetActive(false));
	}

	// Used when a building is already restored at load time: no animation.
	public void HideImmediately()
	{
		fading = true;
		gameObject.SetActive(false);
	}

	private static Material CreateFogMaterial()
	{
		Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
		var mat = new Material(shader);

		// Transparent surface, no depth write, so the fog blends over the ground.
		mat.SetFloat("_Surface", 1f);
		mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
		mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
		mat.SetFloat("_ZWrite", 0f);
		mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
		mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
		mat.SetColor("_BaseColor", FogColor);
		return mat;
	}
}
