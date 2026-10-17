using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

// Manually placed invisible volume marking a real street for a level.
// Drop this on an empty GameObject, size the BoxCollider to hug the road, done.
// The BoxCollider is the single source of truth for shape — its size/center are
// mirrored onto the NavMeshModifierVolume automatically so there is only one box
// to position in the scene view.
[RequireComponent(typeof(BoxCollider))]
[RequireComponent(typeof(NavMeshModifierVolume))]
[RequireComponent(typeof(StreetSegment))]
public class RoadZone : MonoBehaviour
{
	[Tooltip("NavMesh Area override for the NavMeshModifierVolume. Create a custom \"Road\" area " +
		"in Window > AI > Navigation > Areas and assign it here once it exists; defaults to \"Default\" until then.")]
	[SerializeField] private string navMeshAreaOverride = "Default";

	private void Reset()
	{
		gameObject.tag = "Road";
		SyncComponents();
	}

	private void OnValidate()
	{
		SyncComponents();
	}

	private void SyncComponents()
	{
		BoxCollider box = GetComponent<BoxCollider>();
		box.isTrigger = true;

		NavMeshModifierVolume modifierVolume = GetComponent<NavMeshModifierVolume>();
		modifierVolume.size = box.size;
		modifierVolume.center = box.center;

		int areaIndex = NavMesh.GetAreaFromName(navMeshAreaOverride);
		if (areaIndex >= 0)
			modifierVolume.area = areaIndex;

		// Barricade snapping (InputManager.cs) needs the street's travel direction/width -- derive
		// it from the same box the designer already sized to hug the road, so there is still only
		// one box to position in the scene view.
		StreetSegment segment = GetComponent<StreetSegment>();
		if (box.size.x >= box.size.z)
		{
			segment.Direction = Vector3.right;
			segment.Width = box.size.z;
		}
		else
		{
			segment.Direction = Vector3.forward;
			segment.Width = box.size.x;
		}
		segment.IsIntersection = false;
	}
}
