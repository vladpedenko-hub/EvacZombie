using UnityEngine;
using UnityEngine.UI;

// [ГДЕ ВИСИТ]: На корневом объекте префаба визуальной карты региона (например, RegionMap_Nevada)
public class RegionMapVisual : MonoBehaviour
{
	[Header("Визуал Региона")]
	public Button[] levelNodes;
	public Image[] lines;
	public Image[] sectors;
	public GameObject bossIcon;

	[Header("Звезды уровней")]
	public LevelNodeStarsView[] levelStarsViews;
}