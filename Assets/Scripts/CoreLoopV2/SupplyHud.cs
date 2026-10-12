using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

// Core Loop v2 Supply pill (Docs/CORE_LOOP_V2.md HUD rebuild, BOTTOM zone, left slot). Built
// inside BottomZoneV2.SupplySlot. Gated by LevelData.v2ShowSupplyHud -- Supply still exists in
// SupplyManager's logic even when this is hidden (CardUI's cost badge/afford-dim follow the same
// flag, see CardUI.cs).
public class SupplyHud : MonoBehaviour
{
	private static SupplyHud instance;

	private RectTransform root;
	private Image bg;
	private TextMeshProUGUI amountText;
	private bool builtUi;
	private bool wasCapped;

	// Called from LevelManager.LoadLevel() -- the sole initialization entry point.
	public static void OnLevelLoaded(LevelData data)
	{
		if (instance == null)
		{
			var go = new GameObject("[SupplyHud]");
			instance = go.AddComponent<SupplyHud>();
		}

		instance.HandleLevelLoaded(data);
	}

	private void HandleLevelLoaded(LevelData data)
	{
		BuildUiIfNeeded();
		if (!builtUi) return;

		bool show = data != null && data.useCoreLoopV2 && data.v2ShowSupplyHud;
		root.gameObject.SetActive(show);
		wasCapped = false;

		if (SupplyManager.Instance != null)
		{
			SupplyManager.Instance.OnSupplyChanged -= HandleSupplyChanged;
			SupplyManager.Instance.OnSupplyChanged += HandleSupplyChanged;
			SupplyManager.Instance.OnSupplyGained -= HandleSupplyGained;
			SupplyManager.Instance.OnSupplyGained += HandleSupplyGained;
			SupplyManager.Instance.OnSpendFailed -= HandleSpendFailed;
			SupplyManager.Instance.OnSpendFailed += HandleSpendFailed;
			SupplyManager.Instance.OnFirstSpendThisAttempt -= HandleFirstSpend;
			SupplyManager.Instance.OnFirstSpendThisAttempt += HandleFirstSpend;

			if (show) HandleSupplyChanged(SupplyManager.Instance.Current, SupplyManager.Instance.Max);
		}
	}

	private void BuildUiIfNeeded()
	{
		if (builtUi) return;

		BottomZoneV2.EnsureBuilt();
		if (BottomZoneV2.SupplySlot == null) return;

		root = RuntimeUiKit.CreateRect("SupplyHudV2", BottomZoneV2.SupplySlot);
		RuntimeUiKit.SetAnchors(root, 0.04f, 0.12f, 0.96f, 0.88f);

		bg = RuntimeUiKit.CreateImage("Bg", root, V2Theme.PanelBg);
		RuntimeUiKit.Stretch(bg.rectTransform);

		Image icon = RuntimeUiKit.CreateImage("Icon", root, V2Theme.ThreatAmber, rounded: false);
		icon.sprite = RuntimeUiKit.Diamond();
		RuntimeUiKit.SetAnchors(icon.rectTransform, 0.06f, 0.2f, 0.26f, 0.8f);

		amountText = RuntimeUiKit.CreateText("Amount", root, "", V2Theme.KeyNumberSize * 0.72f, V2Theme.TextPrimary, TextAlignmentOptions.Center, FontStyles.Bold);
		RuntimeUiKit.SetAnchors(amountText.rectTransform, 0.28f, 0f, 1f, 1f);

		root.gameObject.SetActive(false);
		builtUi = true;
	}

	private void HandleSupplyChanged(int current, int max)
	{
		if (amountText == null) return;
		amountText.text = $"{current}/{max}";

		bool capped = current >= max;
		if (capped && !wasCapped)
		{
			root.DOKill();
			root.DOScale(1.12f, 0.18f).SetLoops(2, LoopType.Yoyo).SetUpdate(true);
		}
		wasCapped = capped;
	}

	private void HandleSupplyGained(int amount)
	{
		if (root == null || wasCapped) return; // the cap pulse above already covers this moment
		root.DOKill();
		root.DOScale(1.06f, 0.12f).SetLoops(2, LoopType.Yoyo).SetUpdate(true);
	}

	// L2 tutorial step 4 (Docs/CORE_LOOP_V2.md Phase 3): a non-blocking pulse, no text, no pause --
	// deliberately NOT a TutorialStep (those gate input with a full-screen blocker, which this
	// isn't meant to do).
	private void HandleFirstSpend()
	{
		if (root == null) return;
		root.DOKill();
		root.DOScale(1.18f, 0.22f).SetLoops(2, LoopType.Yoyo).SetEase(Ease.OutQuad).SetUpdate(true);
	}

	private void HandleSpendFailed()
	{
		if (root == null) return;

		root.DOKill();
		root.DOShakeAnchorPos(0.3f, strength: 14f, vibrato: 14, randomness: 90f, snapping: false, fadeOut: true).SetUpdate(true);

		if (bg != null)
		{
			bg.DOKill();
			bg.color = V2Theme.LossRed;
			bg.DOColor(V2Theme.PanelBg, 0.35f).SetUpdate(true);
		}
	}

	private void OnDestroy()
	{
		if (instance == this) instance = null;
		if (SupplyManager.Instance != null)
		{
			SupplyManager.Instance.OnSupplyChanged -= HandleSupplyChanged;
			SupplyManager.Instance.OnSupplyGained -= HandleSupplyGained;
			SupplyManager.Instance.OnSpendFailed -= HandleSpendFailed;
			SupplyManager.Instance.OnFirstSpendThisAttempt -= HandleFirstSpend;
		}
	}
}
