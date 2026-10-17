using UnityEngine;

// Data marker dropped on a road piece (procedural sandbox street/alley/ring/intersection cube,
// or a hand-built level's RoadZone) so barricade placement (InputManager.cs) can snap to the
// street's actual centerline/orientation instead of guessing from the hit collider's transform.
public class StreetSegment : MonoBehaviour
{
	[Tooltip("World XZ unit vector along the street (the direction of travel), not the cross-street/width axis.")]
	public Vector3 Direction = Vector3.forward;

	[Tooltip("Width of the street at this segment, in world units (narrower for alleys).")]
	public float Width = 1f;

	[Tooltip("True for an intersection piece -- there is no single travel direction, so barricade placement here is treated as invalid.")]
	public bool IsIntersection = false;
}
