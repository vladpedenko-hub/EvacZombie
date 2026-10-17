# Claude Code Task — Card UI Quick Wins

Three independent fixes, with no dependencies on each other. Can be done one at a time and tested against each other. Touches no card data (`CardData` assets) and isn't tied to the roguelite/PlayerProfile logic.

---

## 1. Fix drag-reflow bug in `CardUI.cs`

**File:** `Assets/Scripts/UI/CardUI.cs`

**Problem:** `OnBeginDrag` does `transform.SetParent(transform.root, false)` — it pulls the card out of `cardsPanel`, which has a Layout Group. This changes the number of children in the group, and it recalculates the positions of the remaining cards → they "jump"/shift while the drag is in progress.

**Fix:** don't remove the card from the panel at all. Instead of reparenting, exclude it from the layout calculation (`LayoutElement.ignoreLayout`) and raise it visually above the rest via its own `Canvas` with override sorting (the standard Unity pattern for "raise a single UI element above the rest without changing its parent" — works regardless of whether `cardsPanel` has a mask/clipping on it).

Replace the file's contents with:

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

**Important — what changes in behavior:** after `OnEndDrag`, once `ignoreLayout` is set back to `false`, the Layout Group recalculates the card's position back to its normal spot in the row (the card never physically left `cardsPanel`, it was only visually ignored during layout calculation). `originalParent`/`originalSiblingIndex`/`originalAnchoredPos` are no longer needed — removed.

**Verify by hand in the Editor:** drag a card in the gameplay scene. Confirm that: (a) the neighboring cards in the panel no longer shift during the drag, (b) the dragged card renders visually above the rest of the HUD graphics, (c) the card isn't clipped/doesn't disappear if the card panel has a `Mask`/`RectMask2D` on itself or a parent (if it does disappear — that's a separate case, report it, don't try to fix it on your own).

---

## 2. Wire `description` into `CardInfoPopup.cs`

**File:** `Assets/Scripts/CardInfoPopup.cs`

The `description` field on `CardData` already exists and is filled in (`[TextArea] public string description;`), it's just not displayed in the popup.

1. Add a new serialized field next to `cardNameText`:
```csharp
[SerializeField] private TextMeshProUGUI descriptionText;
```
2. In the `Show(CardData data, CardProgress progress)` method, right after the line `cardNameText.text = data.cardName;`, add:
```csharp
if (descriptionText != null) descriptionText.text = data.description;
```

**Manual step in the Editor (Vlad):** on the `CardInfoPopup` prefab, a `TextMeshProUGUI` object needs to be added/assigned to `descriptionText` in the inspector — this is not in scope for Claude Code's task, do it yourself after the code is in place (or tell Claude Code to add a new TMP Text child to the `.prefab`/`.unity` file itself by hand via YAML, the same way `Gameplay.unity` was edited before — but it's safer to do this step by hand in the Editor, so as not to break the layout).

---

## 3. Add rarity/category color border to `CardUI` (HUD) and `MetaCardUI` (tile)

**Problem:** the color-by-rarity logic is currently hardcoded only in `CardInfoPopup.cs` (see the switch on `Common/Rare/Epic/Legendary`). We want to reuse it on the HUD card and on the tile in the deck, without duplicating the same switch in 3 places.

### Step 1 — shared helper (DRY)

Create a new file `Assets/Scripts/Data/CardVisuals.cs`:
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

### Step 2 — reuse it in `CardInfoPopup.cs`

In the `Show(...)` method, replace the existing switch:
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
with:
```csharp
if (rarityText != null)
{
	rarityText.text = data.rarity.ToString();
	rarityText.color = CardVisuals.GetRarityColor(data.rarity);
}
```

### Step 3 — `CardUI.cs` (HUD)

Add a field:
```csharp
[Header("Visuals")]
public Image cardImage;
public Image cooldownFill;
public TextMeshProUGUI timerText;
public Image rarityBorder; // NEW — assign in prefab
```
In `Start()`, after setting `cardImage.sprite`:
```csharp
if (myCardData != null && rarityBorder != null)
{
	rarityBorder.color = CardVisuals.GetRarityColor(myCardData.rarity);
}
```

### Step 4 — `MetaCardUI.cs` (deck tile)

Add a field:
```csharp
[Header("UI References")]
[SerializeField] private Image cardIcon;
[SerializeField] private TextMeshProUGUI levelText;
[SerializeField] private TextMeshProUGUI progressText;
[SerializeField] private Image progressBarFill;
[SerializeField] private Image rarityBorder; // NEW — assign in prefab
```
In `Setup(...)`, after `cardIcon.sprite = data.icon;`:
```csharp
if (rarityBorder != null) rarityBorder.color = CardVisuals.GetRarityColor(data.rarity);
```
In `SetupLocked(...)` there's no need to color the border (the card is hidden/a silhouette) — it can be left gray, or set to `rarityBorder.color = Color.black` for consistency with the current silhouette effect.

**Manual step in the Editor (Vlad):** add a child `Image` (border/outline) to the `CardUI` and `MetaCardUI` prefabs in the Editor, and assign it to the `rarityBorder` field in the inspector. Without this step the code will compile, but the border won't be visible (the field stays `null`, every access is guarded with `!= null`).

---

## Acceptance checklist
- [ ] Dragging a card in the gameplay scene no longer shifts the neighboring cards in the panel
- [ ] The card popup (tap in the deck) shows the `description` text
- [ ] The border on the HUD card and on the deck tile is colored by rarity (once Vlad assigns the `Image` in the inspector)
- [ ] Nothing in the existing card-opening/upgrade flow in the Deck scene is broken
