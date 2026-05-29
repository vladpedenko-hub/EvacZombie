using UnityEngine;

[RequireComponent(typeof(RectTransform))]
public class TutorialTarget : MonoBehaviour
{
	[Tooltip("Unique ID used to look up this target (e.g. 'Btn_Play' or 'Card_Heli')")]
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