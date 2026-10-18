using UnityEngine;
using UnityEngine.AI;

// feature/bomb-combo (step 3/4): shared "street-reach" NavMesh check used by both the real
// explosion (Bomb.cs) and the drag-time friendly-fire preview (InputManager.cs), so the preview
// always agrees with what the blast will actually do.
public static class BombBlastUtil
{
	public const float SnapMaxDistance = 2f;

	/// <summary>Snaps a raw point to the nearest NavMesh point within SnapMaxDistance. Used both to
	/// reject an off-mesh Bomb placement and to find the epicenter for the street-reach check.</summary>
	public static bool TrySampleEpicenter(Vector3 point, out Vector3 sampled)
	{
		if (NavMesh.SamplePosition(point, out NavMeshHit hit, SnapMaxDistance, NavMesh.AllAreas))
		{
			sampled = hit.position;
			return true;
		}

		sampled = point;
		return false;
	}

	/// <summary>True if the blast reaches targetPos from the epicenter without being blocked by a
	/// building wall (NavMesh.Raycast). Falls back to true (plain distance test, already satisfied
	/// by the overlap-sphere radius check) whenever the epicenter wasn't sampled, or targetPos has
	/// no nearby NavMesh point of its own to raycast to.</summary>
	public static bool HasStreetReach(Vector3 sampledEpicenter, bool epicenterSampled, Vector3 targetPos)
	{
		if (!epicenterSampled) return true;

		if (!NavMesh.SamplePosition(targetPos, out NavMeshHit targetHit, SnapMaxDistance, NavMesh.AllAreas))
			return true;

		return !NavMesh.Raycast(sampledEpicenter, targetHit.position, out _, NavMesh.AllAreas);
	}
}
