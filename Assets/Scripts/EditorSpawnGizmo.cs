using UnityEngine;

// This script draws an editor-only gizmo on every spawn point object
[ExecuteInEditMode]
public class EditorSpawnGizmo : MonoBehaviour
{
	[Header("Gizmo Settings")]
	// Icon file name. Note: the file must be placed in the Assets/Gizmos folder
	public string gizmoIconName = "SkullIcon.png";
	public Color gizmoTintColor = new Color(1f, 0f, 0f, 0.5f); // Red semi-transparent
	public float gizmoSize = 1f;

	// This is called in the Scene view and editor only
	private void OnDrawGizmos()
	{
		// Draw a sphere so the spawn point is visible
		Gizmos.color = gizmoTintColor;
		Gizmos.DrawSphere(transform.position, gizmoSize * 0.5f);

		// --- Optional: draw an icon instead ---
		// 1. Create the Assets/Gizmos folder (required by Unity)
		// 2. Drop the icon image there (e.g. "SkullIcon.png")
		// 3. Uncomment the line below:
		// Gizmos.DrawIcon(transform.position + Vector3.up * gizmoSize, gizmoIconName, true);
	}
}