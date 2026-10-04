using System;
using UnityEngine;

/// <summary>
/// Player-facing settings (sound / music / vibration / frame rate).
/// Static, PlayerPrefs-backed, safe to read from anywhere before any scene object exists.
/// </summary>
public static class GameSettings
{
	private const string KeySfx = "Settings_Sfx";
	private const string KeyMusic = "Settings_Music";
	private const string KeyVibration = "Settings_Vibration";
	private const string KeyFps60 = "Settings_Fps60";

	/// <summary>Fired after any setting changes (value already saved).</summary>
	public static event Action Changed;

	private static bool loaded;
	private static bool sfx = true;
	private static bool music = true;
	private static bool vibration = true;
	private static bool fps60 = true;

	// Support "Enter Play Mode without domain reload": statics must not leak between sessions.
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ResetStatics()
	{
		loaded = false;
		Changed = null;
	}

	/// <summary>Applies settings that need an engine call (frame rate). Called on startup and on change.</summary>
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	public static void ApplyRuntime()
	{
		EnsureLoaded();
		Application.targetFrameRate = fps60 ? 60 : 30;
	}

	private static void EnsureLoaded()
	{
		if (loaded) return;
		loaded = true;
		sfx = PlayerPrefs.GetInt(KeySfx, 1) == 1;
		music = PlayerPrefs.GetInt(KeyMusic, 1) == 1;
		vibration = PlayerPrefs.GetInt(KeyVibration, 1) == 1;
		fps60 = PlayerPrefs.GetInt(KeyFps60, 1) == 1;
	}

	public static bool SfxEnabled
	{
		get { EnsureLoaded(); return sfx; }
		set { EnsureLoaded(); if (sfx == value) return; sfx = value; Save(KeySfx, value); }
	}

	public static bool MusicEnabled
	{
		get { EnsureLoaded(); return music; }
		set { EnsureLoaded(); if (music == value) return; music = value; Save(KeyMusic, value); }
	}

	public static bool VibrationEnabled
	{
		get { EnsureLoaded(); return vibration; }
		set { EnsureLoaded(); if (vibration == value) return; vibration = value; Save(KeyVibration, value); }
	}

	/// <summary>true = 60 FPS, false = 30 FPS (battery saver).</summary>
	public static bool Fps60
	{
		get { EnsureLoaded(); return fps60; }
		set
		{
			EnsureLoaded();
			if (fps60 == value) return;
			fps60 = value;
			Save(KeyFps60, value);
			ApplyRuntime();
		}
	}

	private static void Save(string key, bool value)
	{
		PlayerPrefs.SetInt(key, value ? 1 : 0);
		PlayerPrefs.Save();
		Changed?.Invoke();
	}
}
