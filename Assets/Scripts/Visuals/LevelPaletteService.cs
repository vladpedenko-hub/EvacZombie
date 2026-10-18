using UnityEngine;

// feature/readability-palette: process-wide "is a readability palette active right now" flag.
// Set by LevelPaletteApplier only when the just-loaded level is a CityGenerator-built generated
// city; stays null for hand-built levels (Level_1..10) and menus. Every tint/recolor call site in
// Zombie.cs/EvacueeBase.cs/Barricade.cs/HelicopterController.cs/ActorDotRenderer.cs reads this and
// no-ops when it's null, so hand-built levels are never touched by this feature.
public static class LevelPaletteService
{
	public static LevelPalette Active { get; private set; }

	public static void SetActive(LevelPalette palette)
	{
		Active = palette;
	}

	// Sandbox-only remembered choice (SandboxDevPanel's palette cycle button writes this).
	private const string PlayerPrefsKey = "Sandbox_LevelPaletteName";
	private const string DefaultPaletteName = "LevelPalette_RefDark";

	// Resources-discovered (Assets/Resources/Palettes/*.asset). Returns the remembered choice if
	// one was saved and still exists, otherwise "Ref Dark", otherwise whatever got found first.
	public static LevelPalette ResolveStartupPalette()
	{
		LevelPalette[] all = Resources.LoadAll<LevelPalette>("Palettes");
		if (all == null || all.Length == 0) return null;

		string savedName = PlayerPrefs.GetString(PlayerPrefsKey, DefaultPaletteName);

		foreach (LevelPalette p in all)
			if (p != null && p.name == savedName) return p;

		foreach (LevelPalette p in all)
			if (p != null && p.name == DefaultPaletteName) return p;

		return all[0];
	}

	public static void RememberSandboxChoice(LevelPalette palette)
	{
		if (palette == null) return;
		PlayerPrefs.SetString(PlayerPrefsKey, palette.name);
	}

	// Support "Enter Play Mode without domain reload" -- don't leak a stale reference between
	// play sessions (same reasoning as CameraReadabilityConfig.ResetStatics).
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ResetStatics() => Active = null;
}
