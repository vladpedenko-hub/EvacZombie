using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// One house on the Base city map. Ruined and restored are uGUI primitives unless the BuildingDefinition
// supplies a UI prefab. Locked = dimmed with a LOCK tag. Available = pulsing "!" marker.
// The zombie fog under the house fades out when the house is restored.
public class BaseHouseView : MonoBehaviour
{
	private static readonly Color RuinedColor = new Color(0.2f, 0.18f, 0.16f);
	private static readonly Color DebrisColor = new Color(0.14f, 0.13f, 0.12f);
	private static readonly Color CleanColor = new Color(0.86f, 0.86f, 0.82f);
	private static readonly Color DoorColor = new Color(0.3f, 0.2f, 0.12f);
	private static readonly Color LockColor = new Color(0.12f, 0.12f, 0.14f, 0.9f);
	private static readonly Color MarkerColor = new Color(0.95f, 0.8f, 0.2f);
	private static readonly Color FogColor = new Color(0.05f, 0.08f, 0.05f, 0.4f);
	private static readonly Color ClearColor = new Color(0f, 0f, 0f, 0f);
	private const float LockedAlpha = 0.45f;
	private const float FadeDuration = 1.2f;

	public BuildingDefinition Building { get; private set; }
	public RectTransform Rect => (RectTransform)transform;

	private CanvasGroup body;
	private GameObject ruined;
	private GameObject restored;
	private GameObject lockTag;
	private RectTransform marker;
	private GameObject fog;
	private CanvasGroup fogGroup;
	private Tween pulse;
	private bool firstRefresh = true;
	private bool fogGone;

	public void Initialize(BuildingDefinition building, Vector2 anchoredPosition, Action<BuildingDefinition> onTap)
	{
		Building = building;

		RectTransform rect = Rect;
		rect.sizeDelta = building.cityLayoutSize;
		rect.anchoredPosition = anchoredPosition;

		// Fog is first so it sits on the ground under the house.
		fog = MetaUI.ImageBox("ZombieFog", rect, FogColor, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
			new Vector2(-building.fogRadius, -building.fogRadius), new Vector2(building.fogRadius, building.fogRadius)).gameObject;
		fog.GetComponent<Image>().raycastTarget = false;
		fogGroup = fog.AddComponent<CanvasGroup>();

		// The root is a transparent button so the whole house block is tappable.
		Image hit = MetaUI.ImageBox("Hit", rect, ClearColor, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
		Button button = hit.gameObject.AddComponent<Button>();
		button.targetGraphic = hit;
		button.onClick.AddListener(() => onTap?.Invoke(Building));

		RectTransform bodyRect = MetaUI.Rect("Body", rect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
		body = bodyRect.gameObject.AddComponent<CanvasGroup>();
		body.blocksRaycasts = false;

		ruined = building.ruinedVisual != null
			? Instantiate(building.ruinedVisual, bodyRect)
			: BuildRuined(bodyRect).gameObject;
		restored = building.restoredVisual != null
			? Instantiate(building.restoredVisual, bodyRect)
			: BuildRestored(bodyRect, building.roofColor).gameObject;

		lockTag = BuildLockTag(rect).gameObject;
		marker = BuildMarker(rect);
	}

	private void OnDestroy() => pulse?.Kill();

	// Called on every state change. Pulse runs only while the Base tab is visible.
	public void Refresh(BuildingStatus status, bool visible)
	{
		bool isRestored = status.state == BuildingState.Restored;
		bool isLocked = status.state == BuildingState.Locked;
		bool isAvailable = status.state == BuildingState.Available;

		ruined.SetActive(!isRestored);
		restored.SetActive(isRestored);
		lockTag.SetActive(isLocked);
		marker.gameObject.SetActive(isAvailable);
		body.alpha = isLocked ? LockedAlpha : 1f;

		SetPulse(isAvailable && visible);

		if (isRestored && !fogGone)
		{
			// A house already restored at load loses its fog at once. A live restore fades it out.
			fogGone = true;
			if (firstRefresh) fog.SetActive(false);
			else fogGroup.DOFade(0f, FadeDuration).OnComplete(() => fog.SetActive(false));
		}

		firstRefresh = false;
	}

	private void SetPulse(bool on)
	{
		if (on && pulse == null)
		{
			marker.localScale = Vector3.one;
			pulse = marker.DOScale(1.15f, 0.6f).SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine);
		}
		else if (!on && pulse != null)
		{
			pulse.Kill();
			pulse = null;
			marker.localScale = Vector3.one;
		}
	}

	private static RectTransform BuildRuined(RectTransform parent)
	{
		RectTransform walls = MetaUI.Rect("Walls", parent, new Vector2(0.1f, 0.05f), new Vector2(0.9f, 0.8f),
			Vector2.zero, Vector2.zero);
		MetaUI.ImageBox("WallsFill", walls, RuinedColor, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

		// Debris: a few small rotated blocks around the base.
		AddDebris(parent, new Vector2(0.05f, 0.04f), 34f, 20f);
		AddDebris(parent, new Vector2(0.78f, 0.08f), 40f, -15f);
		AddDebris(parent, new Vector2(0.62f, 0.12f), 26f, 35f);
		return walls;
	}

	private static void AddDebris(RectTransform parent, Vector2 anchor, float size, float angle)
	{
		RectTransform piece = MetaUI.ImageBox("Debris", parent, DebrisColor, anchor, anchor, Vector2.zero, Vector2.zero).rectTransform;
		piece.sizeDelta = new Vector2(size, size * 0.6f);
		piece.localRotation = Quaternion.Euler(0f, 0f, angle);
	}

	private static RectTransform BuildRestored(RectTransform parent, Color roof)
	{
		RectTransform house = MetaUI.Rect("Restored", parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
		MetaUI.ImageBox("Walls", house, CleanColor, new Vector2(0.1f, 0.05f), new Vector2(0.9f, 0.7f),
			Vector2.zero, Vector2.zero);
		MetaUI.ImageBox("Roof", house, roof, new Vector2(0.05f, 0.7f), new Vector2(0.95f, 0.9f),
			Vector2.zero, Vector2.zero);
		MetaUI.ImageBox("Door", house, DoorColor, new Vector2(0.42f, 0.08f), new Vector2(0.58f, 0.36f),
			Vector2.zero, Vector2.zero);
		return house;
	}

	// Tags sit over the house, so they must not eat taps meant for the house behind them.
	private static RectTransform BuildLockTag(RectTransform parent)
	{
		Image tag = MetaUI.ImageBox("LockTag", parent, LockColor, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
			new Vector2(-70f, -24f), new Vector2(70f, 24f));
		tag.raycastTarget = false;
		TMPro.TextMeshProUGUI text = MetaUI.Text("LockText", tag.transform, "LOCKED", 30,
			Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
		text.raycastTarget = false;
		return tag.rectTransform;
	}

	private static RectTransform BuildMarker(RectTransform parent)
	{
		Image marker = MetaUI.ImageBox("Marker", parent, MarkerColor, new Vector2(1f, 1f), new Vector2(1f, 1f),
			new Vector2(-64f, -64f), new Vector2(-8f, -8f));
		marker.raycastTarget = false;
		TMPro.TextMeshProUGUI text = MetaUI.Text("MarkerText", marker.transform, "!", 48,
			Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, TMPro.TextAlignmentOptions.Center, Color.black);
		text.raycastTarget = false;
		return marker.rectTransform;
	}
}
