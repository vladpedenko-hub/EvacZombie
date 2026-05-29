using UnityEngine;

// This script lives on the main camera
public class CameraRaycaster : MonoBehaviour
{
	[SerializeField] private Transform playerTransform; // Reference to the tracked object (e.g. center of the map)
	[SerializeField] private LayerMask buildingLayer;    // Layer to raycast against (should be "Building")

	void Update()
	{
		if (playerTransform == null) return;

		// Cast a ray from camera toward the player
		Vector3 direction = playerTransform.position - transform.position;
		float distance = direction.magnitude;

		// Cast ray. If a building is hit on the Buildings layer...
		if (Physics.Raycast(transform.position, direction, out RaycastHit hit, distance, buildingLayer))
		{
			// Check if the hit object has a BuildingFader
			BuildingFader fader = hit.collider.GetComponent<BuildingFader>();
			if (fader != null)
			{
				fader.FadeOut(); // Make the building transparent
			}
		}
	}
}