using UnityEngine;

public class TimeManager : MonoBehaviour
{
	public static TimeManager Instance;

	[Header("��������� Slo-mo")]
	[Range(0.1f, 1.0f)] public float slowMoScale = 0.3f; // ��������� ������ ��������� (0.3 = 30% ��������)
	public float transitionSpeed = 10f; // �������� ����� � ������ �� ����-��

	private float targetTimeScale = 1f;
	private float initialFixedDeltaTime;

	private void Awake()
	{
		Instance = this;
		initialFixedDeltaTime = Time.fixedDeltaTime;
	}

	private void Update()
	{
		// ������ ������ �������� ������� ��� ������� "��������"
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

	// �� ������ ���������� ������ ��� �����
	public void ResetTimeInstant()
	{
		targetTimeScale = 1f;
		Time.timeScale = 1f;
		Time.fixedDeltaTime = initialFixedDeltaTime;
	}

	// Полная пауза — используется туториалом и LevelUpScreen
	// Выставляет targetTimeScale=0, чтобы Update() не восстанавливал timeScale обратно
	public void PauseTime()
	{
		targetTimeScale = 0f;
		Time.timeScale = 0f;
		Time.fixedDeltaTime = 0f;
	}

	// Возобновление после паузы — мгновенный возврат к 1
	public void ResumeTime()
	{
		targetTimeScale = 1f;
		Time.timeScale = 1f;
		Time.fixedDeltaTime = initialFixedDeltaTime;
	}
}