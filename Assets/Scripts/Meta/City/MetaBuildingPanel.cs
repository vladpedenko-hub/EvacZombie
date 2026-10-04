using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Bottom sheet for a tapped building. Lives in the thumb zone. Shows the state, the cost or reason, and one action.
public class MetaBuildingPanel : MonoBehaviour
{
	// Raised for buildings that have a skill tree and are already restored.
	public event Action<BuildingDefinition> OnOpenResearch;

	private MetaService service;
	private GameObject root;
	private TextMeshProUGUI title;
	private TextMeshProUGUI description;
	private TextMeshProUGUI status;
	private TextMeshProUGUI reason;
	private TextMeshProUGUI actionLabel;
	private Button action;
	private Image actionImage;
	private BuildingDefinition current;

	public bool IsOpen => root != null && root.activeSelf;

	public void Build(Transform parent, MetaService metaService)
	{
		service = metaService;

		RectTransform panel = MetaUI.Rect("BuildingPanel", parent,
			new Vector2(0f, 0f), new Vector2(1f, 0.42f), new Vector2(24f, 24f), new Vector2(-24f, 0f));
		root = panel.gameObject;
		MetaUI.ImageBox("Background", panel, MetaUI.Panel, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

		title = MetaUI.Text("Title", panel, "", 56, new Vector2(0f, 0.84f), new Vector2(0.8f, 1f),
			new Vector2(28f, -10f), new Vector2(0f, -10f), TextAlignmentOptions.Left);

		Button close = MetaUI.Button("Close", panel, "X", MetaUI.ButtonSecondary,
			new Vector2(0.86f, 0.86f), new Vector2(0.98f, 0.98f), Vector2.zero, Vector2.zero, out _);
		close.onClick.AddListener(Hide);

		description = MetaUI.Text("Description", panel, "", 34, new Vector2(0f, 0.55f), new Vector2(1f, 0.84f),
			new Vector2(28f, 0f), new Vector2(-28f, 0f), TextAlignmentOptions.TopLeft, MetaUI.TextDim);

		status = MetaUI.Text("Status", panel, "", 40, new Vector2(0f, 0.4f), new Vector2(1f, 0.55f),
			new Vector2(28f, 0f), new Vector2(-28f, 0f), TextAlignmentOptions.Left);

		action = MetaUI.Button("Action", panel, "", MetaUI.ButtonPrimary,
			new Vector2(0.1f, 0.18f), new Vector2(0.9f, 0.36f), Vector2.zero, Vector2.zero, out actionLabel);
		actionImage = action.GetComponent<Image>();
		action.onClick.AddListener(OnActionClicked);

		reason = MetaUI.Text("Reason", panel, "", 32, new Vector2(0f, 0.02f), new Vector2(1f, 0.16f),
			new Vector2(28f, 0f), new Vector2(-28f, 0f), TextAlignmentOptions.Center, MetaUI.TextDim);

		root.SetActive(false);
	}

	public void Show(BuildingDefinition building)
	{
		current = building;
		title.text = building.displayName;
		description.text = building.description;
		root.SetActive(true);
		Refresh();
	}

	public void Hide()
	{
		current = null;
		if (root != null) root.SetActive(false);
	}

	public void Refresh()
	{
		if (current == null) return;

		BuildingStatus state = service.GetBuildingState(current.id);
		SkillTreeDefinition tree = service.FindTreeForBuilding(current.id);

		switch (state.state)
		{
			case BuildingState.Locked:
				status.text = $"Locked: {state.reason}";
				ShowRestoreAction(enabled: false, state.reason);
				break;

			case BuildingState.AvailableNotAffordable:
				status.text = $"Restore cost: {current.restorationCostPeople} People";
				ShowRestoreAction(enabled: false, state.reason);
				break;

			case BuildingState.Available:
				status.text = $"Restore cost: {current.restorationCostPeople} People";
				ShowRestoreAction(enabled: true, "");
				break;

			case BuildingState.Restored:
				status.text = "Restored";
				reason.text = "";
				if (tree != null)
				{
					action.gameObject.SetActive(true);
					actionLabel.text = "Open Research";
					actionImage.color = MetaUI.ButtonSecondary;
					action.interactable = true;
				}
				else
				{
					action.gameObject.SetActive(false);
				}
				break;
		}
	}

	private void ShowRestoreAction(bool enabled, string disabledReason)
	{
		action.gameObject.SetActive(true);
		actionLabel.text = "Restore";
		action.interactable = enabled;
		actionImage.color = enabled ? MetaUI.ButtonPrimary : MetaUI.ButtonDisabled;
		reason.text = enabled ? "" : disabledReason;
	}

	private void OnActionClicked()
	{
		if (current == null) return;

		if (service.GetBuildingState(current.id).state == BuildingState.Restored)
		{
			OnOpenResearch?.Invoke(current);
			return;
		}

		// Success or failure, the controller refreshes everything through MetaService.OnChanged.
		service.TryRestoreBuilding(current.id);
	}
}
