using UnityEngine;

[CreateAssetMenu(fileName = "NewBuilding", menuName = "ZombieGame/Meta/Building")]
public class BuildingDefinition : ScriptableObject
{
	[Header("Identity")]
	public string id;
	public string displayName;
	[TextArea(2, 4)] public string description;

	[Header("Placement (MetaCity)")]
	[Tooltip("Position on the city plane (X, Z). Placeholder primitives are placed here.")]
	public Vector2 worldPosition;
	[Tooltip("Radius of the zombie fog zone around this building. Fades out when restored.")]
	[Min(0f)] public float zoneRadius = 6f;

	[Header("Visuals (optional; primitives are used when empty)")]
	public GameObject ruinedVisual;
	public GameObject restoredVisual;
	[Tooltip("Roof color of the primitive restored house.")]
	public Color roofColor = new Color(0.8f, 0.25f, 0.2f);

	[Header("Unlock")]
	[Tooltip("All conditions must be met for the building to become available.")]
	public UnlockCondition[] conditions;

	[Header("Restore")]
	[Min(0)] public int restorationCostPeople = 20;
	public Reward[] rewardsOnRestore;
}
