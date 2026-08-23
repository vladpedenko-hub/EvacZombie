# EvacZombie — Hero System & Roguelite In-Run Progression
**GDD v1.0 — MVP Design Document**

> Этот документ описывает две связанные системы:
> 1. **Система героев** — карты эвакуации становятся персонажами с уникальной идентичностью
> 2. **Roguelite прогрессия** — XP за убийства и эвакуации → левел ап → выбор из 3 улучшений

---

## 1. СИСТЕМА ГЕРОЕВ

### 1.1 Концепция

Три эвакуационные карты (`Helicopter`, `Car`, `CombatHelicopter`) становятся **героями**. Герой — это постоянный персонаж игрока в забеге. Обычные карты-способности (Sniper, Soldier, Bomb, Barricade, Bait) формируют колоду, привязанную к герою.

### 1.2 Список героев

| Герой | CardType | Стартовый | Условие разблокировки |
|---|---|---|---|
| 🚁 Вертолёт «Феникс» | `Helicopter` | ✅ Да | — |
| 🚗 Броневик «Локус» | `Car` | ❌ | Пройти 5 уровней |
| 🚁🔫 Боевой Вертолёт «Призрак» | `CombatHelicopter` | ❌ | Пройти 15 уровней |

### 1.3 Механика героя в забеге

- Игрок входит в уровень с **1 активным героем** и **своей колодой** (3–5 карт-способностей).
- Герой — это единственная эвакуационная карта. Других эвакуационных карт в колоде быть не может.
- Карты-способности используются так же, как сейчас (размещение на поле).
- Герой активируется кнопкой в UI (отдельная зона) — не частью обычной колоды.

### 1.4 Структура данных (C#)

```csharp
// Новый ScriptableObject для героя
[CreateAssetMenu(fileName = "NewHeroData", menuName = "ZombieGame/Hero Data")]
public class HeroData : ScriptableObject
{
    public string heroName;
    public string heroDescription;
    [TextArea] public string loreText;
    public Sprite heroIcon;
    public CardType heroCardType; // Helicopter / Car / CombatHelicopter
    public CardData linkedCardData; // Ссылка на существующий CardData

    // Условие разблокировки
    public int levelsRequiredToUnlock; // 0 = стартовый
}

// В PlayerProfile добавить:
public string selectedHeroId;         // id текущего активного героя
public List<string> unlockedHeroIds;  // список разблокированных героев
```

---

## 2. ROGUELITE ПРОГРЕССИЯ (В РАМКАХ ОДНОГО УРОВНЯ)

### 2.1 Источники XP

| Событие | XP |
|---|---|
| Убийство обычного зомби | +5 |
| Убийство ZombieBoss | +50 |
| Эвакуация обычного гражданина | +20 |
| Эвакуация учёного (Scientist) | +40 |
| Полная загрузка эвакуационного транспорта | +25 (бонус) |

### 2.2 Шкала уровней XP (в рамках одного забега)

```
Уровень 1 → 2:   100 XP
Уровень 2 → 3:   150 XP
Уровень 3 → 4:   200 XP
Уровень 4 → 5:   250 XP
Уровень 5 → 6:   300 XP
Уровень 6 → 7:   375 XP
Уровень 7 → 8:   450 XP
Уровень 8 → 9:   550 XP
Уровень 9 → 10:  700 XP
Уровень 10+:     +200 XP каждый следующий
```

Целевой опыт на уровень: **8–12 левел апов** за средний уровень.

### 2.3 Логика Level Up

1. Когда XP достигает порога — игра **паузится** (Time.timeScale = 0).
2. Появляется UI с **3 вариантами улучшений** на выбор.
3. Игрок выбирает 1 → эффект применяется немедленно → игра возобновляется.

### 2.4 Формирование пула улучшений

Пул строится по этим правилам:
1. Для каждой карты в колоде игрока — добавить **все незамаксированные улучшения** этой карты в пул.
2. Для героя — добавить **все незамаксированные улучшения героя** в пул.
3. Добавить **все общие улучшения**, которые применимы.
4. Из пула случайно выбрать **3 уникальных варианта** (без повторений в одном предложении).
5. Если улучшений одного типа несколько (из-за стакинга) — показать ту же карточку, но с повышенным тиром.

**Весовая система (MVP):**
- Улучшения карт, которые игрок ещё ни разу не выбирал: вес 1.5x (поощряем разнообразие)
- Улучшения, взятые 1 раз (доступен тир 2): вес 1.2x
- Общие улучшения: вес 1.0x

---

## 3. СИСТЕМА СТАКИНГА И УЛЬТИМАТИВНЫХ УЛУЧШЕНИЙ

### 3.1 Принцип

Каждое улучшение имеет 3 тира:
- **Тир 1** (первый выбор): базовое улучшение.
- **Тир 2** (второй выбор той же карточки): **Enhanced** — значительное усиление, меняет поведение.
- **Тир 3 / Ультимативное** (третий выбор): **ULTIMATE** — кардинальное изменение механики.

После выбора тира 3 — улучшение удаляется из пула (максимум достигнут).

### 3.2 Структура данных

```csharp
public enum UpgradeTier { Tier1, Tier2_Enhanced, Tier3_Ultimate }

[System.Serializable]
public class RunUpgradeDefinition
{
    public string upgradeId;           // уникальный ID, напр. "helicopter_quicklanding"
    public CardType targetCardType;    // к какой карте относится (или None для General)
    public string displayName;         // "Быстрая посадка"
    public string description;         // описание тира 1
    public string descriptionEnhanced; // описание тира 2
    public string descriptionUltimate; // описание тира 3
    public Sprite icon;
}

// Хранится в RunSessionData (сбрасывается после уровня)
[System.Serializable]
public class RunSessionData
{
    public Dictionary<string, int> upgradeStacks; // upgradeId → количество раз взято (0-3)
    public int currentXP;
    public int currentRunLevel;
    // Применяемые бонусы хранятся как мультипликаторы/аддитивы:
    public Dictionary<string, float> runModifiers; // "helicopter_capacity" → 8f
}
```

### 3.3 Применение эффектов

Эффекты применяются **в момент размещения карты** (через RunSessionData.runModifiers). Каждый контроллер (HelicopterController, Sniper и т.д.) при Start() запрашивает не только `PlayerProfile`, но и `RunSessionData`:

```csharp
// Пример для HelicopterController
float capacityMod = RunSession.GetModifier("helicopter_capacity"); // 0 если не взято
maxCapacity = (int)(baseCapacity + capacityMod);
```

---

## 4. ДЕРЕВЬЯ УЛУЧШЕНИЙ

### Обозначения в таблицах
- **[Параметр]** — конкретный StatType из CardData (Capacity, Speed, Damage, FireRate, Radius, Duration, Cooldown, Count, Health)
- ✨ Enhanced — тир 2 (взято 2 раза)
- 💥 ULTIMATE — тир 3 (взято 3 раза)

---

## 4.1 ГЕРОЙ: 🚁 ВЕРТОЛЁТ «ФЕНИКС»
*Параметры: Capacity, Speed (вертикальная скорость), Radius (attractRadius), Duration (loadTime), Cooldown (boardingCooldown)*

### Улучшение A: Быстрая Посадка
- **Тир 1** `quicklanding`: `verticalSpeed` +35% → вертолёт быстрее достигает точки посадки
- ✨ **Тир 2** `quicklanding x2` — **«Форсаж»**: `verticalSpeed` x2.5 + первые 3 секунды после посадки зомби не вызывают панику
- 💥 **ULTIMATE** `quicklanding x3` — **«VTOL-Протокол»**: вертолёт телепортируется в точку мгновенно (без анимации полёта), `verticalSpeed` x10

### Улучшение B: Мегафон
- **Тир 1** `megaphone`: `attractRadius` +40% → привлекает гражданских с большей дистанции
- ✨ **Тир 2** `megaphone x2` — **«Экстренное вещание»**: `attractRadius` x2 + все гражданские в зоне начинают бежать (скорость x2)
- 💥 **ULTIMATE** `megaphone x3` — **«Городская Тревога»**: ВСЕ гражданские на карте начинают движение к вертолёту независимо от дистанции

### Улучшение C: Дополнительные Места
- **Тир 1** `extraseats`: `maxCapacity` +2 → берёт больше пассажиров за рейс
- ✨ **Тир 2** `extraseats x2` — **«Тяжёлый Транспорт»**: `maxCapacity` +6 суммарно, учёные грузятся в приоритете
- 💥 **ULTIMATE** `extraseats x3` — **«Массовая Эвакуация»**: `maxCapacity` становится неограниченным (забирает всех, кто успел добежать за loadTime)

### Улучшение D: Скоростная Посадка
- **Тир 1** `fastboarding`: `boardingCooldown` -30% → следующий гражданин садится быстрее
- ✨ **Тир 2** `fastboarding x2` — **«Экспресс»**: `boardingCooldown` -60%, 2 гражданина в тик вместо 1
- 💥 **ULTIMATE** `fastboarding x3` — **«Магнит»**: все гражданские в `boardingRadius` телепортируются в вертолёт мгновенно

### Улучшение E: Долгое Дежурство
- **Тир 1** `longstay`: `loadTime` +6s → вертолёт дольше ждёт на площадке
- ✨ **Тир 2** `longstay x2` — **«Укреплённая ЛЗ»**: `panicRadius` уменьшен вдвое (зомби должны подойти вплотную чтобы вызвать панику)
- 💥 **ULTIMATE** `longstay x3` — **«Несгибаемый»**: вертолёт полностью невосприимчив к панике — никогда не улетает из-за зомби

---

## 4.2 ГЕРОЙ: 🚗 БРОНЕВИК «ЛОКУС»
*Параметры: Capacity, Duration (loadTime), Radius (sirenRadius), Cooldown (boardingCooldown), Speed (moveSpeed)*

### Улучшение A: Турбодвигатель
- **Тир 1** `turboengine`: `moveSpeed` +35%
- ✨ **Тир 2** `turboengine x2` — **«Раллийный Пилот»**: `moveSpeed` x2, машина делает второй рейс после выезда (въезжает снова)
- 💥 **ULTIMATE** `turboengine x3` — **«Спидран»**: машина завершает весь маршрут за 40% обычного времени и игнорирует `dangerRadius`

### Улучшение B: Увеличенный Кузов
- **Тир 1** `biggertrunk`: `maxCapacity` +3
- ✨ **Тир 2** `biggertrunk x2` — **«Автобус»**: `maxCapacity` становится 12
- 💥 **ULTIMATE** `biggertrunk x3` — **«Конвой»**: при активации автоматически спавнится второй Броневик к той же точке

### Улучшение C: Громкая Сирена
- **Тир 1** `loudsiren`: `sirenRadius` +50%
- ✨ **Тир 2** `loudsiren x2` — **«Паника»**: гражданские в зоне начинают бежать к машине (скорость x2)
- 💥 **ULTIMATE** `loudsiren x3` — **«Региональная Тревога»**: ВСЕ гражданские на карте начинают движение к ближайшему выезду

### Улучшение D: Тараньщик
- **Тир 1** `crashbar`: `crushRadius` +50% → машина давит зомби в большем радиусе при движении
- ✨ **Тир 2** `crashbar x2` — **«Берсерк»**: машина давит зомби даже стоя на месте (во время загрузки)
- 💥 **ULTIMATE** `crashbar x3` — **«Танк»**: машина получает полосу HP (200 HP), зомби атакуют машину вместо того чтобы блокировать загрузку; машина не уезжает пока жива

### Улучшение E: Быстрые Двери
- **Тир 1** `fastdoors`: `boardingCooldown` -35%
- ✨ **Тир 2** `fastdoors x2` — **«Открытый Кузов»**: анимация посадки пропускается полностью
- 💥 **ULTIMATE** `fastdoors x3` — **«На Ходу»**: гражданские прыгают в машину пока она едет (загрузка начинается ещё при движении к точке)

---

## 4.3 ГЕРОЙ: 🚁🔫 БОЕВОЙ ВЕРТОЛЁТ «ПРИЗРАК»
*Параметры: Capacity, Speed (flySpeed), Duration (loadTime), Radius (shootRadius), FireRate, Damage*

### Улучшение A: Бронекорпус
- **Тир 1** `heavyarmor`: `loadTime` +4s → дольше остаётся в зоне
- ✨ **Тир 2** `heavyarmor x2` — **«Несокрушимый»**: остаётся в зоне пока не заберёт полную загрузку или всех оставшихся гражданских
- 💥 **ULTIMATE** `heavyarmor x3` — **«Вечный»**: вертолёт становится постоянной боевой единицей на весь уровень

### Улучшение B: Снайперский ИИ
- **Тир 1** `marksman`: `damage` x1.5
- ✨ **Тир 2** `marksman x2` — **«Протокол Одного Выстрела»**: урон x3, убивает любого обычного зомби за 1 выстрел
- 💥 **ULTIMATE** `marksman x3` — **«Рельсотрон»**: выстрелы пробивают до 10 зомби насквозь (piercing по линии огня)

### Улучшение C: Скорострельность
- **Тир 1** `rapidfire`: `fireRate` улучшен на 40% (интервал между выстрелами уменьшен)
- ✨ **Тир 2** `rapidfire x2` — **«Миниган»**: `fireRate` = 0.08s, визуальный трассер становится непрерывным
- 💥 **ULTIMATE** `rapidfire x3` — **«Огненный Шторм»**: каждый выстрел становится мини-взрывом (AoE урон), `fireRate` = 0.2s

### Улучшение D: Широкое Прикрытие
- **Тир 1** `widenet`: `shootRadius` +40%
- ✨ **Тир 2** `widenet x2` — **«Зона Отказа»**: `shootRadius` перекрывает половину карты
- 💥 **ULTIMATE** `widenet x3` — **«Ковровый Огонь»**: одновременно стреляет по ВСЕМ зомби в зоне за каждый тик

### Улучшение E: Перегруз
- **Тир 1** `overloaded`: `maxCapacity` +5
- ✨ **Тир 2** `overloaded x2` — **«Транспортник»**: `maxCapacity` = 25
- 💥 **ULTIMATE** `overloaded x3` — **«Авианосец»**: все гражданские на карте телепортируются на борт при посадке

---

## 4.4 КАРТА: 🎯 СНАЙПЕР
*Параметры (из CardData): Cooldown, Radius (attackRange), Damage, Duration (lifespan)*

### Улучшение A: Удлинённый Ствол
- **Тир 1** `sniper_range`: `attackRange` +35%
- ✨ **Тир 2**: **«Дальнобойная Платформа»** — дальность фактически охватывает всю карту
- 💥 **ULTIMATE**: **«Всевидящее Око»** — снайпер видит и поражает ЛЮБОГО зомби на карте независимо от позиции

### Улучшение B: Бронебойные Патроны
- **Тир 1** `sniper_damage`: `damage` x1.5
- ✨ **Тир 2**: **«Экзекутор»** — инстакилл любого не-босс зомби
- 💥 **ULTIMATE**: **«Противотанковая Винтовка»** — теперь поражает и наносит урон ZombieBoss (30 HP за выстрел)

### Улучшение C: Быстрая Перезарядка
- **Тир 1** `sniper_cooldown`: `cooldownDelay` -35%
- ✨ **Тир 2**: **«Подавляющий Огонь»** — `cooldownDelay` = 0.3s, почти непрерывная стрельба
- 💥 **ULTIMATE**: **«Шторм Свинца»** — одновременно держит на прицеле 3 разных цели, стреляет по всем одновременно

### Улучшение D: Долгая Вахта
- **Тир 1** `sniper_duration`: `lifespan` +8s
- ✨ **Тир 2**: **«Оверватч»** — `lifespan` = весь уровень (постоянная единица)
- 💥 **ULTIMATE**: **«Вечный Страж»** — постоянная единица + перерождается через 10s после уничтожения

### Улучшение E: Повышенное Давление
- **Тир 1** `sniper_pierce`: `maxPierceTargets` +2 → пробивает больше зомби одним выстрелом
- ✨ **Тир 2**: **«Бесконечный Пробой»** — пробивает ВСЕХ зомби по линии огня
- 💥 **ULTIMATE**: **«Пильная Цепь»** — выстрел рикошетит между зомби до 6 раз после пробития

---

## 4.5 КАРТА: 💣 БОМБА
*Параметры: Radius (damageRadius), Damage*

### Улучшение A: Увеличенная Боеголовка
- **Тир 1** `bomb_radius`: `damageRadius` +40%
- ✨ **Тир 2**: **«Тактическое Ядро»** — `damageRadius` x3
- 💥 **ULTIMATE**: **«Термобарика»** — убивает ВСЕХ зомби на половине карты мгновенно

### Улучшение B: Ударная Волна
- **Тир 1** `bomb_damage`: `damage` x2
- ✨ **Тир 2**: **«Избыточная Сила»** — гарантированная смерть любого зомби (любое HP)
- 💥 **ULTIMATE**: **«ЭМИ-Взрыв»** — уцелевшие зомби в радиусе парализованы на 5 секунд

### Улучшение C: Кассетная Начинка
- **Тир 1** `bomb_cluster`: при взрыве спавнятся 3 мини-бомбы в случайных точках в радиусе
- ✨ **Тир 2**: **«Ковровая Бомбардировка»** — 8 мини-бомб, покрывают всю зону
- 💥 **ULTIMATE**: **«Роевой Удар»** — 20 мини-бомб, полное покрытие карты

### Улучшение D: Пробиватель Бункеров
- **Тир 1** `bomb_buildings`: взрыв теперь уничтожает здания в зоне
- ✨ **Тир 2**: **«Выжженная Земля»** — уничтожает все постройки + заборы + баррикады в зоне
- 💥 **ULTIMATE**: **«Сровнять с Землёй»** — всё в зоне разрушается, NavMesh временно перестраивается

### Улучшение E: Разделяющиеся Боеголовки
- **Тир 1** `bomb_mirv`: при активации падают 2 бомбы вместо 1 (небольшое смещение)
- ✨ **Тир 2**: **«Залп»** — 5 бомб в широком разбросе вокруг точки
- 💥 **ULTIMATE**: **«Орбитальный Удар»** — лазерный луч с орбиты прочёсывает линию через всю карту

---

## 4.6 КАРТА: 🧱 БАРРИКАДА
*Параметры: Health*

### Улучшение A: Армированный Бетон
- **Тир 1** `barricade_hp`: `maxHealth` x1.5
- ✨ **Тир 2**: **«Крепостная Стена»** — `maxHealth` x4 (выдерживает атаки стада)
- 💥 **ULTIMATE**: **«Несокрушимый Бункер»** — баррикада не может быть уничтожена зомби (существует до конца уровня)

### Улучшение B: Колючая Проволока
- **Тир 1** `barricade_spike`: атакующие зомби получают обратный урон (25% отражение)
- ✨ **Тир 2**: **«Бритвенный Провод»** — 100% отражение урона + небольшое AoE вокруг баррикады
- 💥 **ULTIMATE**: **«Зона Смерти»** — любой зомби вошедший в радиус 1.5м от баррикады мгновенно умирает

### Улучшение C: Электрификация
- **Тир 1** `barricade_stun`: атакующие зомби оглушены на 1.5s
- ✨ **Тир 2**: **«Электрическая Сеть»** — зона оглушения распространяется на 2м вокруг баррикады
- 💥 **ULTIMATE**: **«Дуговой Реактор»** — непрерывный электрический импульс убивает слабых зомби рядом, сильных оглушает

### Улучшение D: Широкий Охват
- **Тир 1** `barricade_width`: NavMeshObstacle масштаб +50% (перекрывает больше пути)
- ✨ **Тир 2**: **«Великая Стена»** — баррикада перекрывает дорогу от края до края
- 💥 **ULTIMATE**: **«Железный Занавес»** — создаёт полную стену через всю улицу (все NavMesh-пути заблокированы)

### Улучшение E: Быстрое Развёртывание
- **Тир 1** `barricade_count`: 2 баррикады вместо 1 за активацию
- ✨ **Тир 2**: **«Минное Поле»** — 3 баррикады в настраиваемом паттерне
- 💥 **ULTIMATE**: **«Периметр»** — автоматически генерируется кольцо из 6 баррикад вокруг выбранной точки

---

## 4.7 КАРТА: 🪤 ПРИМАНКА
*Параметры: Radius (attractRadius), Duration (lifeTime)*

### Улучшение A: Усиленный Сигнал
- **Тир 1** `bait_radius`: `attractRadius` +50%
- ✨ **Тир 2**: **«Неотразимый»** — ВСЕ зомби на карте привлечены к приманке
- 💥 **ULTIMATE**: **«Мировой Сигнал»** — все зомби мгновенно телепортируются к точке приманки при установке

### Улучшение B: Увеличенный Заряд
- **Тир 1** `bait_duration`: `lifeTime` +6s
- ✨ **Тир 2**: **«Постоянный Декой»** — `lifeTime` = 60s (весь уровень)
- 💥 **ULTIMATE**: **«Вечная Ловушка»** — становится перманентной точкой привлечения на весь уровень, перезапускается при каждой волне

### Улучшение C: Смертоносная Ловушка
- **Тир 1** `bait_lethal`: при истечении времени — взрыв в зоне `attractRadius` (урон = 150)
- ✨ **Тир 2**: **«Гранатный Котлован»** — урон взрыва = полная бомба (1000+)
- 💥 **ULTIMATE**: **«Тактическое Ядро-Приманка»** — убивает ВСЕХ зомби в двойном радиусе attractRadius

### Улучшение D: Массовая Истерия
- **Тир 1** `bait_priority`: зомби в радиусе игнорируют гражданских и сфокусированы ТОЛЬКО на приманке
- ✨ **Тир 2**: **«Зомби-Магнит»** — зомби бегут к приманке со скоростью x2
- 💥 **ULTIMATE**: **«Массовое Захоронение»** — 5 приманок спавнятся одновременно в случайных точках

### Улучшение E: Замедленный Взрыватель
- **Тир 1** `bait_mine`: приманка взрывается при входе зомби в радиус (proximity mine)
- ✨ **Тир 2**: **«Направленный Взрыв»** — explosion направлен в сторону скопления зомби
- 💥 **ULTIMATE**: **«Ядерная Яма»** — взрыв покрывает 50% площади карты

---

## 4.8 КАРТА: 🪖 СОЛДАТ
*Параметры: FireRate, Radius (attackRange), Damage, Duration (lifespan)*

### Улучшение A: Боевая Подготовка
- **Тир 1** `soldier_damage`: `damage` x1.5
- ✨ **Тир 2**: **«Элитный Оперативник»** — `damage` x3
- 💥 **ULTIMATE**: **«Человек-Армия»** — убивает ZombieBoss за 5 попаданий

### Улучшение B: Расширенная Командировка
- **Тир 1** `soldier_duration`: `lifespan` +6s
- ✨ **Тир 2**: **«Наёмник»** — `lifespan` = весь уровень (постоянная единица)
- 💥 **ULTIMATE**: **«Бессмертный»** — перерождается через 10s после смерти (до конца уровня)

### Улучшение C: Подавляющий Огонь
- **Тир 1** `soldier_firerate`: `fireRate` x1.5 (интервал уменьшается)
- ✨ **Тир 2**: **«Пулемёт»** — `fireRate` = 0.08s, непрерывный огонь
- 💥 **ULTIMATE**: **«Пулемётчик»** — одновременно ведёт огонь по всем зомби в зоне

### Улучшение D: Орлиный Глаз
- **Тир 1** `soldier_range`: `attackRange` +35%
- ✨ **Тир 2**: **«Дальний Стрелок»** — дальность охватывает половину карты
- 💥 **ULTIMATE**: **«Глобальное Прикрытие»** — солдат атакует любого зомби на карте

### Улучшение E: Тактика Отряда
- **Тир 1** `soldier_count`: 2 солдата вместо 1
- ✨ **Тир 2**: **«Огневая Группа»** — 4 солдата образуют оборонительный периметр
- 💥 **ULTIMATE**: **«Взвод»** — 8 солдат формируют полный периметр вокруг выбранной точки

---

## 5. ОБЩИЕ УЛУЧШЕНИЯ (General Upgrades)

Всегда доступны в пуле независимо от колоды. MVP: реализовать первые 6.

| ID | Название | Эффект |
|---|---|---|
| `general_adrenaline` | **Адреналиновый Рывок** | Следующая эвакуация завершается на 50% быстрее (одноразово на волну) |
| `general_triage` | **Медицинский Протокол** | Каждая успешная эвакуация восстанавливает 25 HP всем баррикадам |
| `general_combatbonus` | **Боевой Бонус** | Убийство 10 зомби подряд даёт +50% XP за следующие 30 секунд |
| `general_cascade` | **Каскад** | После полной загрузки транспорта все карточки получают -1 от кулдауна |
| `general_veteran` | **Ветеранский Инстинкт** | Герой невосприимчив к панике 5 секунд после обнаружения зомби |
| `general_precisionstrike` | **Точный Удар** | Следующая бомба наносит x2 урон и не ранит гражданских (одноразово) |
| `general_fieldmedic` | **Полевой Медик** | 30% шанс что эвакуированный гражданин даёт двойной XP |
| `general_overcharge` | **Перегрузка** | Все активные карты: +30% к длительности на текущую волну |
| `general_desperatehour` | **Отчаянный Час** | Если осталось ≤5 гражданских — вместимость героя становится неограниченной |
| `general_doublexp` | **Ускоренное Обучение** | +25% к получаемому XP на следующие 2 волны |

---

## 6. СТРУКТУРА ДАННЫХ ДЛЯ CLAUDE CODE

### 6.1 Новые файлы для создания

```
Assets/Scripts/
  Roguelite/
    RunSessionData.cs        ← данные текущего забега (XP, стаки, модификаторы)
    RunUpgradeDefinition.cs  ← ScriptableObject для каждого улучшения
    UpgradeManager.cs        ← логика пула, выбора, применения
    XPManager.cs             ← начисление XP, триггер левел апа

  Heroes/
    HeroData.cs              ← ScriptableObject героя
    HeroManager.cs           ← выбор/смена героя (мета-уровень)

  UI/
    LevelUpScreen.cs         ← UI экрана выбора улучшения (3 карточки)
    UpgradeCardUI.cs         ← одна карточка улучшения в UI
    XPBarUI.cs               ← полоска XP в HUD
```

### 6.2 RunSessionData (синглтон, сбрасывается перед каждым уровнем)

```csharp
public class RunSessionData : MonoBehaviour
{
    public static RunSessionData Instance;

    // XP и прогрессия
    public int currentXP;
    public int currentRunLevel;
    public int[] xpThresholds; // {100, 150, 200, 250, 300, ...}

    // Стаки улучшений: upgradeId → количество раз взято (0-3)
    public Dictionary<string, int> upgradeStacks = new Dictionary<string, int>();

    // Числовые модификаторы: ключ → значение
    // Контроллеры карт считывают их при Init
    public Dictionary<string, float> modifiers = new Dictionary<string, float>();

    // Специальные флаги (для Ultimate эффектов)
    public HashSet<string> activeFlags = new HashSet<string>();

    public float GetModifier(string key, float defaultValue = 0f);
    public void AddModifier(string key, float value); // аддитивно
    public void SetFlag(string flagId);
    public bool HasFlag(string flagId);

    public void AddXP(int amount);
    private void CheckLevelUp();
    public void ResetForNewLevel();
}
```

### 6.3 UpgradeManager

```csharp
public class UpgradeManager : MonoBehaviour
{
    public static UpgradeManager Instance;

    // Все определения улучшений (заполнить в инспекторе)
    public List<RunUpgradeDefinition> allUpgrades;

    // Вернуть 3 случайных улучшения для показа игроку
    public List<RunUpgradeDefinition> GetUpgradeOptions(List<CardData> currentDeck, CardType heroType);

    // Применить выбранное улучшение
    public void ApplyUpgrade(string upgradeId);

    // Получить текущий тир улучшения (для отображения в UI)
    public UpgradeTier GetCurrentTier(string upgradeId);

    private float GetUpgradeWeight(RunUpgradeDefinition upgrade);
    private bool IsUpgradeMaxed(string upgradeId);
}
```

### 6.4 RunUpgradeDefinition (ScriptableObject)

```csharp
[CreateAssetMenu(fileName = "NewUpgrade", menuName = "ZombieGame/Run Upgrade")]
public class RunUpgradeDefinition : ScriptableObject
{
    public string upgradeId;
    public CardType targetCardType; // CardType.None = General upgrade
    public string displayName;
    public string displayNameEnhanced;
    public string displayNameUltimate;
    [TextArea] public string descriptionTier1;
    [TextArea] public string descriptionTier2;
    [TextArea] public string descriptionTier3;
    public Sprite iconTier1;
    public Sprite iconTier2;
    public Sprite iconTier3;
    public Color accentColor; // цвет рамки карточки в UI
}
```

### 6.5 Изменения в существующих контроллерах

Каждый контроллер карты должен быть изменён: после чтения из `PlayerProfile`, применить `RunSessionData` модификаторы.

**Пример для HelicopterController.cs:**
```csharp
private void Start()
{
    // Существующий код чтения из CardData...
    maxCapacity = (int)myCardData.GetCalculatedStat(StatType.Capacity, currentLevel);

    // НОВОЕ: применяем run-модификаторы
    if (RunSessionData.Instance != null)
    {
        maxCapacity += (int)RunSessionData.Instance.GetModifier("helicopter_capacity");
        verticalSpeed *= (1f + RunSessionData.Instance.GetModifier("helicopter_speed_mult"));
        attractRadius *= (1f + RunSessionData.Instance.GetModifier("helicopter_radius_mult"));
        loadTime += RunSessionData.Instance.GetModifier("helicopter_loadtime");
        boardingCooldown *= (1f - RunSessionData.Instance.GetModifier("helicopter_boarding_reduction"));

        // Ultimate флаги
        if (RunSessionData.Instance.HasFlag("helicopter_no_panic"))
            panicRadius = 0f; // никогда не паникует
        if (RunSessionData.Instance.HasFlag("helicopter_instant_landing"))
            verticalSpeed = 9999f; // мгновенная посадка
    }
}
```

**Аналогичные ключи для остальных карт:**
- `car_speed_mult`, `car_capacity`, `car_siren_radius_mult`, `car_boarding_reduction`, `car_crush_radius_mult`
- `sniper_range_mult`, `sniper_damage_mult`, `sniper_cooldown_reduction`, `sniper_duration`, `sniper_pierce_count`
- `bomb_radius_mult`, `bomb_damage_mult`, `bomb_cluster_count`, `bomb_count`
- `barricade_hp_mult`, `barricade_stun_duration`, `barricade_reflect_pct`, `barricade_count`
- `bait_radius_mult`, `bait_duration`, `bait_explosion_damage`, `bait_count`
- `soldier_damage_mult`, `soldier_duration`, `soldier_firerate_mult`, `soldier_range_mult`, `soldier_count`

### 6.6 XP начисление (GameManager + XPManager)

В `GameManager.cs` добавить вызовы при:
- `AddRescuedFromTransport()` → `XPManager.AddXP(humanCount * 20 + scientistCount * 40)`
- `Zombie.Die()` → `XPManager.AddXP(isBoss ? 50 : 5)`
- Полная загрузка транспорта → `XPManager.AddXP(25)` (бонус)

---

## 7. UI ТРЕБОВАНИЯ (MVP)

### 7.1 HUD — XP Бар
- Полоска XP в верхней части экрана (или встроена в карточку героя)
- Показывает: текущий XP / порог следующего уровня
- Лёгкая анимация при начислении XP

### 7.2 Экран Level Up
- `Time.timeScale = 0` при показе
- 3 карточки улучшений горизонтально
- Каждая карточка содержит:
  - Иконку карты (с тиром-бейджем: ★ ★★ ★★★)
  - Название улучшения (с учётом тира: обычное / Enhanced / ULTIMATE)
  - Описание эффекта текущего тира
  - Акцентный цвет для ULTIMATE улучшений (золотой/пурпурный)
- Тап по карточке → применить → скрыть экран → `Time.timeScale = 1`
- При ULTIMATE — особая анимация появления карточки (вспышка/эффект)

### 7.3 Выбор Героя (Meta-UI)
- На экране выбора уровня / главного меню добавить секцию «Герой»
- Показ текущего героя + заблокированные с условием разблокировки
- Кнопка смены героя (если открыт хотя бы 1 дополнительный)

---

## 8. MVP СКОУП

### Что реализовать в MVP

**Обязательно:**
- [ ] `RunSessionData.cs` — XP, стаки, модификаторы
- [ ] `XPManager.cs` — начисление XP из GameManager
- [ ] `UpgradeManager.cs` — пул и применение (без весов, random достаточно)
- [ ] Экран Level Up (3 карточки, пауза)
- [ ] XP бар в HUD
- [ ] Изменения во всех контроллерах карт (чтение модификаторов)
- [ ] 3 улучшения для каждой карты из колоды (Тиры 1–3)
- [ ] 3–4 общих улучшения
- [ ] HeroData + HeroManager (базовый, только Helicopter)
- [ ] `RunUpgradeDefinition` ScriptableObjects для всех MVP-улучшений

**Не в MVP (Post-MVP):**
- Весовая система в пуле
- Разблокировка Car и CombatHelicopter (геройская прогрессия)
- Весь экран выбора героя в мета-UI
- Специальные анимации для Ultimate
- Сохранение run-статистики (лучший уровень, макс. стак и т.д.)
- Синергии между улучшениями разных карт

### Приоритетный порядок реализации

1. `RunSessionData` → базовая система XP и модификаторов
2. `XPManager` → вызовы из `GameManager`
3. XP бар в HUD
4. Экран Level Up (UI без эффектов)
5. `UpgradeManager` — рандомный пул из доступных улучшений
6. Применение модификаторов в `HelicopterController` (начать с одного героя)
7. `RunUpgradeDefinition` ScriptableObjects для Вертолёта (5 улучшений x3 тира)
8. Применение модификаторов в остальных контроллерах
9. ScriptableObjects для остальных карт
10. Тестирование balance: средний уровень = 8–12 левел апов

---

*Документ создан для передачи в Claude Code. При реализации — следовать порядку из раздела 8.*
