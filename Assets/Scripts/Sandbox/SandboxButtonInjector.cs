using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// feature/sandbox-level: adds sandbox launch buttons to the MainMenu scene at runtime -- no scene
// edits, same pattern as SettingsButtonInjector/RuntimeUiKit. Debug.isDebugBuild is true in the
// Editor and in development player builds, false in release builds -- the exact "Editor/
// Development builds" visibility the brief asks for, with no extra scripting-define plumbing.
//
// feature/sandbox-two-levels: now two buttons side by side -- "Sandbox S" (fast daily testing,
// one screen) and "Sandbox L" (the original stress-test city), launching
// SandboxConfig.sandboxLevelSmall/sandboxLevel respectively.
public static class SandboxButtonInjector
{
	private const string MenuSceneName = "MainMenu";
	private static readonly Vector2 Offset = new Vector2(24f, 24f); // from safe-area bottom-left
	private const float ButtonWidth = 150f;
	private const float ButtonHeight = 72f;
	private const float ButtonSpacing = 8f;

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

		Button smallBtn = RuntimeUiKit.CreateButton("SandboxButtonSmall", safe, "Sandbox S", 26f,
			new Color(0.85f, 0.25f, 0.2f, 0.9f), Color.white);
		RectTransform smallRt = smallBtn.GetComponent<RectTransform>();
		smallRt.anchorMin = smallRt.anchorMax = smallRt.pivot = new Vector2(0f, 0f);
		smallRt.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
		smallRt.anchoredPosition = Offset;
		smallBtn.onClick.AddListener(() => LaunchSandbox(small: true));

		Button largeBtn = RuntimeUiKit.CreateButton("SandboxButtonLarge", safe, "Sandbox L", 26f,
			new Color(0.85f, 0.25f, 0.2f, 0.9f), Color.white);
		RectTransform largeRt = largeBtn.GetComponent<RectTransform>();
		largeRt.anchorMin = largeRt.anchorMax = largeRt.pivot = new Vector2(0f, 0f);
		largeRt.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
		largeRt.anchoredPosition = Offset + new Vector2(ButtonWidth + ButtonSpacing, 0f);
		largeBtn.onClick.AddListener(() => LaunchSandbox(small: false));
	}

	private static void LaunchSandbox(bool small)
	{
		SandboxConfig config = Resources.Load<SandboxConfig>("SandboxConfig");
		if (config == null)
		{
			Debug.LogError("[Sandbox] Resources/SandboxConfig.asset is missing.");
			return;
		}

		LevelData target = small ? config.sandboxLevelSmall : config.sandboxLevel;
		if (target == null)
		{
			Debug.LogError($"[Sandbox] SandboxConfig.{(small ? "sandboxLevelSmall" : "sandboxLevel")} isn't assigned.");
			return;
		}

		LevelManager.PendingSandboxLevel = target;
		SceneManager.LoadScene("Gameplay");
	}
}
