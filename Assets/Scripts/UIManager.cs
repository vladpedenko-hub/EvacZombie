using DG.Tweening;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
	public static UIManager Instance;

	[Header("UI Элементы")]
	[SerializeField] private TextMeshProUGUI timerText;
	[SerializeField] private TextMeshProUGUI manaText;
	[SerializeField] private Image manaFillBar;
	[SerializeField] private ResultPopupUI resultPopup;

	[Header("Счетчик спасенных")]
	[SerializeField] private TextMeshProUGUI ingameRescuedText;
	[SerializeField] private GameObject checkmarkIcon;
	[SerializeField] private Color defaultColor = Color.white;
	[SerializeField] private Color successColor = Color.green;

	[Header("Эффект вылетающих цифр")]
	[SerializeField] private GameObject flyingTextPrefab;
	[SerializeField] private RectTransform counterTarget;

	[Header("Окно Поражения")]
	[SerializeField] private GameObject losePopup;

	[Header("Ночной Эффект")]
	public TextMeshProUGUI centerNightText;
	public AudioSource nightSound;

	private void Awake() => Instance = this;

	private void Start()
	{
		if (EnergyManager.Instance != null)
			EnergyManager.Instance.OnEnergyChanged += UpdateManaUI;

		if (centerNightText != null)
		{
			centerNightText.gameObject.SetActive(false);
			centerNightText.color = new Color(centerNightText.color.r, centerNightText.color.g, centerNightText.color.b, 0);
		}
	}

	private void OnDestroy()
	{
		if (EnergyManager.Instance != null)
			EnergyManager.Instance.OnEnergyChanged -= UpdateManaUI;
	}

	private void UpdateManaUI(float currentEnergy, float maxEnergy)
	{
		if (manaText != null) manaText.text = Mathf.FloorToInt(currentEnergy).ToString();
		if (manaFillBar != null && maxEnergy > 0) manaFillBar.fillAmount = currentEnergy / maxEnergy;
	}

	public void UpdateTimer(float time, bool isPlanning)
	{
		if (isPlanning)
		{
			timerText.text = "ПЛАНИРОВАНИЕ";
			timerText.color = Color.white;
			return;
		}

		int seconds = Mathf.CeilToInt(time);
		timerText.text = seconds.ToString("00");
		if (seconds <= 5) timerText.color = Color.red;
		else timerText.color = Color.white;

		if (seconds <= 0) timerText.text = "НОЧЬ!";
	}

	public void UpdateRescuedCount(int rescued, int required, bool animate = true)
	{
		if (ingameRescuedText != null)
		{
			ingameRescuedText.text = $"{rescued} / {required}";

			if (animate)
			{
				ingameRescuedText.transform.DOKill(true);
				ingameRescuedText.transform.DOPunchScale(Vector3.one * 0.2f, 0.3f, 10, 1f);
			}

			if (rescued >= required)
			{
				ingameRescuedText.color = successColor;
				if (checkmarkIcon != null) checkmarkIcon.SetActive(true);
			}
			else
			{
				ingameRescuedText.color = defaultColor;
				if (checkmarkIcon != null) checkmarkIcon.SetActive(false);
			}
		}
	}

	public void SpawnFlyingText(Vector3 worldPosition, int amount)
	{
		if (flyingTextPrefab == null || counterTarget == null)
		{
			GameManager.Instance.OnFlyingTextReached(amount);
			return;
		}

		Canvas uiCanvas = counterTarget.GetComponentInParent<Canvas>();
		if (uiCanvas == null)
		{
			GameManager.Instance.OnFlyingTextReached(amount);
			return;
		}

		GameObject ftObj = Instantiate(flyingTextPrefab, uiCanvas.transform);
		FlyingText ft = ftObj.GetComponent<FlyingText>();

		if (ft != null)
		{
			Vector2 screenPos = Camera.main.WorldToScreenPoint(worldPosition);
			ft.Launch($"+{amount}", screenPos, counterTarget, amount);
		}
		else
		{
			GameManager.Instance.OnFlyingTextReached(amount);
		}
	}

	public void ShowNightPopup()
	{
		if (nightSound != null) nightSound.Play();
		if (centerNightText != null) StartCoroutine(NightTextRoutine());
	}

	private IEnumerator NightTextRoutine()
	{
		centerNightText.gameObject.SetActive(true);
		Color c = centerNightText.color;

		for (float t = 0; t < 1; t += Time.deltaTime * 2)
		{
			centerNightText.color = new Color(c.r, c.g, c.b, t);
			centerNightText.transform.localScale = Vector3.Lerp(Vector3.one * 0.5f, Vector3.one, t);
			yield return null;
		}

		yield return new WaitForSeconds(1.5f);

		for (float t = 1; t > 0; t -= Time.deltaTime)
		{
			centerNightText.color = new Color(c.r, c.g, c.b, t);
			yield return null;
		}
		centerNightText.gameObject.SetActive(false);
	}

	public void ShowLosePopup()
	{
		if (losePopup != null) losePopup.SetActive(true);
	}

	public void ShowResultPopup(
	int rescuedHumans,
	int rescuedTotal,
	int rescuedScientists,
	CardData reward = null,
	bool isPerfect = false,
	int earnedStars = 0,
	int previousBestStars = 0)
	{
		if (resultPopup != null)
		{
			resultPopup.Show(
				rescuedHumans,
				rescuedTotal,
				rescuedScientists,
				reward,
				isPerfect,
				earnedStars,
				previousBestStars
			);
		}
	}
}