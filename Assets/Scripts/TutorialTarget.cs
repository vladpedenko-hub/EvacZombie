using UnityEngine;

[RequireComponent(typeof(RectTransform))]
public class TutorialTarget : MonoBehaviour
{
	[Tooltip("”никальное им€ кнопки дл€ туториала (например: 'Btn_Play' или 'Card_Heli')")]
	public string targetId;

	private void OnEnable()
	{
		if (!string.IsNullOrEmpty(targetId) && TutorialManager.Instance != null)
		{
			TutorialManager.Instance.RegisterTarget(targetId, GetComponent<RectTransform>());
		}
	}

	private void OnDisable()
	{
		if (!string.IsNullOrEmpty(targetId) && TutorialManager.Instance != null)
		{
			TutorialManager.Instance.UnregisterTarget(targetId);
		}
	}
}