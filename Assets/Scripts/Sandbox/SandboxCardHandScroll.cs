using UnityEngine;
using UnityEngine.UI;

// fix/sandbox-city-visuals (card-hand overlap fix): CardManager.cardsPanel is a fixed-width
// HorizontalLayoutGroup with no clipping or scrolling -- fine for a normal level's 3-5 owned
// cards, but the sandbox unlocks all 8 at once (CardManager.SandboxAllCardsActive) and they
// overflow past the panel's edges, overlapping the HUD around it. Wraps cardsPanel in a horizontal
// ScrollRect + clipped viewport, built once at runtime (no scene edit, same pattern as
// SandboxButtonInjector) and reapplied on every later SpawnDeck() rebuild. CardManager only calls
// EnsureWrapped() while SandboxAllCardsActive is true, so a normal level's cardsPanel is never
// touched by this class.
public static class SandboxCardHandScroll
{
	private const string ViewportName = "SandboxCardsScrollViewport";

	public static void EnsureWrapped(Transform cardsPanelTransform)
	{
		RectTransform cardsPanel = cardsPanelTransform as RectTransform;
		if (cardsPanel == null) return;

		// Already wrapped from an earlier sandbox SpawnDeck() this session -- SpawnDeck() only
		// destroys cardsPanel's CHILDREN (the card buttons), never cardsPanel itself, so the
		// wrapper survives a hand rebuild and only needs its content-fit settings reapplied.
		if (cardsPanel.parent != null && cardsPanel.parent.name == ViewportName)
		{
			ApplyContentSettings(cardsPanel);
			return;
		}

		Transform originalParent = cardsPanel.parent;
		int siblingIndex = cardsPanel.GetSiblingIndex();

		RectTransform viewport = RuntimeUiKit.CreateRect(ViewportName, originalParent);
		viewport.SetSiblingIndex(siblingIndex);
		// Take over cardsPanel's original anchored rect exactly, so nothing else in the HUD shifts.
		viewport.anchorMin = cardsPanel.anchorMin;
		viewport.anchorMax = cardsPanel.anchorMax;
		viewport.pivot = cardsPanel.pivot;
		viewport.offsetMin = cardsPanel.offsetMin;
		viewport.offsetMax = cardsPanel.offsetMax;

		Image clip = viewport.gameObject.AddComponent<Image>();
		clip.color = new Color(0f, 0f, 0f, 0f); // invisible; only here so the viewport is raycastable for drag
		viewport.gameObject.AddComponent<RectMask2D>();

		ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
		scroll.horizontal = true;
		scroll.vertical = false;
		scroll.viewport = viewport;
		scroll.content = cardsPanel;
		scroll.movementType = ScrollRect.MovementType.Elastic;
		scroll.inertia = true;

		cardsPanel.SetParent(viewport, false);
		// Content must be free to grow past the viewport's width -- pin it to the left edge instead
		// of stretching to match the viewport, and let ContentSizeFitter size it to its children's
		// combined width (ApplyContentSettings below).
		cardsPanel.anchorMin = new Vector2(0f, cardsPanel.anchorMin.y);
		cardsPanel.anchorMax = new Vector2(0f, cardsPanel.anchorMax.y);
		cardsPanel.pivot = new Vector2(0f, cardsPanel.pivot.y);
		cardsPanel.offsetMin = new Vector2(0f, cardsPanel.offsetMin.y);
		cardsPanel.offsetMax = new Vector2(0f, cardsPanel.offsetMax.y);
		cardsPanel.anchoredPosition = new Vector2(0f, cardsPanel.anchoredPosition.y);

		ApplyContentSettings(cardsPanel);
	}

	private static void ApplyContentSettings(RectTransform cardsPanel)
	{
		ContentSizeFitter fitter = cardsPanel.GetComponent<ContentSizeFitter>();
		if (fitter == null) fitter = cardsPanel.gameObject.AddComponent<ContentSizeFitter>();
		fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
		fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
	}
}
