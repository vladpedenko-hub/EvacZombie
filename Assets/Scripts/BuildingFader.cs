using UnityEngine;

// ЭТОТ СКРИПТ ВЕШАЕТСЯ НА ДОМ (ЗДАНИЕ)
// У здания должен быть коллайдер (BoxCollider) и MeshRenderer.
// Для работы прозрачности материал здания должен поддерживать Rendering Mode: Transparent или Fade.
public class BuildingFader : MonoBehaviour
{
	[SerializeField] private float transparentAlpha = 0.3f; // Насколько прозрачным станет дом (0 - невидим, 1 - плотный)
	[SerializeField] private float fadeSpeed = 5f;          // Скорость ухода в прозрачность

	private MeshRenderer meshRenderer;
	private Color originalColor;
	private Color targetColor;
	private bool isBlocking = false;

	void Start()
	{
		meshRenderer = GetComponent<MeshRenderer>();
		if (meshRenderer != null)
		{
			originalColor = meshRenderer.material.color;
			targetColor = originalColor;
		}
	}

	void Update()
	{
		if (meshRenderer == null) return;

		// Плавно меняем цвет материала к целевому (прозрачному или обычному)
		meshRenderer.material.color = Color.Lerp(meshRenderer.material.color, targetColor, Time.deltaTime * fadeSpeed);

		// Сбрасываем флаг каждый кадр, камера должна подтверждать его заново
		isBlocking = false;
		targetColor = originalColor;
	}

	// Этот метод будет вызывать скрипт камеры, если луч попал в этот дом
	public void FadeOut()
	{
		isBlocking = true;
		targetColor = new Color(originalColor.r, originalColor.g, originalColor.b, transparentAlpha);
	}
}