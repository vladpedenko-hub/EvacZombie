# Claude Code Task — Card Level-5 Bonuses, Batch A (cheap ones)

4 карты: Barricade, Sniper, Bomb, Car. Все читают текущий уровень карты напрямую из `PlayerProfile.Instance.ownedCardsProgress` (уже существующий паттерн в каждом контроллере) — **никакого нового хранилища модификаторов строить не нужно**. Бонус — это просто `if (currentLevel >= 5) { ... }` рядом с уже существующим кодом.

Существующая roguelite-логика через `RunSessionData` в этих файлах **не трогать** — она заморожена для MVP и должна остаться как есть (используется позже, для Talent Tree). Новый код добавляется рядом, независимо.

---

## 1. Barricade — отражает 25% урона

**Файл:** `Assets/Scripts/Bonus/Barricade.cs`

Добавь поле:
```csharp
private bool hasReflectBonus = false;
```

В `Start()`, внутри блока `if (myCardData != null && PlayerProfile.Instance != null) { ... }`, после `if (progress != null) level = progress.currentLevel;`, добавь:
```csharp
hasReflectBonus = level >= 5;
```

В `TakeDamage(int damage)`, добавь новый блок (можно сразу после существующего `var run = RunSessionData.Instance; if (run != null) { ... }` блока, до `currentHealth -= damage;`):
```csharp
if (hasReflectBonus)
{
	Collider[] nearby = Physics.OverlapSphere(transform.position, 3f);
	foreach (var c in nearby)
	{
		Zombie z = c?.GetComponent<Zombie>();
		if (z != null)
		{
			z.TakeDamage(Mathf.RoundToInt(damage * 0.25f));
			break;
		}
	}
}
```

---

## 2. Sniper — пробитие убивает все 3 цели без затухания урона

**Файл:** `Assets/Scripts/Bonus/Sniper.cs`

Добавь поле:
```csharp
private bool hasFullPierceBonus = false;
```

В `Start()`, после блока где читается `currentLevel` (после строки `if (progress != null) currentLevel = progress.currentLevel;`), добавь:
```csharp
hasFullPierceBonus = currentLevel >= 5;
```

В `ApplyPiercingDamage(Zombie primaryTarget)`, найди:
```csharp
currentDamage *= pierceDamageFalloff;
if (currentDamage < 1f) break;
```
замени на:
```csharp
if (!hasFullPierceBonus)
{
	currentDamage *= pierceDamageFalloff;
	if (currentDamage < 1f) break;
}
```

(Урон по каждой из до 3 целей остаётся полным, вместо 100%→50%→25%.)

---

## 3. Bomb — не убивает своих (Human/Soldier/Sniper)

**Файл:** `Assets/Scripts/Bonus/Bomb.cs`

Сейчас `currentLevel` — локальная переменная в `Start()`, недоступная в `Explode()`. Нужно поднять её в поле класса.

Замени:
```csharp
private void Start()
{
	int currentLevel = 1;

	if (PlayerProfile.Instance != null && myCardData != null)
	{
```
на:
```csharp
private int currentLevel = 1;

private void Start()
{
	if (PlayerProfile.Instance != null && myCardData != null)
	{
```

В `Explode()`, найди:
```csharp
Human human = target.GetComponent<Human>();
if (human != null)
{
	Destroy(human.gameObject);
	continue;
}
```
замени на:
```csharp
Human human = target.GetComponent<Human>();
if (human != null)
{
	if (currentLevel < 5) Destroy(human.gameObject);
	continue;
}
```

И следом:
```csharp
if (target.CompareTag("Soldier") || target.CompareTag("Sniper"))
{
	Destroy(target);
	continue;
}
```
замени на:
```csharp
if (target.CompareTag("Soldier") || target.CompareTag("Sniper"))
{
	if (currentLevel < 5) Destroy(target);
	continue;
}
```

(Здание (`Building`) и зомби продолжают получать урон/уничтожаться как раньше — бонус касается только дружественных юнитов.)

---

## 4. Car — увеличенная вместимость

**Файл:** `Assets/Scripts/Bonus/CarController.cs`

В `Start()`, после строки:
```csharp
maxCapacity = (int)myCardData.GetCalculatedStat(StatType.Capacity, currentLevel);
```
добавь:
```csharp
if (currentLevel >= 5) maxCapacity += 3; // level-5 milestone bonus, число временное — баланс на потом
```

---

## Acceptance checklist
- [ ] Прокачать (через дебаг/читы, если есть) любую из 4 карт до уровня 5, убедиться что бонус активируется
- [ ] На уровнях < 5 поведение всех 4 карт визуально не изменилось (regression check)
- [ ] Barricade: при атаке зомби на баррикаду 5 уровня — ближайший зомби получает урон
- [ ] Sniper: пробитие 3 целей на 5 уровне наносит одинаковый (полный) урон по всем, не убывающий
- [ ] Bomb: взрыв 5-уровневой бомбы не убивает Human/Soldier/Sniper в радиусе, но убивает зомби и разрушает здания как раньше
- [ ] Car: вместимость на 5 уровне визуально/по факту выше на 3 единицы
