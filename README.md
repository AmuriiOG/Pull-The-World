# PULL THE WORLD — v2

> Turn the world. Let it fall.

An isometric-flavoured mobile puzzle game where you never touch the character. You **rotate the
level**; gravity stays pointing down the screen, and the character rolls, slides and falls into
whatever pocket you have tipped them towards. Get them to the door.

Unity **6000.2.9f1**, **URP 17.2**, portrait, built for phones.

---

## Run it

Open `Assets/PullTheWorld/Scenes/Game.unity` and press **Play**. You start on the main menu; hit
**PLAY**.

### Controls

One finger. That is the whole control scheme.

| | Mobile | Editor |
|---|---|---|
| Rotate the world | drag anywhere | left-mouse drag |
| Rotate a little | — | `Q` / `E` |
| Restart | button, top right | — |
| Pause / settings | button, top right | — |

The gesture is a **turntable grab**: whatever angle your finger sweeps around the centre of the
island, the island turns by. That means a circular drag, a horizontal swipe and a vertical swipe
near the edge all just work, without any of them being special-cased, and the spot under your
finger stays under your finger. Within ~110px of the pivot the sweep angle gets too noisy to use,
so it falls back to plain horizontal drag.

Rotation is **continuous, not snapped** — gravity is meant to feel analogue rather than stepped.
Most levels cap how far the world can be turned (`angleLimit`), which is what stops the early ones
being solved by one panicked full spin.

---

## What changed from v1, and why

v1 was the opposite game: the player was a kinematic pin that **never moved**, and you dragged and
twisted the entire world around them. It was technically clean — there are 620 deleted lines of
genuinely nice spring-driven rig code in `git show v1-move-world` — and it was consistently
confusing to look at. A fixed camera over a moving world and a moving camera over a fixed world
produce the *same image*. Players read it as "I am walking around", which is exactly what it was
not.

v2 inverts it into something you understand in about a second:

```
rotate the level  ->  gravity now points somewhere else relative to the level
                  ->  the character slides, rolls and falls
                  ->  steer them into the door
```

**Gravity is a constant and is never re-aimed.** This is the whole readability argument, and there
is a test (`GravityIsConstantAndPointsDown`) whose only job is to fail if anything ever starts
writing `Physics.gravity` again. v1's mechanic was "rotation re-aims gravity"; v2's is "rotation
moves the level while gravity stays put". They sound similar and they are not: only one of them
puts "down" where the player's intuition already has it.

Things that were deliberately deleted rather than kept behind a flag:

* **The anchor ring.** Its entire job was to sell "you are the fixed point of the universe".
* **The isometric camera** (46°/45°). A yawed camera turns a clean screen-plane spin into a skewed
  tumble, which is the ambiguity the redesign exists to remove. v2 looks down +Z with **zero yaw**
  and only ~13° of pitch — just enough to catch the top faces of the blocks so they read as solid.
* **The humanoid character.** The player is a physics object that rolls now, and an upright figure
  either has to be animated into a skate or tumble end over end. A ball is honest about what the
  simulation is doing, and the spin *is* the feedback that gravity is working.
* **Parallax on the backdrop.** In v1 the parallax factors were pinned to zero on purpose, because
  a scrolling backdrop is the signature of a moving camera. v2 has a genuinely rotating object in
  the middle of the frame, so a **completely static** backdrop is now the useful reference: if
  something in shot is definitely not turning, the island definitely is.

v1 is preserved and runnable — see [Going back](#going-back).

---

## Layout

```
Assets/PullTheWorld/
  Scripts/
    Core/      WorldRotator, RotateInput, PlayerBody, PlaneCameraRig,
               LevelManager, LevelDefinition, GameDirector, GameProgress,
               DynamicRegistry, DynamicProp, Spring
    Gameplay/  ExitPortal, Hazard, Collectible, PressurePlate, Gate, MovingPlatform
    Feel/      PtwAudio, Haptics, ImpactFeedback, ScreenFillQuad
    UI/        UiRoot, UiPanel, OnboardingHint
    Editor/    Ptw* generators (art, meshes, prefabs, levels, scene, build)
  Art/         generated meshes, materials, textures, physics materials, Poppins (OFL)
  Prefabs/     Blocks/ Props/ Gameplay/ Levels/
  Scenes/      Game.unity
  Tests/       PtwPlayTests
```

### The two files that matter

**`Scripts/Core/WorldRotator.cs`** — the mechanic. The puzzle lives in the world XY plane and this
root spins about world Z. Two implementation details are load-bearing:

* The root carries **one kinematic Rigidbody** and every static level collider is a child of it, so
  PhysX treats the whole island as a single compound actor. One thing to move, no broadphase churn.
* The rotation is applied with **`Rigidbody.MoveRotation` inside FixedUpdate**, not by writing
  `transform.rotation`. MoveRotation gives PhysX a real angular velocity, so contacts against the
  island are solved with the correct relative velocity and a rock resting on a tilting floor gets
  carried and shoved properly. Teleporting the transform instead lets the level pass *through*
  resting objects, which reads as the world scooping out from under them. v1 never hit this because
  it never rotated while anything was touching it.

Also: **rotating the level does not change gravity, so a settled Rigidbody has nothing to wake it**
and will happily hang on a wall that is no longer under it. `WorldRotator` wakes everything in
`DynamicRegistry` whenever the level is actually turning.

**`Scripts/Core/PlayerBody.cs`** — the character. A sphere collider under a ball mesh, locked to the
XY plane (`FreezePositionZ | FreezeRotationX | FreezeRotationY`) so it can only ever roll about Z.

A sphere is not a stylistic choice. v1 already learned the hard way that a box sits at PhysX's
default friction angle on a slope and catches on the seams between floor tiles — which is why it
abandoned crates as puzzle pieces and used boulders instead. A capsule that stays upright has the
same problem plus a tipping failure mode.

The impact reaction is a **uniform scale pulse, not a squash**, and that is a real constraint rather
than laziness: the visual is a child of the Rigidbody so it inherits the roll, and `localScale`
cannot express a scale along an arbitrary world axis. A directional squash would flatten whatever
direction the ball happened to be spinning through at the moment of impact. Uniform is
rotation-invariant, so it is always right.

The player is deliberately **not** a child of the rotating root. It lives in world space and the
level turns around it.

---

## Framing

Every level is framed **individually**, on load, from extents the generator computed by sweeping
the level's half-extents through its own rotation range (`LevelDefinition.viewExtents`).

This is not over-engineering, it is the direct consequence of the mechanic. A level gets spun to
arbitrary angles, so the box it occupies on screen changes constantly — an 11×6 island becomes 6×11
at ninety degrees. One global framing has to assume the largest level at its worst angle, so every
other level ends up sitting in the middle of a big empty screen. A level capped at 40° gets framed
far tighter than a free-spinning one, because it can never present its diagonal to the camera.

In portrait the **width** is what binds (visible width is only 1.125× the ortho size), so there is
always spare height. That slack is where the HUD lives at the top, and the static backdrop islets
at the bottom.

---

## Adding a level

Levels are ASCII maps in `Scripts/Editor/PtwLevels.cs`. The map is a **side view** — what you type
is very nearly what you see, with gravity straight down the page. Row 0 is the top of the screen,
and the grid is auto-centred on its own extent so the level always pivots about its middle.

```csharp
var b = new Builder(11, "My Level") { AngleLimit = 60f };
b.Map(
    "# . P . b . . D #",
    "# g g g g g g g #",
    "# g g g g g g g #",
    ". g g g g g g g .",
    ". . g g g g g . ."
);
b.Save();
```

`g` terrain (grassed automatically if the cell above is empty, stone if not) · `s` stone ·
`d` dark stone · `#` built stone · `P` spawn · `D` door · `K` key · `b` boulder · `c` crate ·
`f` fire · `k` spikes · `p` plate · `X` gate · `M` moving platform ·
`T`/`t` tree · `r` rock · `u` bush · `y` crystal

Then `Rebuild Levels` + `Rebuild Scene`.

Anything that stands on the terrain (`D f k t T r u y`) is **checked for ground beneath it** at
generation time and skipped with a warning if there is none. That check exists because the first
build put a tree one cell too high and it hung in mid-air — five seconds to fix, and a surprisingly
long time to notice in a screenshot.

### How conservative the current layouts are — read this before judging them

A rolling ball on geometry that can be rotated to any angle is **not a system you can design for on
paper**. Whether a ball escapes a one-deep pocket at 45° depends on its radius, the friction pair,
the chamfer on the block it is resting against, and how much speed it arrived with. The first draft
of these ten levels assumed several of those answers and got them wrong in both directions: two
were unsolvable because the only route ran through a hazard, and one was trivially solvable by
tilting either way.

So the shipped set is built from patterns whose traversability does not depend on any of that — a
flat tray the ball can always cross, end walls a cell taller than the floor, and mechanics arranged
along it. Hazards sit where **overshooting** reaches them rather than across the only path. Props
are parked by being run into an end wall, which is a reliable way to hold a rock on a plate rather
than a hopeful one.

That makes the set solvable and readable, and it leaves the **puzzle depth thinner than it should
be**. It needs a play pass with hands on it. The tests can prove that nothing explodes and that
every level loads; they cannot tell you whether a puzzle is interesting.

## Levels

| # | Name | Introduces |
|---|---|---|
| 1 | Tip It Over | rotation itself; any tip wins |
| 2 | Out of the Dip | how *far* you turn, not just which way |
| 3 | Mind the Spikes | a hazard, at the opposite end from the door |
| 4 | Share the Slope | the rock, with no stakes attached to it yet |
| 5 | Hold It Down | plate and gate — the first two-move puzzle |
| 6 | Smother It | fire, put out with the rock |
| 7 | Fetch the Gem | key before door |
| 8 | Catch the Ferry | timing, on a moving platform |
| 9 | Don't Overshoot | plate and gate, with a spiked notch past the door |
| 10 | All Together | a three-move combination of all of it |

---

## UI and onboarding

One canvas, `ScreenSpaceCamera` (**not** Overlay — Overlay bypasses the camera and would be missing
from every capture, and the captures are how this project gets art-directed). Every screen is a
full-bleed `UiPanel`, which is the only thing in the project that knows how to fade and pop, so all
the transitions match. `UiRoot` owns the whole flow: menu → level → complete → next.

Settings (sound, music, haptics) persist in `PlayerPrefs` via `GameProgress`, which is also the
single gate the audio and haptics layers check — rather than 30 call sites each testing a flag.

Onboarding is **wordless**. There is no tutorial text anywhere. Two devices only:

* **Rotate** — a finger sweeping an arc around the island, matching the actual gesture. Retires as
  soon as the world has been turned ~12°.
* **Point** — a pulsing ring parked on a world object (the hazard, the rock, the plate, the gem, the
  platform), which follows it on screen because its target is bolted to a rotating level. Retires on
  a timer or as soon as the player rotates.

Which one a level uses is one enum on `LevelDefinition`, and a level may only teach one thing.
Targets are found by searching the level for the relevant component, so there is nothing to forget
to wire up.

---

## Regenerating everything

All art, prefabs, levels and the scene are **generated from code** — no hand-authored assets, no
store assets. Menu bar → **Pull The World** → `Build Everything`, or the individual steps.

The whole loop is also headless, which is how the art actually gets directed:

```bash
# compile check
Unity.exe -batchmode -nographics -quit -projectPath . \
  -executeMethod PullTheWorld.EditorTools.PTWBatch.Ping

# regenerate art, prefabs, levels, scene
Unity.exe -batchmode -nographics -quit -projectPath . \
  -executeMethod PullTheWorld.EditorTools.PtwBuild.BatchAll

# tests + 1080x1920 captures into /Captures
Unity.exe -runTests -batchmode -projectPath . -testPlatform PlayMode \
  -testResults results.xml -screen-width 1080 -screen-height 1920
```

**Do not** pass `-nographics` to the test run — the captures need a renderer.

**Do not** trust the exit code alone. `PtwBuild` reports `PTW_BATCH_SUCCESS` even when individual
prefab saves have failed. Grep the log for `error CS`, `PTW: no serialized field` (a `Wire()` field
name typo) and `missing script`.

### One MonoBehaviour per file, named to match

Unity resolves a MonoBehaviour's script asset by **file name**. A behaviour declared in a file named
after something else compiles with no error and no warning, and then serializes as
`m_Script: {fileID: 0}`. The symptom is remote from the cause: every prefab containing it refuses to
save with only *"You are trying to save a Prefab with a missing script"*, and the build still
reports success. This cost half of the levels in one build — `DynamicProp` was sitting inside
`DynamicRegistry.cs`.

---

## Tests

13 PlayMode tests. They assert the promises v2 makes, which are nearly the opposite of v1's:

* the camera **never** moves — the one invariant inherited unchanged
* gravity is constant and points down, before and after rotation
* tilting the world **does** move the player
* the player stays in the XY puzzle plane at every angle
* the ball lands and settles rather than falling through the floor
* restart returns it to the spawn point
* angle limits are respected
* nothing goes non-finite or absurdly fast when every level is spun hard both ways
* falling out of the world fails the level
* every level has a spawn and an exit

They also write real 1080×1920 PNGs to `/Captures` — every level, two tilted shots, and the three
UI screens (`ui_01_main_menu`, `ui_02_settings`, `ui_03_level_complete`). The UI ones are in the
automated pass on purpose: those screens are as much of the deliverable as the gameplay is, and
they are the part most likely to be quietly broken by a layout change nobody re-renders.

Captures are rendered explicitly through a RenderTexture rather than via `ScreenCapture` +
`WaitForEndOfFrame` — the latter never fires under `-batchmode` and hangs the run forever rather
than failing.

---

## Going back

v1 is preserved as an annotated tag and a branch, and a checkout of it restores the project
byte-for-byte:

```bash
git checkout v1-move-world            # or: git checkout prototype/move-world-v1
```

v2 lives on `prototype/rotate-gravity-v2`. Nothing about v2 can damage v1.

---

## Known gaps

* **Level design needs a play pass.** See the section above — the layouts are deliberately safe and
  therefore thinner than they should be.
* **Water is not implemented.** `PTW/Water`, `M_Water` and `M_Ocean` still exist in the project from
  v1 and nothing references them. Fire is put out with a rock instead, which the brief allows.
* **No enemies and no breakables** yet. Both were in the brief; neither is in the build.
* The **moving platform** is the least-tested mechanic. It is built quite differently from the gate
  on purpose: a gate only has to block, so it is a plain child collider of the compound body, but a
  platform has to *carry* the player, and a child collider of a compound kinematic body has no
  velocity as far as the solver is concerned — the player gets pushed out by penetration resolution
  rather than carried. So the platform owns its own kinematic Rigidbody, detaches from the level
  hierarchy at startup, and recomputes its pose from the level's rotation every FixedUpdate.
* **Music is a setting with nothing behind it.** There is no music track; the toggle persists and
  gates nothing.
* Art is close to `PicReference/` but **not converged** — the palette was raised once after a
  capture-compare pass because the v1 values landed near `#5A6875` on screen against a `#97A3B3`
  target and the whole frame read as dusk.
* `PicReference/` is now **tracked** (the v1 README said to exclude it). `PALETTE.md` and
  `VISUAL_LANGUAGE.md` in there are the working art direction, and the panel renders are what the
  capture-compare loop is judged against. Note that `VISUAL_LANGUAGE.md` still describes v1's
  fiction — the anchor disc and the always-upright player — and those parts no longer apply.
* `Packages/mcp-unity/` is the Unity MCP bridge — delete it and `.mcp.json` if you do not want it.
  `.mcp.json` contains an absolute path with a Windows username in it.
