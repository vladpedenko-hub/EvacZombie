# CLAUDE.md — инструкции для Claude Code в этом репозитории

> Читать в начале каждой сессии. Это единственный файл, который Claude Code подхватывает автоматически.
> Факты ниже сверены с кодом репозитория 29.08.2026 (Cowork-сессия). Если что-то не совпадает с реальностью — код всегда прав, поправь этот файл заодно.

## Проект

**EvacZombie** — мобильный порт Atom Zombie Smasher (Blendo Games, 2011) под iOS/Android. Unity **6000.6.4f1** (обновлён с 6000.0.62f1 04.10.2026). Solo-разработка (Vlad, дизайн+код через Claude Code), без монетизации, цель — портфолио/сторлонч.

Полный игровой дизайн → `Docs/GDD_CORE_LOOP.md` (актуальный, MVP-скоуп). Не читай `Docs/PostLaunch/*` как активные требования — это заморожённый контент, см. ниже.

## Текущий статус MVP v1 (актуально на 29.08.2026)

Core loop реализован и работает: `GameManager`, `LevelManager` — не трогать без явного запроса.

**Сознательно выключено/заморожено для MVP** (не включать обратно без явного запроса Влада):
- В `Assets/Scenes/Gameplay.unity` объекты `RunSessionData`, `XPManager`, `UpgradeManager`, `LevelUpScreen` имеют `m_IsActive: 0`. Скрипты в `Assets/Scripts/Roguelite/` и 14 ScriptableObject'ов в `Assets/Resources/Upgrades/` НЕ удалены — просто без активного `Instance` в сцене. Весь код, который читает эти синглтоны через `?.` (например `Sniper.cs`, `Barricade.cs`), безопасно это переживает.
- В `Assets/Scripts/UI/CardUI.cs` (строка ~120) закомментирован вызов `EnergyManager.Instance.TrySpendEnergy(cost)`. Карты гейтятся только собственным cooldown. `EnergyManager.cs` в проекте остаётся (энергия всё ещё гейтит *попытку сыграть уровень* через `PlayerProfile`, это другая система — не путать).
- `AbilityManager` / `AbilityButtonUI` (ultimate-способность спасённых, speed-boost) — это ОТДЕЛЬНАЯ система, НЕ заморожена, остаётся в игре.
- Нарративный слой (катсцены, биографии героев, диалоги, boss-регионы, дневник) — не реализован и не в скоупе MVP. Не предлагать и не начинать без явного запроса.

**Контент MVP**: 10 уровней (`Assets/Data/Level_1_Data.asset` … `Level_10_Data.asset`), 8 карт (`Assets/Data/Cards/*.asset`: Bait, Barricade, Bomb, Car, CombatHelicopter, Helicopter, Sniper, Soldier). Новые уровни/карты сейчас не нужны — фокус на балансе существующих.

## Известные незакрытые баги

*(На 29.08.2026 открытых пунктов нет — единственный известный баг ниже уже исправлен.)*

- ~~**Drag-reflow HUD**~~ — ИСПРАВЛЕНО в `f47e1762` (29.08.2026). `CardUI.OnBeginDrag` больше не делает `transform.SetParent(transform.root, false)`. Вместо этого: `LayoutElement.ignoreLayout = true` на перетаскиваемой карте + собственный `Canvas` с `overrideSorting=true` на время драга (поднимает карту визуально без реродителинга). `cardsPanel` Layout Group не видит изменения количества детей — остальные карты не "прыгают".

## Незавершённые ручные шаги в Unity Editor (не код — не забыть сделать руками)

- Тот же коммит `f47e1762` добавил поля `rarityBorder` (`Image`) в `CardUI.cs` и `MetaCardUI.cs` — рамка карты по редкости через новый `CardVisuals.GetRarityColor()`. Поля null-guarded, так что ничего не сломается, но рамка не появится на экране, пока не назначишь Image-компонент рамки на это поле в инспекторе — в HUD-префабе карты (`CardUI`) и в плитке колоды (`MetaCardUI`). Claude Code не может сделать это за тебя — это чисто ручной шаг в Unity Editor.

## Намеренные риск-механики (НЕ баги — не чинить без явного запроса)

- **Bomb friendly fire**: `Bomb.cs` (`Assets/Scripts/Bonus/Bomb.cs`, ~130-148) в радиусе взрыва **уничтожает** (`Destroy()`, не просто урон) `Human`-объекты и объекты с тегами `Soldier`/`Sniper`. Подтверждено Владом (29.08.2026): это намеренная механика риска — бомба единственная карта, которой можно случайно убить спасаемых людей или свой же юнит при неаккуратном размещении (в духе оригинала Atom Zombie Smasher). Level-5 milestone-бонус карты снимает этот риск как награду за прокачку (см. `Docs/mechanics/card-milestone-bonuses.md`) — значит правку кода делать ТОЛЬКО как guard за флагом бонуса, а не как безусловный фикс.

## Текущий рефакторинг архитектуры (начат 09.10.2026)

Ветка `refactor/architecture`. Полный аудит + фазированный план → `Docs/REFACTOR_AUDIT.md` (Phase 0/1). Жёсткое правило: ноль изменений геймплея/поведения на каждом шаге. Статус по фазам:

- **Phase 2A (готово, 09.10.2026)**: `Human.cs` и `Scientist.cs` были на ~95% идентичны (блуждание/паника/бегство от зомби/эвакуация/посадка в транспорт — один и тот же код в обоих файлах). Вынесено в общий `EvacueeBase.cs`; `Human`/`Scientist` теперь ~8-строчные подклассы, у которых свой — только статический список (`Human.AllHumans`/`Scientist.AllScientists`) и регистрация в нём через `OnEnable`/`OnDisable`. Имена и типы полей не менялись — сериализованные значения в `Human.prefab`/`Scientist.prefab` переживают рефакторинг без доп. действий (проверено).
- **Phase 2B (готово, 09.10.2026)**: убраны GC-аллокации в боевых корутинах без изменения поведения. `WaitForSeconds` теперь кэшируется (не создаётся заново на каждый выстрел/атаку/хит) в `Zombie.cs`, `Sniper.cs`, `Soldier.cs`, `CombatHelicopter.cs`, `CarController.cs`, `Barricade.cs`, `Bait.cs`. Новый `SharedMaterials.cs` — один общий `Material` для "Sprites/Default" (как `VfxManager.EnsureMaterial`), используется вместо `new Material(Shader.Find(...))` на каждую размещённую карту в `Sniper.cs`, `Soldier.cs`, `CombatHelicopter.cs`, `Bomb.cs`, `Bait.cs`, `InputManager.cs`. **Исключение**: `HelicopterController.cs` (маркер посадки) мутирует `.material.color` напрямую — туда шарить инстанс материала нельзя (утечёт цвет на всех остальных), там закэширован только `Shader.Find`.
- Остальные фазы (C–F из плана: дедуп сканов "найти ближайшую цель", `Physics.*NonAlloc`, вопрос по `EnergyManager`) — не начаты, ждут "go" по одной за раз.

## Архитектурные паттерны, которые стоит знать

- **Общая AI у гражданских**: см. "Phase 2A" выше — `EvacueeBase.cs` теперь единственное место с логикой Human/Scientist.
- **Модификаторы/флаги (внутриуровневые, roguelite)**: `RunSessionData.cs` (`Assets/Scripts/Roguelite/`) — синглтон с `AddModifier(key, value)` / `GetModifier(key, default)` / `SetFlag(id)` / `HasFlag(id)`. Карточные контроллеры (`Sniper.cs`, `Barricade.cs`, `CombatHelicopter.cs`) читают из него через `RunSessionData.Instance?.GetModifier(...)`. Сейчас `Instance` = null (roguelite заморожен для MVP). **Это НЕ прототип будущего персистентного хранилища** — персистентное хранилище для Talent Tree уже реализовано отдельно, см. следующий пункт (старая формулировка здесь и в `Docs/mechanics/persistent-modifier-store.md` была верна на 23.08, но устарела к 09.10 — вопрос закрыт).
- **Мета-слой "город" + Talent Tree (реализован, но не упоминался в этом файле до 09.10.2026)**: `Assets/Scripts/Meta/*` — восстанавливаемые здания (`Meta/Base/BaseView.cs`, `BaseHouseView.cs`, `Meta/City/MetaBuildingPanel.cs`) и дерево прокачки (`Meta/Research/MetaSkillTreeScreen.cs`), применяется глобально ко всем картам через `MetaRuntime.Modifiers` (`Meta/Modifiers/MetaModifiers.cs`) — именно этот механизм закрывает вопрос из `Docs/mechanics/persistent-modifier-store.md`. Своё сохранение, отдельное от `PlayerProfile`: `MetaProgressionState` + `MetaSaveService` (`Meta/Save/*`), один JSON-блоб под ключом `"MetaProgression"`. Composition root — `MetaRuntime.EnsureInitialized()`. UI строится полностью в коде через `Meta/UI/MetaUI.cs` (без сцен/префабов). Полная карта системы → `Docs/REFACTOR_AUDIT.md` §1.
- **Статы карт**: `CardData.cs` — `StatType` enum + `CardStat{ baseValue, valuePerLevel, unitSuffix }`, значение на уровне = `baseValue + valuePerLevel * (currentLevel - 1)`. `CardCategory` (Evacuation/Combat/Utility), `CardRarity` (Common/Rare/Epic/Legendary).
- **Звук / вибро / VFX / настройки**: всё идёт через фасад `Feedback.Play(FeedbackEvent.X[, worldPos])` (`Assets/Scripts/Feedback/`) — таблица событий в `Feedback.cs`. Менеджеры создаются сами (`RuntimeInitializeOnLoadMethod`), сцены не трогаются. Новый звук/эффект = строка в таблице + одна строка-хук в коде. Заглушки синтезируются кодом; реальный звук — файл `Resources/Audio/Sfx/<SfxId>`. Новые `AudioSource` должны проверять `AudioManager.SfxAllowed`. Детали → `Docs/mechanics/feedback-audio-vfx-haptics.md`.
- **Сохранения**: `PlayerProfile.cs` — синглтон, `DontDestroyOnLoad`, персистит через `PlayerPrefs` + `JsonUtility` (списки вроде `ownedCardsProgress` сериализуются через `SerializationWrapper<T>`-обёртку). Валюта (People/Scientists) живёт здесь же, читается/пишется через `MetaRuntime.Currency`, а не напрямую — не обходи фасад. **Важно**: это не единственный персистентный слой — `MetaProgressionState` (мета-прогресс: здания, узлы дерева) сохраняется отдельно, см. пункт про мета-слой выше. Вопрос "куда класть новое персистентное состояние" решается по месту (talent-tree-подобное → рядом с `MetaProgressionState`; валюта/карты/world-map позиция → `PlayerProfile`), не трогать формат сохранений без явного запроса.

## Документация — где что лежит

- `Docs/GDD_CORE_LOOP.md` — актуальный GDD MVP-скоупа.
- `Docs/REFACTOR_AUDIT.md` — аудит архитектуры + фазированный план рефакторинга (ветка `refactor/architecture`, начат 09.10.2026). Читать перед любой работой в этой ветке — там system map, известные проблемы связности, хотспоты производительности с file:line, и что уже сделано/что дальше.
- `Docs/mechanics/*.md` — спеки конкретных систем (source of truth по дизайну, живут дольше одной сессии). Читать перед работой над соответствующей механикой.
- `Docs/tasks/CLAUDE_CODE_TASK_*.md` — одноразовые брифы на сессию сборки (что делать, что не трогать, критерии приёмки).
- `Docs/PostLaunch/` — заморожённый контент (roguelite, нарратив) — архив, не активные требования.
- `MARKET_RESEARCH.md`, `UX_DesignPhilosophy.md` (корень репо) — рыночный контекст и UX-принципы.
- **Notion "EvacZombie — Dev Board"** — https://app.notion.com/p/a2dce6c7c84c41d794a674f42cc2fc2d — канонический бэклог задач (Task/Phase/Priority/Status). Вне репозитория, недоступен Claude Code напрямую (нет MCP-доступа из этого окружения) — но это единственное место, куда попадают НОВЫЕ задачи. Когда в ходе Cowork-сессии находится что-то actionable (баг, недостающая фича, tech debt, решение) — заводится карточка в Notion, а не только строчка в `Docs/*.md`. `Docs/mechanics/*.md` — это "как это устроено технически" (спека для Claude Code), Notion — это "что и в каком порядке делать" (бэклог для Влада). Карточки в Notion могут ссылаться на конкретные `Docs/mechanics/*.md` файлы за деталями (и наоборот) — см. пример в карточке "Мета: дерево прокачки".

## Инструменты

**Связь с живым Unity Editor — официальный Unity CLI** (бета, тестируется с 04.10.2026). Старый MCP от CoplayDev (`com.coplaydev.unity-mcp`) выведен из использования — не предлагать и не настраивать его.

- Работает через CLI `unity` + пакет `com.unity.pipeline` в проекте. Редактор с этим проектом должен быть **открыт**, иначе команды не дойдут.
- Проверка связи: `unity status` (нужно состояние `ready`). Если команда не найдена — это, скорее всего, устаревший PATH в сессии, а не отсутствие CLI: перезапустить терминал/сессию. Если CLI или пакет реально не установлены — предложить команду установки и дождаться ответа Влада, ничего не ставить молча (`unity pipeline install` для пакета).
- Обзор возможностей: `unity list` (список команд), `unity command` (подробная схема аргументов), `unity <команда> --help`.
- Выполнить C# в редакторе: `unity command eval --code '<C#>'`. Результат приходит JSON с `"success": true`.
- Через CLI можно: пересобирать проект и смотреть ошибки компиляции, запускать тесты и Play mode, читать Console, править сцены/префабы/ассеты.
- Плагин `unity@unity-agent-plugin` (скиллы `/unity:...`) установлен; требует Unity 6+ — проект на 6000.6.4f1, ок.
- После любой правки скриптов на диске проверять компиляцию через CLI, а не считать, что код собрался.
- Если что-то в этой связке ведёт себя нестабильно (бета, пакет экспериментальный) — сначала сообщить Владу, а не обходить молча через правку файлов сцены вручную.

## Git на этом репозитории (только если работаешь через смонтированную папку, не через обычный локальный git-клиент Влада)

- Широкий/неограниченный `git status` и порцелейн `git commit` надёжно **виснут** на этом mount (не поддерживается настоящий `unlink`, стейл lock-файлы). Не гадать — если команда не вернулась за разумное время, не повторять её как есть.
- Рабочий способ закоммитить: `git write-tree` → `git commit-tree <tree> -p <parent-sha> -F <msgfile>` → `git update-ref refs/heads/<branch> <new-sha>`. Предупреждения `unable to unlink tmp_obj/*.lock` игнорировать.
- При ошибках вида `Unable to create .git/index.lock/HEAD.lock/refs/heads/main.lock: File exists` — переименовать (`mv`) lock-файл в сторону (новый суффикс каждый раз, `rm` тоже не работает) и повторить. Не гейтить `mv` через `ls` с `&&` по нескольким файлам — двигать каждый отдельно.
- Это же может аукнуться в обычном локальном git-клиенте Влада (GitHub Desktop и т.п.) — залипший `.git/HEAD.lock` от Cowork/Claude-Code сессии блокирует локальный коммит с ошибкой "A lock file already exists". Если Влад сообщает об этом после сессии, которая трогала репо — сначала проверить/переименовать `.git/*.lock`.
- **Известный мусор в `refs/heads`** (найден 29.08.2026, не убран): `main.lock.stale` и `main.lock.stale3` — это переименованные stale lock-файлы от предыдущих сессий, git видит их как настоящие ветки (`git branch -a` их покажет). Это НЕ реальные ветки, игнорировать/не мержить. `origin/main` на момент проверки == локальный `main` (репозиторий запушен, актуален).
