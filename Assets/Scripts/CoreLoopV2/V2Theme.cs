using UnityEngine;

// Shared palette/sizing for every Core Loop v2 HUD element (Docs/CORE_LOOP_V2.md HUD rebuild),
// per UX_DesignPhilosophy.md: slate/anthracite ground, amber-gold signal, acid green = safe/
// rescue, red-orange = threat/loss. One place to retune "how the v2 HUD looks" instead of
// per-file magic colors.
public static class V2Theme
{
	// III. Material Intelligence -- slate/anthracite ground, laminated-map panels.
	public static readonly Color PanelBg = new Color(0.07f, 0.085f, 0.095f, 0.88f);
	public static readonly Color PanelBgLight = new Color(0.12f, 0.14f, 0.15f, 0.88f);

	// Color signals intent (I. Operational Clarity).
	public static readonly Color SafeGreen = new Color(0.35f, 0.82f, 0.42f, 1f);
	public static readonly Color ThreatAmber = new Color(1f, 0.72f, 0.18f, 1f);
	public static readonly Color LossRed = new Color(0.92f, 0.26f, 0.22f, 1f);

	public static readonly Color TextPrimary = new Color(0.95f, 0.96f, 0.94f, 1f);
	public static readonly Color TextDim = new Color(0.68f, 0.70f, 0.68f, 1f);
	public static readonly Color TextOnDark = Color.white;

	// 3.4 Readability rules: 36px minimum at reference resolution, 56px+ for key numbers.
	public const float MinTextSize = 36f;
	public const float KeyNumberSize = 58f;
	public const float LabelSize = 30f;
	public const float SmallLabelSize = 24f;
}
