# CLAUDE.md — инструкции для Claude Code в этом репозитории

> Читать в начале каждой сессии. Это единственный файл, который Claude Code подхватывает автоматически.
> Факты ниже сверены с кодом репозитория 29.08.2026 (Cowork-сессия). Если что-то не совпадает с реальностью — код всегда прав, поправь этот файл заодно.

## Проект

**EvacZombie** — мобильный порт Atom Zombie Smasher (Blendo Games, 2011) под iOS/Android. Unity **6000.0.62f1**. Solo-разработка (Vlad, дизайн+код через Claude Code), без монетизации, цель — портфолио/сторлонч.

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

- **Drag-reflow HUD**: `CardUI.OnBeginDrag` (`Assets/Scripts/UI/CardUI.cs`, `transform.SetParent(transform.root, false)`) вырывает перетаскиваемую карту из Layout Group `cardsPanel`, из-за чего остальные карты визуально "прыгают". Не исправлено. Фикс: не репарентить в root, использовать `LayoutElement.ignoreLayout = true` на перетаскиваемой карте либо поднимать через sibling index.

## Намеренные риск-механики (НЕ баги — не чинить без явного запроса)

- **Bomb friendly fire**: `Bomb.cs` (`Assets/Scripts/Bonus/Bomb.cs`, ~130-148) в радиусе взрыва **уничтожает** (`Destroy()`, не просто урон) `Human`-объекты и объекты с тегами `Soldier`/`Sniper`. Подтверждено Владом (29.08.2026): это намеренная механика риска — бомба единственная карта, которой можно случайно убить спасаемых людей или свой же юнит при неаккуратном размещении (в духе оригинала Atom Zombie Smasher). Level-5 milestone-бонус карты снимает этот риск как награду за прокачку (см. `Docs/mechanics/card-milestone-bonuses.md`) — значит правку кода делать ТОЛЬКО как guard за флагом бонуса, а не как безусловный фикс.

## Архитектурные паттерны, которые стоит знать

- **Модификаторы/флаги**: `RunSessionData.cs` (`Assets/Scripts/Roguelite/`) — синглтон с `AddModifier(key, value)` / `GetModifier(key, default)` / `SetFlag(id)` / `HasFlag(id)`. Карточные контроллеры (`Sniper.cs`, `Barricade.cs`, `CombatHelicopter.cs`) уже умеют читать из него через `RunSessionData.Instance?.GetModifier(...)`. Сейчас `Instance` = null (система заморожена), но паттерн — образец для будущего **персистентного** хранилища модификаторов, см. `Docs/mechanics/persistent-modifier-store.md`.
- **Статы карт**: `CardData.cs` — `StatType` enum + `CardStat{ baseValue, valuePerLevel, unitSuffix }`, значение на уровне = `baseValue + valuePerLevel * (currentLevel - 1)`. `CardCategory` (Evacuation/Combat/Utility), `CardRarity` (Common/Rare/Epic/Legendary).
- **Сохранения**: `PlayerProfile.cs` — синглтон, `DontDestroyOnLoad`, персистит через `PlayerPrefs` + `JsonUtility` (списки вроде `ownedCardsProgress` сериализуются через `SerializationWrapper<T>`-обёртку). Это единственный существующий персистентный слой в проекте — любое новое персистентное состояние (talent tree, milestone-бонусы) логично вешать сюда же или рядом по тому же паттерну.

## Документация — где что лежит

- `Docs/GDD_CORE_LOOP.md` — актуальный GDD MVP-скоупа.
- `Docs/mechanics/*.md` — спеки конкретных систем (source of truth по дизайну, живут дольше одной сессии). Читать перед работой над соответствующей механикой.
- `Docs/tasks/CLAUDE_CODE_TASK_*.md` — одноразовые брифы на сессию сборки (что делать, что не трогать, критерии приёмки).
- `Docs/PostLaunch/` — заморожённый контент (roguelite, нарратив) — архив, не активные требования.
- `MARKET_RESEARCH.md`, `UX_DesignPhilosophy.md` (корень репо) — рыночный контекст и UX-принципы.
- **Notion "EvacZombie — Dev Board"** — https://app.notion.com/p/a2dce6c7c84c41d794a674f42cc2fc2d — канонический бэклог задач (Task/Phase/Priority/Status). Вне репозитория, недоступен Claude Code напрямую (нет MCP-доступа из этого окружения) — но это единственное место, куда попадают НОВЫЕ задачи. Когда в ходе Cowork-сессии находится что-то actionable (баг, недостающая фича, tech debt, решение) — заводится карточка в Notion, а не только строчка в `Docs/*.md`. `Docs/mechanics/*.md` — это "как это устроено технически" (спека для Claude Code), Notion — это "что и в каком порядке делать" (бэклог для Влада). Карточки в Notion могут ссылаться на конкретные `Docs/mechanics/*.md` файлы за деталями (и наоборот) — см. пример в карточке "Мета: дерево прокачки".

## Инструменты

В `Packages/manifest.json` подключён `com.coplaydev.unity-mcp` (Unity MCP от CoplayDev) — если MCP-сервер запущен и подключён к этой сессии, можно опрашивать/менять состояние живого редактора напрямую, а не только через правку сцен/скриптов на диске.

## Git на этом репозитории (только если работаешь через смонтированную папку, не через обычный локальный git-клиент Влада)

- Широкий/неограниченный `git status` и порцелейн `git commit` надёжно **виснут** на этом mount (не поддерживается настоящий `unlink`, стейл lock-файлы). Не гадать — если команда не вернулась за разумное время, не повторять её как есть.
- Рабочий способ закоммитить: `git write-tree` → `git commit-tree <tree> -p <parent-sha> -F <msgfile>` → `git update-ref refs/heads/<branch> <new-sha>`. Предупреждения `unable to unlink tmp_obj/*.lock` игнорировать.
- При ошибках вида `Unable to create .git/index.lock/HEAD.lock/refs/heads/main.lock: File exists` — переименовать (`mv`) lock-файл в сторону (новый суффикс каждый раз, `rm` тоже не работает) и повторить. Не гейтить `mv` через `ls` с `&&` по нескольким файлам — двигать каждый отдельно.
- Это же может аукнуться в обычном локальном git-клиенте Влада (GitHub Desktop и т.п.) — залипший `.git/HEAD.lock` от Cowork/Claude-Code сессии блокирует локальный коммит с ошибкой "A lock file already exists". Если Влад сообщает об этом после сессии, которая трогала репо — сначала проверить/переименовать `.git/*.lock`.
- **Известный мусор в `refs/heads`** (найден 29.08.2026, не убран): `main.lock.stale` и `main.lock.stale3` — это переименованные stale lock-файлы от предыдущих сессий, git видит их как настоящие ветки (`git branch -a` их покажет). Это НЕ реальные ветки, игнорировать/не мержить. `origin/main` на момент проверки == локальный `main` (репозиторий запушен, актуален).
