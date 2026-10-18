using TMPro;
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

    // feature/hud-sandbox-ui (step 2): the ability widget shrank to a plain icon + charge ring,
    // so the remaining-civilians count that used to be implicit in its (larger) on-screen size
    // now needs an explicit label. Built in code (RuntimeUiKit, same pattern as the rest of this
    // project's newer HUD) -- no scene/prefab edit needed.
    private TextMeshProUGUI remainingText;

    private void Start()
    {
        if (AbilityManager.Instance == null)
        {
            gameObject.SetActive(false);
            return;
        }

        EnsureRemainingLabel();

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
        SetRemainingText(Mathf.Max(0, Mathf.CeilToInt(max - current)));
    }

    private void HandleAbilityReady()
    {
        button.interactable = true;
        fillImage.fillOrigin = 0;
        fillImage.fillAmount = 1f;
        fillImage.DOColor(readyColor, 0.3f).SetUpdate(true);
        SetRemainingText(0);

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

        Feedback.Play(FeedbackEvent.AbilityReady);
    }

    private void HandleAbilityActivated()
    {
        Feedback.Play(FeedbackEvent.AbilityActivate);
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

        int max = AbilityManager.Instance != null ? AbilityManager.Instance.maxCharge : 0;
        SetRemainingText(Mathf.Max(0, Mathf.RoundToInt(max * (1f - fillAmount))));
    }

    // feature/hud-sandbox-ui (step 2): lazily builds the small "remaining civilians needed" label
    // -- a child of this button's own rect, so it scales with whatever size the button is given
    // (BottomZoneV2.RepositionAbilityButton currently sets 96px at the 1080 reference).
    private void EnsureRemainingLabel()
    {
        if (remainingText != null) return;

        remainingText = RuntimeUiKit.CreateText("RemainingLabel", transform, "", 30f, Color.white,
            TextAlignmentOptions.Center, FontStyles.Bold);
        RectTransform rt = remainingText.rectTransform;
        rt.anchorMin = new Vector2(0.1f, 0.05f);
        rt.anchorMax = new Vector2(0.9f, 0.4f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private void SetRemainingText(int remaining)
    {
        if (remainingText == null) return;
        remainingText.text = remaining.ToString();
    }
}
