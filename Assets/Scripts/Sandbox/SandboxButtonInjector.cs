using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// feature/sandbox-level: adds a "SANDBOX" button to the MainMenu scene at runtime -- no scene
// edits, same pattern as SettingsButtonInjector/RuntimeUiKit. Debug.isDebugBuild is true in the
// Editor and in development player builds, false in release builds -- the exact "Editor/
// Development builds" visibility the brief asks for, with no extra scripting-define plumbing.
public static class SandboxButtonInjector
{
	private const string MenuSceneName = "MainMenu";
	private static readonly Vector2 Offset = new Vector2(24f, 24f); // from safe-area bottom-left
	private const float ButtonWidth = 220f;
	private const float ButtonHeight = 72f;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Init()
	{
		if (!Debug.isDebugBuild) return;

		SceneManager.sceneLoaded -= OnSceneLoaded;
		SceneManager.sceneLoaded += OnSceneLoaded;
	}

	private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		if (scene.name != MenuSceneName) return;

		Canvas canvas = RuntimeUiKit.CreateOverlayCanvas("[SandboxButton]", 4000);
		SceneManager.MoveGameObjectToScene(canvas.gameObject, scene);

		RectTransform safe = RuntimeUiKit.CreateRect("SafeArea", canvas.transform);
		RuntimeUiKit.Stretch(safe);
		safe.gameObject.AddComponent<SafeArea>();

		Button btn = RuntimeUiKit.CreateButton("SandboxButton", safe, "SANDBOX", 28f,
			new Color(0.85f, 0.25f, 0.2f, 0.9f), Color.white);

		RectTransform rt = btn.GetComponent<RectTransform>();
		rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0f);
		rt.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
		rt.anchoredPosition = Offset;

		btn.onClick.AddListener(LaunchSandbox);
	}

	private static void LaunchSandbox()
	{
		SandboxConfig config = Resources.Load<SandboxConfig>("SandboxConfig");
		if (config == null || config.sandboxLevel == null)
		{
			Debug.LogError("[Sandbox] Resources/SandboxConfig.asset is missing or its sandboxLevel isn't assigned.");
			return;
		}

		LevelManager.PendingSandboxLevel = config.sandboxLevel;
		SceneManager.LoadScene("Gameplay");
	}
}
