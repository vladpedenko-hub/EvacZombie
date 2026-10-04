using UnityEngine;

[CreateAssetMenu(fileName = "NewBuilding", menuName = "ZombieGame/Meta/Building")]
public class BuildingDefinition : ScriptableObject
{
	[Header("Identity")]
	public string id;
	public string displayName;
	[TextArea(2, 4)] public string description;

	[Header("Placement (Base city, canvas pixels)")]
	[Tooltip("Center of the house on the city map, in canvas pixels from the city's center.")]
	public Vector2 cityPosition;
	[Tooltip("Size of the house block on the city map, in canvas pixels.")]
	public Vector2 cityLayoutSize = new Vector2(300f, 300f);
	[Tooltip("Radius of the zombie fog around this house, in canvas pixels. Fades out when restored.")]
	[Min(0f)] public float fogRadius = 420f;

	[Header("Visuals (optional; primitives are used when empty)")]
	[Tooltip("UI prefab shown instead of the ruined primitives. Must have a RectTransform root.")]
	public GameObject ruinedVisual;
	[Tooltip("UI prefab shown instead of the restored primitives. Must have a RectTransform root.")]
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
