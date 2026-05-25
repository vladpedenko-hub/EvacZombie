using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

public class LevelNodeStarsView : MonoBehaviour
{
	[SerializeField] private Image[] starImages;
	[SerializeField] private Sprite emptyStarSprite;
	[SerializeField] private Sprite filledStarSprite;

	public void SetStarsInstant(int stars)
	{
		stars = Mathf.Clamp(stars, 0, 3);

		if (starImages == null) return;

		for (int i = 0; i < starImages.Length; i++)
		{
			if (starImages[i] == null) continue;

			starImages[i].transform.DOKill();
			starImages[i].transform.localScale = Vector3.one;

			if (i < stars)
			{
				if (filledStarSprite != null)
					starImages[i].sprite = filledStarSprite;
			}
			else
			{
				if (emptyStarSprite != null)
					starImages[i].sprite = emptyStarSprite;
			}
		}
	}

	public Sequence PlayStarUpgradeAnimation(int oldStars, int newStars)
	{
		oldStars = Mathf.Clamp(oldStars, 0, 3);
		newStars = Mathf.Clamp(newStars, 0, 3);

		SetStarsInstant(oldStars);

		Sequence seq = DOTween.Sequence();

		for (int i = oldStars; i < newStars; i++)
		{
			if (i >= starImages.Length || starImages[i] == null) continue;

			Image star = starImages[i];
			star.transform.localScale = Vector3.zero;

			seq.AppendInterval(0.12f);
			seq.AppendCallback(() =>
			{
				if (filledStarSprite != null)
					star.sprite = filledStarSprite;
			});
			seq.Append(star.transform.DOScale(1.2f, 0.18f).SetEase(Ease.OutBack));
			seq.Append(star.transform.DOScale(1f, 0.12f).SetEase(Ease.InOutSine));
			seq.Join(star.transform.DORotate(new Vector3(0f, 0f, 10f), 0.1f).SetLoops(2, LoopType.Yoyo));
		}

		return seq;
	}
}