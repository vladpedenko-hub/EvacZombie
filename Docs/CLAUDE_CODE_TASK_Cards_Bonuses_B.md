# Claude Code Task — Card Level-5 Bonuses, Batch B (needs new logic)

4 cards: CombatHelicopter, Soldier, Bait, Helicopter. Harder than batch A — either a new mechanic, or logic that lives in another file (`Zombie.cs`). Do them one at a time, testing in between — don't try to do all 4 in one pass.

Same as batch A: the card level is already available via `PlayerProfile.Instance.ownedCardsProgress` in every controller, no new modifier store is needed.

---

## 1. CombatHelicopter — finishes off zombies below 20% HP

**Files:** `Assets/Scripts/Zombie.cs`, `Assets/Scripts/Bonus/CombatHelicopter.cs`

### Step 1 — add a public HP getter in `Zombie.cs`

`currentHealth` is currently `protected`, inaccessible from outside. Add a public property (e.g. next to `public bool IsDead => isDead;`):
```csharp
public float HealthFraction => maxHealth > 0 ? (float)currentHealth / maxHealth : 0f;
```

### Step 2 — `CombatHelicopter.cs`

Right now `currentLevel` is a local variable in `Start()`, but the bonus is needed in `SniperShoot()`. Promote it to a class field:

Replace:
```csharp
private void Start()
{
	int currentLevel = 1;
	if (PlayerProfile.Instance != null && myCardData != null)
	{
```
with:
```csharp
private int currentLevel = 1;

private void Start()
{
	if (PlayerProfile.Instance != null && myCardData != null)
	{
```

In `SniperShoot()`, find:
```csharp
Zombie targetZombie = FindClosestZombie();
if (targetZombie != null)
{
	targetZombie.TakeDamage(sniperDamage);
	shootTimer = 0f;
	StartCoroutine(DrawTracer(transform.position, targetZombie.transform.position + Vector3.up));
}
```
and replace with:
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

## 2. Soldier — fires at 2 targets simultaneously

**File:** `Assets/Scripts/Bonus/Soldier.cs`

The most invasive change in this batch — it touches targeting. Do it carefully, don't change the aim/visual for a single target.

### Step 1 — promote `currentLevel` to a field, add a flag

Replace:
```csharp
private void Start()
{
	int currentLevel = 1;
	if (PlayerProfile.Instance != null && myCardData != null)
	{
```
with:
```csharp
private int currentLevel = 1;
private bool hasDualTargetBonus = false;

private void Start()
{
	if (PlayerProfile.Instance != null && myCardData != null)
	{
```
After the block where `currentLevel` is read (after `if (progress != null) currentLevel = progress.currentLevel;`), add:
```csharp
hasDualTargetBonus = currentLevel >= 5;
```

### Step 2 — add a method to find up to N targets

Following the pattern of `FindTopNTargets` in `Sniper.cs`, add to `Soldier.cs`:
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

### Step 3 — in `ShootRoutine()`, shoot at multiple targets

Find:
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
and replace with:
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

Leave the aiming (`Update()`, turning to face `FindTarget()`) alone — the soldier visually turns toward the nearest target, and tracers are drawn for both targets via `DoShot`.

---

## 3. Bait — attracted zombies slow down

**Files:** `Assets/Scripts/Bonus/Bait.cs`, `Assets/Scripts/Zombie.cs`

The logic for zombies moving toward the bait lives in `Zombie.cs` (`currentBait`, `FindTargetWithBaitPriority`, `SetBaitTarget`), not in `Bait.cs` — `Bait.cs` itself only sets the attraction radius/point. The bonus touches `Zombie.cs`.

### Step 1 — `Bait.cs`: flag whether the bonus is unlocked

Add a public property and a private level field:
```csharp
public bool HasSlowBonus { get; private set; }
```
In `InitStatsFromCardData()`, inside the `if (PlayerProfile.Instance != null && myCardData != null) { ... }` block, add reading the level (currently not read there at all) and setting the flag:
```csharp
var progress = PlayerProfile.Instance.ownedCardsProgress.Find(p => p.cardId == myCardData.name);
int currentLevel = progress != null ? progress.currentLevel : 1;
HasSlowBonus = currentLevel >= 5;

attractRadius = myCardData.GetCalculatedStat(StatType.Radius, currentLevel);
```
(replace the existing line `attractRadius = myCardData.GetCalculatedStat(StatType.Radius, currentLevel);`, which previously didn't declare `currentLevel` at all — use the same variable everywhere that `duration` reads below it.)

### Step 2 — `Zombie.cs`: centralize changing `currentBait`

Right now `currentBait = ...` is assigned in several places (`FindTargetWithBaitPriority`, `SetBaitTarget`, and cleared in a couple of places as `currentBait = null`). To make sure none of those paths get missed, add a private helper and replace ALL direct assignments of `currentBait = X;` with a call to this method:

```csharp
private void SetCurrentBait(Bait bait)
{
	currentBait = bait;

	if (bait != null && bait.HasSlowBonus)
		agent.speed = moveSpeed * 0.5f; // slowdown, the number is provisional -- balance it later
	else
		agent.speed = moveSpeed;
}
```

Find every place in the file that writes `currentBait = ...` directly (including the couple of `currentBait = null;` resets), and replace them with `SetCurrentBait(...)` using the same argument (`SetCurrentBait(bait);` or `SetCurrentBait(null);` respectively). This guarantees speed is correctly restored when the bait target is lost/changed.

**Important:** if `agent.speed` is set directly anywhere else in the file (e.g. after death/despawn), check that it doesn't conflict with the new slowdown — if there is a conflict, report it, don't guess.

---

## 4. Helicopter — mini machine gun during loading

**File:** `Assets/Scripts/Bonus/HelicopterController.cs`

This is a pure rescue helicopter, currently unable to shoot at all. We reuse the shooting pattern from `CombatHelicopter.SniperShoot()` (see that card's section in this same batch) — a simplified version, without the execute bonus.

### Step 1 — promote `currentLevel` to a field, add shooting fields

Replace:
```csharp
private void Start()
{
	int currentLevel = 1;
	if (PlayerProfile.Instance != null && myCardData != null)
	{
```
with:
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
After the block that reads the level, add:
```csharp
hasGunBonus = currentLevel >= 5;
```

### Step 2 — shooting method (copy of the CombatHelicopter pattern)

Add:
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

### Step 3 — call it during loading

In `LoadRoutine()`, inside the `while (currentLoad < maxCapacity && !isTooHot && waitTimer < loadTime) { ... }` loop, add a call to `MiniGunShoot();` at the start of the loop body (right after the `panicCheckTimer += dt;` line works).

**`gunDamage`/`gunFireRate`/the 15f radius are provisional numbers, tune them later.** No visual (tracer) is added in this task — logic only, just to verify the bonus works at all; a tracer can be added as a separate step later, copying `DrawTracer` from `CombatHelicopter.cs`, if it reads well in the game.

---

## Acceptance checklist
- [ ] A level-5 CombatHelicopter finishes off zombies with HP ≤ 20%; at levels < 5, normal damage, unchanged
- [ ] A level-5 Soldier fires at 2 targets simultaneously (if there are ≥ 2 targets in range); at levels < 5, as before, a single target
- [ ] A level-5 Bait: zombies heading to the bait are noticeably slower than normal; once the bait disappears/the zombie picks a new target, speed is restored to normal (no "stuck" slowdown)
- [ ] A level-5 Helicopter shoots nearby zombies while loading, without interfering with civilians boarding; at levels < 5, as before, unarmed
- [ ] None of the cards below level 5 have changed behavior (regression check)
