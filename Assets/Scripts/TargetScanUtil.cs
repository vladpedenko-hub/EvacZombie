using System;
using System.Collections.Generic;
using UnityEngine;

// Shared "scan a static AllX roster, keep the single nearest match" loop — the same shape used to
// be hand-copied in EvacueeBase (panic target), Soldier.FindTarget, CombatHelicopter.FindClosestZombie,
// and Zombie's FindClosestVictim/TryFindNearestBarricade/bait search, each with its own
// Vector3.Distance + "if closer, keep it" loop. One implementation here, zero change to which
// target gets picked or how often each caller runs — see Docs/REFACTOR_AUDIT.md, Phase D.
//
// NOT used for the other scan shapes in the codebase (Sniper's multi-criteria threat scoring,
// HelicopterController's "does any zombie exist within radius" early-break check, or the
// "collect every match within radius and act on each" loops in CarController/HelicopterController)
// — those aren't the same "keep the single nearest" algorithm, and forcing them through this
// helper would either change their early-exit performance characteristics or need a much more
// general (and riskier) helper than this mechanical dedup phase calls for.
public static class TargetScanUtil
{
	/// <summary>
	/// Scans <paramref name="candidates"/> for the entry closest to <paramref name="origin"/>.
	/// Null entries are always skipped. <paramref name="maxDistance"/> is the starting/ceiling
	/// distance — a match only replaces the current best if it's strictly closer, matching every
	/// original "d &lt; best" loop this replaces (so a tie keeps whichever candidate was found
	/// first). <paramref name="accept"/>, if given, is an extra per-candidate/per-distance
	/// condition (e.g. Soldier's "not already dead" or Zombie's bait-attract-radius check) that
	/// must also hold for a candidate to be adopted as the new best.
	/// </summary>
	public static T FindNearest<T>(
		IReadOnlyList<T> candidates,
		Vector3 origin,
		float maxDistance,
		Func<T, Vector3> getPosition,
		out float distance,
		Func<T, float, bool> accept = null) where T : class
	{
		T nearest = null;
		float best = maxDistance;

		for (int i = 0; i < candidates.Count; i++)
		{
			T candidate = candidates[i];
			if (candidate == null) continue;

			float d = Vector3.Distance(origin, getPosition(candidate));
			if (d < best && (accept == null || accept(candidate, d)))
			{
				best = d;
				nearest = candidate;
			}
		}

		distance = best;
		return nearest;
	}
}
