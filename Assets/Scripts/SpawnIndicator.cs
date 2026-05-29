using UnityEngine;

public class SpawnIndicator : MonoBehaviour
{
	[Tooltip("If true, the indicator destroys itself when gameplay starts (used for card placement). If false, it is managed by LevelManager (wave indicator).")]
	public bool isPlanningIndicator = true;

	private void Update()
	{
		// Destroy the indicator once planning phase ends and the game has started
		if (isPlanningIndicator && GameManager.Instance.State != GameManager.GameState.Planning)
		{
			Destroy(gameObject);
		}
	}
}