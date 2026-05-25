using UnityEngine;

public class SpawnIndicator : MonoBehaviour
{
	[Tooltip("Если true, индикатор исчезнет сам, когда начнется игра (для фазы планирования). Если false, его удалит LevelManager (для волн).")]
	public bool isPlanningIndicator = true;

	private void Update()
	{
		// Удаляем себя только если мы принадлежим фазе планирования, а игра уже началась
		if (isPlanningIndicator && GameManager.Instance.State != GameManager.GameState.Planning)
		{
			Destroy(gameObject);
		}
	}
}