# Camera Readability Pass

Branch: `feature/camera-readability`, started 11.10.2026. Problem it fixes: the fit-to-width
camera (`CameraFraming.cs`, commit `557e90bb`) only ever *grows* the framing to fit the HUD/
card-hand/aspect — it was never meant to be the player's default view, but on L4-L10
(108-154 buildings, designed for a whole-city PC-style view) that's what the player always saw:
humans/zombies a couple of millimetres tall. This pass makes the default view close enough to
read, while keeping the old fit-to-width shot as the zoom-out ceiling the player can still reach.

Single source of truth for every tunable: `Assets/Resources/CameraReadabilityConfig.asset`
(script: `Assets/Scripts/Camera/CameraReadabilityConfig.cs`). Open it in the Inspector — every
field has a tooltip. Nothing below requires touching code to retune.

## What was added

| File | What it does |
|---|---|
| `Assets/Scripts/Camera/CameraReadabilityConfig.cs` | The tunables ScriptableObject. Lazy-loaded via `Resources.Load` (static `.Active` accessor) — no scene/prefab wiring anywhere. |
| `Assets/Scripts/Camera/CameraZoomPanController.cs` | Pinch-zoom, one-finger pan, edge auto-pan, the level-intro shot. Self-adds to the Main Camera from `CameraController.Awake()` — no manual Editor step. |
| `Assets/Scripts/Camera/OffscreenThreatIndicatorManager.cs` | Pooled screen-edge arrows pointing at off-screen zombie clusters + a dedicated boss indicator. Self-bootstraps like `Feedback`/`AudioManager`. |
| `Assets/Scripts/Camera/ActorVisualScaler.cs` | Applies `actorVisualScaleMultiplier` to Human/Scientist/Zombie/ZombieBoss without touching colliders or `NavMeshAgent`. |
| `Assets/Scripts/Feedback/RuntimeUiKit.cs` (+1 method) | Added `Triangle()` — a tiny procedural arrow sprite, same pattern as the existing `Rounded()`/`Gear()` helpers. |
| `Assets/Scripts/CameraController.cs` (+6 lines) | Creates `CameraZoomPanController` if missing; hands it the fit-to-width result at the end of `SetupCamera()`. The existing fit-to-width math itself is untouched. |
| `Assets/Scripts/InputManager.cs` (+1 line) | `UpdateDragging()` now also notifies the zoom/pan controller of the dragged card's screen position, for edge auto-pan. |
| `Assets/Scripts/EvacueeBase.cs`, `Zombie.cs` (+1 line each) | Apply the actor visual-scale multiplier in `Awake()`, before anything caches `transform.localScale`. |

**No manual Unity Editor step is needed for this pass** — everything self-wires at runtime.

## Tunables reference (`CameraReadabilityConfig.asset`)

### Zoom range
| Field | Default | What it controls |
|---|---|---|
| `humanReferenceHeightWorldUnits` | 0.3 | Human's rendered body height in world units (measured from `Human.prefab`'s `MeshRenderer` bounds). Only feeds the zoom-in calculation below. |
| `targetHumanScreenHeightMm` | 9 | How tall a Human should read on screen at max zoom-in. Brief's target was 8-10mm. |
| `referenceScreenHeightMm` | 150 | Visible screen height of the reference device (~6.5" phone, portrait) this is tuned against. |
| `defaultZoomFraction` | 0.475 | Where the level starts: 0 = the old fit-to-width shot, 1 = max zoom-in. Brief: ~45-50%. |

**If the default view still looks too zoomed out:** raise `defaultZoomFraction` first (quick, no
math). **If humans still look too small even at max zoom-in:** lower `targetHumanScreenHeightMm`
has no effect on that — raise it instead (counter-intuitive: a bigger target mm → a closer
required camera → a bigger human on screen). If that still isn't enough, `humanReferenceHeightWorldUnits`
may be stale (re-measure `Human.prefab`'s `MeshRenderer.bounds.size.y` in a quick `eval` call if the
model/scale ever changes).

### Pan
| Field | Default | What it controls |
|---|---|---|
| `panBoundsPadding` | 4 | Extra world-space margin around the computed playable area before pan clamps. |
| `panSensitivity` | 1 | One-finger-drag / mouse-drag responsiveness. 1 = the point under your finger stays under your finger. |

Pan bounds are computed once per level load (one frame after spawn, so Humans/Scientists are
already placed) from: every `SpawnPointMarker`, every `Building`-tagged object's `BoxCollider`
bounds, and the level's actual spawned Human/Scientist positions — **not** the level prefab's own
renderer bounds, which the original `557e90bb` commit already found to be inflated by background
geometry (skydomes, oversized ground tiles) and therefore useless as a bounds source.

**If pan feels too tight/claustrophobic:** raise `panBoundsPadding`. **If pan reaches into empty
dead space beyond the city:** lower it.

### Pinch / scroll zoom
| Field | Default | What it controls |
|---|---|---|
| `pinchDistanceForFullZoom` | 800 | Screen-pixel pinch-distance change needed to sweep the *entire* zoom range. Lower = touchier pinch. |
| `scrollZoomSpeed` | 0.08 | Editor/desktop-only: mouse-scroll-wheel zoom speed. Touch devices use pinch; this exists purely so the feature is testable without a device (see "What couldn't be verified" below). |

### Edge auto-pan (while dragging a card)
| Field | Default | What it controls |
|---|---|---|
| `edgeAutoPanScreenFraction` | 0.1 | How close to a screen edge (fraction of width/height) a dragged card needs to be before the camera starts panning that way. |
| `edgeAutoPanSpeed` | 18 | Camera pan speed (world units/sec) while edge auto-pan is active. |

### Level-intro shot
| Field | Default | What it controls |
|---|---|---|
| `introHoldDuration` | 1.2s | How long the full zoom-out (fit-to-width) view holds at level start. |
| `introZoomDuration` | 0.6s | How long the ease from zoom-out to the default view takes. |
| `introSkippable` | true | Any tap/click during the intro jumps straight to the default view. |

### Off-screen threat indicators
| Field | Default | What it controls |
|---|---|---|
| `indicatorPollInterval` | 0.25s | How often the off-screen scan re-runs. Lower = more responsive, more CPU. |
| `indicatorClusterRadius` | 15 | World-space radius for grouping nearby off-screen zombies into one arrow. |
| `maxIndicatorCount` | 8 | Max simultaneous *regular*-zombie indicators (the boss always gets its own on top of this). |
| `indicatorEdgeInset` | 0.04 | How far the indicator sits inside the *safe area* edge (fraction of safe-area height). |
| `indicatorSize` | 64 | Icon size, reference-resolution (1080x1920) pixels. |
| `bossIndicatorSizeMultiplier` | 1.5 | Boss icon size, as a multiple of the regular size. |
| `indicatorPulseScale` / `indicatorPulseDuration` | 1.6 / 0.35s | Scale-pulse applied when an indicator first appears. |
| `regularIndicatorColor` / `bossIndicatorColor` | red / purple | Tints — these are plain `Image.color` on a white procedural triangle, so any color works. |

The "pulse on new spawn" behavior is an approximation: it pulses whenever the total off-screen
zombie count grows since the last poll, not a true per-wave spawn event (that would need a
`LevelManager` hook, which this pass deliberately avoided — see "Design notes" below). In
practice this reads the same to a player.

### Actor visual scale
| Field | Default | What it controls |
|---|---|---|
| `actorVisualScaleMultiplier` | 1.0 | Global visual-only scale for Human, Scientist, Zombie, ZombieBoss. Colliders and `NavMeshAgent` radii are compensated/unaffected — gameplay ranges (attack distance, panic radius, etc.) never change, only render size. |

**This is the one field the brief said Vlad would tune himself.** Start here if actors still look
too small/large after the camera changes above — it's a flat multiplier, so 1.2 = 20% bigger,
0.8 = 20% smaller, with zero risk of breaking hitboxes or pathing.

## Design notes (why things work the way they do)

- **Zoom is one scalar ("ground distance"), not separate ortho/perspective logic duplicated
  everywhere.** All 10 levels' camera data use `cameraType: Perspective` with a fixed straight-down
  rotation (`cameraRotation: {x: 90, y: 0, z: 0}`), so "zoom" is just how far the camera sits above
  the ground along its forward axis, interpolated between the fit-to-width result (max zoom-out)
  and a value derived from the Human-readability target (max zoom-in). The orthographic branch in
  `CameraZoomPanController.ApplyToCamera()` is best-effort for the debug 2D toggle or any future
  orthographic level data — not exercised by any of the 10 current levels, so treat it as
  unverified.
- **Playable bounds never touch `LevelManager.cs`.** Per this repo's rule (`CLAUDE.md`:
  `LevelManager` — don't touch without an explicit ask), bounds are gathered independently via
  `FindObjectsByType<SpawnPointMarker>()` + `GameObject.FindGameObjectsWithTag("Building")` +
  `Human.AllHumans`/`Scientist.AllScientists`, in a coroutine that waits one frame after
  `CameraController.SetupCamera()` is called — by then `LevelManager.LoadLevel()`'s synchronous
  `SpawnHumans`/`SpawnScientists` calls (which happen later in that same frame) have already run.
- **A touch is classified once, at `TouchPhase.Began`, and remembered for its whole lifetime.**
  `CardUI.OnBeginDrag` disables its own `CanvasGroup.blocksRaycasts` mid-drag (so raycasts can
  reach the world underneath for placement targeting) — if the pan system re-checked
  `IsPointerOverGameObject` every frame instead of once at touch-down, a card drag could get
  reinterpreted as a pan the instant that happens. Classifying once at `Began` (before
  `OnBeginDrag` fires) avoids that.
- **Actor scaling compensates colliders instead of restructuring prefabs.** Human/Scientist/
  Zombie all have their `MeshRenderer` on the same GameObject as their `BoxCollider` and
  `NavMeshAgent` (no separate "visual" child) — confirmed by walking all four prefabs' hierarchies
  before writing this. Scaling the root transform is the simplest way to grow the render size, so
  `BoxCollider.size`/`center` on that same object is divided by the multiplier to cancel the
  effect on its world-space footprint (`NavMeshAgent.radius`/`height` need no compensation —
  Unity always treats them as absolute world units, never scaled by the transform). `ZombieBoss`
  is the one exception: its body render already lives on a separate `Visual` child with no
  collider, so that child just scales directly and nothing needs compensating.
- **`CameraSwitcher` (the debug 2D/3D toggle) was left untouched.** It isn't actually attached to
  anything in `Gameplay.unity` today (checked — no match in the scene file), so there's no live
  conflict with the new zoom/pan controller to design around. If Vlad ever manually drags that
  component onto the camera for debugging, expect it to fight `CameraZoomPanController` for
  control of `transform.position` every frame (whichever's `Update()` runs last wins) — a known,
  pre-existing-component edge case, not something this pass attempted to reconcile.

## What couldn't be verified without a device build

This environment has no connected phone and no Device Simulator package installed (same
limitation noted in the responsive-layout pass, `CLAUDE.md` §"Респонсив-лейаут"). Everything
below was checked as far as this Editor session allows — math verified via direct `eval` calls and
reflection on live field values — but not on real touch hardware:

- **Real multi-touch gesture feel.** `Input.touchCount` stays 0 in this Editor without real touch
  hardware, so pinch/one-finger-pan/edge-auto-pan were verified by calling the underlying private
  methods directly via reflection and checking the resulting `panCenterXZ`/`zoomLevel` deltas, not
  by an actual pinch gesture. The *math* is confirmed correct (direction, magnitude, screen-edge
  clamping); the *feel* (gesture recognition latency, accidental-pinch-during-pan, OS-level
  edge-swipe gestures stealing an edge-pan touch) is not.
- **The actual "8-10mm" physical size.** The zoom-in limit is derived from a reference device
  (150mm visible screen height) and the Human prefab's measured world size — correct in theory,
  but a real device's PPI, viewing distance, and the Editor Game view's non-physical scaling mean
  this should be eyeballed on Vlad's own phone before trusting the number blindly.
  `defaultZoomFraction`/`targetHumanScreenHeightMm` are the two fastest knobs to correct it.
- **Real notch/safe-area behavior for the threat indicators.** `Screen.safeArea` returns the full
  screen with zero inset in this Editor (no Device Simulator), so the indicator's safe-area-aware
  edge clamping was verified against a notch-less "safe area" only.
- **On-device performance** of the per-frame touch classification + the indicator poll loop.
  Both are cheap (a handful of list iterations, no per-frame allocation in the hot paths other
  than the clustering list, which is small), but only a real device can confirm it doesn't show up
  in a profiler.
