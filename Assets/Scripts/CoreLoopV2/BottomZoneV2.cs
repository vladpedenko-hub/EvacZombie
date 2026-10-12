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
	private static RectTransform supplySlot;
	private static RectTransform adSlot;
	private static RectTransform goSlot;

	public static RectTransform ControlRow => controlRow;
	public static RectTransform SupplySlot => supplySlot;
	public static RectTransform AdSlot => adSlot;
	public static RectTransform GoSlot => goSlot;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ResetStatics() { controlRow = null; supplySlot = null; adSlot = null; goSlot = null; }

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

		// Left: Supply pill. Center-right: ad (smaller) + GO. Far right: ultimate (repositions the
		// existing scene AbilityButton rather than building a new one).
		supplySlot = BuildSlot("SupplySlot", 0f, 0.34f);
		adSlot = BuildSlot("AdSlot", 0.37f, 0.57f);
		goSlot = BuildSlot("GoSlot", 0.59f, 0.86f);
		RepositionAbilityButton(0.88f, 1f);

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
	private static void RepositionAbilityButton(float minX, float maxX)
	{
		GameObject go = GameObject.Find("AbilityButton");
		if (go == null) return;
		RectTransform rt = go.GetComponent<RectTransform>();
		if (rt == null || controlRow == null) return;

		rt.anchorMin = new Vector2(minX, controlRow.anchorMin.y);
		rt.anchorMax = new Vector2(maxX, controlRow.anchorMax.y);
		rt.offsetMin = Vector2.zero;
		rt.offsetMax = Vector2.zero;
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
