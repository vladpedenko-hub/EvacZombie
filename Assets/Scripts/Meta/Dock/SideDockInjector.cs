using UnityEngine;
using UnityEngine.SceneManagement;

// Adds the side dock to MainMenu at runtime, so no scene edits are needed. Same pattern as the settings gear.
// The dock lives on its own overlay canvas and is destroyed with the MainMenu scene.
public static class SideDockInjector
{
	private const string MenuSceneName = "MainMenu";

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Init()
	{
		SceneManager.sceneLoaded -= OnSceneLoaded;
		SceneManager.sceneLoaded += OnSceneLoaded;
	}

	private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		if (scene.name != MenuSceneName) return;
		if (!MetaRuntime.EnsureInitialized() || MetaRuntime.Service == null) return;

		Canvas canvas = MetaUI.CreateOverlayCanvas("[SideDock]", 1500);
		SceneManager.MoveGameObjectToScene(canvas.gameObject, scene);

		var go = new GameObject("SideDock", typeof(SideDock));
		go.transform.SetParent(canvas.transform, false);
		go.GetComponent<SideDock>().Build(canvas, MetaRuntime.Service,
			Object.FindAnyObjectByType<BaseView>(), MetaRuntime.Service.DockShortcuts);
	}
}
