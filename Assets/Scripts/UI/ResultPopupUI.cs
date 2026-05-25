using DG.Tweening;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class ResultPopupUI : MonoBehaviour
{
	[Header("Тексты результатов")]
	[SerializeField] private TextMeshProUGUI totalResultText;
	[SerializeField] private TextMeshProUGUI humansResultText;
	[SerializeField] private TextMeshProUGUI scientistsResultText;

	[Header("Идеальное прохождение")]
	[Tooltip("Перетащи сюда UI-объект плашки ИДЕАЛЬНО! (Сделай его выключенным по умолчанию)")]
	public GameObject perfectBadge;

	[Header("Звезды результата")]
	[SerializeField] private Image[] resultStars;
	[SerializeField] private Sprite emptyStarSprite;
	[SerializeField] private Sprite filledStarSprite;
	[SerializeField] private float starAppearDelay = 0.18f;

	[Header("Кнопки")]
	[SerializeField] private Transform btnX2;
	[SerializeField] private GameObject btnNoThanks;

	[Header("Настройки анимаций")]
	[SerializeField] private float noThanksDelay = 1.5f;
	[SerializeField] private float pulseSpeed = 5f;
	[SerializeField] private float pulseMagnitude = 0.05f;

	[Header("Окно Лутбокса (Награда)")]
	[SerializeField] private GameObject rewardPanel;
	[SerializeField] private Image rewardCardIcon;
	[SerializeField] private TextMeshProUGUI rewardCardName;

	private Vector3 initialX2Scale;
	private Sequence starsSequence;

	private int shownEarnedStars;
	private int shownPreviousStars;

	private void Awake()
	{
		if (btnX2 != null)
			initialX2Scale = btnX2.localScale;
	}

	public void Show(
		int rescuedHumans,
		int rescuedTotal,
		int rescuedScientists,
		CardData rewardCard,
		bool isPerfect = false,
		int earnedStars = 0,
		int previousBestStars = 0)
	{
		gameObject.SetActive(true);

		shownEarnedStars = Mathf.Clamp(earnedStars, 0, 3);
		shownPreviousStars = Mathf.Clamp(previousBestStars, 0, 3);

		if (totalResultText != null)
			totalResultText.text = $"ВСЕГО СПАСЕНО:\n{rescuedTotal}";

		if (humansResultText != null)
			humansResultText.text = $"ЛЮДИ:\n{rescuedHumans}";

		bool levelHasScientists = GameManager.Instance != null && GameManager.Instance.totalScientists > 0;

		if (scientistsResultText != null)
		{
			scientistsResultText.gameObject.SetActive(levelHasScientists);

			if (levelHasScientists)
				scientistsResultText.text = $"УЧЁНЫЕ:\n{rescuedScientists}";
		}

		if (perfectBadge != null)
		{
			perfectBadge.SetActive(false);
			perfectBadge.transform.localScale = Vector3.zero;
		}

		if (btnNoThanks != null)
		{
			btnNoThanks.SetActive(false);
			CancelInvoke(nameof(ShowNoThanksButton));
			Invoke(nameof(ShowNoThanksButton), noThanksDelay);
		}

		ResetStarsVisual();
		PlayStarsAnimation();

		if (isPerfect && perfectBadge != null)
		{
			perfectBadge.SetActive(true);
			perfectBadge.transform.DOScale(Vector3.one, 0.5f)
				.SetEase(Ease.OutBack)
				.SetDelay(0.3f);
		}

		if (rewardCard != null && rewardPanel != null)
		{
			rewardPanel.SetActive(true);

			if (rewardCardIcon != null)
				rewardCardIcon.sprite = rewardCard.icon;

			bool isNew = true;
			if (PlayerProfile.Instance != null)
			{
				var progress = PlayerProfile.Instance.ownedCardsProgress.Find(p => p.cardId == rewardCard.name);
				if (progress != null && (progress.currentLevel > 1 || progress.collectedShards > 0))
				{
					isNew = false;
				}
			}

			if (rewardCardName != null)
			{
				if (isNew)
					rewardCardName.text = $"НОВАЯ КАРТА!\n<color=yellow>{rewardCard.cardName}</color>";
				else
					rewardCardName.text = $"ОСКОЛОК!\n<color=orange>{rewardCard.cardName}</color>";
			}

			StartCoroutine(AnimateRewardPopup());
		}
		else if (rewardPanel != null)
		{
			rewardPanel.SetActive(false);
		}
	}

	private void ResetStarsVisual()
	{
		if (starsSequence != null && starsSequence.IsActive())
			starsSequence.Kill();

		if (resultStars == null) return;

		for (int i = 0; i < resultStars.Length; i++)
		{
			if (resultStars[i] == null) continue;

			resultStars[i].transform.DOKill();
			Color c = resultStars[i].color;
			c.a = 1f;
			resultStars[i].color = c;

			if (i < shownPreviousStars)
			{
				if (filledStarSprite != null)
					resultStars[i].sprite = filledStarSprite;

				resultStars[i].transform.localScale = Vector3.one;
			}
			else
			{
				if (emptyStarSprite != null)
					resultStars[i].sprite = emptyStarSprite;

				resultStars[i].transform.localScale = Vector3.one * 0.8f;
			}
		}
	}

	private void PlayStarsAnimation()
	{
		if (resultStars == null || resultStars.Length == 0) return;

		starsSequence = DOTween.Sequence();

		for (int i = 0; i < resultStars.Length; i++)
		{
			if (resultStars[i] == null) continue;

			int starIndex = i;

			if (starIndex < shownPreviousStars)
			{
				resultStars[starIndex].transform.localScale = Vector3.one;
				continue;
			}

			if (starIndex < shownEarnedStars)
			{
				resultStars[starIndex].transform.localScale = Vector3.zero;

				starsSequence.AppendInterval(starAppearDelay);
				starsSequence.AppendCallback(() =>
				{
					if (resultStars[starIndex] == null) return;
					if (filledStarSprite != null)
						resultStars[starIndex].sprite = filledStarSprite;
				});

				starsSequence.Append(resultStars[starIndex].transform.DOScale(1.2f, 0.18f).SetEase(Ease.OutBack));
				starsSequence.Append(resultStars[starIndex].transform.DOScale(1f, 0.12f).SetEase(Ease.InOutSine));
				starsSequence.Join(resultStars[starIndex].transform.DORotate(new Vector3(0f, 0f, 12f), 0.1f).SetLoops(2, LoopType.Yoyo));
			}
			else
			{
				if (emptyStarSprite != null)
					resultStars[starIndex].sprite = emptyStarSprite;

				resultStars[starIndex].transform.localScale = Vector3.one * 0.8f;
			}
		}
	}

	private IEnumerator AnimateRewardPopup()
	{
		if (rewardPanel == null) yield break;

		rewardPanel.transform.localScale = Vector3.zero;
		float time = 0f;
		float duration = 0.5f;

		while (time < duration)
		{
			time += Time.deltaTime;
			float t = time / duration;
			float scale = Mathf.LerpUnclamped(0f, 1.2f, t) - (t > 0.8f ? (t - 0.8f) * 1f : 0f);
			rewardPanel.transform.localScale = new Vector3(scale, scale, 1f);
			yield return null;
		}

		rewardPanel.transform.localScale = Vector3.one;
	}

	private void ShowNoThanksButton()
	{
		if (btnNoThanks != null)
			btnNoThanks.SetActive(true);
	}

	private void Update()
	{
		if (btnX2 != null)
		{
			float multiplier = 1f + Mathf.Sin(Time.time * pulseSpeed) * pulseMagnitude;
			btnX2.localScale = initialX2Scale * multiplier;
		}
	}

	public void FinishLevelAndGoToMenu()
	{
		if (PlayerProfile.Instance != null)
		{
			PlayerProfile.Instance.CompleteCurrentLevel();
		}

		SceneManager.LoadScene("MainMenu");
	}

	private void OnDisable()
	{
		if (starsSequence != null && starsSequence.IsActive())
			starsSequence.Kill();

		transform.DOKill();
	}
}