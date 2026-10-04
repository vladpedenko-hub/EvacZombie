using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Small helpers for building uGUI from code (used by the Settings popup + gear button),
/// so the settings feature needs no scene/prefab edits. Replace with a designed prefab whenever art is ready.
/// </summary>
public static class RuntimeUiKit
{
	private static Sprite roundedSprite;
	private static Sprite gearSprite;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ResetStatics() { roundedSprite = null; gearSprite = null; }

	/// <summary>9-sliced rounded rectangle (white, tint it with Image.color).</summary>
	public static Sprite Rounded()
	{
		if (roundedSprite != null) return roundedSprite;

		const int size = 64;
		const int r = 20;
		var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
		tex.wrapMode = TextureWrapMode.Clamp;
		tex.filterMode = FilterMode.Bilinear;

		for (int y = 0; y < size; y++)
		{
			for (int x = 0; x < size; x++)
			{
				float px = x + 0.5f, py = y + 0.5f;
				float dx = Mathf.Max(Mathf.Max(r - px, px - (size - r)), 0f);
				float dy = Mathf.Max(Mathf.Max(r - py, py - (size - r)), 0f);
				float dist = Mathf.Sqrt(dx * dx + dy * dy);
				float a = Mathf.Clamp01(r - dist + 0.5f);
				tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
			}
		}
		tex.Apply();

		roundedSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
			SpriteMeshType.FullRect, new Vector4(r, r, r, r));
		return roundedSprite;
	}

	/// <summary>White gear icon (tint with Image.color).</summary>
	public static Sprite Gear()
	{
		if (gearSprite != null) return gearSprite;

		const int size = 96;
		float c = (size - 1) * 0.5f;
		var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
		tex.wrapMode = TextureWrapMode.Clamp;
		tex.filterMode = FilterMode.Bilinear;

		for (int y = 0; y < size; y++)
		{
			for (int x = 0; x < size; x++)
			{
				float dx = x - c, dy = y - c;
				float d = Mathf.Sqrt(dx * dx + dy * dy);
				float ang = Mathf.Atan2(dy, dx);

				float outer = 33f + 8f * Mathf.Clamp01((Mathf.Cos(8f * ang) + 0.2f) * 3f); // 8 teeth
				float aOuter = Mathf.Clamp01(outer - d + 0.5f);
				float aInner = Mathf.Clamp01(d - 14f + 0.5f); // centre hole
				tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Min(aOuter, aInner)));
			}
		}
		tex.Apply();

		gearSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
		return gearSprite;
	}

	public static RectTransform CreateRect(string name, Transform parent)
	{
		var go = new GameObject(name, typeof(RectTransform));
		go.transform.SetParent(parent, false);
		return (RectTransform)go.transform;
	}

	public static void Stretch(RectTransform rt)
	{
		rt.anchorMin = Vector2.zero;
		rt.anchorMax = Vector2.one;
		rt.offsetMin = Vector2.zero;
		rt.offsetMax = Vector2.zero;
	}

	public static void SetAnchors(RectTransform rt, float minX, float minY, float maxX, float maxY)
	{
		rt.anchorMin = new Vector2(minX, minY);
		rt.anchorMax = new Vector2(maxX, maxY);
		rt.offsetMin = Vector2.zero;
		rt.offsetMax = Vector2.zero;
	}

	public static Image CreateImage(string name, Transform parent, Color color, bool rounded = true)
	{
		RectTransform rt = CreateRect(name, parent);
		var img = rt.gameObject.AddComponent<Image>();
		img.color = color;
		if (rounded)
		{
			img.sprite = Rounded();
			img.type = Image.Type.Sliced;
		}
		return img;
	}

	public static TextMeshProUGUI CreateText(string name, Transform parent, string text, float size, Color color,
		TextAlignmentOptions align = TextAlignmentOptions.Center, FontStyles style = FontStyles.Normal)
	{
		RectTransform rt = CreateRect(name, parent);
		var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
		t.text = text;
		t.fontSize = size;
		t.color = color;
		t.alignment = align;
		t.fontStyle = style;
		t.raycastTarget = false;
		return t;
	}

	/// <summary>Rounded button with a centered label. Returns the Button.</summary>
	public static Button CreateButton(string name, Transform parent, string label, float fontSize, Color bg, Color textColor)
	{
		Image img = CreateImage(name, parent, bg);
		var btn = img.gameObject.AddComponent<Button>();
		btn.targetGraphic = img;

		TextMeshProUGUI t = CreateText("Label", img.transform, label, fontSize, textColor, TextAlignmentOptions.Center, FontStyles.Bold);
		Stretch(t.rectTransform);
		return btn;
	}

	public static Canvas CreateOverlayCanvas(string name, int sortingOrder, Transform parent = null)
	{
		var go = new GameObject(name, typeof(RectTransform));
		if (parent != null) go.transform.SetParent(parent, false);

		var canvas = go.AddComponent<Canvas>();
		canvas.renderMode = RenderMode.ScreenSpaceOverlay;
		canvas.sortingOrder = sortingOrder;

		var scaler = go.AddComponent<CanvasScaler>();
		scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
		scaler.referenceResolution = new Vector2(1080f, 1920f);
		scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
		scaler.matchWidthOrHeight = 0.5f;

		go.AddComponent<GraphicRaycaster>();
		return canvas;
	}
}
