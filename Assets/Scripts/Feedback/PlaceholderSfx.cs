using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Synthesises throw-away placeholder sounds in code so every event is audible before real assets exist.
/// Real files in Resources/Audio/Sfx/ always win over these (see AudioManager).
/// </summary>
public static class PlaceholderSfx
{
	private enum Wave { Sine, Square, Saw }

	private struct Recipe
	{
		public Wave wave;
		public float f0, f1;      // start / end frequency (Hz)
		public float dur;         // seconds
		public float noise;       // 0..1 blend of white noise
		public float vol;         // 0..1
		public float decay;       // envelope curvature, higher = snappier

		public Recipe(Wave wave, float f0, float f1, float dur, float noise, float vol, float decay)
		{
			this.wave = wave; this.f0 = f0; this.f1 = f1; this.dur = dur;
			this.noise = noise; this.vol = vol; this.decay = decay;
		}
	}

	private const int SampleRate = 22050;
	private static readonly Dictionary<SfxId, Recipe> recipes = new Dictionary<SfxId, Recipe>
	{
		// UI
		{ SfxId.UiClick,          new Recipe(Wave.Sine,   900,  600, 0.05f, 0.00f, 0.50f, 2.0f) },
		{ SfxId.UiBack,           new Recipe(Wave.Sine,   600,  400, 0.06f, 0.00f, 0.50f, 2.0f) },
		{ SfxId.UiPopupOpen,      new Recipe(Wave.Sine,   400,  800, 0.12f, 0.00f, 0.45f, 1.5f) },
		{ SfxId.UiToggle,         new Recipe(Wave.Square, 700,  700, 0.04f, 0.00f, 0.25f, 2.0f) },
		{ SfxId.UiError,          new Recipe(Wave.Square, 180,  140, 0.18f, 0.00f, 0.35f, 1.5f) },
		{ SfxId.UiStar,           new Recipe(Wave.Sine,   700, 1400, 0.20f, 0.00f, 0.50f, 1.5f) },
		{ SfxId.UiReward,         new Recipe(Wave.Sine,   500, 1500, 0.45f, 0.00f, 0.50f, 1.2f) },
		{ SfxId.UiLevelStart,     new Recipe(Wave.Saw,    200,  500, 0.30f, 0.05f, 0.40f, 1.5f) },
		{ SfxId.UiUpgrade,        new Recipe(Wave.Sine,   600, 1200, 0.30f, 0.00f, 0.50f, 1.3f) },
		{ SfxId.UiEquip,          new Recipe(Wave.Sine,   500,  700, 0.08f, 0.00f, 0.45f, 2.0f) },

		// Cards
		{ SfxId.CardPickUp,       new Recipe(Wave.Sine,   500,  700, 0.06f, 0.00f, 0.40f, 2.0f) },
		{ SfxId.CardPlaceOk,      new Recipe(Wave.Sine,   300,  150, 0.14f, 0.10f, 0.60f, 2.0f) },
		{ SfxId.CardPlaceInvalid, new Recipe(Wave.Square, 150,  100, 0.15f, 0.00f, 0.35f, 1.5f) },
		{ SfxId.CardReady,        new Recipe(Wave.Sine,  1000, 1000, 0.05f, 0.00f, 0.20f, 2.0f) },

		// Zombies / civilians
		{ SfxId.WaveWarning,      new Recipe(Wave.Saw,    250,  400, 0.50f, 0.05f, 0.45f, 1.2f) },
		{ SfxId.ZombieHit,        new Recipe(Wave.Sine,   200,   80, 0.08f, 0.70f, 0.40f, 2.5f) },
		{ SfxId.ZombieDeath,      new Recipe(Wave.Sine,   160,   60, 0.20f, 0.50f, 0.50f, 2.0f) },
		{ SfxId.ZombieInfect,     new Recipe(Wave.Saw,    120,   60, 0.25f, 0.30f, 0.55f, 1.8f) },
		{ SfxId.CivilianLost,     new Recipe(Wave.Sine,   300,  100, 0.25f, 0.30f, 0.50f, 1.8f) },

		// Weapons
		{ SfxId.SniperShot,       new Recipe(Wave.Sine,   600,   60, 0.25f, 0.85f, 0.80f, 3.0f) },
		{ SfxId.SoldierShot,      new Recipe(Wave.Sine,   400,  100, 0.08f, 0.80f, 0.45f, 3.0f) },
		{ SfxId.HeliGun,          new Recipe(Wave.Sine,   300,  100, 0.06f, 0.80f, 0.30f, 3.0f) },
		{ SfxId.BombExplosion,    new Recipe(Wave.Sine,   120,   30, 0.90f, 0.90f, 1.00f, 2.0f) },
		{ SfxId.BarricadeHit,     new Recipe(Wave.Sine,   220,  120, 0.10f, 0.60f, 0.45f, 2.5f) },
		{ SfxId.BarricadeBreak,   new Recipe(Wave.Sine,   150,   60, 0.30f, 0.80f, 0.60f, 2.0f) },

		// Transport / rescue
		{ SfxId.HeliArrive,       new Recipe(Wave.Saw,     80,  120, 0.50f, 0.50f, 0.45f, 1.0f) },
		{ SfxId.HeliTakeoff,      new Recipe(Wave.Saw,    120,   80, 0.60f, 0.50f, 0.45f, 1.0f) },
		{ SfxId.CarArrive,        new Recipe(Wave.Saw,    180,  240, 0.30f, 0.20f, 0.40f, 1.5f) },
		{ SfxId.RescueDeparted,   new Recipe(Wave.Sine,   600,  900, 0.15f, 0.00f, 0.40f, 1.5f) },
		{ SfxId.RescueTick,       new Recipe(Wave.Sine,  1200, 1200, 0.04f, 0.00f, 0.30f, 2.0f) },
		{ SfxId.GoalReached,      new Recipe(Wave.Sine,   600, 1200, 0.50f, 0.00f, 0.55f, 1.2f) },

		// Abilities / boss
		{ SfxId.AbilityReady,     new Recipe(Wave.Sine,   800, 1600, 0.30f, 0.00f, 0.50f, 1.3f) },
		{ SfxId.AbilityActivate,  new Recipe(Wave.Saw,    300,  900, 0.35f, 0.05f, 0.45f, 1.4f) },
		{ SfxId.BossRage,         new Recipe(Wave.Saw,     70,   40, 0.90f, 0.30f, 0.90f, 1.3f) },

		// Results
		{ SfxId.Win,              new Recipe(Wave.Sine,   500, 1000, 0.80f, 0.00f, 0.60f, 1.2f) },
		{ SfxId.PerfectClear,     new Recipe(Wave.Sine,   600, 1600, 1.00f, 0.00f, 0.60f, 1.2f) },
		{ SfxId.Lose,             new Recipe(Wave.Sine,   300,   80, 0.90f, 0.05f, 0.60f, 1.2f) },
	};

	public static AudioClip Get(SfxId id)
	{
		if (!recipes.TryGetValue(id, out Recipe r)) return null;
		return Build(id.ToString(), r, (int)id);
	}

	private static AudioClip Build(string name, Recipe r, int seed)
	{
		int n = Mathf.Max(1, Mathf.CeilToInt(r.dur * SampleRate));
		float[] data = new float[n];
		var rnd = new System.Random(seed * 7919 + 13);

		float phase = 0f;
		int attackSamples = Mathf.Max(1, (int)(0.004f * SampleRate)); // 4 ms click-free attack

		for (int i = 0; i < n; i++)
		{
			float t = i / (float)n;
			float freq = Mathf.Lerp(r.f0, r.f1, t);
			phase += 2f * Mathf.PI * freq / SampleRate;
			if (phase > 2f * Mathf.PI) phase -= 2f * Mathf.PI;

			float tone;
			switch (r.wave)
			{
				case Wave.Square: tone = Mathf.Sin(phase) >= 0f ? 0.6f : -0.6f; break;
				case Wave.Saw:    tone = (phase / Mathf.PI) - 1f; break;
				default:          tone = Mathf.Sin(phase); break;
			}

			float noise = (float)(rnd.NextDouble() * 2.0 - 1.0);
			float sample = Mathf.Lerp(tone, noise, r.noise);

			float env = Mathf.Pow(1f - t, r.decay);
			if (i < attackSamples) env *= i / (float)attackSamples;

			data[i] = sample * env * r.vol;
		}

		AudioClip clip = AudioClip.Create("ph_" + name, n, 1, SampleRate, false);
		clip.SetData(data, 0);
		return clip;
	}

	/// <summary>
	/// Seamless looping ambient drone used as placeholder music so the Music toggle is testable.
	/// Frequencies are chosen so an integer number of cycles fits the loop length (no click at the seam).
	/// </summary>
	public static AudioClip BuildMusicDrone(bool gameplay)
	{
		const float seconds = 8f;
		int n = Mathf.RoundToInt(seconds * SampleRate);
		float[] data = new float[n];

		float fA = gameplay ? 73.5f : 55f;
		float fB = gameplay ? 110.25f : 82.5f;

		for (int i = 0; i < n; i++)
		{
			float t = i / (float)SampleRate;
			float lfo = 0.65f + 0.35f * Mathf.Sin(2f * Mathf.PI * t / seconds);
			float s = Mathf.Sin(2f * Mathf.PI * fA * t) * 0.6f + Mathf.Sin(2f * Mathf.PI * fB * t) * 0.4f;
			data[i] = s * lfo * 0.35f;
		}

		AudioClip clip = AudioClip.Create(gameplay ? "ph_music_gameplay" : "ph_music_menu", n, 1, SampleRate, false);
		clip.SetData(data, 0);
		return clip;
	}
}
