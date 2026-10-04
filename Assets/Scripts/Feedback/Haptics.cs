using System;
using UnityEngine;

public enum HapticType
{
	None,
	Light,    // UI tick, small confirmations
	Medium,   // placing a card, losing a civilian
	Heavy,    // explosion, boss
	Success,  // goal reached, win
	Warning,  // invalid action
	Failure   // lose
}

/// <summary>
/// Cross-platform haptics. Respects GameSettings.VibrationEnabled.
/// Android: short one-shot / waveform effects through the system Vibrator (API 26+ is the project minimum).
/// iOS: Unity has no light-impact API without a native plugin, so only strong events use Handheld.Vibrate().
///      To get proper iOS taptic feedback later, swap the iOS branch for a plugin (e.g. Nice Vibrations).
/// </summary>
public static class Haptics
{
	private static float lastPlayTime = -10f;
	private const float GlobalMinInterval = 0.05f;

#if UNITY_ANDROID && !UNITY_EDITOR
	private static AndroidJavaObject vibrator;
	private static int sdkInt;
	private static bool initTried;

	private static void InitAndroid()
	{
		initTried = true;
		try
		{
			using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
				sdkInt = version.GetStatic<int>("SDK_INT");

			using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
			using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
				vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
		}
		catch (Exception e)
		{
			Debug.LogWarning("[Haptics] Android vibrator init failed: " + e.Message);
			vibrator = null;
		}
	}

	private static void AndroidOneShot(long ms, int amplitude)
	{
		if (vibrator == null) return;
		try
		{
			if (sdkInt >= 26)
			{
				using (var effectClass = new AndroidJavaClass("android.os.VibrationEffect"))
				using (var effect = effectClass.CallStatic<AndroidJavaObject>("createOneShot", ms, amplitude))
					vibrator.Call("vibrate", effect);
			}
			else
			{
				vibrator.Call("vibrate", ms);
			}
		}
		catch (Exception e) { Debug.LogWarning("[Haptics] vibrate failed: " + e.Message); }
	}

	private static void AndroidPattern(long[] timings)
	{
		if (vibrator == null) return;
		try
		{
			if (sdkInt >= 26)
			{
				using (var effectClass = new AndroidJavaClass("android.os.VibrationEffect"))
				using (var effect = effectClass.CallStatic<AndroidJavaObject>("createWaveform", timings, -1))
					vibrator.Call("vibrate", effect);
			}
			else
			{
				vibrator.Call("vibrate", timings, -1);
			}
		}
		catch (Exception e) { Debug.LogWarning("[Haptics] vibrate pattern failed: " + e.Message); }
	}
#endif

	public static void Play(HapticType type)
	{
		if (type == HapticType.None) return;
		if (!GameSettings.VibrationEnabled) return;

		float now = Time.unscaledTime;
		if (now - lastPlayTime < GlobalMinInterval) return;
		lastPlayTime = now;

#if UNITY_ANDROID && !UNITY_EDITOR
		if (!initTried) InitAndroid();

		if (vibrator == null)
		{
			// Last-resort fallback; also makes sure Unity adds the VIBRATE permission to the manifest.
			if (type >= HapticType.Heavy) Handheld.Vibrate();
			return;
		}

		switch (type)
		{
			case HapticType.Light:   AndroidOneShot(12, 70); break;
			case HapticType.Medium:  AndroidOneShot(25, 140); break;
			case HapticType.Heavy:   AndroidOneShot(50, 255); break;
			case HapticType.Success: AndroidPattern(new long[] { 0, 20, 60, 40 }); break;
			case HapticType.Warning: AndroidPattern(new long[] { 0, 35, 50, 35 }); break;
			case HapticType.Failure: AndroidPattern(new long[] { 0, 60, 70, 120 }); break;
		}
#elif UNITY_IOS && !UNITY_EDITOR
		// Handheld.Vibrate() is a long, harsh buzz on iPhone — only use it for strong moments.
		if (type == HapticType.Heavy || type == HapticType.Failure || type == HapticType.Success)
			Handheld.Vibrate();
#else
		// Editor / desktop: no hardware. Uncomment to trace haptics in the console while tuning.
		// Debug.Log("[Haptics] " + type);
#endif
	}
}
