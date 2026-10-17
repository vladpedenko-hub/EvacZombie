# EvacZombie — Priorities & Scope Audit
*Producer review · August 23, 2026*

---

## TL;DR

The diagnosis checks out against the numbers: over 5 days of active development (May 25-29) you closed out the core loop and meta layer in the first ~1.5 days, and the last ~2.5 days went almost entirely into designing and implementing the Heroes/Roguelite system — a massive layer (90+ unique upgrades) on top of a game whose core loop had never once been tested on real people. After that — 3 months of silence. This is a classic pattern: an exciting system won out over validation, and at some point the task volume became overwhelming.

**Verdict:** the game's core is solid and working. The problem isn't code quality — it's priority ordering. What to do next, below.

---

## 1. What actually happened (per the git log and file dates)

| Date | What happened |
|---|---|
| 05.25 | Project init, first commit |
| 05.26 | Market research, Barricade card added |
| 05.27 | `GDD_Heroes_Roguelite_System.md` (40KB) and `CLAUDE_CODE_TASK_Roguelite.md` (48KB) written in a single evening — a full design of 3 heroes × 5 upgrade trees × 3 tiers, plus the same for 5 ability cards |
| 05.28 | MVP version of the roguelite system implemented (RunSessionData, XPManager, UpgradeManager, LevelUpScreen) + a separate Ultimate Ability feature (civilian speed boost) |
| 05.29 | Final roguelite tweaks, "roguelike fix" commit at 14:12 — **and from this point on, silence in the repo until today** |

Out of 9 commits in the project's history, 5 are roguelite/ability work. That's not a feeling, it's a fact from the git log.

---

## 2. Inventory: what actually exists

### ✅ Core loop — built and looks functional
`GameManager` + `LevelManager`: a wave timeline with warning indicators, day/night transition, sudden death, a planning phase, win/lose conditions, per-level stars, a perfect-clear bonus (every survivor gets an animated evacuation). This is a genuine Atom Zombie Smasher core loop, and it's written carefully.

### ✅ Unit roster — complete
Sniper, Soldier, Bait, Bomb, Barricade, Car, Helicopter, CombatHelicopter — all 8 card types from the original are in place, each with its own controller.

### ✅ Meta layer — already a full F2P stack
Energy (cap 20 / 5-min regen), a card deck (5 slots), a regional saga map (4 regions), per-level stars, lootbox rewards, a tutorial system, a cheat manager. This is already EVERYTHING meta progression needs for a store release — and it's also already done.

### ⚠️ Meanwhile, content is thin
10 `LevelData` across 4 regions — that's ~2-3 levels per region. The meta layer on top (energy, stars, region rewards) is designed as if for dozens of levels, but in fact covers a very thin content layer. This is also a symptom of the same pattern: building the system before the content.

### 🟡 Roguelite Hero System — half the scope, but which half
Designed: 3 heroes × 5 branches × 3 tiers + 5 cards × 5 branches × 3 tiers + shared upgrades = **90+ unique effects**.
Actually implemented: 1 hero (helicopter) + 3 cards (Sniper/Bomb/Barricade) × 3 upgrades × 3 tiers + 2 shared = 14 ScriptableObjects in `Assets/Resources/Upgrades`.

So even the "trimmed-down MVP" is already a standalone system on the scale of its own game (in-run progression à la Vampire Survivors), bolted onto an already-complete F2P stack, before a single playtest of the core loop.

### 🟡 Ultimate Ability (civilian speed boost on charge)
A separate, far more modest feature — build up charge by rescuing people → one-shot speed boost for every civilian. It's much closer to "a feature the gameplay actually needs" than the roguelite layer.

### ❓ Unknown
You're not even sure the project currently runs without errors — it hasn't been opened in 3 months after a string of roguelite edits. This is the first thing to check, before any scope decisions.

---

## 3. Why this is over-engineering — not just a feeling

1. **The order is backwards.** Your own MARKET_RESEARCH.md stated priority #1 in plain text: "Solve the tap-to-place control UX — build a prototype and test it with real people" BEFORE anything else. Instead, the next step was the heaviest feature set in the whole project by volume. Not a single retention/engagement conclusion from the roguelite system was ever validated with players — you designed the balance of 90 upgrades blind.
2. **The reference you chose yourself doesn't do this.** Rebel Inc. (your own "ceiling and the path there" per your own market research) is a strategic map + tactical decisions, WITHOUT in-run roguelite progression. You added a system the benchmark you chose to aim for doesn't have.
3. **The goal is a portfolio, not revenue.** You confirmed it: learning + portfolio, a store release is mandatory, making money isn't the goal. For that goal, roguelite depth is worth close to nothing — a reviewer/recruiter cares more about seeing a finished, polished game than an unfinished system with 14 of 90 upgrades.
4. **The meta layer is already oversized relative to the content.** You've already built a polish-heavy F2P skeleton (energy/lootbox/region-map) for 10 levels of content. Adding yet another progression layer on top of that is a warning sign of "building systems because it's more interesting than content and polish."

This isn't a criticism of taste — the roguelite document's design is strong and well thought out, the upgrade weighting and tier structure are written competently. The problem is purely **ordering**: it should have come AFTER validation and release, not BEFORE.

---

## 4. Priorities: what to do next

### 🔴 Step 0 — Reality check (before deciding anything else)
- Open the project in Unity, confirm it compiles with no console errors
- Play through 1-2 levels start to finish as a player
- Write down every bug/crash/broken UI in its own list
- **Why first:** the last 3 commits are roguelite edits, ending in "roguelike fix." There's a real chance something is shaky. No point trimming feature scope that might simply be broken.

### 🟢 NOW (this week, before any new features)
- Fix everything found in step 0
- **Freeze the roguelite system as-is** (1 hero, 3 cards, 14 upgrades) — don't expand it to the full GDD. Make an explicit call: either it stays in v1 in its current trimmed form, or it's switched off behind a flag until a post-launch update (see below — I recommend the latter)
- Playtest the core loop with 2-3 real people (not developers) — the exact step from your own market research that's never been done once. Look specifically at the tap-to-place UX — that's your own stated top risk

### 🟡 NEXT (before the store release)
- Content: 10 levels is thin for a "real game" feel. Either add levels by hand, or (closer to the original's spirit and cheaper in content terms) add simplified procedural generation of waves/spawn points on the existing maps
- Mobile input/UX polish — based on the NOW-step playtest results
- UI polish per `UX_DesignPhilosophy.md` — the document is recent and solid, it's the one design doc worth carrying through to release as-is
- Settle monetization given the "no hope of revenue" goal: can be radically simplified — leave energy/lootbox as-is (already built, no need to touch it), but don't add a Battle Pass, rewarded ads, or IAP infrastructure — that's pure scope with no benefit toward your actual goal

### ⚪ LATER (after the v1 release, a content update)
- Expanding the roguelite to the full GDD (the remaining 2 heroes, the remaining cards) — this is where that work actually belongs: as post-launch content for a game that's already live and validated
- Additional regions / deeper procedural generation
- Anything social/PvP-shaped — not needed, your own market research already rules it out

### ❌ CUT / don't start
- Any new systems on top of the current ones (new card types, new heroes, new meta mechanics) until v1 is in the store
- Battle Pass, gacha tuning, rewarded ads — not needed without a monetization goal

---

## 5. What I'd suggest for the next session

1. Reality check in Unity (step 0) — 30-60 minutes, gives an accurate picture of "what we're actually working with"
2. Based on the results — either fix bugs, or (if everything's fine) go straight into planning a content pass and a mobile playtest
3. An explicit decision on the roguelite system (keep it trimmed / switch it off until an update) — write it into the GDD in one line so the question doesn't keep coming back

Tell me where you want to start — go through the reality check together, or straight to the level content plan.
