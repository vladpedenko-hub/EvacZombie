using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

// [ГДЕ ВИСИТ]: На пустом объекте GlobalCheatManager на самой первой сцене игры.
// [ПОВЕДЕНИЕ]: Скрипт сам сделает себя бессмертным и будет доступен везде.
public class CheatManager : MonoBehaviour
{
	public static CheatManager Instance;

	[Header("Префабы для спавна")]
	public GameObject humanPrefab;
	public GameObject zombiePrefab;

	private Camera mainCam;
	private bool isDevMenuUnlocked = false;
	private bool showMenu = false;
	private enum SpawnMode { None, Human, Zombie }
	private SpawnMode currentMode = SpawnMode.None;

	private int tapCount = 0;
	private float tapTimer = 0f;

	private void Awake()
	{
		// --- ЛОГИКА СИНГЛТОНА (БЕССМЕРТИЯ) ---
		if (Instance == null)
		{
			Instance = this;
			DontDestroyOnLoad(gameObject); // Не уничтожать при смене сцен
		}
		else
		{
			Destroy(gameObject); // Удалить дубликат, если он случайно появился
			return;
		}
	}

	private void Update()
	{
		// Нам нужно обновлять ссылку на камеру на каждой новой сцене
		if (mainCam == null) mainCam = Camera.main;

		// Секретный анлок (5 тапов в левый верхний угол)
		HandleSecretUnlock();

		// ПК-КНОПКИ (РАБОТАЮТ ВЕЗДЕ)
		if (Input.GetKeyDown(KeyCode.F1)) GiveMaxResources();
		if (Input.GetKeyDown(KeyCode.F7)) ResetSaves();
		if (Input.GetKeyDown(KeyCode.F8)) GiveCardsCheat();
		if (Input.GetKeyDown(KeyCode.F9)) AddHardCurrency(); // Чит на Ученых (Хард)

		// Остальные читы работают только если мы нашли GameManager (значит мы в бою)
		if (GameManager.Instance != null)
		{
			if (Input.GetKeyDown(KeyCode.F2)) currentMode = SpawnMode.Human;
			if (Input.GetKeyDown(KeyCode.F3)) currentMode = SpawnMode.Zombie;
			if (Input.GetKeyDown(KeyCode.F4)) FillMana();
			if (Input.GetKeyDown(KeyCode.F5)) CheatWin();
			if (Input.GetKeyDown(KeyCode.F6)) AddCurrency(); // Чит на Людей (Софт)

			if (currentMode != SpawnMode.None && Input.GetMouseButtonDown(0))
			{
				if (showMenu && Input.mousePosition.x < Screen.width * 0.35f) return;
				SpawnAtTap(Input.mousePosition);
			}
		}
	}

	private void HandleSecretUnlock()
	{
		if (isDevMenuUnlocked) return;

		if (tapTimer > 0) tapTimer -= Time.deltaTime;
		else tapCount = 0;

		if (Input.GetMouseButtonDown(0))
		{
			if (Input.mousePosition.x < Screen.width * 0.3f && Input.mousePosition.y > Screen.height * 0.7f)
			{
				tapCount++;
				tapTimer = 1.0f;
				if (tapCount >= 5) isDevMenuUnlocked = true;
			}
		}
	}

	private void OnGUI()
	{
		if (!isDevMenuUnlocked) return;

		int fontSize = Mathf.Clamp(Screen.width / 55, 12, 24);
		GUI.skin.button.fontSize = fontSize;
		GUI.skin.label.fontSize = fontSize;

		float btnW = Screen.width * 0.25f;
		float btnH = Screen.height * 0.05f;
		float padding = 10f;

		if (GUI.Button(new Rect(padding, padding, btnW, btnH), "🛠 DEV MENU"))
		{
			showMenu = !showMenu;
			currentMode = SpawnMode.None;
		}

		if (showMenu)
		{
			float yPos = padding + btnH + padding;

			if (GUI.Button(new Rect(padding, yPos, btnW, btnH), "Unlock All (F1)")) GiveMaxResources();
			yPos += btnH + padding;

			if (GUI.Button(new Rect(padding, yPos, btnW, btnH), "+100 Shards + Cash + Energy (F8)")) GiveCardsCheat();
			yPos += btnH + padding;

			if (GUI.Button(new Rect(padding, yPos, btnW, btnH), "RESET ALL (F7)")) ResetSaves();
			yPos += btnH + padding;

			if (GameManager.Instance != null)
			{
				string hText = currentMode == SpawnMode.Human ? "> TAP MAP <" : "Spawn Human (F2)";
				if (GUI.Button(new Rect(padding, yPos, btnW, btnH), hText)) currentMode = SpawnMode.Human;
				yPos += btnH + padding;

				string zText = currentMode == SpawnMode.Zombie ? "> TAP MAP <" : "Spawn Zombie (F3)";
				if (GUI.Button(new Rect(padding, yPos, btnW, btnH), zText)) currentMode = SpawnMode.Zombie;
				yPos += btnH + padding;

				if (GUI.Button(new Rect(padding, yPos, btnW, btnH), "Win Level (F5)")) CheatWin();
				yPos += btnH + padding;
			}

			if (GUI.Button(new Rect(padding, yPos, btnW, btnH), "+100 People (F6)")) AddCurrency();
			yPos += btnH + padding;

			if (GUI.Button(new Rect(padding, yPos, btnW, btnH), "+100 Scientists (F9)")) AddHardCurrency();
		}
	}

	private void ResetSaves()
	{
		if (PlayerProfile.Instance != null) PlayerProfile.Instance.ResetProfile();
		Debug.Log("<color=red>СБРОС: Все данные стерты. Перезапуск...</color>");
		SceneManager.LoadScene(0);
	}

	private void GiveMaxResources()
	{
		if (PlayerProfile.Instance == null) return;

		PlayerProfile.Instance.totalCurrency += 10000;
		PlayerProfile.Instance.totalScientistsCurrency += 1000;

		// --- НОВОЕ: Чит на энергию ---
		PlayerProfile.Instance.currentEnergy = PlayerProfile.Instance.maxEnergy;

		foreach (CardData card in PlayerProfile.Instance.allAvailableCards)
		{
			if (PlayerProfile.Instance.ownedCardsProgress.Find(p => p.cardId == card.name) == null)
				PlayerProfile.Instance.ownedCardsProgress.Add(new CardProgress(card.name));
		}

		PlayerProfile.Instance.SaveProfile();
		RefreshDeckMenu();
	}

	private void GiveCardsCheat()
	{
		if (PlayerProfile.Instance == null) return;

		PlayerProfile.Instance.totalCurrency += 5000;

		// --- НОВОЕ: Чит на энергию ---
		PlayerProfile.Instance.currentEnergy = PlayerProfile.Instance.maxEnergy;

		foreach (CardData card in PlayerProfile.Instance.allAvailableCards)
		{
			CardProgress progress = PlayerProfile.Instance.ownedCardsProgress.Find(p => p.cardId == card.name);

			if (progress == null)
			{
				progress = new CardProgress(card.name);
				PlayerProfile.Instance.ownedCardsProgress.Add(progress);
			}

			progress.collectedShards += 100;
		}

		PlayerProfile.Instance.SaveProfile();
		RefreshDeckMenu();
		Debug.Log("<color=green>ЧИТ: Добавлено +100 дубликатов карт, 5000 людей и фулл энергия!</color>");
	}

	private void RefreshDeckMenu()
	{
		var menu = FindFirstObjectByType<DeckMenuManager>();
		if (menu != null) menu.RefreshUI();
	}

	private void CheatWin() => GameManager.Instance?.EndLevel();
	private void FillMana() => EnergyManager.Instance?.CheatFillEnergy();

	private void AddCurrency()
	{
		if (GameManager.Instance != null) GameManager.Instance.AddRescuedHumans(10, Vector3.zero);
		else if (PlayerProfile.Instance != null)
		{
			PlayerProfile.Instance.totalCurrency += 100;
			PlayerProfile.Instance.SaveProfile();
		}
	}

	private void AddHardCurrency()
	{
		if (PlayerProfile.Instance != null)
		{
			PlayerProfile.Instance.totalScientistsCurrency += 100;
			PlayerProfile.Instance.SaveProfile();
		}
	}

	private void SpawnAtTap(Vector2 screenPos)
	{
		GameObject prefab = currentMode == SpawnMode.Human ? humanPrefab : zombiePrefab;
		if (prefab == null || mainCam == null) return;

		Ray ray = mainCam.ScreenPointToRay(screenPos);
		if (Physics.Raycast(ray, out RaycastHit hit))
		{
			if (NavMesh.SamplePosition(hit.point, out NavMeshHit navHit, 5f, NavMesh.AllAreas))
				Instantiate(prefab, navHit.position, Quaternion.identity);
		}
	}
}