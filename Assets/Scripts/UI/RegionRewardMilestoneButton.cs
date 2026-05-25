using UnityEngine;
using UnityEngine.UI;

public class RegionRewardMilestoneButton : MonoBehaviour
{
	[SerializeField] private Button button;

	private MapController mapController;
	private int rewardIndex = -1;

	private void Awake()
	{
		if (button == null)
			button = GetComponent<Button>();

		if (button != null)
		{
			button.onClick.RemoveAllListeners();
			button.onClick.AddListener(OnClicked);
		}
	}

	public void Init(MapController controller, int index)
	{
		mapController = controller;
		rewardIndex = index;
	}

	private void OnClicked()
	{
		if (mapController != null && rewardIndex >= 0)
			mapController.TryClaimRegionReward(rewardIndex);
	}
}