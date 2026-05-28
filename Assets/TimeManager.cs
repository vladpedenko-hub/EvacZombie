using UnityEngine;

public class TimeManager : MonoBehaviour
{
	public static TimeManager Instance;

	[Header("Slo-mo Settings")]
	[Range(0.1f, 1.0f)] public float slowMoScale = 0.3f; // Target slow-motion scale (0.3 = 30% speed)
	public float transitionSpeed = 10f; // Lerp speed between normal and slow-mo

	private float targetTimeScale = 1f;
	private float initialFixedDeltaTime;

	private void Awake()
	{
		Instance = this;
		initialFixedDeltaTime = Time.fixedDeltaTime;
	}

	private void Update()
	{
		// Smoothly approach the target timescale, except during a hard pause
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

	// Instantly reset time to normal without lerping
	public void ResetTimeInstant()
	{
		targetTimeScale = 1f;
		Time.timeScale = 1f;
		Time.fixedDeltaTime = initialFixedDeltaTime;
	}

	// Full pause — used by the tutorial and LevelUpScreen
	// Sets targetTimeScale=0 so Update() does not restore timeScale
	public void PauseTime()
	{
		targetTimeScale = 0f;
		Time.timeScale = 0f;
		Time.fixedDeltaTime = 0f;
	}

	// Resume after pause — instant return to 1
	public void ResumeTime()
	{
		targetTimeScale = 1f;
		Time.timeScale = 1f;
		Time.fixedDeltaTime = initialFixedDeltaTime;
	}
}