# TASK: Упрощение до MVP v1 (снять двойной ресурсный гейт + заморозить roguelite)
> Передай этот файл Claude Code как промпт. Два независимых, низкорисковых изменения — не затрагивают core loop.

---

## ЗАДАЧА 1 — Убрать двойное гейтирование размещения карт

**Файл:** `Assets/Scripts/UI/CardUI.cs`

Сейчас `OnBeginDrag` проверяет ОБА условия:
```csharp
if (isOnCooldown || EnergyManager.Instance.CurrentEnergy < cost || myCardData == null) return;
```

Убрать проверку энергии, оставить только cooldown:
```csharp
if (isOnCooldown || myCardData == null) return;
```

В `OnEndDrag`, убрать вызов траты энергии:
```csharp
if (success)
{
    // EnergyManager.Instance.TrySpendEnergy(cost); ← убрать эту строку
    StartCooldown();
}
```

**Не трогать:** `EnergyManager.cs` оставить в проекте как есть (просто перестаёт вызываться) — не удалять файл, не удалять компонент со сцены. `cost`-поле в `CardUI` можно оставить неиспользуемым или скрыть из инспектора позже — не критично для MVP.

**Проверка:** после изменения карты должны быть ограничены только собственным cooldown, без учёта общего пула энергии.

---

## ЗАДАЧА 2 — Заморозить Roguelite in-run прогрессию

**Не менять код.** Система построена null-safe (`RunSessionData.Instance?.…`, `XPManager.Instance?.…` и т.д. — см. `Docs/CLAUDE_CODE_TASK_Roguelite.md`, п. "ОГРАНИЧЕНИЯ", правило 4), поэтому достаточно убрать из сцены объекты, отвечающие за неё.

**Файл сцены:** `Assets/Scenes/Gameplay.unity`

Найти и **отключить (SetActive false) или удалить со сцены** следующие GameObject'ы:
1. `RunSessionData`
2. `XPManager`
3. `UpgradeManager`
4. `LevelUpScreen` (Canvas-объект с UI выбора апгрейда)

После этого все `Instance` этих классов останутся `null`, и весь код в `HelicopterController`, `Sniper`, `Bomb`, `Barricade`, `GameManager`, `Zombie`, которые опрашивают эти синглтоны через `?.`, будет их безопасно пропускать.

**Не удалять:**
- Скрипты в `Assets/Scripts/Roguelite/` и `Assets/Scripts/UI/LevelUpScreen.cs`, `UpgradeCardUI.cs`, `XPBarUI.cs`
- ScriptableObject'ы в `Assets/Resources/Upgrades/` (14 штук)
- `AbilityManager` / `AbilityButtonUI` (ultimate-способность от спасённых) — это ОТДЕЛЬНАЯ система, её НЕ трогаем, она остаётся в игре

**Проверка:** после запуска уровня НЕ должно появляться окно выбора апгрейда, XP-бар не должен отображаться (либо должен быть скрыт если завязан на несуществующий `RunSessionData`).

---

## Контекст решения

Оба пункта — часть решения от 23.08.2026 зафиксированного в Notion Dev Board (карточки "УБРАТЬ EnergyManager mana-gate" и "ЗАМОРОЗИТЬ Roguelite-объекты", фаза 🚨 Phase 0 — Critical Fix). Полное обоснование — там же и в `Docs/GDD_CORE_LOOP.md`.
