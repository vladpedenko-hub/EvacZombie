using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

// Core Loop v2 Supply counter (Docs/CORE_LOOP_V2.md Phase 1), built entirely in code -- no scene
// or prefab edits. Lives as a sibling of CardManager.cardsPanel (so it shares its parent's anchor
// space and safe-area inheritance -- CLAUDE.md "Респонсив-лейаут": CardsPanel already lives
// inside SafeAreaContainer, so any sibling of it does too), anchored in a band just above it.
// Scene-scoped (not DontDestroyOnLoad): destroyed automatically when Gameplay unloads.
public class SupplyHud : MonoBehaviour
{
	private static SupplyHud instance;

	private TextMeshProUGUI amountText;
	private Image bg;
	private RectTransform root;
	private bool builtUi;
	private bool wasCapped;

	public static void EnsureBuilt()
	{
		if (instance != null) return;
		if (CardManager.Instance == null || CardManager.Instance.cardsPanel == null) return;

		var go = new GameObject("[SupplyHud]");
		instance = go.AddComponent<SupplyHud>();
		instance.Build();
	}

	private void Build()
	{
		RectTransform cardsPanel = CardManager.Instance.cardsPanel as RectTransform;
		Transform parent = cardsPanel != null ? cardsPanel.parent : CardManager.Instance.cardsPanel.parent;

		root = RuntimeUiKit.CreateRect("SupplyHud", parent);

		if (cardsPanel != null)
		{
			float bandHeight = 0.065f;
			root.anchorMin = new Vector2(0.5f - 0.17f, cardsPanel.anchorMax.y);
			root.anchorMax = new Vector2(0.5f + 0.17f, cardsPanel.anchorMax.y + bandHeight);
		}
		else
		{
			root.anchorMin = new Vector2(0.33f, 0.2f);
			root.anchorMax = new Vector2(0.67f, 0.26f);
		}
		root.offsetMin = Vector2.zero;
		root.offsetMax = Vector2.zero;

		bg = root.gameObject.AddComponent<Image>();
		bg.sprite = RuntimeUiKit.Rounded();
		bg.type = Image.Type.Sliced;
		bg.color = new Color(0.05f, 0.08f, 0.12f, 0.85f);
		bg.raycastTarget = false;

		amountText = RuntimeUiKit.CreateText("Amount", root, "", 30f, new Color(0.65f, 1f, 0.75f, 1f), TextAlignmentOptions.Center, FontStyles.Bold);
		RuntimeUiKit.Stretch(amountText.rectTransform);

		builtUi = true;
		root.gameObject.SetActive(false);

		if (SupplyManager.Instance != null)
			SupplyManager.Instance.OnSupplyChanged += HandleSupplyChanged;
	}

	private void OnDestroy()
	{
		if (instance == this) instance = null;
		if (SupplyManager.Instance != null)
			SupplyManager.Instance.OnSupplyChanged -= HandleSupplyChanged;
	}

	private void Update()
	{
		if (!builtUi) return;

		bool active = CoreLoopV2.IsActive && SupplyManager.Instance != null && SupplyManager.Instance.IsActive;
		if (root.gameObject.activeSelf != active)
		{
			root.gameObject.SetActive(active);
			if (active && SupplyManager.Instance != null)
				HandleSupplyChanged(SupplyManager.Instance.Current, SupplyManager.Instance.Max);
		}
	}

	private void HandleSupplyChanged(int current, int max)
	{
		if (amountText == null) return;

		amountText.text = $"Supply {current}/{max}";

		bool capped = current >= max;
		if (capped && !wasCapped)
		{
			root.DOKill();
			root.DOScale(1.12f, 0.18f).SetLoops(2, LoopType.Yoyo).SetUpdate(true);
		}
		wasCapped = capped;
	}
}
