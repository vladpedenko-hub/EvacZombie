using UnityEngine;

// Helper for the Physics.*NonAlloc overloads: grows a caller-owned buffer (passed by ref) just
// enough to fit every actual hit, so results are never silently truncated the way a fixed-size
// buffer would be if more colliders/hits exist than it can hold. Steady state (results fit in the
// buffer's current size) allocates nothing; growth only happens on the rare call where more hits
// come back than the buffer currently holds, and the larger size then stays cached in the caller's
// field for subsequent calls.
//
// Buffers are owned by the caller (one field per call site), not shared globally here — some
// callers (e.g. CarController's boarding loop) iterate the result across a yield, during which
// another system must not be able to overwrite the same array out from under it.
public static class PhysicsNonAlloc
{
	public static int OverlapSphere(Vector3 position, float radius, ref Collider[] buffer,
		int layerMask = Physics.AllLayers, QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal)
	{
		int count;
		while ((count = Physics.OverlapSphereNonAlloc(position, radius, buffer, layerMask, queryTriggerInteraction)) == buffer.Length)
		{
			System.Array.Resize(ref buffer, buffer.Length * 2);
		}
		return count;
	}

	public static int Raycast(Vector3 origin, Vector3 direction, float maxDistance, ref RaycastHit[] buffer,
		int layerMask = Physics.DefaultRaycastLayers, QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.UseGlobal)
	{
		int count;
		while ((count = Physics.RaycastNonAlloc(origin, direction, buffer, maxDistance, layerMask, queryTriggerInteraction)) == buffer.Length)
		{
			System.Array.Resize(ref buffer, buffer.Length * 2);
		}
		return count;
	}
}
