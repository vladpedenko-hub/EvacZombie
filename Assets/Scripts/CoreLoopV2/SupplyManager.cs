using System;
using System.Collections;
using UnityEngine;

// Core Loop v2 Supply economy (Docs/CORE_LOOP_V2.md Phase 1). Runtime-created persistent
// singleton, same bootstrap pattern as Feedback/AudioManager/VfxManager. Inert (Current stays 0)
// on legacy levels -- every public mutator is a no-op unless the current level is v2.
public class SupplyManager : MonoBehaviour
{
	public static SupplyManager Instance { get; private set; }

	public int Current { get; private set; }
	public int Max { get; private set; }
	public bool IsActive { get; private set; }

	/// <summary>(current, max) whenever Supply changes.</summary>
	public event Action<int, int> OnSupplyChanged;

	/// <summary>Fired with the delta whenever Supply is gained (for the HUD's "+N" fly-in).</summary>
	public event Action<int> OnSupplyGained;

	private Coroutine trickleCoroutine;
	private int pendingRetryBonus = 0;
	private bool fakeDoorAdUsedThisAttempt = false;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ResetStatics() => Instance = null;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Bootstrap()
	{
		if (Instance != null) return;
		var go = new GameObject("[SupplyManager]");
		DontDestroyOnLoad(go);
		Instance = go.AddComponent<SupplyManager>();
	}

	private void Awake()
	{
		if (Instance != null && Instance != this) { Destroy(gameObject); return; }
		Instance = this;
	}

	private void OnDestroy()
	{
		if (Instance == this) Instance = null;
	}

	// Called from LevelManager.LoadLevel() -- the sole initialization entry point, same spot
	// AbilityManager.Instance?.Initialize(data) is already called from.
	public void OnLevelLoaded(LevelData data)
	{
		if (trickleCoroutine != null) { StopCoroutine(trickleCoroutine); trickleCoroutine = null; }

		IsActive = data != null && data.useCoreLoopV2;
		fakeDoorAdUsedThisAttempt = false;

		if (!IsActive)
		{
			Current = 0;
			Max = 0;
			return;
		}

		CoreLoopV2Config cfg = CoreLoopV2Config.Active;
		Max = cfg != null ? cfg.supplyCap : 20;
		int baseStart = data.startingSupply > 0 ? data.startingSupply : (cfg != null ? cfg.startingSupplyDefault : 6);

		Current = Mathf.Clamp(baseStart + pendingRetryBonus, 0, Max);
		pendingRetryBonus = 0;

		OnSupplyChanged?.Invoke(Current, Max);
		trickleCoroutine = StartCoroutine(TrickleRoutine());
	}

	private IEnumerator TrickleRoutine()
	{
		CoreLoopV2Config cfg = CoreLoopV2Config.Active;
		float interval = cfg != null ? cfg.supplyTrickleInterval : 5f;
		int amount = cfg != null ? cfg.supplyTrickleAmount : 1;

		while (IsActive)
		{
			yield return new WaitForSeconds(interval);

			if (!IsActive) yield break;

			// Only trickles during gameplay phases, never during Planning.
			if (GameManager.Instance != null && GameManager.Instance.State != GameManager.GameState.Planning)
				Gain(amount);
		}
	}

	public bool CanAfford(int cost) => !IsActive || cost <= 0 || Current >= cost;

	public bool TrySpend(int cost)
	{
		if (!IsActive || cost <= 0) return true;
		if (Current < cost) return false;

		Current -= cost;
		OnSupplyChanged?.Invoke(Current, Max);
		return true;
	}

	public void Gain(int amount)
	{
		if (!IsActive || amount <= 0) return;

		Current = Mathf.Min(Current + amount, Max);
		OnSupplyChanged?.Invoke(Current, Max);
		OnSupplyGained?.Invoke(amount);
	}

	public void AddWaveClearedBonus()
	{
		CoreLoopV2Config cfg = CoreLoopV2Config.Active;
		Gain(cfg != null ? cfg.waveClearedBonus : 2);
	}

	// Planning fake-door ad (brief §5): one grant per attempt.
	public bool TryUseFakeDoorAd()
	{
		if (!IsActive || fakeDoorAdUsedThisAttempt) return false;

		fakeDoorAdUsedThisAttempt = true;
		CoreLoopV2Config cfg = CoreLoopV2Config.Active;
		Gain(cfg != null ? cfg.fakeDoorAdSupplyGrant : 3);
		return true;
	}

	// Lose-popup fake-door ad retry bonus (brief §5): applied on the next OnLevelLoaded (i.e. the
	// reloaded attempt), then cleared. Survives the scene reload RestartLevel() does because this
	// MonoBehaviour is DontDestroyOnLoad.
	public void QueueRetryBonus()
	{
		CoreLoopV2Config cfg = CoreLoopV2Config.Active;
		pendingRetryBonus = cfg != null ? cfg.loseRetryAdSupplyGrant : 5;
	}
}
