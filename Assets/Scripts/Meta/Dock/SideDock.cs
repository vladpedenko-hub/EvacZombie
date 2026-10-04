using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Shortcut icons pinned to the right edge of MainMenu. Visible on every tab (Deck, Map, Base).
// Shortcuts are data (MetaContentDatabase.dockShortcuts). Adding one of an existing kind is an asset edit.
// Subscribes in OnEnable and unsubscribes in OnDisable. Nothing runs in Update.
public class SideDock : MonoBehaviour
{
	public const float IconSize = 96f;
	public const float RightInset = 16f;

	// Vertical position, as a fraction of the screen height from the bottom. Chosen so it sits in open space on
	// Deck (empty right column), Map (water) and Base (city edge, which BaseView keeps clear of the dock).
	private const float HeightFraction = 0.36f;

	private class Entry
	{
		public DockShortcutDefinition definition;
		public GameObject root;
		public GameObject badge;
	}

	private readonly List<Entry> entries = new List<Entry>();
	private MetaService service;
	private BaseView view;
	private bool subscribed;

	// Tutorial highlight targets, keyed by shortcut id (see TutorialManager.RegisterTarget).
	public RectTransform IconRect(string shortcutId)
	{
		foreach (Entry entry in entries)
		{
			if (entry.definition.id == shortcutId) return (RectTransform)entry.root.transform;
		}
		return null;
	}

	public void Build(Canvas canvas, MetaService metaService, BaseView baseView, IReadOnlyList<DockShortcutDefinition> shortcuts)
	{
		service = metaService;
		view = baseView;

		RectTransform column = MetaUI.Rect("DockColumn", canvas.transform, new Vector2(1f, HeightFraction),
			new Vector2(1f, HeightFraction), Vector2.zero, Vector2.zero);
		column.pivot = new Vector2(1f, 0.5f);
		column.sizeDelta = new Vector2(IconSize, 0f);
		column.anchoredPosition = new Vector2(-RightInset, 0f);

		float y = 0f;
		foreach (DockShortcutDefinition definition in shortcuts)
		{
			if (definition == null || string.IsNullOrEmpty(definition.id)) continue;
			entries.Add(BuildIcon(definition, column, y));
			y -= IconSize + 18f;
		}

		Subscribe();
		Refresh();
	}

	private Entry BuildIcon(DockShortcutDefinition definition, RectTransform column, float y)
	{
		Image body = MetaUI.ImageBox("Dock_" + definition.id, column, MetaUI.ButtonSecondary,
			new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-IconSize / 2f, y - IconSize), new Vector2(IconSize / 2f, y));
		Button button = body.gameObject.AddComponent<Button>();
		button.targetGraphic = body;
		DockShortcutDefinition captured = definition;
		button.onClick.AddListener(() => OnTapped(captured));

		MetaUI.Text("Label", body.transform, definition.label, 44, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

		Image badge = MetaUI.ImageBox("Badge", body.transform, MetaUI.ButtonPrimary,
			new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-34f, -34f), new Vector2(0f, 0f));
		badge.raycastTarget = false;
		MetaUI.Text("BadgeText", badge.transform, "!", 36, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

		return new Entry { definition = definition, root = body.gameObject, badge = badge.gameObject };
	}

	private void OnEnable()
	{
		Subscribe();
		Refresh();
	}

	private void OnDisable() => Unsubscribe();

	// Both sources go through here, so a disabled dock never keeps a handler alive on a shared object.
	// Tutorial target that keeps the dock visible while a tutorial is running (the icon is the highlight).
	public const string TutorialTargetId = "dock_research";

	// Currency is listened to too: a Scientists change can make a node purchasable without any meta change.
	// The tutorial's step events are static, so they are also removed on disable.
	private void Subscribe()
	{
		if (subscribed || service == null) return;
		service.OnChanged += Refresh;
		if (MetaRuntime.Currency != null) MetaRuntime.Currency.OnChanged += OnCurrencyChanged;
		if (view != null) view.ResearchStateChanged += Refresh;
		TutorialManager.OnStepChanged += Refresh;
		subscribed = true;
	}

	private void Unsubscribe()
	{
		if (!subscribed) return;
		service.OnChanged -= Refresh;
		if (MetaRuntime.Currency != null) MetaRuntime.Currency.OnChanged -= OnCurrencyChanged;
		if (view != null) view.ResearchStateChanged -= Refresh;
		TutorialManager.OnStepChanged -= Refresh;
		subscribed = false;
	}

	// Hidden during a blocking tutorial step (dialogue or any step that is not pointing at the icon).
	private static bool TutorialBlocksDock()
	{
		TutorialManager tutorial = TutorialManager.Instance;
		return tutorial != null && tutorial.IsActive && tutorial.CurrentTargetId != TutorialTargetId;
	}

	private void OnCurrencyChanged(CurrencyType type, int value) => Refresh();

	// Hidden while the research overlay is open, because the overlay covers the screen.
	private void Refresh()
	{
		if (service == null) return;

		bool overlayOpen = view != null && view.IsResearchOpen;
		bool blocked = TutorialBlocksDock();
		foreach (Entry entry in entries)
		{
			bool shown = !overlayOpen && !blocked && IsVisible(entry.definition);
			if (entry.root.activeSelf != shown) entry.root.SetActive(shown);
			entry.badge.SetActive(shown && HasBadge(entry.definition));
		}
	}

	private bool IsVisible(DockShortcutDefinition definition)
	{
		switch (definition.visibility)
		{
			case DockShortcutDefinition.Visibility.Always:
				return true;
			case DockShortcutDefinition.Visibility.AfterAnyTreeUnlocked:
				return service.State.unlockedTrees.Count > 0;
			default:
				return false;
		}
	}

	private bool HasBadge(DockShortcutDefinition definition)
	{
		switch (definition.badge)
		{
			case DockShortcutDefinition.Badge.PurchasableNode:
				return service.HasPurchasableNode();
			default:
				return false;
		}
	}

	// One switch per action kind. New kinds get a case here and an enum value on the definition.
	private void OnTapped(DockShortcutDefinition definition)
	{
		switch (definition.action)
		{
			case DockShortcutDefinition.Action.OpenResearch:
				if (view != null) view.OpenResearch(service.PickResearchTree());
				break;
		}
	}
}
