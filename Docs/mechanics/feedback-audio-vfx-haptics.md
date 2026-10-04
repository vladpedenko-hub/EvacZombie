# Feedback: звук, вибро, эффекты, настройки

Статус: реализовано как **заглушки** (04.10.2026). Вся логика готова, ассеты заменяются без правки кода.
Код: `Assets/Scripts/Feedback/`.

## Как это устроено (1 минута)

Игровой код вызывает **одну строку**:

```csharp
Feedback.Play(FeedbackEvent.ZombieDeath, position);   // с позицией — будут частицы, off-screen события отсекаются
Feedback.Play(FeedbackEvent.UiToggle);                // без позиции — UI / глобальные моменты
```

Таблица `Feedback.table` в `Feedback.cs` — **единственное место**, где событию назначены звук + вибро + частицы + тряска камеры. Хотите поменять «как ощущается» — правите одну строку там.

| Файл | Роль |
|---|---|
| `Feedback.cs` | Фасад + таблица событий (тюнинг тут) |
| `AudioManager.cs` | Пул источников, лимиты частоты, музыка по сцене. Создаётся сам до первой сцены |
| `PlaceholderSfx.cs` | Синтезирует заглушки кодом, если реального файла нет |
| `Haptics.cs` | Android: JNI VibrationEffect; iOS: только сильные события (см. ограничения) |
| `VfxManager.cs` | Пул процедурных частиц (таблица `specs` — тюнинг) |
| `CameraShake.cs` | Тряска камеры только на время рендера (не конфликтует с `CameraController`) |
| `UiClickFeedback.cs` | Автоматический клик-звук на **любую** Button/Toggle во всех сценах |
| `GameSettings.cs` | Sound / Music / Vibration / 60 FPS, PlayerPrefs |
| `SettingsPopup.cs`, `SettingsButtonInjector.cs`, `RuntimeUiKit.cs` | Окно настроек и шестерёнка в MainMenu (строятся кодом, сцены не менялись) |
| `GameLinks.cs` | URL Privacy / Terms, e-mail поддержки — **заполнить до сабмита** |

Все менеджеры создаются через `[RuntimeInitializeOnLoadMethod]` — в сцены ничего добавлять не нужно.

## Где подключено (хуки в существующем коде)

Везде одна добавленная строка, логика не менялась.

| Момент | Файл | Событие |
|---|---|---|
| Взять карту / отпустить в зоне игры с отказом / кулдаун прошёл | `CardUI` | `CardPickUp` / `CardPlaceInvalid` / `CardReady` |
| Карта успешно разыграна | `InputManager.EndDragging` | `CardPlaceOk` (+ кольцо частиц) |
| Волна предупреждена | `LevelManager.PrepareAndWarnWave` | `WaveWarning` |
| Наступила ночь | `UIManager.ShowNightPopup` | `NightStart` (крик — существующий AudioSource, теперь учитывает настройку Sound) |
| Зомби: попадание / смерть / заразил человека или учёного | `Zombie` | `ZombieHit` / `ZombieDeath` / `ZombieInfect` |
| Снайпер / солдат / боевой вертолёт стреляет | `Sniper`, `Soldier`, `CombatHelicopter` | `SniperShot` / `SoldierShot` / `HeliGun` |
| Бомба: взрыв / гибель своих | `Bomb.Explode` | `BombExplosion` (+тряска, тяжёлая вибрация) / `CivilianLost` |
| Баррикада: удар / разрушение | `Barricade` | `BarricadeHit` / `BarricadeBreak` |
| Вертолёт эвакуации: посадка / взлёт | `HelicopterController` | `HeliArrive` / `HeliTakeoff` |
| Машина выехала | `CarController.Launch` | `CarArrive` |
| Жители уехали / +N долетело до счётчика / цель выполнена | `UIManager` | `RescueDeparted` / `RescueTick` / `GoalReached` (один раз за уровень) |
| Ультимейт готов / активирован | `AbilityButtonUI` | `AbilityReady` (заменил голый `Handheld.Vibrate()`) / `AbilityActivate` |
| Босс впадает в ярость | `ZombieBoss.RageLoop` | `BossRage` (+тряска) |
| Победа / идеальная победа / поражение | `UIManager.ShowResultPopup` / `ShowLosePopup` | `Win` / `PerfectClear` / `Lose` |
| Звёзды на экране результата, награда-карта | `ResultPopupUI` | `UiStar` (на каждую звезду) / `UiReward` |
| Старт уровня / нет энергии | `LevelMissionPopupUI` | `UiLevelStart` / `UiError` |
| Покупка энергии / реклама-награда | `EnergyStorePopupUI` | `UiReward` |
| Экипировать / снять / апгрейд карты | `CardPopupManager`, `CardInfoPopup` | `UiEquip` / `UiUpgrade` |
| Награда региона / переход региона | `MapController` | `UiReward` |
| Любой клик по кнопке | `UiClickFeedback` (авто) | `UiClick` / `UiBack` (имя содержит close/back/cancel/nothanks) |

Защита от спама (рой зомби): минимальный интервал на звук (`AudioManager.minInterval`), кулдаун вибро на событие (`hapticCooldown`), глобальный лимит вибро 50 мс, лимит 60 одновременных партиклов, события за пределами экрана отбрасываются целиком.

## Как заменить заглушку на настоящий звук

Положить файл **с именем значения enum** в `Assets/Resources/Audio/Sfx/`, например `Assets/Resources/Audio/Sfx/SniperShot.ogg` (wav/mp3/ogg). Больше ничего.
Музыка: `Assets/Resources/Audio/Music/menu.ogg` и `gameplay.ogg` (зациклится сама; сейчас играет синтезированный дрон — выключить: `UsePlaceholderMusic = false` в `AudioManager`).

Рекомендации по импорту (мобайл): SFX — mono, Load Type «Decompress On Load», Compression «Vorbis» ~q50 (короткие, <1 с — «ADPCM»); музыка — «Streaming».
В проекте уже лежит CC0-пак `ExternalAssets/kenney_ui-audio` — подходит под UI-звуки (click/switch/rollover) почти без поиска.

Список имён файлов:

```
UiClick
UiBack
UiPopupOpen
UiToggle
UiError
UiStar
UiReward
UiLevelStart
UiUpgrade
UiEquip
CardPickUp
CardPlaceOk
CardPlaceInvalid
CardReady
WaveWarning
ZombieHit
ZombieDeath
ZombieInfect
CivilianLost
SniperShot
SoldierShot
HeliGun
BombExplosion
BarricadeHit
BarricadeBreak
HeliArrive
HeliTakeoff
CarArrive
RescueDeparted
RescueTick
GoalReached
AbilityReady
AbilityActivate
BossRage
Win
PerfectClear
Lose
```

## Как добавить новое событие

1. Значение в `FeedbackEvent` (и, если нужен новый звук, в `SfxId`; частицы — в `VfxId` + строка в `VfxManager.specs`).
2. Строка в `Feedback.table`.
3. `Feedback.Play(...)` в нужном месте.

## Настройки (экран)

Шестерёнка в правом верхнем углу MainMenu (отдельный Canvas, sorting 4000, внутри safe area; позиция — константа `Offset` в `SettingsButtonInjector`). Окно: **Sound**, **Music**, **Vibration** (скрывается на устройствах без вибромотора), **Smooth 60 FPS** (выкл = 30 FPS, экономия батареи), **Privacy Policy**, **Terms of Service**, **Contact Support**, версия.

Открыть из любого места: `SettingsPopup.Show()` — пригодится для паузы в Gameplay.

### Что ещё обычно нужно в настройках (не сделано — по мере появления фич)

| Пункт | Когда нужен |
|---|---|
| Privacy choices / Consent (повторно открыть окно согласия) | Как только подключим рекламу / аналитику (GDPR: отозвать согласие должно быть так же просто, как дать) |
| Language | Когда появится локализация |
| Notifications | Когда добавим пуши «энергия восстановлена» |
| Restore Purchases | Как только появится IAP (требование App Store) |
| Reset progress / Delete my data | Желательно к релизу; обязательно, если появятся аккаунты (Google Play требует удаление аккаунта в приложении) |
| Credits / Licenses | Synty, Kenney (CC0), DOTween — перед релизом |
| Rate us | После soft-launch |
| Cloud save / аккаунт | Если решим |

## Privacy / GDPR — нужна ли ссылка

- **Privacy Policy URL нужен всегда**: Google Play (поле в карточке + Data Safety форма) и App Store (URL + privacy labels) требуют его для любого приложения, даже если мы ничего не собираем. Ссылка в самом приложении обязательна для Google Play, если приложение работает с персональными данными, и считается нормой в остальных случаях.
- Отдельная «GDPR ссылка» не нужна — достаточно политики, где есть GDPR-раздел (какие данные, зачем, как удалить, контакты).
- **Окно согласия (consent) сегодня не нужно**: монетизации и аналитики нет, `Watch Ad` — заглушка. Как только подключим рекламу/аналитику: EEA/UK — сертифицированный CMP (Google UMP для AdMob/MAX), iOS — ATT-запрос, и пункт «Privacy choices» в настройках.
- Не юридическая консультация — перед сабмитом проверить формулировки политики.

**Блокер до сабмита:** заполнить `GameLinks.PrivacyPolicyUrl` (сейчас пусто — кнопка логирует предупреждение и играет звук ошибки).

## Известные ограничения / TODO

- **iOS-хаптики**: у Unity нет API лёгкого тапа без нативного плагина — на iOS вибрируют только Heavy/Success/Failure через `Handheld.Vibrate()` (грубый buzz). Для нормального Taptic: Nice Vibrations / Lofelt или свой нативный плагин (заменить iOS-ветку в `Haptics.Play`).
- **Android**: `VIBRATE` permission добавляется Unity, пока в коде есть `Handheld.Vibrate()` (оставлен как fallback в `Haptics`). Если убирать — добавить permission вручную.
- **UI настроек строится кодом** (простые прямоугольники, шрифт TMP по умолчанию) — когда будет арт, перенести в префаб, сохранив API `SettingsPopup.Show()`.
- **В Gameplay нет входа в настройки** — есть только существующая `MenuButton`. Нужна кнопка паузы → `SettingsPopup.Show()` + `TimeManager.PauseTime()/ResumeTime()` (учесть `InputManager.IsPaused`).
- Озвучка не затухает (ducking) при перекрытии, зацикленных звуков (ротор вертолёта) пока нет — заглушки одноразовые.
- Тряска камеры не отключается настройкой (можно добавить «Reduce motion»).
- Нейтрализованы только звуки, идущие через `Feedback`/`AudioManager` + два существующих AudioSource (`nightSound`, `Bait`). Новые AudioSource в сценах/префабах должны проверять `AudioManager.SfxAllowed`.
- Код не прогонялся в Unity Editor (в среде сборки нет Unity): проверен синтаксис всех файлов, логика ревьюилась вручную. Первый запуск → открыть MainMenu, нажать шестерёнку, затем сыграть уровень.
