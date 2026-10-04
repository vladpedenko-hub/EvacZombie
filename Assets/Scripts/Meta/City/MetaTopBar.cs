using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Top bar: People and Scientists counters, plus a way back to the main menu.
public class MetaTopBar
{
	private TextMeshProUGUI people;
	private TextMeshProUGUI scientists;

	public void Build(Transform parent)
	{
		Image bar = MetaUI.ImageBox("TopBar", parent, MetaUI.Panel,
			new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -130f), Vector2.zero);

		Button back = MetaUI.Button("Back", bar.transform, "MENU", MetaUI.ButtonSecondary,
			new Vector2(0.02f, 0.18f), new Vector2(0.26f, 0.82f), Vector2.zero, Vector2.zero, out _);
		back.onClick.AddListener(() => SceneManager.LoadScene("MainMenu"));

		people = MetaUI.Text("People", bar.transform, "", 44, new Vector2(0.3f, 0f), new Vector2(0.64f, 1f),
			Vector2.zero, Vector2.zero);
		scientists = MetaUI.Text("Scientists", bar.transform, "", 44, new Vector2(0.66f, 0f), new Vector2(0.99f, 1f),
			Vector2.zero, Vector2.zero);
	}

	public void Refresh(CurrencyService currency)
	{
		if (currency == null) return;
		people.text = $"People {currency.Get(CurrencyType.People)}";
		scientists.text = $"Scientists {currency.Get(CurrencyType.Scientists)}";
	}
}
