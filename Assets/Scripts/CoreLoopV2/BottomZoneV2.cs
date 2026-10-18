using System.Collections.Generic;
using UnityEngine;

// Core Loop v2 HUD rebuild: the shared BOTTOM-zone control row (Docs/CORE_LOOP_V2.md "A strict
// HUD zone system... bottom = everything the player touches"). Sits directly above
// CardManager.cardsPanel as its sibling, split into named slots that SupplyHud/PlanningControllerV2
// build into, plus the pre-existing AbilityButton repositioned into its own slot. Not a
// MonoBehaviour -- a plain static registry, rebuilt (Unity's fake-null makes stale references
// detect correctly) the first time any v2 system asks for it after a level load.
public static class BottomZoneV2
{
	private static RectTransform controlRow;
	private static RectTransform adSlot;
	private static RectTransform goSlot;

	public static RectTransform ControlRow => controlRow;
	public static RectTransform AdSlot => adSlot;
	public static RectTransform GoSlot => goSlot;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ResetStatics() { controlRow = null; adSlot = null; goSlot = null; }

	// Called from LevelManager.LoadLevel(), before SupplyHud/PlanningControllerV2's own hooks.
	public static void EnsureBuilt()
	{
		if (controlRow != null) return;
		if (CardManager.Instance == null || CardManager.Instance.cardsPanel == null) return;

		RectTransform cardsPanel = CardManager.Instance.cardsPanel as RectTransform;
		if (cardsPanel == null) return;
		Transform parent = cardsPanel.parent;

		controlRow = RuntimeUiKit.CreateRect("BottomControlRowV2", parent);
		controlRow.anchorMin = new Vector2(0.04f, cardsPanel.anchorMax.y);
		controlRow.anchorMax = new Vector2(0.96f, cardsPanel.anchorMax.y + 0.105f);
		controlRow.offsetMin = Vector2.zero;
		controlRow.offsetMax = Vector2.zero;

		// Ad (smaller) + GO. The ultimate button is positioned separately, above this row on the
		// right edge (feature/hud-sandbox-ui) -- Supply moved out entirely (now in TopHudV2's zone,
		// see SupplyHud.cs), freeing the rest of this row for ad/GO alone.
		adSlot = BuildSlot("AdSlot", 0.37f, 0.57f);
		goSlot = BuildSlot("GoSlot", 0.59f, 0.86f);
		RepositionAbilityButton();

		if (LevelManager.Instance != null) LevelManager.Instance.RegisterBottomReservedUI(controlRow);
	}

	private static RectTransform BuildSlot(string name, float minX, float maxX)
	{
		RectTransform rt = RuntimeUiKit.CreateRect(name, controlRow);
		rt.anchorMin = new Vector2(minX, 0f);
		rt.anchorMax = new Vector2(maxX, 1f);
		rt.offsetMin = Vector2.zero;
		rt.offsetMax = Vector2.zero;
		return rt;
	}

	// The ultimate button is a pre-existing scene object (SafeAreaContainer/AbilityButton, with its
	// own BackgroundImage/FillImage/PulseGlow children and AbilityButtonUI wiring) -- repositioned
	// in place each level load rather than rebuilt, so none of that wiring is disturbed.
	//
	// feature/hud-sandbox-ui: fixed-size (96px at the 1080 reference) touch target on the right
	// screen edge, ~25-30% up from the bottom -- clear of both the card row below and this
	// control row's own slots (it no longer shares a rect with either).
	private static void RepositionAbilityButton()
	{
		GameObject go = GameObject.Find("AbilityButton");
		if (go == null) return;
		RectTransform rt = go.GetComponent<RectTransform>();
		if (rt == null) return;

		const float size = 96f;
		rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.28f);
		rt.pivot = new Vector2(1f, 0.5f);
		rt.sizeDelta = new Vector2(size, size);
		rt.anchoredPosition = new Vector2(-16f, 0f);
	}

	// Section 3.5: card placement must be blocked under every bottom-zone UI, not only CardsPanel
	// itself -- used by CardManager.IsScreenPointOverCardsPanel.
	public static bool IsScreenPointOverControlRow(Vector2 screenPoint)
	{
		if (controlRow == null || !controlRow.gameObject.activeInHierarchy) return false;

		Canvas canvas = controlRow.GetComponentInParent<Canvas>();
		Camera uiCamera = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;
		return RectTransformUtility.RectangleContainsScreenPoint(controlRow, screenPoint, uiCamera);
	}
}
