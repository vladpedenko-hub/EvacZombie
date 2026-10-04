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

		Canvas canvas = MetaUI.CreateOverlayCanvas("[SideDock]", 90); // under the tutorial canvas (100), so tutorial masks cover it
		SceneManager.MoveGameObjectToScene(canvas.gameObject, scene);

		BaseView view = Object.FindAnyObjectByType<BaseView>();
		var go = new GameObject("SideDock", typeof(SideDock));
		go.transform.SetParent(canvas.transform, false);
		SideDock dock = go.GetComponent<SideDock>();
		dock.Build(canvas, MetaRuntime.Service, view, MetaRuntime.Service.DockShortcuts);

		// The Laboratory tutorial chain. It has no UI of its own, so it lives under the same canvas.
		var directorGo = new GameObject("MetaTutorialDirector", typeof(MetaTutorialDirector));
		directorGo.transform.SetParent(canvas.transform, false);
		directorGo.GetComponent<MetaTutorialDirector>().Build(MetaRuntime.Service, view, dock);
	}
}
