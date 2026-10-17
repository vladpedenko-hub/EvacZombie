# Claude Code Task — Card Level-5 Bonuses, Batch A (cheap ones)

4 cards: Barricade, Sniper, Bomb, Car. All of them read the card's current level directly from `PlayerProfile.Instance.ownedCardsProgress` (an already-existing pattern in every controller) — **no new modifier store needs to be built**. The bonus is simply `if (currentLevel >= 5) { ... }` next to the already-existing code.

The existing roguelite logic via `RunSessionData` in these files is **not to be touched** — it's frozen for MVP and should stay as-is (used later, for the Talent Tree). New code is added alongside it, independently.

---

## 1. Barricade — reflects 25% of damage

**File:** `Assets/Scripts/Bonus/Barricade.cs`

Add a field:
```csharp
private bool hasReflectBonus = false;
```

In `Start()`, inside the `if (myCardData != null && PlayerProfile.Instance != null) { ... }` block, after `if (progress != null) level = progress.currentLevel;`, add:
```csharp
hasReflectBonus = level >= 5;
```

In `TakeDamage(int damage)`, add a new block (can go right after the existing `var run = RunSessionData.Instance; if (run != null) { ... }` block, before `currentHealth -= damage;`):
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

## 2. Sniper — piercing kills all 3 targets with no damage falloff

**File:** `Assets/Scripts/Bonus/Sniper.cs`

Add a field:
```csharp
private bool hasFullPierceBonus = false;
```

In `Start()`, after the block where `currentLevel` is read (after the line `if (progress != null) currentLevel = progress.currentLevel;`), add:
```csharp
hasFullPierceBonus = currentLevel >= 5;
```

In `ApplyPiercingDamage(Zombie primaryTarget)`, find:
```csharp
currentDamage *= pierceDamageFalloff;
if (currentDamage < 1f) break;
```
and replace with:
```csharp
if (!hasFullPierceBonus)
{
	currentDamage *= pierceDamageFalloff;
	if (currentDamage < 1f) break;
}
```

(Damage against each of up to 3 targets stays full, instead of 100%→50%→25%.)

---

## 3. Bomb — doesn't kill allies (Human/Soldier/Sniper)

**File:** `Assets/Scripts/Bonus/Bomb.cs`

Right now `currentLevel` is a local variable in `Start()`, not accessible in `Explode()`. It needs to be promoted to a class field.

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

In `Explode()`, find:
```csharp
Human human = target.GetComponent<Human>();
if (human != null)
{
	Destroy(human.gameObject);
	continue;
}
```
and replace with:
```csharp
Human human = target.GetComponent<Human>();
if (human != null)
{
	if (currentLevel < 5) Destroy(human.gameObject);
	continue;
}
```

And right after that:
```csharp
if (target.CompareTag("Soldier") || target.CompareTag("Sniper"))
{
	Destroy(target);
	continue;
}
```
replace with:
```csharp
if (target.CompareTag("Soldier") || target.CompareTag("Sniper"))
{
	if (currentLevel < 5) Destroy(target);
	continue;
}
```

(Buildings (`Building`) and zombies keep taking damage/getting destroyed as before — the bonus only concerns friendly units.)

---

## 4. Car — increased capacity

**File:** `Assets/Scripts/Bonus/CarController.cs`

In `Start()`, after the line:
```csharp
maxCapacity = (int)myCardData.GetCalculatedStat(StatType.Capacity, currentLevel);
```
add:
```csharp
if (currentLevel >= 5) maxCapacity += 3; // level-5 milestone bonus, the number is provisional -- balance it later
```

---

## Acceptance checklist
- [ ] Level up (via debug/cheats, if available) any of the 4 cards to level 5, confirm the bonus activates
- [ ] At levels < 5, all 4 cards' behavior is visually unchanged (regression check)
- [ ] Barricade: when a zombie attacks a level-5 barricade, the nearest zombie takes damage
- [ ] Sniper: piercing 3 targets at level 5 deals the same (full) damage to all of them, not decreasing
- [ ] Bomb: a level-5 bomb's explosion doesn't kill Human/Soldier/Sniper in the radius, but still kills zombies and destroys buildings as before
- [ ] Car: capacity at level 5 is visibly/actually 3 units higher
