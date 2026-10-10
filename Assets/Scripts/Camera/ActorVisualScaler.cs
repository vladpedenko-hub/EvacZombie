using UnityEngine;

// Applies CameraReadabilityConfig.actorVisualScaleMultiplier to an actor's root transform
// without changing its gameplay footprint:
//  - BoxCollider.size/center on the SAME object is compensated by the inverse factor
//    (BoxCollider.size is defined in local space and scaled by the transform, so shrinking it
//    by the same factor the transform grows by keeps its world-space footprint identical).
//  - NavMeshAgent.radius/height need no compensation at all -- Unity always treats them as
//    absolute world units, never scaled by the transform (a well-known NavMeshAgent quirk).
//  - Any child transform passed in (e.g. Zombie's "AttractedVisual" bait-attraction hitbox) is
//    compensated the same way: its own localScale is divided by the multiplier, cancelling the
//    bigger inherited parent scale so its world-space size doesn't change either.
// Call once, from Awake(), and call it FIRST -- before any other field that caches
// transform.localScale (e.g. Zombie.initialScale) -- so that cached value already reflects the
// scaled baseline and later reset-to-initialScale calls (pool reuse, respawn) don't un-scale it.
public static class ActorVisualScaler
{
	public static void Apply(Transform root, params Transform[] childrenToKeepUnscaled)
	{
		CameraReadabilityConfig config = CameraReadabilityConfig.Active;
		float multiplier = config != null ? config.actorVisualScaleMultiplier : 1f;
		if (multiplier <= 0f || Mathf.Approximately(multiplier, 1f)) return;

		root.localScale *= multiplier;

		BoxCollider box = root.GetComponent<BoxCollider>();
		if (box != null)
		{
			box.size /= multiplier;
			box.center /= multiplier;
		}

		if (childrenToKeepUnscaled != null)
		{
			foreach (Transform child in childrenToKeepUnscaled)
			{
				if (child != null) child.localScale /= multiplier;
			}
		}
	}
}
