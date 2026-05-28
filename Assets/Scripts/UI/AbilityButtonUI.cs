using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

public class AbilityButtonUI : MonoBehaviour
{
    [Header("References")]
    public Button button;
    public Image fillImage;
    public GameObject pulseGlow;

    [Header("Colors")]
    public Color chargingColor = new Color(0.7f, 0.7f, 0.7f, 1f);
    public Color readyColor = new Color(1f, 0.85f, 0f, 1f);
    public Color activeColor = new Color(0.2f, 0.6f, 1f, 1f);

    private Tween glowTween;
    private bool gameEventsSubscribed = false;
    private bool levelUpEventsSubscribed = false;

    private void Start()
    {
        if (AbilityManager.Instance == null)
        {
            gameObject.SetActive(false);
            return;
        }

        // Subscribe to initialization event
        AbilityManager.Instance.OnInitialized += HandleInitialized;

        // If LevelManager.LoadLevel() already ran before our Start() —
        // handle the state immediately without waiting for the event
        if (AbilityManager.Instance.IsInitialized)
            HandleInitialized(AbilityManager.Instance.IsEnabled);
    }

    private void HandleInitialized(bool isEnabled)
    {
        // Unsubscribe: we only care about the first invocation
        AbilityManager.Instance.OnInitialized -= HandleInitialized;

        if (!isEnabled)
        {
            gameObject.SetActive(false);
            return;
        }

        SubscribeToGameEvents();
        ResetToCharging(0f);
    }

    private void SubscribeToGameEvents()
    {
        if (gameEventsSubscribed) return;
        gameEventsSubscribed = true;

        AbilityManager.Instance.OnChargeChanged += HandleChargeChanged;
        AbilityManager.Instance.OnAbilityReady += HandleAbilityReady;
        AbilityManager.Instance.OnAbilityActivated += HandleAbilityActivated;
        AbilityManager.Instance.OnAbilityTick += HandleAbilityTick;
        AbilityManager.Instance.OnAbilityExpired += HandleAbilityExpired;

        if (!levelUpEventsSubscribed)
        {
            levelUpEventsSubscribed = true;
            AbilityManager.Instance.OnButtonShouldHide += HideButton;
            AbilityManager.Instance.OnButtonShouldShow += ShowButton;
        }
    }

    private void HideButton() => gameObject.SetActive(false);
    private void ShowButton() => gameObject.SetActive(true);

    private void OnDestroy()
    {
        if (AbilityManager.Instance == null) return;

        AbilityManager.Instance.OnInitialized -= HandleInitialized;

        if (gameEventsSubscribed)
        {
            AbilityManager.Instance.OnChargeChanged -= HandleChargeChanged;
            AbilityManager.Instance.OnAbilityReady -= HandleAbilityReady;
            AbilityManager.Instance.OnAbilityActivated -= HandleAbilityActivated;
            AbilityManager.Instance.OnAbilityTick -= HandleAbilityTick;
            AbilityManager.Instance.OnAbilityExpired -= HandleAbilityExpired;
        }

        if (levelUpEventsSubscribed)
        {
            AbilityManager.Instance.OnButtonShouldHide -= HideButton;
            AbilityManager.Instance.OnButtonShouldShow -= ShowButton;
        }
    }

    private void HandleChargeChanged(float current, float max)
    {
        fillImage.fillOrigin = 0;
        fillImage.fillAmount = current / max;
    }

    private void HandleAbilityReady()
    {
        button.interactable = true;
        fillImage.fillOrigin = 0;
        fillImage.fillAmount = 1f;
        fillImage.DOColor(readyColor, 0.3f).SetUpdate(true);

        if (pulseGlow != null)
        {
            pulseGlow.SetActive(true);
            Image glowImage = pulseGlow.GetComponent<Image>();
            if (glowImage != null)
            {
                glowTween?.Kill();
                glowTween = glowImage.DOFade(0.8f, 0.5f)
                    .From(0.3f)
                    .SetLoops(-1, LoopType.Yoyo)
                    .SetUpdate(true);
            }
        }

        Handheld.Vibrate();
    }

    private void HandleAbilityActivated()
    {
        button.interactable = false;
        fillImage.fillOrigin = 1;
        fillImage.fillAmount = 1f;
        fillImage.DOColor(activeColor, 0.2f).SetUpdate(true);

        glowTween?.Kill();
        if (pulseGlow != null) pulseGlow.SetActive(false);
    }

    private void HandleAbilityTick(float remainingNormalized)
    {
        fillImage.fillAmount = remainingNormalized;
    }

    private void HandleAbilityExpired()
    {
        button.interactable = false;
        fillImage.fillOrigin = 0;
        fillImage.fillAmount = 0f;
        fillImage.DOColor(chargingColor, 0.4f).SetUpdate(true);

        glowTween?.Kill();
        if (pulseGlow != null) pulseGlow.SetActive(false);
    }

    private void ResetToCharging(float fillAmount)
    {
        button.interactable = false;
        fillImage.fillOrigin = 0;
        fillImage.fillAmount = fillAmount;
        fillImage.color = chargingColor;

        glowTween?.Kill();
        if (pulseGlow != null) pulseGlow.SetActive(false);
    }
}
