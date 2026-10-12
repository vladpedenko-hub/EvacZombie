using UnityEngine;

// Shared helpers for the Core Loop v2 vertical slice (Docs/CORE_LOOP_V2.md). Every v2-only system
// checks CoreLoopV2.IsActive before doing anything, and every legacy branch in the six touched
// files stays a literal unmoved code path guarded by `if (!CoreLoopV2.IsActive)`.
public static class CoreLoopV2
{
	public static bool IsActive =>
		LevelManager.Instance != null &&
		LevelManager.Instance.currentData != null &&
		LevelManager.Instance.currentData.useCoreLoopV2;

	// Nearest active transport "Human/Scientist" rescue targets can be redirected to: Helicopter,
	// Car, CombatHelicopter (including a Last Transport, which is just a HelicopterController with
	// ConfigureAsLastTransport applied) or null if none exist. Reads CarController/CombatHelicopter
	// via FindObjectsByType rather than editing those files (only HelicopterController is on the
	// brief's protected-touch list).
	public static Transform FindNearestActiveTransport(Vector3 point)
	{
		Transform best = null;
		float bestSqrDist = float.MaxValue;

		void Consider(Transform t)
		{
			if (t == null) return;
			float d = (t.position - point).sqrMagnitude;
			if (d < bestSqrDist) { bestSqrDist = d; best = t; }
		}

		foreach (var h in Object.FindObjectsByType<HelicopterController>(FindObjectsSortMode.None)) Consider(h.transform);
		foreach (var c in Object.FindObjectsByType<CarController>(FindObjectsSortMode.None)) Consider(c.transform);
		foreach (var ch in Object.FindObjectsByType<CombatHelicopter>(FindObjectsSortMode.None)) Consider(ch.transform);

		return best;
	}
}
