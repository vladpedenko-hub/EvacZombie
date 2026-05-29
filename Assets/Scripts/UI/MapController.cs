using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

// [WHERE IT LIVES]: On the MapPanel object in the scene.
public class MapController : MonoBehaviour
{
	[Header("UI References")]
	[SerializeField] private RectTransform mapSpawnContainer;
	[SerializeField] private RectTransform playerToken;
	[SerializeField] private ParticleSystem confettiFX;
	[SerializeField] private LevelMissionPopupUI missionPopup;

	[Header("Trophy Road UI")]
	[SerializeField] private Image regionProgressFill;
	[SerializeField] private RectTransform[] rewardMilestones;
	[SerializeField] private GameObject[] rewardClaimedMarks;
	[SerializeField] private GameObject[] rewardReadyMarks;

	[Header("Milestone Offset")]
	[SerializeField] private float milestoneYOffset = 0f;

	[Header("Colors and Token")]
	[SerializeField] private Color lockedColor = new Color(0.5f, 0.5f, 0.5f);
	[SerializeField] private Color currentColor = new Color(1f, 0.8f, 0f);
	[SerializeField] private Color completedColor = new Color(0.2f, 0.8f, 0.2f);
	[SerializeField] private Vector3 tokenOffset = new Vector3(0, 150f, 0);
	[SerializeField] private float animDuration = 0.5f;

	private CanvasGroup canvasGroup;
	private RegionMapVisual currentVisualMap;
	private GameObject currentSpawnedPrefab;
	private Dictionary<Image, Vector3> initialSectorScales = new Dictionary<Image, Vector3>();

	private int previewRegionIndex = -1;

	private void Start()
	{
		canvasGroup = GetComponent<CanvasGroup>();
		if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();

		BindRewardMilestones();

		if (PlayerProfile.Instance != null)
			previewRegionIndex = PlayerProfile.Instance.currentRegionIndex;

		RefreshMap();
	}

	private void BindRewardMilestones()
	{
		if (rewardMilestones == null) return;

		for (int i = 0; i < rewardMilestones.Length; i++)
		{
			if (rewardMilestones[i] == null) continue;

			RegionRewardMilestoneButton milestoneButton =
				rewardMilestones[i].GetComponent<RegionRewardMilestoneButton>();

			if (milestoneButton == null)
				milestoneButton = rewardMilestones[i].gameObject.AddComponent<RegionRewardMilestoneButton>();

			milestoneButton.Init(this, i);
		}
	}

	public void RefreshMap()
	{
		if (PlayerProfile.Instance == null || PlayerProfile.Instance.allRegions.Count == 0) return;

		bool animateRegion = PlayerProfile.Instance.hasPendingRegionAnimation;
		int realRegionIndex = PlayerProfile.Instance.currentRegionIndex;

		if (previewRegionIndex < 0)
			previewRegionIndex = realRegionIndex;

		int regionIdx = animateRegion ? Mathf.Max(0, realRegionIndex - 1) : previewRegionIndex;
		regionIdx = Mathf.Clamp(regionIdx, 0, PlayerProfile.Instance.allRegions.Count - 1);

		RegionConfig regionToDisplay = PlayerProfile.Instance.allRegions[regionIdx];
		SpawnOrUpdateRegionVisual(regionToDisplay);

		if (currentVisualMap == null) return;

		bool isUnlockedRegion = regionIdx <= realRegionIndex;

		int actualLevel = isUnlockedRegion && regionIdx == realRegionIndex
			? PlayerProfile.Instance.currentLevelIndex
			: 0;

		bool animateStep = isUnlockedRegion &&
						   regionIdx == realRegionIndex &&
						   PlayerProfile.Instance.hasPendingMapAnimation;

		int visualLevel = animateStep ? Mathf.Max(0, actualLevel - 1) : actualLevel;
		if (animateRegion) visualLevel = 5;

		UpdateMapVisualState(visualLevel, isUnlockedRegion);
		UpdateAllNodeStars(regionIdx, isUnlockedRegion);
		UpdateRegionProgress(regionIdx, false, isUnlockedRegion);

		if (animateRegion)
			StartCoroutine(RegionTransitionRoutine());
		else if (animateStep && actualLevel <= currentVisualMap.levelNodes.Length)
			StartCoroutine(AnimateProgressRoutine(regionIdx, visualLevel, actualLevel));
		else if (isUnlockedRegion && regionIdx == realRegionIndex && PlayerProfile.Instance.hasPendingStarAnimation)
			StartCoroutine(PlayPendingStarAnimationOnly(regionIdx));
	}

	private void UpdateMapVisualState(int visualLevel, bool isUnlockedRegion)
	{
		for (int i = 0; i < currentVisualMap.levelNodes.Length; i++)
		{
			Color stateColor = isUnlockedRegion ? GetColorForState(i, visualLevel) : lockedColor;

			if (currentVisualMap.levelNodes[i] != null)
			{
				Image nodeImage = currentVisualMap.levelNodes[i].GetComponent<Image>();
				if (nodeImage != null)
					nodeImage.color = stateColor;
			}

			if (i < currentVisualMap.sectors.Length && currentVisualMap.sectors[i] != null)
			{
				Image s = currentVisualMap.sectors[i];
				s.color = stateColor;
				if (initialSectorScales.ContainsKey(s)) s.transform.localScale = initialSectorScales[s];
			}
		}

		for (int i = 0; i < currentVisualMap.lines.Length; i++)
		{
			if (currentVisualMap.lines[i] == null) continue;
			currentVisualMap.lines[i].color = (isUnlockedRegion && i < visualLevel) ? completedColor : lockedColor;
		}

		if (currentVisualMap.bossIcon != null)
		{
			Image bossIconImage = currentVisualMap.bossIcon.GetComponent<Image>();
			if (bossIconImage != null)
				bossIconImage.color = isUnlockedRegion ? Color.white : new Color(0.45f, 0.45f, 0.45f, 1f);
		}

		if (playerToken != null)
		{
			playerToken.gameObject.SetActive(isUnlockedRegion);

			if (isUnlockedRegion && visualLevel < currentVisualMap.levelNodes.Length)
				playerToken.position = currentVisualMap.levelNodes[visualLevel].transform.position + tokenOffset;
		}
	}

	private void SpawnOrUpdateRegionVisual(RegionConfig config)
	{
		if (currentSpawnedPrefab != null && currentSpawnedPrefab.name == config.regionUIPrefab.name + "(Clone)")
			return;

		if (currentSpawnedPrefab != null)
			Destroy(currentSpawnedPrefab);

		currentSpawnedPrefab = Instantiate(config.regionUIPrefab, mapSpawnContainer);
		currentVisualMap = currentSpawnedPrefab.GetComponent<RegionMapVisual>();

		initialSectorScales.Clear();
		foreach (var s in currentVisualMap.sectors)
		{
			if (s != null && !initialSectorScales.ContainsKey(s))
				initialSectorScales.Add(s, s.transform.localScale);
		}

		for (int i = 0; i < currentVisualMap.levelNodes.Length; i++)
		{
			if (currentVisualMap.levelNodes[i] == null) continue;

			int index = i;
			currentVisualMap.levelNodes[i].onClick.RemoveAllListeners();
			currentVisualMap.levelNodes[i].onClick.AddListener(() => OnNodeClicked(index));
		}
	}

	private void UpdateAllNodeStars(int regionIndex, bool isUnlockedRegion)
	{
		if (currentVisualMap == null || currentVisualMap.levelStarsViews == null) return;
		if (PlayerProfile.Instance == null) return;

		RegionConfig region = PlayerProfile.Instance.allRegions[regionIndex];
		if (region == null || region.levels == null) return;

		bool hasPending = isUnlockedRegion &&
						  regionIndex == PlayerProfile.Instance.currentRegionIndex &&
						  PlayerProfile.Instance.hasPendingStarAnimation;

		string pendingLevelId = PlayerProfile.Instance.pendingStarLevelId;
		int pendingOldStars = PlayerProfile.Instance.pendingStarOldValue;

		for (int i = 0; i < currentVisualMap.levelStarsViews.Length; i++)
		{
			if (currentVisualMap.levelStarsViews[i] == null) continue;

			if (!isUnlockedRegion)
			{
				currentVisualMap.levelStarsViews[i].SetStarsInstant(0);
				continue;
			}

			if (i >= region.levels.Count || region.levels[i] == null)
			{
				currentVisualMap.levelStarsViews[i].SetStarsInstant(0);
				continue;
			}

			LevelData levelData = region.levels[i];
			int starsToShow = PlayerProfile.Instance.GetSavedStarsForLevel(levelData);

			if (hasPending && levelData.name == pendingLevelId)
				starsToShow = pendingOldStars;

			currentVisualMap.levelStarsViews[i].SetStarsInstant(starsToShow);
		}
	}

	private void UpdateRegionProgress(int regionIndex, bool animate, bool isUnlockedRegion)
	{
		if (PlayerProfile.Instance == null) return;
		if (regionIndex < 0 || regionIndex >= PlayerProfile.Instance.allRegions.Count) return;

		RegionConfig region = PlayerProfile.Instance.allRegions[regionIndex];
		if (region == null) return;

		float progress01 = isUnlockedRegion ? PlayerProfile.Instance.GetRegionProgress01(regionIndex) : 0f;

		if (regionProgressFill != null)
		{
			regionProgressFill.DOKill();
			if (animate)
				regionProgressFill.DOFillAmount(progress01, 0.6f).SetEase(Ease.OutCubic);
			else
				regionProgressFill.fillAmount = progress01;
		}

		UpdateRewardMilestonePositions(region);

		if (region.trophyRoadRewards != null)
		{
			for (int i = 0; i < region.trophyRoadRewards.Length; i++)
			{
				bool reached = isUnlockedRegion && progress01 >= region.trophyRoadRewards[i].progressThreshold;
				bool claimed = isUnlockedRegion && PlayerProfile.Instance.IsRegionRewardClaimed(regionIndex, i);

				if (i < rewardClaimedMarks.Length && rewardClaimedMarks[i] != null)
					rewardClaimedMarks[i].SetActive(claimed);

				if (i < rewardReadyMarks.Length && rewardReadyMarks[i] != null)
					rewardReadyMarks[i].SetActive(reached && !claimed);

				if (i < rewardMilestones.Length && rewardMilestones[i] != null)
				{
					Button btn = rewardMilestones[i].GetComponent<Button>();
					if (btn != null)
						btn.interactable = reached && !claimed;
				}

				CanvasGroup cg = rewardMilestones[i] != null ? rewardMilestones[i].GetComponent<CanvasGroup>() : null;
				if (cg == null && rewardMilestones[i] != null)
					cg = rewardMilestones[i].gameObject.AddComponent<CanvasGroup>();

				if (cg != null)
					cg.alpha = isUnlockedRegion ? 1f : 0.45f;
			}
		}
	}

	private void UpdateRewardMilestonePositions(RegionConfig region)
	{
		if (region == null || region.trophyRoadRewards == null) return;
		if (regionProgressFill == null) return;
		if (rewardMilestones == null) return;

		RectTransform fillRect = regionProgressFill.GetComponent<RectTransform>();
		if (fillRect == null) return;

		float barWidth = fillRect.rect.width;
		float startX = -barWidth * 0.5f;

		for (int i = 0; i < rewardMilestones.Length; i++)
		{
			if (rewardMilestones[i] == null) continue;
			if (i >= region.trophyRoadRewards.Length) continue;

			float threshold = Mathf.Clamp01(region.trophyRoadRewards[i].progressThreshold);
			float posX = startX + (barWidth * threshold);

			Vector2 anchoredPos = rewardMilestones[i].anchoredPosition;
			anchoredPos.x = posX;
			anchoredPos.y = milestoneYOffset;
			rewardMilestones[i].anchoredPosition = anchoredPos;
		}
	}

	private Color GetColorForState(int index, int visualLevel)
	{
		if (index < visualLevel) return completedColor;
		if (index == visualLevel) return currentColor;
		return lockedColor;
	}

	private IEnumerator AnimateProgressRoutine(int regionIdx, int fromIndex, int toIndex)
	{
		canvasGroup.blocksRaycasts = false;
		MainMenuSwipeController.IsSwipeLocked = true;
		yield return new WaitForSeconds(0.3f);

		if (fromIndex < currentVisualMap.levelNodes.Length)
		{
			Image img = currentVisualMap.levelNodes[fromIndex].GetComponent<Image>();
			if (img != null) img.DOColor(completedColor, animDuration);
		}

		if (fromIndex < currentVisualMap.sectors.Length)
			currentVisualMap.sectors[fromIndex].DOColor(completedColor, animDuration);

		if (fromIndex < currentVisualMap.lines.Length && currentVisualMap.lines[fromIndex] != null)
		{
			currentVisualMap.lines[fromIndex].DOColor(completedColor, animDuration);
			currentVisualMap.lines[fromIndex].transform.DOPunchScale(new Vector3(0.1f, 0.1f, 0), animDuration, 1);
		}

		yield return new WaitForSeconds(animDuration);

		if (toIndex < currentVisualMap.levelNodes.Length)
		{
			if (playerToken != null)
				playerToken.DOJump(currentVisualMap.levelNodes[toIndex].transform.position + tokenOffset, 70f, 1, 0.6f).SetEase(Ease.OutQuad);

			yield return new WaitForSeconds(0.2f);

			Image img = currentVisualMap.levelNodes[toIndex].GetComponent<Image>();
			if (img != null) img.DOColor(currentColor, animDuration);

			if (toIndex < currentVisualMap.sectors.Length)
			{
				Image s = currentVisualMap.sectors[toIndex];
				s.DOColor(currentColor, animDuration);

				Vector3 orig = initialSectorScales.ContainsKey(s) ? initialSectorScales[s] : Vector3.one;
				s.transform.localScale = orig * 0.8f;
				s.transform.DOScale(orig, animDuration).SetEase(Ease.OutBack);
			}
		}

		yield return new WaitForSeconds(0.45f);

		if (PlayerProfile.Instance.hasPendingStarAnimation)
			yield return StartCoroutine(PlayPendingStarAnimationOnly(regionIdx, true));
		else
			UpdateRegionProgress(regionIdx, true, true);

		yield return new WaitForSeconds(0.35f);

		PlayerProfile.Instance.hasPendingMapAnimation = false;
		PlayerProfile.Instance.SaveProfile();
		MainMenuSwipeController.IsSwipeLocked = false;
		canvasGroup.blocksRaycasts = true;
	}

	private IEnumerator PlayPendingStarAnimationOnly(int regionIdx, bool skipUnlockToggle = false)
	{
		if (PlayerProfile.Instance == null) yield break;
		if (currentVisualMap == null || currentVisualMap.levelStarsViews == null) yield break;

		RegionConfig region = PlayerProfile.Instance.allRegions[regionIdx];
		if (region == null || region.levels == null) yield break;

		string targetLevelId = PlayerProfile.Instance.pendingStarLevelId;
		if (string.IsNullOrEmpty(targetLevelId)) yield break;

		int levelIndex = -1;
		for (int i = 0; i < region.levels.Count; i++)
		{
			if (region.levels[i] != null && region.levels[i].name == targetLevelId)
			{
				levelIndex = i;
				break;
			}
		}

		if (levelIndex < 0 || levelIndex >= currentVisualMap.levelStarsViews.Length) yield break;
		if (currentVisualMap.levelStarsViews[levelIndex] == null) yield break;

		Sequence seq = currentVisualMap.levelStarsViews[levelIndex]
			.PlayStarUpgradeAnimation(PlayerProfile.Instance.pendingStarOldValue, PlayerProfile.Instance.pendingStarNewValue);

		yield return seq.WaitForCompletion();

		UpdateRegionProgress(regionIdx, true, true);

		yield return new WaitForSeconds(0.2f);
		PlayerProfile.Instance.ClearPendingStarAnimation();
	}

	private IEnumerator RegionTransitionRoutine()
	{
		canvasGroup.blocksRaycasts = false;
		MainMenuSwipeController.IsSwipeLocked = true;
		yield return new WaitForSeconds(0.5f);

		if (currentVisualMap != null)
		{
			if (confettiFX != null) confettiFX.Play();

			foreach (var sector in currentVisualMap.sectors)
			{
				if (sector != null)
					sector.transform.DOPunchScale(Vector3.one * 0.1f, 0.5f, 5, 1f);
			}
		}

		yield return new WaitForSeconds(2.0f);

		if (confettiFX != null) confettiFX.Stop();
		PlayerProfile.Instance.hasPendingRegionAnimation = false;
		PlayerProfile.Instance.SaveProfile();

		previewRegionIndex = PlayerProfile.Instance.currentRegionIndex;
		RefreshMap();

		MainMenuSwipeController.IsSwipeLocked = false;
		canvasGroup.blocksRaycasts = true;
	}

	private void OnNodeClicked(int nodeIndex)
	{
		if (PlayerProfile.Instance == null) return;

		int realRegionIndex = PlayerProfile.Instance.currentRegionIndex;
		bool isUnlockedRegion = previewRegionIndex <= realRegionIndex;
		if (!isUnlockedRegion) return;

		if (previewRegionIndex != realRegionIndex) return;

		if (nodeIndex > PlayerProfile.Instance.currentLevelIndex)
		{
			if (currentVisualMap != null && currentVisualMap.levelNodes[nodeIndex] != null)
			{
				Transform nodeTransform = currentVisualMap.levelNodes[nodeIndex].transform;
				nodeTransform.DOComplete();
				nodeTransform.DOPunchPosition(new Vector3(15f, 0, 0), 0.4f, 10, 1f);
				nodeTransform.DOPunchRotation(new Vector3(0, 0, 5f), 0.4f, 10, 1f);
			}
			return;
		}

		int regionIdx = previewRegionIndex;
		if (regionIdx < 0 || regionIdx >= PlayerProfile.Instance.allRegions.Count) return;

		RegionConfig region = PlayerProfile.Instance.allRegions[regionIdx];
		if (region == null || nodeIndex < 0 || nodeIndex >= region.levels.Count) return;

		if (missionPopup != null)
		{
			missionPopup.Show(regionIdx, nodeIndex, region.levels[nodeIndex]);
		}
		else
		{
			PlayerPrefs.SetInt("SelectedLevelToPlay", nodeIndex);
			DOTween.KillAll();
			SceneManager.LoadScene("Gameplay");
		}
	}

	public void TryClaimRegionReward(int rewardIndex)
	{
		if (PlayerProfile.Instance == null) return;
		if (previewRegionIndex != PlayerProfile.Instance.currentRegionIndex) return;

		int regionIdx = previewRegionIndex;
		if (regionIdx < 0 || regionIdx >= PlayerProfile.Instance.allRegions.Count) return;

		RegionConfig region = PlayerProfile.Instance.allRegions[regionIdx];
		if (region == null || region.trophyRoadRewards == null) return;
		if (rewardIndex < 0 || rewardIndex >= region.trophyRoadRewards.Length) return;

		RegionRewardData reward = region.trophyRoadRewards[rewardIndex];
		if (reward == null) return;

		float progress01 = PlayerProfile.Instance.GetRegionProgress01(regionIdx);
		bool reached = progress01 >= reward.progressThreshold;
		bool claimed = PlayerProfile.Instance.IsRegionRewardClaimed(regionIdx, rewardIndex);

		if (!reached || claimed)
			return;

		switch (reward.rewardType)
		{
			case TrophyRewardType.SoftCurrency:
				PlayerProfile.Instance.totalCurrency += reward.softCurrencyAmount;
				break;

			case TrophyRewardType.Lootbox:
				if (reward.lootboxReward != null)
				{
					CardData droppedCard = reward.lootboxReward.OpenBox();
					if (droppedCard != null)
						PlayerProfile.Instance.AddCardReward(droppedCard);
				}
				break;

			case TrophyRewardType.Card:
				if (reward.cardReward != null)
					PlayerProfile.Instance.AddCardReward(reward.cardReward);
				break;

			case TrophyRewardType.Skin:
				Debug.Log("Skin reward claimed: " + reward.customRewardId);
				break;

			case TrophyRewardType.Custom:
				Debug.Log("Custom reward claimed: " + reward.customRewardId);
				break;
		}

		PlayerProfile.Instance.MarkRegionRewardClaimed(regionIdx, rewardIndex);
		PlayerProfile.Instance.SaveProfile();
		UpdateRegionProgress(regionIdx, false, true);

		if (rewardIndex < rewardMilestones.Length && rewardMilestones[rewardIndex] != null)
			rewardMilestones[rewardIndex].DOPunchScale(Vector3.one * 0.2f, 0.35f, 8, 1f);
	}

	public void TryPreviewNextRegion()
	{
		if (PlayerProfile.Instance == null) return;

		int maxIndex = PlayerProfile.Instance.allRegions.Count - 1;
		if (previewRegionIndex >= maxIndex) return;

		previewRegionIndex++;
		RefreshMap();
	}

	public void TryPreviewPreviousRegion()
	{
		if (PlayerProfile.Instance == null) return;

		if (previewRegionIndex <= 0) return;

		previewRegionIndex--;
		RefreshMap();
	}

	private void OnDestroy() => transform.DOKill();
}