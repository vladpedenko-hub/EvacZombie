# Claude Code Task — Card Level-5 Bonuses, Batch B (needs new logic)

4 карты: CombatHelicopter, Soldier, Bait, Helicopter. Сложнее батча A — либо новая механика, либо логика вынесена в другой файл (`Zombie.cs`). Делать по одной, тестировать между собой — не пытаться сделать все 4 в одном заходе.

Как и в батче A: уровень карты уже доступен через `PlayerProfile.Instance.ownedCardsProgress` в каждом контроллере, никакого нового хранилища модификаторов не нужно.

---

## 1. CombatHelicopter — добивает зомби ниже 20% HP

**Файлы:** `Assets/Scripts/Zombie.cs`, `Assets/Scripts/Bonus/CombatHelicopter.cs`

### Шаг 1 — добавить публичный геттер HP в `Zombie.cs`

`currentHealth` сейчас `protected`, снаружи недоступен. Добавь публичное свойство (например, рядом с `public bool IsDead => isDead;`):
```csharp
public float HealthFraction => maxHealth > 0 ? (float)currentHealth / maxHealth : 0f;
```

### Шаг 2 — `CombatHelicopter.cs`

Сейчас `currentLevel` — локальная переменная в `Start()`, а бонус нужен в `SniperShoot()`. Подними в поле класса:

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

В `SniperShoot()`, найди:
```csharp
Zombie targetZombie = FindClosestZombie();
if (targetZombie != null)
{
	targetZombie.TakeDamage(sniperDamage);
	shootTimer = 0f;
	StartCoroutine(DrawTracer(transform.position, targetZombie.transform.position + Vector3.up));
}
```
замени на:
```csharp
Zombie targetZombie = FindClosestZombie();
if (targetZombie != null)
{
	bool willExecute = currentLevel >= 5 && targetZombie.HealthFraction <= 0.2f;
	targetZombie.TakeDamage(willExecute ? 99999 : sniperDamage);
	shootTimer = 0f;
	StartCoroutine(DrawTracer(transform.position, targetZombie.transform.position + Vector3.up));
}
```

---

## 2. Soldier — стреляет по 2 целям одновременно

**Файл:** `Assets/Scripts/Bonus/Soldier.cs`

Самая инвазивная правка в этом батче — трогает таргетинг. Делать аккуратно, не менять аим/визуал одиночной цели.

### Шаг 1 — поднять `currentLevel` в поле, добавить флаг

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
private bool hasDualTargetBonus = false;

private void Start()
{
	if (PlayerProfile.Instance != null && myCardData != null)
	{
```
После блока, где читается `currentLevel` (после `if (progress != null) currentLevel = progress.currentLevel;`), добавь:
```csharp
hasDualTargetBonus = currentLevel >= 5;
```

### Шаг 2 — добавить метод для поиска до N целей

По аналогии с `FindTopNTargets` в `Sniper.cs`, добавь в `Soldier.cs`:
```csharp
private System.Collections.Generic.List<Zombie> FindTargets(int n)
{
	var result = new System.Collections.Generic.List<Zombie>();
	foreach (var z in Zombie.AllZombies)
	{
		if (z == null || z.IsDead) continue;
		float d = Vector3.Distance(transform.position, z.transform.position);
		if (d <= attackRange) result.Add(z);
	}
	result.Sort((a, b) => Vector3.Distance(transform.position, a.transform.position)
		.CompareTo(Vector3.Distance(transform.position, b.transform.position)));
	if (result.Count > n) result.RemoveRange(n, result.Count - n);
	return result;
}
```

### Шаг 3 — в `ShootRoutine()`, стрелять по нескольким целям

Найди:
```csharp
private IEnumerator ShootRoutine()
{
	WaitForSeconds idleWait = new WaitForSeconds(0.03f);

	while (!isExtracting)
	{
		Zombie target = FindTarget();

		if (target != null)
		{
			DoShot(target);
			yield return new WaitForSeconds(currentFireRate);
		}
		else
		{
			yield return idleWait;
		}
	}
}
```
замени на:
```csharp
private IEnumerator ShootRoutine()
{
	WaitForSeconds idleWait = new WaitForSeconds(0.03f);

	while (!isExtracting)
	{
		Zombie target = FindTarget();

		if (target != null)
		{
			if (hasDualTargetBonus)
			{
				var targets = FindTargets(2);
				foreach (var t in targets)
					DoShot(t);
			}
			else
			{
				DoShot(target);
			}
			yield return new WaitForSeconds(currentFireRate);
		}
		else
		{
			yield return idleWait;
		}
	}
}
```

Прицеливание (`Update()`, поворот к `FindTarget()`) не трогаем — солдат визуально доворачивается на ближайшую цель, а трассеры рисуются на обе цели через `DoShot`.

---

## 3. Bait — притянутые зомби замедляются

**Файлы:** `Assets/Scripts/Bonus/Bait.cs`, `Assets/Scripts/Zombie.cs`

Логика движения зомби к приманке живёт в `Zombie.cs` (`currentBait`, `FindTargetWithBaitPriority`, `SetBaitTarget`), не в `Bait.cs` — сама `Bait.cs` только выставляет радиус/точку притяжения. Бонус трогает `Zombie.cs`.

### Шаг 1 — `Bait.cs`: пометить, разблокирован ли бонус

Добавь публичное свойство и приватное поле уровня:
```csharp
public bool HasSlowBonus { get; private set; }
```
В `InitStatsFromCardData()`, внутри блока `if (PlayerProfile.Instance != null && myCardData != null) { ... }`, добавь чтение уровня (сейчас там его нет вообще) и выставление флага:
```csharp
var progress = PlayerProfile.Instance.ownedCardsProgress.Find(p => p.cardId == myCardData.name);
int currentLevel = progress != null ? progress.currentLevel : 1;
HasSlowBonus = currentLevel >= 5;

attractRadius = myCardData.GetCalculatedStat(StatType.Radius, currentLevel);
```
(замени существующую строку `attractRadius = myCardData.GetCalculatedStat(StatType.Radius, currentLevel);`, которая раньше не объявляла `currentLevel` вообще — используй везде ту же переменную, которую ниже читает `duration`.)

### Шаг 2 — `Zombie.cs`: централизовать смену `currentBait`

Сейчас `currentBait = ...` присваивается в нескольких местах (`FindTargetWithBaitPriority`, `SetBaitTarget`, и очищается в паре мест как `currentBait = null`). Чтобы не забыть какой-то из путей, добавь приватный helper и замени ВСЕ прямые присваивания `currentBait = X;` на вызов этого метода:

```csharp
private void SetCurrentBait(Bait bait)
{
	currentBait = bait;

	if (bait != null && bait.HasSlowBonus)
		agent.speed = moveSpeed * 0.5f; // замедление, число временное — баланс на потом
	else
		agent.speed = moveSpeed;
}
```

Найди все места в файле, где напрямую пишется `currentBait = ...` (включая `currentBait = null;` в паре мест сброса), и замени на `SetCurrentBait(...)` с тем же аргументом (`SetCurrentBait(bait);` или `SetCurrentBait(null);` соответственно). Это гарантирует, что скорость корректно восстанавливается при потере/смене цели-приманки.

**Важно:** если `agent.speed` где-то ещё переустанавливается напрямую в других местах файла (например, после смерти/деспавна), проверь, что это не конфликтует с новым замедлением — если конфликт есть, сообщить, не гадать.

---

## 4. Helicopter — мини-пулемёт во время погрузки

**Файл:** `Assets/Scripts/Bonus/HelicopterController.cs`

Это чистый рескью-вертолёт, сейчас вообще не умеет стрелять. Паттерн стрельбы переиспользуем из `CombatHelicopter.SniperShoot()` (см. батч этого же файла) — упрощённая версия, без execute-бонуса.

### Шаг 1 — поднять `currentLevel` в поле, добавить поля стрельбы

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
private bool hasGunBonus = false;
private float gunShootTimer = 0f;
private float gunFireRate = 0.5f;
private int gunDamage = 10;

private void Start()
{
	if (PlayerProfile.Instance != null && myCardData != null)
	{
```
После блока чтения уровня, добавь:
```csharp
hasGunBonus = currentLevel >= 5;
```

### Шаг 2 — метод стрельбы (копия паттерна из CombatHelicopter)

Добавь:
```csharp
private void MiniGunShoot()
{
	if (!hasGunBonus) return;

	gunShootTimer += Time.deltaTime;
	if (gunShootTimer >= gunFireRate)
	{
		Zombie target = FindClosestZombieInRange(shootRangeForGun: 15f);
		if (target != null)
		{
			target.TakeDamage(gunDamage);
			gunShootTimer = 0f;
		}
	}
}

private Zombie FindClosestZombieInRange(float shootRangeForGun)
{
	Zombie closest = null;
	float minDist = shootRangeForGun;

	foreach (var zombie in Zombie.AllZombies)
	{
		if (zombie == null) continue;
		float dist = Vector3.Distance(transform.position, zombie.transform.position);
		if (dist < minDist)
		{
			minDist = dist;
			closest = zombie;
		}
	}
	return closest;
}
```

### Шаг 3 — вызывать во время погрузки

В `LoadRoutine()`, внутри цикла `while (currentLoad < maxCapacity && !isTooHot && waitTimer < loadTime) { ... }`, добавь вызов `MiniGunShoot();` в начале тела цикла (после строки `panicCheckTimer += dt;` подойдёт).

**Баланс `gunDamage`/`gunFireRate`/радиус 15f — временные числа, тюнить позже.** Визуал (трассер) не добавляем в этой задаче — только логика урона, чтобы проверить, что бонус вообще работает; трассер можно добавить отдельным шагом, скопировав `DrawTracer` из `CombatHelicopter.cs`, если понравится в игре.

---

## Acceptance checklist
- [ ] CombatHelicopter 5 уровня добивает зомби с HP ≤ 20%, на уровнях < 5 — обычный урон без изменений
- [ ] Soldier 5 уровня стреляет по 2 целям одновременно (если целей ≥ 2 в радиусе), на уровнях < 5 — как раньше, одна цель
- [ ] Bait 5 уровня: зомби, идущие к приманке, заметно медленнее обычных; после того как приманка исчезла/зомби выбрал новую цель — скорость восстанавливается до нормальной (нет "зависшего" замедления)
- [ ] Helicopter 5 уровня отстреливает ближайших зомби во время погрузки, не мешая посадке гражданских; на уровнях < 5 — как раньше, безоружный
- [ ] Ни у одной из карт < 5 уровня поведение не изменилось (regression check)
