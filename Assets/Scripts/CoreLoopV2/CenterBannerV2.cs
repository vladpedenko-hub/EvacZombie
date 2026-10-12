using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

// Core Loop v2 CENTER-zone transient banners (Docs/CORE_LOOP_V2.md "center = gameplay + transient
// banners only -- no persistent UI here"). A single queued slot: never overlapping, non-
// interactive (raycastTarget off), each banner on screen for at most 2s. Scene-scoped, same
// rebuild-per-level pattern as the other v2 HUD pieces.
public class CenterBannerV2 : MonoBehaviour
{
	private static CenterBannerV2 instance;

	private RectTransform root;
	private TextMeshProUGUI label;
	private bool builtUi;
	private readonly Queue<string> queue = new Queue<string>();
	private Coroutine pumpCoroutine;

	private const float DisplayDuration = 2f;

	public static void Show(string text)
	{
		EnsureBuilt();
		if (instance == null || string.IsNullOrEmpty(text)) return;

		instance.queue.Enqueue(text);
		if (instance.pumpCoroutine == null)
			instance.pumpCoroutine = instance.StartCoroutine(instance.PumpRoutine());
	}

	private static void EnsureBuilt()
	{
		if (instance != null) return;

		var go = new GameObject("[CenterBannerV2]");
		instance = go.AddComponent<CenterBannerV2>();
		instance.BuildUi();
	}

	private void BuildUi()
	{
		GameObject timerTextGo = GameObject.Find("TimerText");
		Transform parent = timerTextGo != null ? timerTextGo.transform.parent : FindSafeAreaContainer();

		// Upper-center of the gameplay area -- below the top zone, well clear of the bottom zone.
		root = RuntimeUiKit.CreateRect("CenterBannerV2", parent);
		root.anchorMin = new Vector2(0.5f, 0.68f);
		root.anchorMax = new Vector2(0.5f, 0.68f);
		root.pivot = new Vector2(0.5f, 0.5f);
		root.sizeDelta = new Vector2(760f, 100f);
		root.anchoredPosition = Vector2.zero;

		Image bg = RuntimeUiKit.CreateImage("Bg", root, V2Theme.PanelBg);
		bg.raycastTarget = false;
		RuntimeUiKit.Stretch(bg.rectTransform);

		label = RuntimeUiKit.CreateText("Label", root, "", V2Theme.KeyNumberSize * 0.62f, V2Theme.ThreatAmber, TextAlignmentOptions.Center, FontStyles.Bold);
		label.raycastTarget = false;
		RuntimeUiKit.Stretch(label.rectTransform);

		root.gameObject.SetActive(false);
		builtUi = true;
	}

	private IEnumerator PumpRoutine()
	{
		while (queue.Count > 0)
		{
			string text = queue.Dequeue();
			yield return ShowOne(text);
		}
		pumpCoroutine = null;
	}

	private IEnumerator ShowOne(string text)
	{
		if (!builtUi) yield break;

		label.text = text;
		root.gameObject.SetActive(true);
		root.localScale = Vector3.one * 0.7f;
		root.DOKill();
		root.DOScale(1f, 0.2f).SetEase(Ease.OutBack).SetUpdate(true);

		yield return new WaitForSeconds(DisplayDuration);

		root.gameObject.SetActive(false);
	}

	private Transform FindSafeAreaContainer()
	{
		GameObject go = GameObject.Find("SafeAreaContainer");
		return go != null ? go.transform : null;
	}

	private void OnDestroy()
	{
		if (instance == this) instance = null;
	}
}
