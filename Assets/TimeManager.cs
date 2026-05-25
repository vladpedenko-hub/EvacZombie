using UnityEngine;

public class TimeManager : MonoBehaviour
{
	public static TimeManager Instance;

	[Header("Настройки Slo-mo")]
	[Range(0.1f, 1.0f)] public float slowMoScale = 0.3f; // Насколько сильно замедляем (0.3 = 30% скорости)
	public float transitionSpeed = 10f; // Скорость входа и выхода из слоу-мо

	private float targetTimeScale = 1f;
	private float initialFixedDeltaTime;

	private void Awake()
	{
		Instance = this;
		initialFixedDeltaTime = Time.fixedDeltaTime;
	}

	private void Update()
	{
		// Плавно меняем скорость времени для эффекта "вязкости"
		if (Mathf.Abs(Time.timeScale - targetTimeScale) > 0.01f)
		{
			Time.timeScale = Mathf.Lerp(Time.timeScale, targetTimeScale, Time.unscaledDeltaTime * transitionSpeed);
			Time.fixedDeltaTime = initialFixedDeltaTime * Time.timeScale;
		}
	}

	public void StartSlowMo()
	{
		targetTimeScale = slowMoScale;
	}

	public void StopSlowMo()
	{
		targetTimeScale = 1f;
	}

	// На случай завершения уровня или паузы
	public void ResetTimeInstant()
	{
		targetTimeScale = 1f;
		Time.timeScale = 1f;
		Time.fixedDeltaTime = initialFixedDeltaTime;
	}
}