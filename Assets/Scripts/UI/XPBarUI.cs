using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class XPBarUI : MonoBehaviour
{
    [Header("UI References")]
    public Slider xpSlider;
    public TextMeshProUGUI levelText;
    public TextMeshProUGUI xpText;

    private void Update()
    {
        var session = RunSessionData.Instance;
        if (session == null) return;

        int current = session.CurrentXP;
        int max = session.GetXpForNextLevel();

        if (xpSlider != null)
        {
            xpSlider.minValue = 0;
            xpSlider.maxValue = max;
            xpSlider.value = current;
        }

        if (levelText != null)
            levelText.text = $"Ур. {session.CurrentRunLevel}";

        if (xpText != null)
            xpText.text = $"{current} / {max}";
    }
}
