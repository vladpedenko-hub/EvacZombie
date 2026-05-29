using UnityEngine;
using UnityEngine.UI;

// [WHERE IT LIVES]: On the root prefab of each region visual map (e.g. RegionMap_Nevada)
public class RegionMapVisual : MonoBehaviour
{
	[Header("Level Nodes")]
	public Button[] levelNodes;
	public Image[] lines;
	public Image[] sectors;
	public GameObject bossIcon;

	[Header("Stars Views")]
	public LevelNodeStarsView[] levelStarsViews;
}