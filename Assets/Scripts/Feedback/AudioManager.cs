using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Global audio: pooled SFX sources + one music source. Created automatically before the first scene
/// (no scene setup needed), survives scene loads.
///
/// Clip lookup order for SfxId.X:
///   1) Resources/Audio/Sfx/X   (drop your real file here, same name as the enum value)
///   2) synthesised placeholder (PlaceholderSfx)
/// Music: Resources/Audio/Music/menu and gameplay, otherwise a placeholder drone.
/// </summary>
public class AudioManager : MonoBehaviour
{
	public static AudioManager Instance { get; private set; }

	/// <summary>True when SFX are enabled — use this to gate AudioSources that live in scenes/prefabs.</summary>
	public static bool SfxAllowed => GameSettings.SfxEnabled;

	/// <summary>Set false to ship without the placeholder music drone.</summary>
	private const bool UsePlaceholderMusic = true;

	private const int PoolSize = 12;
	private const float DefaultMinInterval = 0.03f;
	private const float MusicVolume = 0.35f;

	private AudioSource[] pool;
	private int nextSource;
	private AudioSource musicSource;

	private readonly Dictionary<SfxId, AudioClip> clipCache = new Dictionary<SfxId, AudioClip>();
	private readonly Dictionary<SfxId, float> lastPlayed = new Dictionary<SfxId, float>();
	private string currentMusicKey;

	// Minimum seconds between two plays of the same sound (anti-spam for zombie swarms etc.).
	private static readonly Dictionary<SfxId, float> minInterval = new Dictionary<SfxId, float>
	{
		{ SfxId.ZombieHit, 0.07f },
		{ SfxId.ZombieDeath, 0.09f },
		{ SfxId.ZombieInfect, 0.12f },
		{ SfxId.SoldierShot, 0.05f },
		{ SfxId.HeliGun, 0.09f },
		{ SfxId.BarricadeHit, 0.12f },
		{ SfxId.RescueTick, 0.06f },
		{ SfxId.RescueDeparted, 0.10f },
		{ SfxId.WaveWarning, 0.50f },
		{ SfxId.UiClick, 0.05f },
	};

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ResetStatics() => Instance = null;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Bootstrap()
	{
		if (Instance != null) return;
		var go = new GameObject("[AudioManager]");
		DontDestroyOnLoad(go);
		Instance = go.AddComponent<AudioManager>();
	}

	private void Awake()
	{
		if (Instance != null && Instance != this) { Destroy(gameObject); return; }
		Instance = this;

		pool = new AudioSource[PoolSize];
		for (int i = 0; i < PoolSize; i++)
		{
			var src = gameObject.AddComponent<AudioSource>();
			src.playOnAwake = false;
			src.spatialBlend = 0f; // 2D — top-down game, positional audio not needed
			pool[i] = src;
		}

		musicSource = gameObject.AddComponent<AudioSource>();
		musicSource.playOnAwake = false;
		musicSource.loop = true;
		musicSource.spatialBlend = 0f;
		musicSource.volume = MusicVolume;

		GameSettings.Changed += OnSettingsChanged;
		SceneManager.sceneLoaded += OnSceneLoaded;
	}

	private void Start()
	{
		// First scene's sceneLoaded may have fired before we subscribed — make sure music matches the scene.
		PlayMusicForScene(SceneManager.GetActiveScene().name);
	}

	private void OnDestroy()
	{
		GameSettings.Changed -= OnSettingsChanged;
		SceneManager.sceneLoaded -= OnSceneLoaded;
		if (Instance == this) Instance = null;
	}

	private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => PlayMusicForScene(scene.name);

	private void OnSettingsChanged()
	{
		if (!GameSettings.MusicEnabled)
		{
			musicSource.Pause();
		}
		else if (musicSource.clip != null)
		{
			if (!musicSource.isPlaying) musicSource.Play();
		}
		else
		{
			PlayMusicForScene(SceneManager.GetActiveScene().name);
		}

		if (!GameSettings.SfxEnabled)
		{
			foreach (var s in pool) s.Stop();
		}
	}

	// ---------------------------------------------------------------- SFX

	public static void PlaySfx(SfxId id, float volume = 1f, float pitchVariation = 0.06f)
	{
		if (Instance == null) return;
		Instance.PlaySfxInternal(id, volume, pitchVariation);
	}

	private void PlaySfxInternal(SfxId id, float volume, float pitchVariation)
	{
		if (id == SfxId.None) return;
		if (!GameSettings.SfxEnabled) return;

		float now = Time.unscaledTime;
		float gap = minInterval.TryGetValue(id, out float custom) ? custom : DefaultMinInterval;
		if (lastPlayed.TryGetValue(id, out float last) && now - last < gap) return;
		lastPlayed[id] = now;

		AudioClip clip = ResolveClip(id);
		if (clip == null) return;

		AudioSource src = GetFreeSource();
		src.pitch = 1f + Random.Range(-pitchVariation, pitchVariation);
		src.PlayOneShot(clip, Mathf.Clamp01(volume));
	}

	private AudioSource GetFreeSource()
	{
		// Prefer an idle source; otherwise steal round-robin.
		for (int i = 0; i < PoolSize; i++)
		{
			int idx = (nextSource + i) % PoolSize;
			if (!pool[idx].isPlaying)
			{
				nextSource = (idx + 1) % PoolSize;
				return pool[idx];
			}
		}

		AudioSource stolen = pool[nextSource];
		nextSource = (nextSource + 1) % PoolSize;
		return stolen;
	}

	private AudioClip ResolveClip(SfxId id)
	{
		if (clipCache.TryGetValue(id, out AudioClip cached)) return cached;

		AudioClip clip = Resources.Load<AudioClip>("Audio/Sfx/" + id);
		if (clip == null) clip = PlaceholderSfx.Get(id);

		clipCache[id] = clip; // cache nulls too, so we don't hit Resources every time
		return clip;
	}

	// ---------------------------------------------------------------- Music

	private void PlayMusicForScene(string sceneName)
	{
		string key = sceneName == "MainMenu" ? "menu" : "gameplay";
		if (key == currentMusicKey && musicSource.clip != null)
		{
			ApplyMusicEnabled();
			return;
		}

		currentMusicKey = key;

		AudioClip clip = Resources.Load<AudioClip>("Audio/Music/" + key);
		if (clip == null && UsePlaceholderMusic)
			clip = PlaceholderSfx.BuildMusicDrone(key == "gameplay");

		musicSource.clip = clip;
		ApplyMusicEnabled();
	}

	private void ApplyMusicEnabled()
	{
		if (musicSource.clip == null) return;

		if (GameSettings.MusicEnabled)
		{
			if (!musicSource.isPlaying) musicSource.Play();
		}
		else
		{
			musicSource.Pause();
		}
	}
}
