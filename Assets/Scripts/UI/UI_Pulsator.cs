using UnityEngine;

public class UI_Pulsator : MonoBehaviour
{
	[Header("Настройки Пульсации")]
	public float pulseSpeed = 2f;      // Скорость пульсации
	public float maxScaleMulti = 1.2f; // Максимальный размер (1.2 от базового)
	public float minScaleMulti = 0.9f; // Минимальный размер

	private Vector3 baseScale;

	private void Start()
	{
		// Запоминаем базовый масштаб, настроенный в редакторе
		baseScale = transform.localScale;
	}

	private void Update()
	{
		// Используем Sin для создания плавной волны от -1 до 1
		float sinValue = Mathf.Sin(Time.time * pulseSpeed);

		// Мапим это значение в диапазон от minScale до maxScale
		float t = Mathf.InverseLerp(-1f, 1f, sinValue);
		float currentScaleMulti = Mathf.Lerp(minScaleMulti, maxScaleMulti, t);

		// Применяем новый масштаб к базовому
		transform.localScale = baseScale * currentScaleMulti;
	}
}