using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Adds a gear button (top-right, inside the safe area) to the MainMenu scene at runtime — no scene edits.
/// It lives on its own overlay canvas, so it sits above the menu tabs/panels.
/// If it collides with future HUD: change Offset below, or remove this class and call SettingsPopup.Show()
/// from any button you place in the scene yourself.
/// </summary>
public static class SettingsButtonInjector
{
	private const string MenuSceneName = "MainMenu";
	private static readonly Vector2 Offset = new Vector2(-24f, -24f); // from the safe-area top-right corner
	private const float ButtonSize = 96f;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Init()
	{
		SceneManager.sceneLoaded -= OnSceneLoaded;
		SceneManager.sceneLoaded += OnSceneLoaded;
	}

	private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		if (scene.name != MenuSceneName) return;

		// Lives in the menu scene -> destroyed with it automatically.
		Canvas canvas = RuntimeUiKit.CreateOverlayCanvas("[SettingsButton]", 4000);
		SceneManager.MoveGameObjectToScene(canvas.gameObject, scene); // guarantee it dies with the menu scene

		RectTransform safe = RuntimeUiKit.CreateRect("SafeArea", canvas.transform);
		RuntimeUiKit.Stretch(safe);
		safe.gameObject.AddComponent<SafeArea>(); // existing component: fits the rect to Screen.safeArea

		Image bg = RuntimeUiKit.CreateImage("GearButton", safe, new Color(0.08f, 0.09f, 0.12f, 0.75f));
		var btn = bg.gameObject.AddComponent<Button>();
		btn.targetGraphic = bg;
		bg.gameObject.AddComponent<SuppressClickSfx>(); // popup-open plays its own sound

		var rt = bg.rectTransform;
		rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
		rt.sizeDelta = new Vector2(ButtonSize, ButtonSize);
		rt.anchoredPosition = Offset;

		Image icon = RuntimeUiKit.CreateImage("Icon", rt, Color.white, rounded: false);
		icon.sprite = RuntimeUiKit.Gear();
		icon.raycastTarget = false;
		RuntimeUiKit.SetAnchors(icon.rectTransform, 0.15f, 0.15f, 0.85f, 0.85f);

		btn.onClick.AddListener(SettingsPopup.Show);
	}
}
