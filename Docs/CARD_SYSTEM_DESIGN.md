# EvacZombie — Card System Design Doc
*Living document, updated as discussion progresses. Started 2026-08-23.*

---

## 1. Audit of the current art style (as it actually is, not as it feels)

Checked the actual assets in the project, not just your "cubes" description. Here's what's really there:

### 3D world (level)
- `Human.prefab` and `Scientist.prefab` use Unity's **built-in Cube mesh** (not a custom model).
- `Zombie.prefab` uses the built-in **Capsule mesh**.
- Materials are flat solid colors with no textures (`_BaseColor` / `_Color`, no texture maps): Bait = light blue, Barricade = teal, Bomb = orange, etc.

**Conclusion**: this isn't "minimalism as a deliberate art style" — it's literally Unity's default primitives with a color fill. Technically this is programmer art / a placeholder, not a chosen direction. BUT that's not bad in itself — flat geometry with color-coding by function (each card has its own material color) is already a working base for a minimalist direction, if done deliberately (genre references: Rebel Inc, Plague Inc, Mini Motorways — flat geometry, but it reads as a style, not a stand-in).

### Card icons (an important finding)
Checked where the icons on the screenshots come from (that "guy in a turban", "crate", "little green monster") — they ALL point to the same file: `Assets/TextMesh Pro/Resources/Sprite Assets/InlineIcons.png`.

**This isn't our art — it's the built-in icon set that ships WITH the TextMesh Pro package** (used for inline icons in text). So the card icons right now aren't a placeholder "in our style" — they're a random set of someone else's icons that happened to be on hand. None of the 8 card icons is art drawn for this game.

### What this means for card design
We currently have a **blank slate** for card art — not "minimalism that needs developing further", but "nothing of our own has been drawn yet". This changes the question from "how do we adapt our style to cards" to "which style do we pick from scratch, given that the 3D world is already flat/geometric and we're a solo dev with no art budget".

**Recommendation (to discuss)**: deliberately take the "flat/geometric minimalism" direction as a style, not as a temporary placeholder — cheap to do yourself/generate, visually matches the already-existing 3D world (no dissonance between a card icon and the unit on the field), and it's a real genre precedent, not a "cheap look". Alternative — stylized illustrations (like Marvel Snap/Hearthstone) look better on their own, but need art skill/time we don't have, and would clash in style with the cubes at level scope unless we overhaul all the art at once (not in MVP scope).

---

## 2. Card anatomy across 3 contexts

### In combat (HUD, `CardUI.cs`)
- Icon
- Cooldown fill + timer (already there)
- Border by category/rarity — color only, no text (new)
- NO name, NO stats — deliberate, so as not to overload the player in real time (see item 4 — this isn't "that's how everyone does it", it's a deliberate difference from turn-based card games like Marvel Snap)

### In the deck (`MetaCardUI.cs`, tile)
- Icon
- Card name (needs wiring up — currently unused)
- Level + duplicate progress (already there)
- Border by rarity (new)

### On tap (`CardInfoPopup.cs` — already almost fully implemented)
- Full name, description (needs wiring up — field is currently empty/duplicates the name)
- Category + rarity as text
- Level, curated "headline" stats with "+X at next level" (already there)
- Progress to upgrade, upgrade button (already there)

---

## 3. Rarity color coding

Checked against references (Hearthstone, Marvel Snap) — an almost universal convention:
**Common = white/gray → Rare = blue → Epic = purple → Legendary = orange**

`CardInfoPopup.cs` already has EXACTLY this palette hardcoded (4 tiers, no "uncommon/green" — like Hearthstone, not like Marvel Snap with its 6 tiers). Nothing needs to change, just extend it to the HUD and the tile.

---

## 4. References — what held up, what didn't

- **Color by rarity** — confirmed, a universal genre standard.
- **"In combat the card should be simpler"** — NOT a universal rule (Marvel Snap does the opposite, adding "juice" at the moment of play). For us this is a deliberate decision stemming from the real-time nature of the game (zombies move simultaneously, unlike turn-based card games), not blind convention-following.
- **"Leveling up is an event"** — top card games do this through a visual evolution of the art by rarity/tier. For us this will play out through a **gameplay bonus at level 5** (see section 5) instead of a visual change — we don't duplicate one idea two ways.

---

## 5. Card bonus at level 5 (finalized 2026-08-23)

Motivation: give the player a qualitative (not just numeric) leap when upgrading a card, reusing content from the frozen roguelite system instead of building from scratch.

**Decided**: one bonus per card, at the current max level (5), with no raised ceiling for now. The duplicate economy is untouched.

| Card | Bonus at level 5 | Technical note |
|---|---|---|
| Barricade | Reflects 25% of damage to the attacking zombie | Code already exists in `Barricade.cs` (`barricade_reflect_pct`), currently inactive (RunSessionData is frozen) |
| Sniper | Piercing kills all 3 targets instead of falling off (100%→50%→25%) | Almost free — `maxPierceTargets`/`pierceDamageFalloff` already exist in `Sniper.cs` |
| Bomb | Deals no damage to allies (Human/Soldier/Sniper) | Fixes a real current bug: Bomb can currently kill allies in the blast radius |
| Helicopter | A mini machine gun shoots zombies during loading | New mechanic, but the pattern already exists in `CombatHelicopter.SniperShoot()` — reuse a weaker version |
| Bait | Attracted zombies slow down on approach | New mechanic in `Bait.cs` |
| Soldier | Each soldier fires at 2 targets simultaneously | New mechanic |
| Car | Increased capacity (just a number) | Simple stat buff, a better idea to be figured out separately later |
| CombatHelicopter | Finishes off zombies below 20% HP | Checked: NOT useless (35 damage at level 5 vs. 100 HP zombie) |

**Technical dependency — UPDATED 2026-08-29**: initially thought all 8 bonuses would need a shared persistent modifier store (similar to `RunSessionData.GetModifier/HasFlag`). Checked the code of all 8 controllers — turns out it's NOT needed. Every controller (`Bait.cs`, `Bomb.cs`, `CarController.cs`, `CombatHelicopter.cs`, `HelicopterController.cs`, `Sniper.cs`, `Soldier.cs`, `Barricade.cs`) already reads the card's current level directly from `PlayerProfile.Instance.ownedCardsProgress` at start. The level-5 bonus is simply `if (currentLevel >= 5) { ... }` next to the already-existing code, with no new infrastructure.

The persistent modifier store is still needed ONLY for the future Talent Tree (where bonuses aren't tied to a specific card's level) — building it now for the sake of card bonuses would be premature infrastructure, solving a problem that doesn't exist yet.

---

## 6. Open questions / TODO

- [ ] Finalize the card art-style direction (flat minimalism vs. illustrations) — section 1
- [x] ~~Design a persistent modifier store~~ — not needed for card bonuses, see section 5 (updated 2026-08-29). Will be needed separately for the Talent Tree, once we get there.
- [ ] Fix the drag-reflow bug in the HUD (`CardUI.OnBeginDrag` reparenting breaks the Layout Group) — brief ready, see section 7
- [ ] Wire up `description` in `CardInfoPopup` — brief ready, see section 7
- [ ] Add a category/rarity border to `CardUI` (HUD) and `MetaCardUI` (tile) — brief ready, see section 7
- [ ] Curate, for each of the 8 cards, which stats to show the player (not automatable, manual work)
- [ ] Implement the 8 level-5 bonuses in each card's code — briefs ready, see section 7
- [ ] Commission/make real card icons (currently someone else's TMP placeholders)

---

## 7. Implementation plan — briefs for Claude Code (2026-08-29)

Order — from cheap/safe to expensive/risky. Each brief is a separate file next to this document, and they can be run one at a time and tested against each other.

1. **`CLAUDE_CODE_TASK_Cards_QuickWins.md`** — 3 independent fixes with no dependencies: the drag-reflow bug (`LayoutElement.ignoreLayout` + its own `Canvas` with override sorting instead of reparenting), wiring up `description` in the popup, a rarity border on the HUD/tile (factored into a shared `CardVisuals.GetRarityColor()` so we don't duplicate the switch a third time).
2. **`CLAUDE_CODE_TASK_Cards_Bonuses_A.md`** — level-5 bonuses for Barricade, Sniper, Bomb, Car. Cheap — either the code already exists (just swap the data source from `RunSessionData` to a direct level check), or it's a trivial stat buff.
3. **`CLAUDE_CODE_TASK_Cards_Bonuses_B.md`** — level-5 bonuses for CombatHelicopter, Soldier, Bait, Helicopter. More expensive — either a new mechanic (Helicopter's mini machine gun, Soldier's double-target), or logic that lives in a neighboring file (the Bait slowdown is implemented in `Zombie.cs`, not `Bait.cs`, since all the bait-chasing logic lives there).

**Not implementing now**: a persistent modifier store — see section 5, deferred until the Talent Tree.
