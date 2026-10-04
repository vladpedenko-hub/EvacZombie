using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Research screen for one skill tree. A vertical chain: the first node is at the bottom, the last at the top.
// Round node = normal, diamond = milestone. Connector green once the node below is purchased.
public class MetaSkillTreeScreen : MonoBehaviour
{
	private const float NodeSize = 130f;
	private const float NodeSpacing = 230f;
	private const float ChainPadding = 160f;

	private static readonly Color PurchasedColor = new Color(0.25f, 0.75f, 0.35f);
	private static readonly Color AvailableColor = new Color(0.85f, 0.85f, 0.9f);
	private static readonly Color NotAffordableColor = new Color(0.45f, 0.5f, 0.55f);
	private static readonly Color LockedColor = new Color(0.18f, 0.19f, 0.22f);
	private static readonly Color LinkOnColor = new Color(0.25f, 0.75f, 0.35f);
	private static readonly Color LinkOffColor = new Color(0.3f, 0.3f, 0.33f);

	private MetaService service;
	private MetaModifiers modifiers;
	private GameObject root;
	private TextMeshProUGUI title;
	private TextMeshProUGUI scientistsText;
	private RectTransform content;
	private ScrollRect scroll;
	private SkillTreeDefinition tree;
	private readonly List<Image> bodies = new List<Image>();
	private readonly List<Image> links = new List<Image>();
	private int selected = -1;

	private GameObject infoSheet;
	private TextMeshProUGUI infoName;
	private TextMeshProUGUI infoEffects;
	private TextMeshProUGUI infoPreview;
	private TextMeshProUGUI reason;
	private TextMeshProUGUI buyLabel;
	private Button buy;
	private Image buyImage;

	public bool IsOpen => root != null && root.activeSelf;

	// bottomInset keeps the overlay above the tab bar, so the bar stays usable while research is open.
	public void Build(Transform parent, MetaService metaService, MetaModifiers metaModifiers, float bottomInset = 0f)
	{
		service = metaService;
		modifiers = metaModifiers;

		RectTransform screen = MetaUI.Rect("ResearchScreen", parent, Vector2.zero, Vector2.one,
			new Vector2(0f, bottomInset), Vector2.zero);
		root = screen.gameObject;
		MetaUI.ImageBox("Background", screen, new Color(0.07f, 0.08f, 0.1f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

		Button back = MetaUI.Button("Back", screen, "BACK", MetaUI.ButtonSecondary,
			new Vector2(0.03f, 0.93f), new Vector2(0.27f, 0.99f), Vector2.zero, Vector2.zero, out _);
		back.onClick.AddListener(Close);

		title = MetaUI.Text("Title", screen, "", 46, new Vector2(0.3f, 0.93f), new Vector2(0.7f, 0.99f),
			Vector2.zero, Vector2.zero);
		scientistsText = MetaUI.Text("Scientists", screen, "", 40, new Vector2(0.72f, 0.93f), new Vector2(0.98f, 0.99f),
			Vector2.zero, Vector2.zero, TextAlignmentOptions.Right);

		// Scrolling chain. The content is sized in BuildChain.
		RectTransform viewport = MetaUI.Rect("Viewport", screen, new Vector2(0f, 0.36f), new Vector2(1f, 0.92f),
			Vector2.zero, Vector2.zero);
		viewport.gameObject.AddComponent<RectMask2D>();
		content = MetaUI.Rect("Content", viewport, Vector2.zero, new Vector2(1f, 0f), Vector2.zero, Vector2.zero);
		content.pivot = new Vector2(0.5f, 0f);

		scroll = viewport.gameObject.AddComponent<ScrollRect>();
		scroll.viewport = viewport;
		scroll.content = content;
		scroll.horizontal = false;
		scroll.vertical = true;

		// Bottom sheet, in the thumb zone.
		infoSheet = MetaUI.ImageBox("InfoSheet", screen, MetaUI.Panel, new Vector2(0f, 0f), new Vector2(1f, 0.34f),
			new Vector2(24f, 24f), new Vector2(-24f, 0f)).gameObject;

		infoName = MetaUI.Text("NodeName", infoSheet.transform, "", 50, new Vector2(0f, 0.8f), new Vector2(1f, 1f),
			new Vector2(28f, 0f), new Vector2(-28f, -8f), TextAlignmentOptions.Left);
		infoEffects = MetaUI.Text("Effects", infoSheet.transform, "", 34, new Vector2(0f, 0.56f), new Vector2(1f, 0.8f),
			new Vector2(28f, 0f), new Vector2(-28f, 0f), TextAlignmentOptions.TopLeft, MetaUI.TextDim);
		infoPreview = MetaUI.Text("Preview", infoSheet.transform, "", 34, new Vector2(0f, 0.36f), new Vector2(1f, 0.56f),
			new Vector2(28f, 0f), new Vector2(-28f, 0f), TextAlignmentOptions.TopLeft);

		buy = MetaUI.Button("Buy", infoSheet.transform, "", MetaUI.ButtonPrimary,
			new Vector2(0.1f, 0.14f), new Vector2(0.9f, 0.32f), Vector2.zero, Vector2.zero, out buyLabel);
		buy.onClick.AddListener(OnBuyClicked);
		buyImage = buy.GetComponent<Image>();

		reason = MetaUI.Text("Reason", infoSheet.transform, "", 30, new Vector2(0f, 0f), new Vector2(1f, 0.13f),
			new Vector2(28f, 0f), new Vector2(-28f, 0f), TextAlignmentOptions.Center, MetaUI.TextDim);

		infoSheet.SetActive(false);
		root.SetActive(false);
	}

	public void Open(SkillTreeDefinition treeDefinition)
	{
		tree = treeDefinition;
		selected = -1;
		BuildChain();
		infoSheet.SetActive(false);
		root.SetActive(true);

		// Start at the bottom, where the first node is.
		scroll.verticalNormalizedPosition = 0f;
		Refresh();
	}

	public void Close()
	{
		selected = -1;
		if (root != null) root.SetActive(false);
	}

	public void Refresh()
	{
		if (!IsOpen || tree == null) return;

		title.text = tree.displayName;
		scientistsText.text = $"Scientists {MetaRuntime.Currency.Get(CurrencyType.Scientists)}";

		for (int i = 0; i < tree.nodes.Count; i++)
		{
			bodies[i].color = ColorFor(service.GetNodeState(tree.nodes[i].id));
			if (i > 0)
			{
				bool unlocked = service.State.purchasedNodes.Contains(tree.nodes[i - 1].id);
				links[i].color = unlocked ? LinkOnColor : LinkOffColor;
			}
		}

		if (selected >= 0) RefreshInfo();
	}

	// --- Chain ---

	private void BuildChain()
	{
		for (int i = content.childCount - 1; i >= 0; i--) Destroy(content.GetChild(i).gameObject);
		bodies.Clear();
		links.Clear();

		int count = tree.nodes.Count;
		float height = ChainPadding * 2f + NodeSize + (count - 1) * NodeSpacing;
		content.offsetMax = new Vector2(0f, height);

		for (int i = 0; i < count; i++)
		{
			float y = ChainPadding + NodeSize / 2f + i * NodeSpacing;
			SkillNodeDefinition node = tree.nodes[i];

			if (i == 0)
			{
				links.Add(null);
			}
			else
			{
				// Connector from the node below. Its length is the gap between the two node edges.
				var link = MetaUI.ImageBox("Link_" + i, content, LinkOffColor, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
				Place(link.rectTransform, new Vector2(0f, y - NodeSpacing / 2f), new Vector2(12f, NodeSpacing - NodeSize));
				links.Add(link);
			}

			Image body = MetaUI.ImageBox("Node_" + node.id, content, LockedColor, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
			Place(body.rectTransform, new Vector2(0f, y), Vector2.one * NodeSize);
			if (node.isMilestone)
			{
				// A square rotated 45 degrees is a diamond. Shrink a little so the corners fit the same footprint.
				body.rectTransform.localEulerAngles = new Vector3(0f, 0f, 45f);
				body.rectTransform.sizeDelta = Vector2.one * NodeSize * 0.8f;
			}

			int index = i;
			var button = body.gameObject.AddComponent<Button>();
			button.targetGraphic = body;
			button.onClick.AddListener(() => Select(index));
			bodies.Add(body);

			TextMeshProUGUI badge = MetaUI.Text("Badge_" + (i + 1), content, (i + 1).ToString(), 40,
				Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
			Place(badge.rectTransform, new Vector2(-NodeSize / 2f - 70f, y), new Vector2(90f, 60f));
		}
	}

	private static void Place(RectTransform rect, Vector2 center, Vector2 size)
	{
		rect.anchorMin = new Vector2(0.5f, 0f);
		rect.anchorMax = new Vector2(0.5f, 0f);
		rect.pivot = new Vector2(0.5f, 0.5f);
		rect.sizeDelta = size;
		rect.anchoredPosition = center;
	}

	private static Color ColorFor(NodeState state) => state switch
	{
		NodeState.Purchased => PurchasedColor,
		NodeState.Available => AvailableColor,
		NodeState.NotAffordable => NotAffordableColor,
		_ => LockedColor
	};

	private void Select(int index)
	{
		selected = index;
		infoSheet.SetActive(true);
		RefreshInfo();
	}

	// --- Bottom sheet ---

	private void RefreshInfo()
	{
		SkillNodeDefinition node = tree.nodes[selected];
		NodeState state = service.GetNodeState(node.id);

		infoName.text = node.isMilestone ? $"{node.displayName}  (milestone)" : node.displayName;
		infoEffects.text = DescribeEffects(node);
		infoPreview.text = DescribePreview(node);

		buyLabel.text = state == NodeState.Purchased ? "Researched" : $"Research  {node.cost} Scientists";
		buy.interactable = state == NodeState.Available;
		buyImage.color = buy.interactable ? MetaUI.ButtonPrimary : MetaUI.ButtonDisabled;

		reason.text = state switch
		{
			NodeState.Purchased => "",
			NodeState.Available => "",
			NodeState.NotAffordable => $"Need {node.cost} Scientists ({MetaRuntime.Currency.Get(CurrencyType.Scientists)}/{node.cost})",
			_ => LockReason(node)
		};
	}

	private string LockReason(SkillNodeDefinition node)
	{
		if (!service.State.unlockedTrees.Contains(tree.id)) return "Restore the building to open research.";

		foreach (string prerequisite in node.prerequisites)
		{
			if (service.State.purchasedNodes.Contains(prerequisite)) continue;
			SkillNodeDefinition previous = tree.nodes.Find(n => n.id == prerequisite);
			return $"Research \"{previous?.displayName ?? prerequisite}\" first.";
		}
		return "";
	}

	private static string DescribeEffects(SkillNodeDefinition node)
	{
		var sb = new StringBuilder();
		foreach (StatModifier effect in node.effects)
		{
			sb.Append(EffectLabel(effect)).Append("  ").Append(FormatValue(effect.value, effect.mode)).AppendLine();
		}
		return sb.ToString().TrimEnd();
	}

	// "current -> new" for each effect. The current value is what is already researched for the same stat and filter.
	private string DescribePreview(SkillNodeDefinition node)
	{
		var sb = new StringBuilder();
		foreach (StatModifier effect in node.effects)
		{
			float current = modifiers.SumForFilter(effect.stat, effect.category, effect.mode);
			float next = current + effect.value;
			sb.Append(EffectLabel(effect)).Append(":  ")
				.Append(FormatValue(current, effect.mode)).Append(" -> ").Append(FormatValue(next, effect.mode))
				.AppendLine();
		}
		return sb.ToString().TrimEnd();
	}

	private static string EffectLabel(StatModifier effect)
	{
		string category = effect.category == CardCategoryFilter.All ? "all cards" : $"{effect.category} cards";
		return $"{effect.stat} ({category})";
	}

	private static string FormatValue(float value, ModifierMode mode)
	{
		string sign = value >= 0f ? "+" : "";
		return mode == ModifierMode.Percent ? $"{sign}{value:0.#}%" : $"{sign}{value:0.#}";
	}

	private void OnBuyClicked()
	{
		if (selected < 0) return;
		service.TryPurchaseNode(tree.nodes[selected].id);
		Refresh();
	}
}
