using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Every gameplay/UI moment that should give the player feedback.
/// Add a new value here + one row in Feedback.table and call Feedback.Play(...) from game code.
/// </summary>
public enum FeedbackEvent
{
	// UI
	UiClick, UiBack, UiPopupOpen, UiToggle, UiError, UiStar, UiReward, UiLevelStart, UiUpgrade, UiEquip,

	// Cards
	CardPickUp, CardPlaceOk, CardPlaceInvalid, CardReady,

	// Zombies / civilians
	WaveWarning, NightStart, ZombieHit, ZombieDeath, ZombieInfect, CivilianLost,

	// Weapons
	SniperShot, SoldierShot, HeliGun, BombExplosion, BarricadeHit, BarricadeBreak,

	// Transport / rescue
	HeliArrive, HeliTakeoff, CarArrive, RescueDeparted, RescueTick, GoalReached,

	// Abilities / boss
	AbilityReady, AbilityActivate, BossRage,

	// Results
	Win, PerfectClear, Lose
}

/// <summary>
/// One-line facade: Feedback.Play(FeedbackEvent.X) or Feedback.Play(FeedbackEvent.X, worldPosition).
/// Each event maps to sound + haptic + particles + camera shake in the table below — tune everything here.
/// Events with a world position are skipped entirely when that position is off-screen.
/// </summary>
public static class Feedback
{
	private struct Entry
	{
		public SfxId sfx;
		public float sfxVolume;
		public HapticType haptic;
		public float hapticCooldown;
		public VfxId vfx;
		public float vfxScale;
		public float shakeAmplitude;
		public float shakeDuration;
	}

	private static Entry E(
		SfxId sfx,
		HapticType haptic = HapticType.None,
		VfxId vfx = VfxId.None,
		float vol = 1f,
		float hapticCd = 0.15f,
		float vfxScale = 1f,
		float shake = 0f,
		float shakeTime = 0.25f)
	{
		return new Entry
		{
			sfx = sfx, sfxVolume = vol,
			haptic = haptic, hapticCooldown = hapticCd,
			vfx = vfx, vfxScale = vfxScale,
			shakeAmplitude = shake, shakeDuration = shakeTime
		};
	}

	// =====================================================================  TUNE HERE
	private static readonly Dictionary<FeedbackEvent, Entry> table = new Dictionary<FeedbackEvent, Entry>
	{
		// ---- UI
		{ FeedbackEvent.UiClick,          E(SfxId.UiClick, vol: 0.8f) },
		{ FeedbackEvent.UiBack,           E(SfxId.UiBack, vol: 0.8f) },
		{ FeedbackEvent.UiPopupOpen,      E(SfxId.UiPopupOpen, vol: 0.8f) },
		{ FeedbackEvent.UiToggle,         E(SfxId.UiToggle, HapticType.Light) },
		{ FeedbackEvent.UiError,          E(SfxId.UiError, HapticType.Warning) },
		{ FeedbackEvent.UiStar,           E(SfxId.UiStar, HapticType.Light) },
		{ FeedbackEvent.UiReward,         E(SfxId.UiReward, HapticType.Success) },
		{ FeedbackEvent.UiLevelStart,     E(SfxId.UiLevelStart, HapticType.Medium) },
		{ FeedbackEvent.UiUpgrade,        E(SfxId.UiUpgrade, HapticType.Success) },
		{ FeedbackEvent.UiEquip,          E(SfxId.UiEquip, HapticType.Light) },

		// ---- Cards
		{ FeedbackEvent.CardPickUp,       E(SfxId.CardPickUp, HapticType.Light) },
		{ FeedbackEvent.CardPlaceOk,      E(SfxId.CardPlaceOk, HapticType.Medium, VfxId.PlacePop) },
		{ FeedbackEvent.CardPlaceInvalid, E(SfxId.CardPlaceInvalid, HapticType.Warning) },
		{ FeedbackEvent.CardReady,        E(SfxId.CardReady, vol: 0.6f) },

		// ---- Zombies / civilians
		{ FeedbackEvent.WaveWarning,      E(SfxId.WaveWarning, HapticType.Medium, hapticCd: 1.0f) },
		{ FeedbackEvent.NightStart,       E(SfxId.None, HapticType.Heavy, shake: 0.3f, shakeTime: 0.5f) }, // scream = existing scene AudioSource
		{ FeedbackEvent.ZombieHit,        E(SfxId.ZombieHit, vfx: VfxId.HitSpark, vol: 0.7f) },
		{ FeedbackEvent.ZombieDeath,      E(SfxId.ZombieDeath, vfx: VfxId.DeathPuff, vol: 0.8f) },
		{ FeedbackEvent.ZombieInfect,     E(SfxId.ZombieInfect, HapticType.Medium, VfxId.InfectBurst, hapticCd: 0.4f) },
		{ FeedbackEvent.CivilianLost,     E(SfxId.CivilianLost, HapticType.Medium, VfxId.CivilianLost, hapticCd: 0.4f) },

		// ---- Weapons
		{ FeedbackEvent.SniperShot,       E(SfxId.SniperShot, HapticType.Light, VfxId.MuzzleFlash, hapticCd: 0.25f) },
		{ FeedbackEvent.SoldierShot,      E(SfxId.SoldierShot, vfx: VfxId.MuzzleFlash, vol: 0.6f, vfxScale: 0.7f) },
		{ FeedbackEvent.HeliGun,          E(SfxId.HeliGun, vfx: VfxId.MuzzleFlash, vol: 0.5f, vfxScale: 0.8f) },
		{ FeedbackEvent.BombExplosion,    E(SfxId.BombExplosion, HapticType.Heavy, shake: 0.5f, shakeTime: 0.45f) },
		{ FeedbackEvent.BarricadeHit,     E(SfxId.BarricadeHit, vfx: VfxId.BarricadeDust, vol: 0.8f) },
		{ FeedbackEvent.BarricadeBreak,   E(SfxId.BarricadeBreak, HapticType.Light, VfxId.BarricadeDebris, hapticCd: 0.3f) },

		// ---- Transport / rescue
		{ FeedbackEvent.HeliArrive,       E(SfxId.HeliArrive, vol: 0.8f) },
		{ FeedbackEvent.HeliTakeoff,      E(SfxId.HeliTakeoff, vol: 0.8f) },
		{ FeedbackEvent.CarArrive,        E(SfxId.CarArrive) },
		{ FeedbackEvent.RescueDeparted,   E(SfxId.RescueDeparted, HapticType.Light, VfxId.RescueSparkle, hapticCd: 0.2f) },
		{ FeedbackEvent.RescueTick,       E(SfxId.RescueTick, HapticType.Light, hapticCd: 0.15f) },
		{ FeedbackEvent.GoalReached,      E(SfxId.GoalReached, HapticType.Success) },

		// ---- Abilities / boss
		{ FeedbackEvent.AbilityReady,     E(SfxId.AbilityReady, HapticType.Success) },
		{ FeedbackEvent.AbilityActivate,  E(SfxId.AbilityActivate, HapticType.Medium) },
		{ FeedbackEvent.BossRage,         E(SfxId.BossRage, HapticType.Heavy, hapticCd: 1.0f, shake: 0.35f, shakeTime: 0.6f) },

		// ---- Results
		{ FeedbackEvent.Win,              E(SfxId.Win, HapticType.Success) },
		{ FeedbackEvent.PerfectClear,     E(SfxId.PerfectClear, HapticType.Success) },
		{ FeedbackEvent.Lose,             E(SfxId.Lose, HapticType.Failure) },
	};
	// =====================================================================

	private static readonly Dictionary<FeedbackEvent, float> lastHaptic = new Dictionary<FeedbackEvent, float>();

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ResetStatics() => lastHaptic.Clear();

	/// <summary>Play without a world position (UI, global moments).</summary>
	public static void Play(FeedbackEvent e) => Dispatch(e, null);

	/// <summary>Play at a world position (particles spawn there; off-screen events are skipped).</summary>
	public static void Play(FeedbackEvent e, Vector3 worldPosition) => Dispatch(e, worldPosition);

	private static void Dispatch(FeedbackEvent e, Vector3? position)
	{
		if (!table.TryGetValue(e, out Entry entry)) return;

		if (position.HasValue && !IsRoughlyOnScreen(position.Value)) return;

		AudioManager.PlaySfx(entry.sfx, entry.sfxVolume);

		if (entry.haptic != HapticType.None && HapticReady(e, entry.hapticCooldown))
			Haptics.Play(entry.haptic);

		if (entry.vfx != VfxId.None && position.HasValue)
			VfxManager.Play(entry.vfx, position.Value, entry.vfxScale);

		if (entry.shakeAmplitude > 0f)
			CameraShake.Shake(entry.shakeAmplitude, entry.shakeDuration);
	}

	private static bool HapticReady(FeedbackEvent e, float cooldown)
	{
		float now = Time.unscaledTime;
		if (lastHaptic.TryGetValue(e, out float last) && now - last < cooldown) return false;
		lastHaptic[e] = now;
		return true;
	}

	private static bool IsRoughlyOnScreen(Vector3 worldPos)
	{
		Camera cam = Camera.main;
		if (cam == null) return true;

		Vector3 v = cam.WorldToViewportPoint(worldPos);
		return v.z > 0f && v.x > -0.15f && v.x < 1.15f && v.y > -0.15f && v.y < 1.15f;
	}
}
