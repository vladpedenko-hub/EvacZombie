using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Small uGUI factory for the meta screens. Placeholder look: rounded built-in sprite, flat colors.
public static class MetaUI
{
	public static readonly Color Panel = new Color(0.08f, 0.09f, 0.11f, 0.94f);
	public static readonly Color ButtonPrimary = new Color(0.22f, 0.62f, 0.32f);
	public static readonly Color ButtonDisabled = new Color(0.3f, 0.3f, 0.32f);
	public static readonly Color ButtonSecondary = new Color(0.25f, 0.35f, 0.5f);
	public static readonly Color TextMain = Color.white;
	public static readonly Color TextDim = new Color(0.7f, 0.72f, 0.75f);

	// A UI Image needs a sprite to draw; a 4x4 white texture gives a plain colored quad.
	private static Sprite whiteSprite;
	private static Sprite WhiteSprite => whiteSprite != null
		? whiteSprite
		: whiteSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f));

	public static Canvas CreateOverlayCanvas(string name, int sortOrder)
	{
		var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
		var canvas = go.GetComponent<Canvas>();
		canvas.renderMode = RenderMode.ScreenSpaceOverlay;
		canvas.sortingOrder = sortOrder;

		var scaler = go.GetComponent<CanvasScaler>();
		scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
		scaler.referenceResolution = new Vector2(1080, 1920);
		scaler.matchWidthOrHeight = 0.5f;
		return canvas;
	}

	public static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
		Vector2 offsetMin, Vector2 offsetMax)
	{
		var go = new GameObject(name, typeof(RectTransform));
		go.transform.SetParent(parent, false);
		var rt = (RectTransform)go.transform;
		rt.anchorMin = anchorMin;
		rt.anchorMax = anchorMax;
		rt.offsetMin = offsetMin;
		rt.offsetMax = offsetMax;
		return rt;
	}

	public static Image ImageBox(string name, Transform parent, Color color, Vector2 anchorMin, Vector2 anchorMax,
		Vector2 offsetMin, Vector2 offsetMax)
	{
		RectTransform rt = Rect(name, parent, anchorMin, anchorMax, offsetMin, offsetMax);
		var img = rt.gameObject.AddComponent<Image>();
		img.color = color;
		img.sprite = WhiteSprite;
		img.type = Image.Type.Simple;
		return img;
	}

	public static TextMeshProUGUI Text(string name, Transform parent, string text, float fontSize,
		Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax,
		TextAlignmentOptions alignment = TextAlignmentOptions.Center, Color? color = null)
	{
		RectTransform rt = Rect(name, parent, anchorMin, anchorMax, offsetMin, offsetMax);
		var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
		tmp.text = text;
		tmp.fontSize = fontSize;
		tmp.alignment = alignment;
		tmp.color = color ?? TextMain;
		return tmp;
	}

	public static Button Button(string name, Transform parent, string label, Color color,
		Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, out TextMeshProUGUI labelText)
	{
		Image image = ImageBox(name, parent, color, anchorMin, anchorMax, offsetMin, offsetMax);
		var button = image.gameObject.AddComponent<Button>();
		button.targetGraphic = image;

		labelText = Text(name + "_Label", image.transform, label, 42,
			Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
		return button;
	}
}
