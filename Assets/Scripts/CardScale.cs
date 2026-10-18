using System.Collections.Generic;
using UnityEngine;

// feature/card-scale: global, single-knob adaptation of every card's effective radius to the
// current level's street width. Generated cities (CityGenerator) have much narrower streets than
// the ~6m hand-built levels every card radius was originally tuned for (Bomb 6, Bait 5, Soldier 5,
// Sniper 10, Heli 8) -- on a generated city those ranges can span several blocks (see the
// feature/card-scale audit for every consumer).
//
// RadiusMult is a computed RESULT (set once per level load by LevelManager, and live-recomputed by
// the SandboxDevPanel "Card scale" cycle); CardScaleTuning holds the FORMULA's configurable
// parameters. CardData.GetCalculatedStat multiplies every StatType.Radius by RadiusMult centrally
// -- the one place that touches every card's main range with a single call-site change.
public static class CardScale
{
	public static float RadiusMult { get; private set; } = 1f;

	public static bool ScaleHardcodedRanges =>
		CardScaleTuning.Active == null || CardScaleTuning.Active.scaleHardcodedRanges;

	// Support "Enter Play Mode without domain reload" -- don't leak a stale multiplier between
	// play sessions (same reasoning as every other *Tuning.Active cache in this project).
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ResetStatics() => RadiusMult = 1f;

	// feature/card-scale (step 2): called once per level load from LevelManager.LoadLevel, right
	// after LevelPaletteApplier.OnLevelLoaded -- also safe to call anytime after that (the
	// SandboxDevPanel "Card scale" cycle calls it again to apply a manual override live, without a
	// level reload).
	//
	// "Is this a generated city" reuses LevelPaletteService.Active (set by LevelPaletteApplier's own
	// Buildings/Frame/Ground-child detection) rather than duplicating that detection here or
	// repurposing LevelData.isSandboxLevel, which gates dev-panel/deck-override behavior, not level
	// geometry, and isn't guaranteed to stay 1:1 with "this level's environment is generated" if a
	// second generated level is ever added.
	public static void OnLevelLoaded(LevelData data)
	{
		CardScaleTuning tuning = CardScaleTuning.Active;

		if (tuning != null && !tuning.autoFromStreets)
		{
			Apply(tuning.manualMult, $"manual override ({data?.name ?? "unknown level"})");
			return;
		}

		bool isGeneratedCity = LevelPaletteService.Active != null;
		if (!isGeneratedCity)
		{
			Apply(1f, $"hand-built level ({data?.name ?? "unknown level"})");
			return;
		}

		float referenceWidth = tuning != null ? Mathf.Max(0.01f, tuning.referenceStreetWidth) : 6f;
		float minMult = tuning != null ? tuning.minMult : 0.3f;

		float streetWidth = ComputeMedianStreetWidth();
		if (streetWidth <= 0f)
		{
			Apply(1f, $"generated city ({data?.name ?? "unknown level"}), but no StreetSegment found to measure");
			return;
		}

		float mult = Mathf.Clamp(streetWidth / referenceWidth, minMult, 1f);
		Apply(mult, $"generated city ({data?.name ?? "unknown level"}), median street width {streetWidth:F2}m / reference {referenceWidth:F2}m");
	}

	private static void Apply(float mult, string reason)
	{
		RadiusMult = mult;
		Debug.Log($"[CardScale] RadiusMult = {mult:F3} ({reason})");
	}

	// feature/card-scale (step 2): "the median StreetSegment width" option from the task -- read
	// from actual placed geometry rather than CityGenConfig (an explicitly Editor/dev-time-only
	// asset per its own header comment: "nothing here is read at runtime"), and robust to alleys
	// (narrower than the base roadWidth) pulling the median down, which a single config field
	// wouldn't reflect.
	private static float ComputeMedianStreetWidth()
	{
		StreetSegment[] segments = Object.FindObjectsByType<StreetSegment>(FindObjectsSortMode.None);
		if (segments.Length == 0) return 0f;

		var widths = new List<float>(segments.Length);
		foreach (StreetSegment s in segments)
		{
			if (s == null || s.IsIntersection) continue;
			if (s.Width > 0f) widths.Add(s.Width);
		}

		if (widths.Count == 0) return 0f;

		widths.Sort();
		return widths[widths.Count / 2];
	}
}
