# Feature Spec: Hero Ability — Civilian Speed Boost

**Статус:** Design-ready, не начата  
**Приоритет:** High (MVP feature)  
**Зависимости:** TutorialManager, GameManager, Human.cs, Scientist.cs

---

## 1. Суть фичи

Игрок накапливает заряд, спасая мирных жителей. Когда заряд полон — на экране появляется активная кнопка. Нажав на неё, игрок временно ускоряет всех мирных на карте.

**Core loop:** Спасаешь людей → копишь заряд → активируешь → спасаешь ещё больше.

---

## 2. Параметры (балансные, можно твикать)

| Параметр | Значение | Описание |
|---|---|---|
| `maxCharge` | 5 | Нужно спасти 5 мирных для полного заряда |
| `chargePerCivilian` | 1 | Каждый спасённый даёт 1 заряд |
| `abilityDuration` | 8f | Секунд действия ability |
| `speedMultiplier` | 2.0f | Во сколько раз ускоряются мирные |
| `tutorialId` | `"Tutorial_Ability"` | ID для PlayerPrefs (показывается 1 раз) |

---

## 3. Компоненты

### 3.1 `AbilityManager.cs` — новый скрипт

Синглтон. Управляет зарядом, состоянием, speed boost.

**Состояния:**
- `Idle` — идёт накопление
- `Ready` — заряд полон, ожидает нажатия
- `Active` — ability активна, идёт drain

**Публичные методы:**
```
void AddCharge(int civiliansCount)
void ActivateAbility()
float GetSpeedMultiplier()   // 1f в обычном состоянии, speedMultiplier когда Active
```

**События (C# events):**
```
event Action<float, float> OnChargeChanged   // currentCharge, maxCharge
event Action OnAbilityReady
event Action OnAbilityActivated
event Action<float> OnAbilityTick            // remainingNormalized (1→0)
event Action OnAbilityExpired
```

**Логика заряда:**
- `AddCharge()` вызывается из `GameManager.AddRescuedFromTransport()`
- Заряд не накапливается пока ability `Active`
- При переходе в `Ready` — поднимается `OnAbilityReady` + триггер туториала (один раз за сессию)

**Логика speed boost:**
- Пока state == `Active`: возвращает `speedMultiplier`
- Иначе: возвращает `1f`
- Мирные сами читают это значение через `AbilityManager.Instance.GetSpeedMultiplier()`

**Tutorial trigger (срабатывает один раз за всю игру):**
```csharp
if (!PlayerPrefs.HasKey("ABILITY_TUTORIAL_SHOWN"))
{
    PlayerPrefs.SetInt("ABILITY_TUTORIAL_SHOWN", 1);
    TutorialManager.Instance.StartTutorial(abilityReadyTutorialSequence);
}
```
> Поле `abilityReadyTutorialSequence` — ссылка на TutorialSequence SO, назначается в инспекторе.

---

### 3.2 `AbilityButtonUI.cs` — новый скрипт

Управляет визуальными состояниями кнопки. Регистрируется как TutorialTarget.

**Иерархия UI-объекта (в Canvas):**
```
AbilityButton (Button)
  ├── BackgroundImage         ← статичная иконка/рамка
  ├── FillImage (Image)       ← единственный Image с fillType Vertical
  ├── PulseGlow (Image)       ← полупрозрачный glow, скрыт по умолчанию
  └── AbilityButtonUI (.cs)
      └── TutorialTarget (.cs)  ← targetId = "ability_button"
```

**FillImage — логика двух состояний одним компонентом:**

| Состояние | `fillOrigin` | `fillAmount` | Направление |
|---|---|---|---|
| Charging | `0` (Bottom) | растёт `0 → 1` | снизу вверх |
| Active/Draining | `1` (Top) | уменьшается `1 → 0` | сверху вниз |

Смена `fillOrigin` при активации даёт визуальный "переворот" без второго Image.

**Состояния кнопки:**

1. **Charging** (Idle/накопление):
   - `Button.interactable = false`
   - `FillImage.fillOrigin = 0` (Bottom)
   - `FillImage.fillAmount` = `currentCharge / maxCharge`
   - Цвет заливки: нейтральный (серо-белый)
   - PulseGlow: `SetActive(false)`

2. **Ready** (заряд полон, ожидает нажатия):
   - `Button.interactable = true`
   - `FillImage.fillAmount = 1f`
   - Цвет заливки: акцентный (жёлтый / ярко-зелёный)
   - PulseGlow: `SetActive(true)` + DOTween pulse (alpha 0.3 → 0.8, loop Yoyo, 0.5s)
   - Вибрация устройства: `Handheld.Vibrate()` один раз при переходе

3. **Active** (ability работает, идёт drain):
   - `Button.interactable = false`
   - `FillImage.fillOrigin = 1` (Top)  ← меняем при активации
   - `FillImage.fillAmount` = `remainingNormalized` (1 → 0)
   - Цвет заливки: активный (синий / голубой — отличается от ready)
   - PulseGlow: `SetActive(false)`

**Подписки на события:**
```
AbilityManager.OnChargeChanged   → обновить fillAmount (Charging state)
AbilityManager.OnAbilityReady    → перейти в Ready state
AbilityManager.OnAbilityActivated → перейти в Active state, сменить fillOrigin
AbilityManager.OnAbilityTick      → обновить fillAmount (drain)
AbilityManager.OnAbilityExpired   → сбросить в Charging state, fillAmount = 0
```

**Анимация цвета заливки (DOTween):**
```csharp
FillImage.DOColor(readyColor, 0.3f);    // при переходе в Ready
FillImage.DOColor(activeColor, 0.2f);   // при активации
FillImage.DOColor(chargingColor, 0.4f); // при сбросе
```

---

### 3.3 Изменения в `Human.cs` и `Scientist.cs`

Минимальные правки — читать speed multiplier из AbilityManager.

В `Human.Update()`, в каждом месте где устанавливается `agent.speed`:

```csharp
// БЫЛО:
agent.speed = runSpeed;

// СТАЛО:
float boost = AbilityManager.Instance != null ? AbilityManager.Instance.GetSpeedMultiplier() : 1f;
agent.speed = runSpeed * boost;
```

Аналогично для `walkSpeed`:
```csharp
agent.speed = walkSpeed * boost;
```

То же самое в `Scientist.cs` (если там аналогичная логика скорости).

> **Важно:** `GetSpeedMultiplier()` возвращает 1f когда AbilityManager не активен — null-safe.

---

### 3.4 Изменения в `GameManager.cs`

В методе `AddRescuedFromTransport()`, после существующей логики:

```csharp
// Существующий код:
XPManager.Instance?.OnCivilianEvacuated(humans, scientists);

// Добавить строку:
AbilityManager.Instance?.AddCharge(total);
```

---

## 4. Tutorial Step — "Ability Ready"

### TutorialSequence ScriptableObject

Создать: `Assets/Data/Tutorials/Tutorial_AbilityReady.asset`

```
tutorialId: "Tutorial_Ability"
steps:
  [0]:
    stepType: DialogAndClick
    targetId: "ability_button"
    useDarkMask: true
    dialogText: "Ты спас достаточно людей! Нажми на способность — ускорь всех мирных!"
    characterIcon: [иконка персонажа, та же что в Level 1 туторе]
    dialogPosition: Bottom
```

**Как это работает технически:**
1. `AbilityManager` вызывает `TutorialManager.Instance.StartTutorial(abilityReadyTutorialSequence)`
2. `TutorialManager.StartTutorial()` устанавливает `Time.timeScale = 0f`
3. Появляется диалог + finger pointer на кнопке ability
4. Кнопка остаётся интерактивной (TutorialTarget "ability_button" = "дырка" в маске)
5. Игрок нажимает кнопку → срабатывают оба листенера:
   - `AbilityManager.ActivateAbility()` (назначен в инспекторе через Button.onClick)
   - `TutorialManager.NextStep()` (добавляется динамически TutorialManager'ом)
6. `TutorialManager.FinishTutorial()` → `Time.timeScale = 1f`
7. Ability активируется, мирные ускоряются

> **Порядок имеет значение:** `FinishTutorial` восстанавливает timeScale ДО того, как coroutine drain в AbilityManager начнёт считать время. Это нормально — Unity обрабатывает onClick-листенеры в порядке добавления. `ActivateAbility()` запускается после `NextStep()` так как добавлен в инспекторе раньше, либо можно явно вызвать `ActivateAbility()` из `NextStep` через `OnAbilityTutorialCompleted`-коллбек. **Рекомендация:** вызывать `ActivateAbility()` в конце `FinishTutorial` через событие, а не через Button.onClick, чтобы избежать гонки.

### Альтернативная clean-реализация (рекомендуется):

```csharp
// В AbilityManager:
private void OnAbilityButtonClicked()
{
    if (state == AbilityState.Ready)
    {
        ActivateAbility();
    }
}
```

Кнопка вызывает только `AbilityManager.OnAbilityButtonClicked()`. TutorialManager добавляет `NextStep` как второй листенер. Порядок не имеет значения — оба метода идемпотентны.

---

## 5. Edge Cases

| Сценарий | Поведение |
|---|---|
| Ability заряжается когда GameState = Planning | `AddCharge()` принимает заряд, но туториал не запускается до Playing |
| Игрок умирает пока ability Active | `AbilityManager.OnGameOver()` → сбрасывает state в Idle, не сохраняет заряд |
| Мирных нет на карте в момент активации | Ability работает, просто не на кого влиять. Не критично. |
| Заряд накапливается повторно после использования | Да, ability перезаряжается. Без cooldown между uses в MVP. |
| Туториал уже показан (повторный запуск уровня) | `PlayerPrefs.HasKey("ABILITY_TUTORIAL_SHOWN")` = true → туториал пропускается |
| AbilityManager = null (не на игровой сцене) | Все обращения через `?.` — null-safe |

---

## 6. VFX / Audio (scope на усмотрение)

- **При переходе в Ready:** sound "charge complete" + вибрация
- **При активации:** sound "whoosh/boost" + мирные получают визуальный эффект (частицы над головой или кольцо по земле)
- **При окончании:** sound "fade out" тихий

---

## 7. Новые файлы

| Файл | Тип | Описание |
|---|---|---|
| `Assets/Scripts/AbilityManager.cs` | MonoBehaviour (Singleton) | Логика заряда и ability |
| `Assets/Scripts/UI/AbilityButtonUI.cs` | MonoBehaviour | Визуальные состояния кнопки |
| `Assets/Data/Tutorials/Tutorial_AbilityReady.asset` | ScriptableObject | TutorialSequence для ability |

## 8. Изменения существующих файлов

| Файл | Изменение |
|---|---|
| `GameManager.cs` | +1 строка в `AddRescuedFromTransport()` |
| `Human.cs` | Умножать `agent.speed` на `AbilityManager.GetSpeedMultiplier()` (3 места) |
| `Scientist.cs` | Аналогично Human.cs |
| Игровая сцена | Добавить AbilityManager GO, AbilityButtonUI в Canvas, TutorialTarget на кнопку |
| `Tutorial_AbilityReady.asset` | Создать через ScriptableObject меню |
