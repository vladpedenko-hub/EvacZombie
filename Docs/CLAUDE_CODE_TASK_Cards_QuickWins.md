# Claude Code Task — Card UI Quick Wins

Три независимых фикса, без зависимостей друг от друга. Можно делать по одному и тестировать между собой. Ничего не трогает данные карт (`CardData` ассеты) и не завязано на roguelite/PlayerProfile логику.

---

## 1. Fix drag-reflow bug in `CardUI.cs`

**Файл:** `Assets/Scripts/UI/CardUI.cs`

**Проблема:** `OnBeginDrag` делает `transform.SetParent(transform.root, false)` — вытаскивает карту из `cardsPanel`, у которого есть Layout Group. Это меняет количество детей в группе, и она пересчитывает позиции оставшихся карт → они "прыгают"/сдвигаются, пока идёт драг.

**Фикс:** не убирать карту из панели вообще. Вместо реродителинга — исключить её из расчёта layout (`LayoutElement.ignoreLayout`) и поднять её визуально поверх остальных через собственный `Canvas` с override sorting (стандартный Unity-паттерн для "поднять один UI-элемент над остальными без смены родителя" — не зависит от того, есть ли на `cardsPanel` маска/clipping).

Замени содержимое файла на:

```csharp
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

[RequireComponent(typeof(CanvasGroup))]
public class CardUI : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
	[Header("Card & Settings")]
	public CardData myCardData; // Always assign CARD DATA here, not an ENUM

	[Header("Gameplay")]
	public float cost;
	public float cooldownTime = 3f;

	[Header("Visuals")]
	public Image cardImage;
	public Image cooldownFill;
	public TextMeshProUGUI timerText;

	private CanvasGroup canvasGroup;
	private RectTransform rectTransform;
	private LayoutElement layoutElement;
	private Canvas dragSortCanvas;

	private bool isOnCooldown = false;
	private float currentCooldown = 0f;
	private bool isCurrentlyDragging = false;

	private void Awake()
	{
		canvasGroup = GetComponent<CanvasGroup>();
		rectTransform = GetComponent<RectTransform>();

		layoutElement = GetComponent<LayoutElement>();
		if (layoutElement == null) layoutElement = gameObject.AddComponent<LayoutElement>();

		dragSortCanvas = GetComponent<Canvas>();
		if (dragSortCanvas == null) dragSortCanvas = gameObject.AddComponent<Canvas>();
		dragSortCanvas.overrideSorting = false; // off by default, only on while dragging

		if (GetComponent<GraphicRaycaster>() == null)
			gameObject.AddComponent<GraphicRaycaster>();
	}

	private void Start()
	{
		if (cooldownFill != null) cooldownFill.fillAmount = 0;
		if (timerText != null) timerText.text = "";

		if (myCardData != null && cardImage != null)
		{
			cardImage.sprite = myCardData.icon;
		}
	}

	private void Update()
	{
		if (isOnCooldown)
		{
			currentCooldown -= Time.deltaTime;

			if (cooldownFill != null)
			{
				cooldownFill.gameObject.SetActive(true);
				cooldownFill.fillAmount = currentCooldown / cooldownTime;
			}

			if (timerText != null)
			{
				timerText.gameObject.SetActive(true);
				timerText.text = currentCooldown.ToString("F1");
			}

			if (currentCooldown <= 0)
			{
				isOnCooldown = false;
				if (cooldownFill != null) cooldownFill.fillAmount = 0;
				if (timerText != null) timerText.text = "";
				if (cardImage != null) cardImage.color = Color.white;
			}
		}
	}

	public void OnBeginDrag(PointerEventData eventData)
	{
		if (isOnCooldown || myCardData == null) return;

		// Exclude from the Layout Group's arrangement WITHOUT leaving cardsPanel —
		// this is what stops the other cards from reflowing.
		layoutElement.ignoreLayout = true;

		// Render above siblings/HUD without reparenting.
		dragSortCanvas.overrideSorting = true;
		dragSortCanvas.sortingOrder = 1000;

		canvasGroup.blocksRaycasts = false;
		if (cardImage != null) cardImage.color = new Color(1, 1, 1, 0.5f);

		isCurrentlyDragging = true;

		InputManager.Instance.StartDragging(myCardData);
	}

	public void OnDrag(PointerEventData eventData)
	{
		if (!isCurrentlyDragging) return;

		rectTransform.position = eventData.position;
		InputManager.Instance.UpdateDragging(eventData.position);
	}

	public void OnEndDrag(PointerEventData eventData)
	{
		if (!isCurrentlyDragging) return;
		isCurrentlyDragging = false;

		layoutElement.ignoreLayout = false;
		dragSortCanvas.overrideSorting = false;

		canvasGroup.blocksRaycasts = true;
		if (cardImage != null) cardImage.color = Color.white;

		bool success = InputManager.Instance.EndDragging();

		if (success)
		{
			StartCooldown();
		}
	}

	public void StartCooldown()
	{
		if (!isOnCooldown)
		{
			isOnCooldown = true;
			currentCooldown = cooldownTime;
			if (cardImage != null) cardImage.color = Color.gray;
		}
	}
}
```

**Важно — что изменилось в поведении:** после `OnEndDrag`, если `ignoreLayout` возвращается в `false`, Layout Group пересчитает позицию карты обратно на её нормальное место в ряду (карта никогда физически не покидала `cardsPanel`, только визуально игнорировалась при расчёте). `originalParent`/`originalSiblingIndex`/`originalAnchoredPos` больше не нужны — убраны.

**Проверить руками в Editor:** перетащить карту в игровую сцену. Убедиться, что: (а) соседние карты в панели больше НЕ сдвигаются во время драга, (б) перетаскиваемая карта визуально поверх остальной HUD-графики, (в) карта не обрезается/не пропадает, если панель карт имеет `Mask`/`RectMask2D` на себе или родителе (если пропадает — это отдельный кейс, сообщить, не чинить самостоятельно).

---

## 2. Wire `description` into `CardInfoPopup.cs`

**Файл:** `Assets/Scripts/CardInfoPopup.cs`

Поле `description` в `CardData` уже существует и заполнено (`[TextArea] public string description;`), просто не выводится в попапе.

1. Добавь новое serialized-поле рядом с `cardNameText`:
```csharp
[SerializeField] private TextMeshProUGUI descriptionText;
```
2. В методе `Show(CardData data, CardProgress progress)`, сразу после строки `cardNameText.text = data.cardName;`, добавь:
```csharp
if (descriptionText != null) descriptionText.text = data.description;
```

**Ручной шаг в Editor (Vlad):** на префабе `CardInfoPopup` нужно добавить/назначить `TextMeshProUGUI` объект под `descriptionText` в инспекторе — это не в скоупе задачи для Claude Code, сделать самому после того, как код появится (либо сказать Клод Коду добавить новый TMP Text child в сам `.prefab`/`.unity` файл вручную через YAML, по аналогии с тем, как раньше редактировался `Gameplay.unity` — но безопаснее сделать этот шаг руками в Editor, чтобы не сломать вёрстку).

---

## 3. Add rarity/category color border to `CardUI` (HUD) and `MetaCardUI` (tile)

**Проблема:** цвет по редкости сейчас зашит только в `CardInfoPopup.cs` (см. switch на `Common/Rare/Epic/Legendary`). Хотим переиспользовать его на HUD-карте и на плитке в колоде, не дублируя один и тот же switch в 3 местах.

### Шаг 1 — общий helper (DRY)

Создай новый файл `Assets/Scripts/Data/CardVisuals.cs`:
```csharp
using UnityEngine;

public static class CardVisuals
{
	public static Color GetRarityColor(CardRarity rarity)
	{
		switch (rarity)
		{
			case CardRarity.Common: return Color.white;
			case CardRarity.Rare: return new Color(0.2f, 0.6f, 1f);
			case CardRarity.Epic: return new Color(0.7f, 0.2f, 1f);
			case CardRarity.Legendary: return new Color(1f, 0.6f, 0f);
			default: return Color.white;
		}
	}
}
```

### Шаг 2 — переиспользовать в `CardInfoPopup.cs`

В методе `Show(...)`, замени существующий switch:
```csharp
if (rarityText != null)
{
	rarityText.text = data.rarity.ToString();
	switch (data.rarity)
	{
		case CardRarity.Common: rarityText.color = Color.white; break;
		case CardRarity.Rare: rarityText.color = new Color(0.2f, 0.6f, 1f); break;
		case CardRarity.Epic: rarityText.color = new Color(0.7f, 0.2f, 1f); break;
		case CardRarity.Legendary: rarityText.color = new Color(1f, 0.6f, 0f); break;
	}
}
```
на:
```csharp
if (rarityText != null)
{
	rarityText.text = data.rarity.ToString();
	rarityText.color = CardVisuals.GetRarityColor(data.rarity);
}
```

### Шаг 3 — `CardUI.cs` (HUD)

Добавь поле:
```csharp
[Header("Visuals")]
public Image cardImage;
public Image cooldownFill;
public TextMeshProUGUI timerText;
public Image rarityBorder; // NEW — assign in prefab
```
В `Start()`, после установки `cardImage.sprite`:
```csharp
if (myCardData != null && rarityBorder != null)
{
	rarityBorder.color = CardVisuals.GetRarityColor(myCardData.rarity);
}
```

### Шаг 4 — `MetaCardUI.cs` (плитка в колоде)

Добавь поле:
```csharp
[Header("UI References")]
[SerializeField] private Image cardIcon;
[SerializeField] private TextMeshProUGUI levelText;
[SerializeField] private TextMeshProUGUI progressText;
[SerializeField] private Image progressBarFill;
[SerializeField] private Image rarityBorder; // NEW — assign in prefab
```
В `Setup(...)`, после `cardIcon.sprite = data.icon;`:
```csharp
if (rarityBorder != null) rarityBorder.color = CardVisuals.GetRarityColor(data.rarity);
```
В `SetupLocked(...)` рамку красить не нужно (карта скрыта/silhouette) — можно оставить серой или сделать `rarityBorder.color = Color.black` для консистентности с текущим silhouette-эффектом.

**Ручной шаг в Editor (Vlad):** добавить дочерний `Image` (рамка/обводка) на префабы `CardUI` и `MetaCardUI` в Editor, назначить в поле `rarityBorder` в инспекторе. Без этого шага код скомпилируется, но рамка не будет видна (поле останется `null`, все обращения защищены `!= null`).

---

## Acceptance checklist
- [ ] Драг карты в геймплейной сцене больше не сдвигает соседние карты в панели
- [ ] В попапе карты (тап в колоде) отображается текст `description`
- [ ] Рамка на HUD-карте и на плитке в колоде окрашена по редкости (после того как Vlad назначит `Image` в инспекторе)
- [ ] Ничего не сломалось в существующем flow открытия/апгрейда карты в Deck-сцене
