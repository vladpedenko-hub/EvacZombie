using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class UpgradeCardUI : MonoBehaviour
{
    [Header("UI Elements")]
    public Image cardBackground;
    public Image upgradeIcon;
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI descriptionText;
    public TextMeshProUGUI tierBadgeText;
    public Image tierGlow;
    public Button selectButton;

    private RunUpgradeDefinition _upgrade;
    private Action<RunUpgradeDefinition> _onSelected;

    public void Setup(RunUpgradeDefinition upgrade, int tier, Action<RunUpgradeDefinition> onSelected)
    {
        _upgrade = upgrade;
        _onSelected = onSelected;

        titleText.text = upgrade.GetDisplayName(tier);
        descriptionText.text = upgrade.GetDescription(tier);

        var icon = upgrade.GetIcon(tier);
        if (upgradeIcon != null && icon != null)
            upgradeIcon.sprite = icon;

        Color accent = upgrade.GetAccentColor(tier);
        if (cardBackground != null)
            cardBackground.color = new Color(accent.r, accent.g, accent.b, 0.15f);

        if (tierBadgeText != null)
            tierBadgeText.text = tier switch { 1 => "★", 2 => "★★", 3 => "★★★ ULTIMATE", _ => "★" };

        if (tierGlow != null)
            tierGlow.gameObject.SetActive(tier == 3);

        selectButton.onClick.RemoveAllListeners();
        selectButton.onClick.AddListener(() => _onSelected?.Invoke(_upgrade));
    }
}
