using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Settings popup, built entirely in code (no prefab/scene setup). Open it from anywhere with SettingsPopup.Show().
/// Contents: Sound, Music, Vibration, 60 FPS, Privacy Policy / Terms / Contact (see GameLinks), version.
/// To restyle: replace the construction code with a designed prefab and keep the same Show()/Close() API.
/// </summary>
public class SettingsPopup : MonoBehaviour
{
	private static SettingsPopup instance;

	public static bool IsOpen => instance != null && instance.root != null && instance.root.activeSelf;

	public static void Show()
	{
		if (instance == null) instance = Create();
		instance.Open();
	}

	private static readonly Color PanelColor = new Color(0.11f, 0.12f, 0.15f, 0.98f);
	private static readonly Color RowColor = new Color(1f, 1f, 1f, 0.06f);
	private static readonly Color OnColor = new Color(0.25f, 0.70f, 0.35f, 1f);
	private static readonly Color OffColor = new Color(0.35f, 0.37f, 0.42f, 1f);
	private static readonly Color LinkColor = new Color(0.22f, 0.30f, 0.42f, 1f);
	private static readonly Color AccentColor = new Color(0.85f, 0.35f, 0.20f, 1f);

	private GameObject root;
	private RectTransform panel;
	private Action refresh;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ResetStatics() => instance = null;

	private static SettingsPopup Create()
	{
		Canvas canvas = RuntimeUiKit.CreateOverlayCanvas("[SettingsPopup]", 5000);
		DontDestroyOnLoad(canvas.gameObject);

		var popup = canvas.gameObject.AddComponent<SettingsPopup>();
		popup.Build(canvas.transform);
		return popup;
	}

	private void Build(Transform canvasTransform)
	{
		root = RuntimeUiKit.CreateRect("Root", canvasTransform).gameObject;
		RuntimeUiKit.Stretch((RectTransform)root.transform);

		// Dim background — tap outside to close
		Image dim = RuntimeUiKit.CreateImage("Dim", root.transform, new Color(0f, 0f, 0f, 0.65f), rounded: false);
		RuntimeUiKit.Stretch(dim.rectTransform);
		var dimButton = dim.gameObject.AddComponent<Button>();
		dimButton.targetGraphic = dim;
		dimButton.transition = Selectable.Transition.None;
		dimButton.onClick.AddListener(Close);
		dim.gameObject.AddComponent<SuppressClickSfx>();

		// Panel
		Image panelImg = RuntimeUiKit.CreateImage("Panel", root.transform, PanelColor);
		panel = panelImg.rectTransform;
		panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
		panel.sizeDelta = new Vector2(820f, 0f);
		panelImg.raycastTarget = true; // swallow taps so they don't hit the dim button

		var layout = panelImg.gameObject.AddComponent<VerticalLayoutGroup>();
		layout.padding = new RectOffset(40, 40, 40, 40);
		layout.spacing = 16f;
		layout.childAlignment = TextAnchor.UpperCenter;
		layout.childControlWidth = true;
		layout.childControlHeight = true;
		layout.childForceExpandWidth = true;
		layout.childForceExpandHeight = false;

		var fitter = panelImg.gameObject.AddComponent<ContentSizeFitter>();
		fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
		fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

		// Title
		TextMeshProUGUI title = RuntimeUiKit.CreateText("Title", panel, "SETTINGS", 56f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
		SetHeight(title.gameObject, 90f);

		// Toggles
		AddToggleRow("Sound", () => GameSettings.SfxEnabled, v => GameSettings.SfxEnabled = v);
		AddToggleRow("Music", () => GameSettings.MusicEnabled, v => GameSettings.MusicEnabled = v);

		if (Application.isEditor || SystemInfo.supportsVibration)
		{
			AddToggleRow("Vibration", () => GameSettings.VibrationEnabled, v =>
			{
				GameSettings.VibrationEnabled = v;
				if (v) Haptics.Play(HapticType.Medium); // let the player feel it right away
			});
		}

		AddToggleRow("Smooth 60 FPS", () => GameSettings.Fps60, v => GameSettings.Fps60 = v);

		// Links
		AddLinkButton("Privacy Policy", () => OpenUrl(GameLinks.PrivacyPolicyUrl, "PrivacyPolicyUrl"));
		AddLinkButton("Terms of Service", () => OpenUrl(GameLinks.TermsOfServiceUrl, "TermsOfServiceUrl"));
		AddLinkButton("Contact Support", OpenSupportMail);

		// Version
		TextMeshProUGUI version = RuntimeUiKit.CreateText("Version", panel, "EvacZombie v" + Application.version, 28f,
			new Color(1f, 1f, 1f, 0.45f), TextAlignmentOptions.Center);
		SetHeight(version.gameObject, 50f);

		// Close
		Button close = RuntimeUiKit.CreateButton("CloseButton", panel, "CLOSE", 38f, AccentColor, Color.white);
		SetHeight(close.gameObject, 100f);
		close.onClick.AddListener(Close);

		GameSettings.Changed += RefreshAll;
		root.SetActive(false);
	}

	private void OnDestroy()
	{
		GameSettings.Changed -= RefreshAll;
		if (instance == this) instance = null;
	}

	private void RefreshAll() => refresh?.Invoke();

	private void Open()
	{
		RefreshAll();
		root.SetActive(true);

		panel.DOKill();
		panel.localScale = Vector3.one * 0.9f;
		panel.DOScale(1f, 0.18f).SetEase(Ease.OutBack).SetUpdate(true);

		Feedback.Play(FeedbackEvent.UiPopupOpen);
	}

	private void Close()
	{
		if (!root.activeSelf) return;

		panel.DOKill();
		root.SetActive(false);
		Feedback.Play(FeedbackEvent.UiBack);
	}

	// ------------------------------------------------------------------ rows

	private static void SetHeight(GameObject go, float height)
	{
		var le = go.GetComponent<LayoutElement>();
		if (le == null) le = go.AddComponent<LayoutElement>();
		le.preferredHeight = height;
		le.minHeight = height;
	}

	private void AddToggleRow(string label, Func<bool> get, Action<bool> set)
	{
		Image row = RuntimeUiKit.CreateImage(label + "Row", panel, RowColor);
		row.raycastTarget = false;
		SetHeight(row.gameObject, 100f);

		TextMeshProUGUI text = RuntimeUiKit.CreateText("Label", row.transform, label, 38f, Color.white, TextAlignmentOptions.MidlineLeft);
		RuntimeUiKit.SetAnchors(text.rectTransform, 0f, 0f, 0.62f, 1f);
		text.rectTransform.offsetMin = new Vector2(30f, 0f);

		Button toggle = RuntimeUiKit.CreateButton("Toggle", row.transform, "ON", 34f, OnColor, Color.white);
		toggle.gameObject.AddComponent<SuppressClickSfx>(); // the toggle plays its own sound
		var rt = (RectTransform)toggle.transform;
		rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
		rt.pivot = new Vector2(1f, 0.5f);
		rt.sizeDelta = new Vector2(190f, 66f);
		rt.anchoredPosition = new Vector2(-24f, 0f);

		TextMeshProUGUI stateLabel = toggle.GetComponentInChildren<TextMeshProUGUI>();
		Image stateBg = toggle.GetComponent<Image>();

		Action update = () =>
		{
			bool on = get();
			stateLabel.text = on ? "ON" : "OFF";
			stateBg.color = on ? OnColor : OffColor;
		};
		refresh += update;
		update();

		toggle.onClick.AddListener(() =>
		{
			set(!get());
			update();
			Feedback.Play(FeedbackEvent.UiToggle);
		});
	}

	private void AddLinkButton(string label, Action onClick)
	{
		Button b = RuntimeUiKit.CreateButton(label.Replace(" ", "") + "Button", panel, label, 34f, LinkColor, Color.white);
		SetHeight(b.gameObject, 90f);
		b.onClick.AddListener(() => onClick());
	}

	// ------------------------------------------------------------------ actions

	private static void OpenUrl(string url, string fieldName)
	{
		if (string.IsNullOrEmpty(url))
		{
			Debug.LogWarning("[Settings] GameLinks." + fieldName + " is empty — set it before store submission.");
			Feedback.Play(FeedbackEvent.UiError);
			return;
		}

		Application.OpenURL(url);
	}

	private static void OpenSupportMail()
	{
		if (string.IsNullOrEmpty(GameLinks.SupportEmail))
		{
			Debug.LogWarning("[Settings] GameLinks.SupportEmail is empty — set it before store submission.");
			Feedback.Play(FeedbackEvent.UiError);
			return;
		}

		string subject = Uri.EscapeDataString("EvacZombie v" + Application.version + " feedback");
		Application.OpenURL("mailto:" + GameLinks.SupportEmail + "?subject=" + subject);
	}
}
