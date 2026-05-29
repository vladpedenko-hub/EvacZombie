using UnityEngine;
using TMPro;
using DG.Tweening;

// [WHERE IT LIVES]: On the flying text prefab (UI Canvas -> TextMeshPro).
public class FlyingText : MonoBehaviour
{
	[SerializeField] private TextMeshProUGUI textComponent;
	[SerializeField] private float flyDuration = 0.6f;

	private int _amount;

	public void Launch(string text, Vector2 startScreenPos, RectTransform target, int amount)
	{
		_amount = amount;

		if (textComponent != null) textComponent.text = text;
		transform.position = startScreenPos;

		transform.SetAsLastSibling();

		transform.DOMove(target.position, flyDuration).SetEase(Ease.InOutQuad);

		transform.DOScale(Vector3.zero, flyDuration)
			.SetEase(Ease.InBack)
			.OnComplete(OnArrived);
	}

	private void OnArrived()
	{
		GameManager.Instance.OnFlyingTextReached(_amount);
		Destroy(gameObject);
	}
}