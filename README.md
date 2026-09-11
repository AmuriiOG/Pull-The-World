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
  the middle of the frame, so the backdrop's job is to be the thing that is **definitely not
  turning**. It is allowed to *translate*, though, and now does — see [Sky motion](#sky-motion):
  clouds drift at depth-scaled speeds and every layer takes a small share of a shift driven by
  the level's tilt and the orb's position. Nothing in the sky ever rotates.

v1 is preserved and runnable — see [Going back](#going-back).

---

## Layout

```
Assets/PullTheWorld/
  Scripts/
    Core/      WorldRotator, RotateInput, PlayerBody, PlaneCameraRig,
               LevelManager, LevelDefinition, GameDirector, GameProgress,
               DynamicRegistry, DynamicProp, Spring
    Gameplay/  ExitPortal, Hazard, Collectible, PressurePlate, Gate, MovingPlatform,
               WaterVolume, Enemy, Breakable
    Feel/      PtwAudio, PtwMusic, Haptics, ImpactFeedback, ScreenFillQuad, SkyTheme
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

And: **the level is a turntable, and PhysX will treat it as one unless told otherwise.** A rock
resting on the floor picks up the floor's tangential velocity through the contact, so when a drag
stops the level stops but the rock keeps going — straight up if it was on the rising side. In
testing, a 110°/s drag hopped a rock 1.2 m and clean over a crate; low-friction rocks also lag the
floor sliding under them and drift *uphill* mid-drag. `WorldRotator.CoRotate` therefore makes every
**prop** in `DynamicRegistry` ride the level explicitly each physics step: strip the velocity it was
given last step, rotate its own motion by this step's turn, add the exact chord velocity that keeps
it with the level. Turning the world changes *only* gravity for rocks and enemies — no sling, no
lag, no centrifugal drift — so a puzzle piece ends up where the puzzle says. `DynamicProp`'s speed
clamp clamps the prop's own motion, not the ride.

**The player is deliberately left out.** The ball keeps raw turntable physics — carried and slung
by a fast spin, skidding rather than rolling above ~2.4 m/s (PhysX's default spin ceiling, left
alone on purpose). Play-testing called that feel perfect, and an attempt to make the ball ride the
level and roll "correctly" was reverted the same day. The ball is the toy; the rocks are the tools.

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

**Falling off restarts fast.** The ball is doomed the moment it is below the level's framing box
(`viewExtents.y` plus a small margin) — that box already covers the level's whole rotation range, so
below it there is nothing that can ever be under the ball. The first build waited for a 26 m radius,
which at the speed cap was nearly two seconds of empty screen, then a further 0.75 s death pause; a
fall now restarts in about a quarter of a second (`fallRestartDelay`). On-screen deaths keep the
longer pause so the pop reads.

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
`w` pool (goes **in the floor row** in place of a block) ·
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
| 11 | Both Gems | two keys on opposite sides; the door between them is only live once you hold both |
| 12 | Careful Now | fire at both ends, door in the middle — judging the angle, not just choosing it |
| 13 | Rock Traffic | two rocks, one plate; the props interfere with each other |
| 14 | Stop at the Door | gem, door, spiked notch just past it |
| 15 | Three Moves | plate, gate and two gems in a fixed order |
| 16 | Ferry to the Gem | the moving platform, now delivering you to a key |
| 17 | One Rock, Two Fires | a single rock smothers both on one pass |
| 18 | The Last Turn | every piece, in an order that has to be worked out |
| 19 | Wade Through | water, with no stakes: roll in, bob, roll out |
| 20 | Pour It Out | water as a tool — tip the pool onto the fire |
| 21 | Bowl It Over | the enemy; the rock between you and it is the answer |
| 22 | Break Through | a crate only a fast heavy rock can smash |
| 23 | Clear the Way | one rock through crate and enemy, then the gem, then the door |
| 24 | Spring Step | the **spring pad**: roll on, get thrown onto a two-block ledge |
| 25 | Hold, Then Cross | plate at the near end, ferry over a pit, gate before the door |
| 26 | Smash and Grab | the crate guards the gem |
| 27 | Over the Wall | the rock goes over a wall via the pad; the tilt aims it onto the plate |
| 28 | Long Jump | a hole through the island: tip the enemy in, then jump it |
| 29 | Double Trouble | one rock, two enemies in a line |
| 30 | Steam | pour the pool on the fire, float across, pad over the wall |
| 31 | Break In | crate between rock and plate — needs a hard tilt |
| 32 | Stairway | two pads, three ledges; aim the second throw |
| 33 | Gauntlet | right: rock bowls the enemy; left: douse, float, gem, door |
| 34 | Trampoline Park | gem on a floating slab between two pads |
| 35 | The Long Way | rock through crate and enemy, then water, fire, pad, ledge, door |
| 36 | Two Steps Up | two throws up two ledges; the second has to be aimed |
| 37 | Rock Ferry | the rock rides the ferry to the far plate |
| 38 | Mind the Gap | throw the rock over a hole onto the plate |
| 39 | Bowling Alley | one rock, three enemies |
| 40 | Double Ferry | two ferries in a row |
| 41 | Drop In | door and enemy share a pit; drop the rock on it first |
| 42 | Switchback | roll under the tier, bounce onto it, roll back the other way |
| 43 | Plug the Hole | roll the rock off a tier into a one-cell pit holding an enemy, then roll over it |
| 44 | Ferry, Then Fly | off the ferry onto a pad, up to the door |
| 45 | Twin Gaps | ferry across one gap, throw across the next |
| 46 | Hopscotch | two one-cell holes, a pad before each |
| 47 | Long Shot | the gem hangs over the hole; only one arc takes it |
| 48 | Rolling Thunder | rock → crate → rock → crate → enemy → plate, in one hard tilt |
| 49 | Skylight | up through a gap in the tier and onto the roof |
| 50 | Grand Finale | everything, then two ledges to the door |

Levels 36–50 stop being trays: floating **tiers** the ball rolls under and is thrown onto, **holes**
it must clear, ferries in sequence, rocks dropped from height. Two more numbers were relied on: a
rock falling one block lands at ~7 m/s (enough to crush or break), and a rock sitting in a one-cell
pit leaves a 0.32 m gap that the ball rolls over.

Levels 24–35 add one new toy — the **spring pad** (`j`) — and then combine it with everything
before it. A pad throws whatever lands on it (ball, rock, enemy) at 11 m/s along the **level's** up,
so a tilt aims the throw: 2.5 m straight up under 24 m/s² gravity, enough for a two-block ledge and
never a three-block one. Detection is the usual explicit zone check (`BouncePad`), with a per-body
cooldown so the zone cannot re-fire while the body is still inside it. The ball's own physics
settings are untouched; the pad acts on it exactly the way the portal does.

## Enemies and breakables

**Enemy** — a hostile thing. It obeys gravity exactly like a boulder, so tilting moves it, and it
kills the player on contact. It can be **killed**: bowled over by a heavy prop arriving at speed,
or rolled into spikes or fire.

What makes it scary rather than a purple rock, in order of how much each does:

* **It notices you.** Inside 5.5 m the eyes flare from a dull ember to a hard pulsing red glare,
  the aura swells, it hisses and does a startle hop. Calm → alert is a visible, audible moment,
  and you learn the range by being noticed. It loses interest again past 7.5 m.
* **It lunges.** Alert, grounded, within 2.5 m and roughly level with you, it coils for 0.18 s
  (the tell — it squashes and snarls) and springs: a burst along the floor with a little lift.
  Two-second cooldown, so it is a threat you can time, and a tilt still always wins.
* **It stares.** The body, grin and eyes are held world-upright and lean towards you, while the
  nine-spike ring rolls with the rigidbody — a creature gliding on a saw, not a tumbling ball
  with a face painted on. A red point light and an additive aura follow the eyes.
* **It breathes** — slow and deep when calm, fast and shallow when hunting — and sheds dark smoke
  when it moves.

Four throat sounds (`EnemyAlert`, `EnemySnarl`, `EnemyBite`, `EnemyDie`) are synthesised like
everything else, all built on one `Growl()` (odd harmonics under a 27 Hz tremor).

**Breakable crate** — blocks the way until something with mass ≥ 1.2 approaches it at ≥ 3 m/s. The
player is mass 1.0, so the ball can lean on it forever; only a rock with real momentum breaks it.

Detection differs between the two, and the reason is a PhysX detail worth knowing. An enemy is its
own rigidbody, so rock-vs-enemy is a fresh actor pair and the rock's `OnCollisionEnter` fires.
A crate is a child collider of the level's compound kinematic body — the same body the rolling rock
is already touching through the floor — so the crate contact arrives as a *Stay* on the existing
pair and `Enter` never comes. `Breakable` therefore detects the hit itself in `FixedUpdate`: it
walks `DynamicRegistry`, takes each heavy body's approach speed along the line to the crate, and
breaks when the surface gap will close this step. Same explicit-query pattern as `Hazard`,
`PressurePlate` and `WaterVolume`.

**A lost rock comes back.** A hard fling can hop a rock clean off the island (the rising side of
the floor kicks it). `DynamicProp` notices when a prop passes the fall radius and puts it back at
its start cell — in the level's *current* orientation, with a puff — so a plate or crate level can
never be soft-locked. Enemies opt out (`respawnIfLost = false`); an enemy that falls off is dead.

**Pressure plates now need a rock.** The player rolling onto a plate does nothing. With the player
able to press, every plate level collapsed into "tilt towards the plate"; requiring a parked rock
is what makes them two-move puzzles.

## Theme — from the artists' mockups

The look is derived from two reference paintings in `Assets/PullTheWorld/Art/Mockup/`
(`image (7).png` is gameplay, `image (9).png` is the main menu). They replaced the earlier night
theme wholesale: a **pastel dawn** — peach-to-lilac sky with a soft sun, three hazy lilac mountain
ridges, cumulus clouds in front of and behind the island, small floating islets with their own
portals — around a light-grey stone island capped with mossy grass, a turf fringe, tufts, flowers
and hanging vines. The player is a **glass orb** (its own shader: fresnel rim, iridescent band,
fixed highlight) with a four-point star core, an orbit ring, a halo, sparkles and a cyan light.
The exit is a pointed cream arch with diamond studs and a warm swirling energy fill.

Everything is procedural and lives in three places, so the theme stays swappable: the palette
and materials in `PtwArt`, the per-chapter sky in `SkyTheme`, and lighting, grade and UI colour in
`PtwScene`. Lighting is deliberately almost flat — a strong neutral ambient with a weak warm key —
because in the mockup a block's top face is only ~6% brighter than its front. The grade is close
to identity: no tonemapping (Neutral and ACES both pull pastels towards grey), saturation +6, a
light blush vignette, and a warm bloom that only the portal, the orb rim and the studs can reach.

Measured targets from the mockups that the captures are checked against: sky top ≈ (254,214,198),
stone front face ≈ (172,161,153) (the *gameplay* painting's wall — the menu painting's stone is
lighter, and using that reading first made the island near-white), grass top ≈ (141,184,106),
portal interior ≈ (254,200,123), thick cloud ≈ (254,229,208) thinning to lilac (220,212,220),
PLAY button ≈ (121,144,119). Current capture: stone fronts ≈ (181,169,158), portal ≈ (243,196,130).

**The "too white" pass (2026-09-11).** The first pastel build read as a wall of white: opaque white
cloud quads over half the frame, near-white stone, a portal blown out to a white oval. Four
changes, all in `PtwArt`/`PtwScene`: stone albedos darkened ~30% in linear to hit the gameplay
mockup's wall; cloud quads made smaller, fewer, peach-tinted and translucent (0.72 behind the
ridges, 0.55 in front of the island) with a wide feathered edge, and the backdrop shader's own
clouds thinned; the near ridges shifted from lilac to the mockup's grey-teal; and the portal fill
switched from additive to alpha blending — additive light over a bright sky can only go whiter,
so gold was unreachable — with an amber core kept under the bloom threshold. The orb also lost
its multiply **contact-shadow blob**: it stayed world-flat while the level tilted, so it ended up
as a dark blue-grey ellipse floating beside a glowing glass ball ("something black on the player").
The mockup's orb is a light source and casts nothing.

### The doorway

Rebuilt against the paintings on 2026-09-11 (`PtwMeshes.DoorArch`/`ArchFill`, `PtwPortalEnergy.shader`,
`PtwPrefabs.BuildPortal`). What the reference actually shows, and what was changed to match:

* **An equilateral pointed arch of large cream stones.** Two tall jambs a side, three voussoirs a
  side *swept along the arc* (`ArcStone`) so the inner and outer curves are smooth, a pointed
  keystone filling the notch where the arcs meet, hairline joints. The old arch was tilted boxes
  with a jagged inner edge, three short stones a jamb, and a base step the paintings do not have.
  Opening 0.80 wide by 1.47 tall (the paintings' opening is a little over half as wide as tall),
  frame 0.27 thick, no step: the jambs stand on the grass.
* **Cream, not grey.** The arch samples at (250–255, 234–243, 205–221) in the paintings against a
  wall at (172,161,153); `M_ArchStone` is cream (`#F2E8D8`), clearly lighter than the island.
* **Carved diamonds, not lamps.** Two down each jamb, one on each arch side, a larger one on the
  keystone: flat, a shade darker than the stone (`M_ArchCarve`), and only the keystone's glows a
  little. The old studs were bright emissive blobs.
* **A golden mandala inside.** Cream at the threshold (mockup 253,231,184) to amber at the top
  (246,184,95), five thin concentric rings and twelve slow spokes round a bright core at 45%
  height, a pale rim where the light meets the frame. Alpha-blended and held under the bloom
  threshold; only the core blooms. Capture: (253,200,98) at the top, (253,244,193) at the
  threshold.
* **Light on the grass.** A faint additive halo round the arch (`M_PortalGlow`) and a separate
  pool on the grass at the threshold (`M_PortalFloor`), because the grass under the painted door
  goes yellow (252,234,155). Both are `SrcAlpha One`, so what they add is tint × alpha × blob -
  the first retint kept full RGB and only lowered alpha, tripled the add, and clipped the doorway
  to white. Keep that product small wherever a spill overlaps the doorway or the frame.

**Depth pass (2026-09-12).** The user's read after the rebuild: right shape and colours, but "a
flat 2D yellow shape" where the painting is "a deep gateway emitting light". What closed that gap,
without touching the design or proportions:

* **The interior is layered, back to front.** Field (amber deepening into the upper corners, so
  the opening reads as a hollow) → two octaves of slowly drifting haze (body, not paint) → the
  mandala, soft-edged and thinned by the haze → a *golden* wide glow from the core that dissolves
  the rings where the light is strongest, which is what puts them inside the light rather than on
  top of it → the small white core with a four-point glint. Only the core crosses the bloom
  threshold. A first attempt with a white glow of the same width read as a flat disc and swallowed
  the lower half; the glow has to stay golden and only the core may go white.
* **The stone has edges again.** The voussoirs are swept with a chamfered profile (`ArcStone`
  takes a chamfer), the masonry material has a soft specular sheen, and the faces that look into
  the doorway - the arch's inner wall and its chamfers, plus plates on the jambs' inner faces - are
  a fourth submesh with `M_ArchInner`, cream stone with the portal's warmth as emission. That is
  the "glow along the inner edge of the arch".
* **Real light spill.** The point light moved inside the doorway (intensity 1.4, range 5), so it
  falls on the inner faces, the threshold and the blocks either side: the wall block next to the
  door samples (189,177,166) against (181,169,158) further along, and the grass a block away lifts
  from (138,172,93) to (160,186,95). The pool quad was reduced to keep the sill from clipping.
* **Sparkles.** Two emitters of warm four-point glints (`M_PortalSpark`, the orb's star texture in
  cream-gold): small ones twinkling inside the doorway, a few larger ones drifting in front of the
  frame.

### The orb

Polished against the paintings on 2026-09-12, same brief as the doorway: only the differences,
no redesign. What the reference orb is, and what changed:

* **An opalescent pearl, not clear glass.** The painted body is milky and mostly opaque, and its
  hue drifts across the surface: pale cyan upper-left (207,239,251), pink to the right
  (241,223,242), mint below (214,249,235), white at the core. `PTW/Orb` tints a milky body by the
  surface normal (`_TintCyan/_TintPink/_TintMint`), keeps a quiet iridescent band, and has only a
  thin pastel edge - the old hard cyan rim at strength 2.2 read as a bubble outline with a flare
  in it. Body alpha 0.86, so the pink sky no longer bleeds through the edge.
* **Two glass highlights.** A broad soft blob upper-right (`_SpecDir` (0.55,0.65,-0.5), power 16)
  and a crescent along the lower-left edge (`_CrescentStrength`), where the painting has them.
* **A twinkle, not a flare.** The star texture is the astroid |x|^(2/3)+|y|^(2/3) ≤ 1 - a four-point
  star whose sides curve inward - over a soft round glow; the core quad is half the ball wide. The
  same texture serves the sparkles and the portal's glints. The centre glow is kept low (0.12) so
  the star stays crisp against a tinted body rather than vanishing into white.
* **A thin pale orbit line with a bead.** The ring mesh is 0.02 wide (was 0.04), its material a
  translucent mint-white at alpha 0.22 (was cyan at 0.55), and a small billboard glint rides on it
  as a child of the ring, the way the painting's ring carries one bright knot.
* **A mint tint on the grass, not a green flare.** Halo and point light are mint-white and softer
  (halo alpha 0.22 base, light 0.45 base); at speed they still swell.

### Sky motion

`Feel/SkyLayer.cs` + `Feel/SkyParallax.cs`. Every sky element is a `SkyLayer` child of the
camera, placed by viewport fraction. Three kinds of motion, all translations, all scaled by depth:

* **Drift** — clouds glide sideways on their own and wrap at the frustum edge; far puffs at
  ~0.1 u/s, the ones in front of the island at ~0.45 u/s (they cross the frame in about 40 s).
  Ridges cannot wrap, so they never drift.
* **Parallax** — `SkyParallax` (on the camera) publishes one small shared offset: as the level
  tips, the sky slides the way the orb is about to roll (1.1 u at a quarter turn for the nearest
  layer) and sinks a little; as the orb travels from the pivot the sky leans the other way
  (6% of its offset, capped). Each layer takes a share: far ridge 0.1, mid 0.18, near 0.28,
  clouds 0.12–0.35 by distance, islets 0.5, the foreground clouds 1.0. Smoothed so the sky lags
  the island by a beat, like weight.
* **Bob** — the islets float up and down a fifth of a block at 0.06 Hz, out of phase with each
  other.

Three things went wrong on the way and are worth knowing about:

* **Captures were linear, not sRGB.** The project renders in Linear colour space and the capture
  path encoded raw HDR floats. Every screenshot was ~40% too dark and two rounds of art direction
  chased that. `Grab()` now converts with `Color.gamma`; verified against an sRGB render target.
* **The post-processing profile never saved its components.** `VolumeProfile.Add()` makes loose
  objects; without `AssetDatabase.AddObjectToAsset` they are `{fileID: 0}` in the next session, so
  the grade was silently the URP template's `SampleSceneProfile` (Neutral tonemapping, black
  vignette). `PtwScene.EnsureProfile` adds them as sub-assets and `ConfigureUrp` makes the profile
  the pipeline default too.
* **A zero mesh normal became a screen-sized white disc.** The grass tufts taper to a 0.003-unit
  tip; `Vector3.Normalize` zeroes anything under 1e-5, the foliage shader's `normalize` turned
  that into NaN, and bloom's default clamp (65472) inflated one NaN pixel into a block-sized glow.
  `MeshBuilder.SafeUnit`, `SafeNormalize` in the shaders and a bloom clamp of 12 close all three.

## Water

A pool is a **half-height solid bed** in a floor cell with a `WaterVolume` filling the other half,
so the surface sits level with the grass and the ball rolls straight in. Three behaviours, all
cheap and all on explicit overlap queries like the rest of the gameplay layer:

* **Floats.** Anything dynamic inside gets an upward force scaled by submersion and by its own
  buoyancy (`PlayerBody` 1.6 → floats; `DynamicProp` 0.55 → sinks), plus drag. The ball bobs
  across at surface level; a rock settles onto the bed — which is what lets a rock sit on a plate
  under a pool.
* **Pours.** Past ~22° of world tilt the pool drains from whichever lip is downhill: droplets, a
  sound, the surface visibly sloping (the v1 `PTW/Water` shader's `_Tilt`). Anything burning just
  past that lip is put out. Water is **finite** — tipping the wrong way first wastes it, which is
  the whole puzzle on level 20.
* **Douses** fire it actually touches, immediately.

There is no fluid sim and there should not be one on a phone. Buoyancy is reckoned against
**world** down, because the pool tilts with the level and gravity does not.

---

## Music

`Feel/PtwMusic.cs`. One short, tuneful **music-box** loop per chapter, **synthesised at runtime**
for the same reason every sound effect is: the project ships with real audio and zero licensed
assets. Each loop is eight bars of I–V–vi–IV with an authored melody (music box: a struck-tooth
tone with two fast-dying inharmonic partials), a harp arpeggio in eighths under it, a plucked bass
on one and three, and a very quiet pad. Dawn is C at 92 BPM, Morning D at 96, Golden Hour F at 84;
the three melodies are one family, rising through the first half and walking down to the tonic so
the loop closes on itself. Notes that ring past the loop end wrap round into its start, so the seam
is seamless rather than faded. **Nothing is random and nothing is noise-based.** The earlier
soundtrack — a slow drone pad with RNG sparkles, a wind-noise bed and a glass tick every 18° of
rotation — was rejected in three rounds ("I don't like it", "sounds like water", "a weird sound
when I tilt"), and each of those complaints traced to one of those three things. The only other
loop left is the **portal hum**, which swells as the orb nears the door. Sound effects are
bell-based (`PtwAudio.Bell`) — gentle taps, sparkles and a chime on entering the portal.

All three loops are built a few thousand samples per frame in the background, chapter one first,
so nothing hitches: the menu is silent for about a second and the music fades in. Two
`AudioSource`s crossfade on chapter change; the **Music** setting fades it out and back in; the
pause screen ducks it. `overrideLoops` has one slot per chapter for an authored track — drop a
clip in and it replaces the synthesised one. **Pull The World → Render Music To WAV** (or
`PtwBuild.BatchRenderMusic` headless) writes the loops to `Captures/music_*.wav` so they can be
auditioned in any player without launching the game.

**Why it sounded out of tune until 2026-09-11.** The pad's vibrato was written as
`sin(2π · f · (1 + d·sin(ωt)) · t)`. That is not a vibrato: the instantaneous pitch of that
expression drifts by `f·d·ω·t`, which grows without bound — over a semitone one second into
the loop and a siren by the end of the 18-second chord cycle. Both the earlier night-theme
music and the pastel rewrite had it, so both were "off". The vibrato is now applied to the
phase as the integral of the wobbling frequency,
`2π · f · (t − (d/ω)(cos(ωt + c) − cos c))`, which is a true ±5-cent wobble. Do not multiply
a time-varying frequency by `t`; integrate it.

---

## UI and onboarding

One canvas, `ScreenSpaceCamera` (**not** Overlay — Overlay bypasses the camera and would be missing
from every capture, and the captures are how this project gets art-directed). Every screen is a
full-bleed `UiPanel`, which is the only thing in the project that knows how to fade and pop, so all
the transitions match. `UiRoot` owns the whole flow: menu → level → complete → next.

Settings (sound, music, haptics) persist in `PlayerPrefs` via `GameProgress`, which is also the
single gate the audio and haptics layers check — rather than 30 call sites each testing a flag.

Opening settings during play **really pauses** (`Time.timeScale = 0`). The first build only
disabled rotation input, so the ball kept rolling — and dying — behind the overlay. Panels animate
on unscaled time so they still work while paused.

**Restart all levels** lives in settings and wipes saved progress, so it is a **two-tap confirm**:
the first tap arms it and relabels the button *TAP AGAIN TO CONFIRM*, the second does it, and it
disarms itself after a few seconds. Mid-game it also drops you straight onto level 1; from the menu
it just resets, so the next PLAY starts over.

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

### Android

```bash
Unity.exe -batchmode -quit -buildTarget Android -projectPath . \
  -executeMethod PullTheWorld.EditorTools.PtwBuild.BatchAndroid [-ptwOut path\to\file.apk]
```

Defaults to `%USERPROFILE%\Desktop\AmuriiBuild\AmuriiBuild.apk`. Also in the menu as
**Pull The World → Build Android APK**. IL2CPP, **ARM64 only** (Mono is 32-bit and a store upload
needs 64-bit anyway; a single architecture halves the IL2CPP time), debug keystore — so it installs
on a phone but cannot go to a store as-is. The Editor's bundled SDK/NDK/OpenJDK are used; nothing
else needs installing. Expect the first Android build to take several minutes: switching target
re-imports every asset for the platform, then IL2CPP compiles the whole game to C++.

## UI package

The UI uses **Unity UI Extensions** (`com.unity.uiextensions` 2.3.2, MIT) from the OpenUPM
registry — it's in `Packages/manifest.json` with a scoped registry, so it installs on project open
with no Asset Store login. It is a set of effects on top of uGUI rather than a replacement for it:
`PtwScene.Gloss()` puts a vertical `Gradient` (multiply mode, so hue and press-tint survive) plus
uGUI's own `Outline` and `Shadow` on every button, the settings card and the toggle tracks. That is
the difference between a flat pill and a button.

Things worth knowing if you touch it:
* The package's `NicerOutline` is an **empty stub** in this Unity version — its real body is behind
  an `#else` for older Unity, so it compiles with no members. Use the built-in `Outline`.
* `UnityEngine.UI.Extensions.Gradient` collides with `UnityEngine.Gradient` (used by the particle
  code), so it is fully qualified rather than imported.
* Mesh effects do nothing on TextMeshPro — TMP doesn't go through `VertexHelper`. Text legibility is
  still the TMP shadow material's job.
* The manifest must be **UTF-8 without BOM**. PowerShell's `Set-Content -Encoding UTF8` writes a
  BOM and Unity's Package Manager then rejects the whole manifest as invalid JSON ("Non-whitespace
  before {"), silently skipping the package.

## Juice

The second juice pass (all visual/audio, none of it touches the ball's body):

* **Fireflies** drift through the sky, tinted to the chapter's glow by `SkyTheme`.
* **The ball blinks** and its eyes go wide while airborne (`BallFace`), and it leaves a soft streak
  above 4.5 m/s (`BallTrail`, on a non-rolling child, cleared on any teleport so a restart never
  draws a line from the death spot to spawn).
* **Danger vignette** (`DangerVignette`): while an enemy is hunting you the screen edges darken
  and redden with a slow heartbeat, driven by `Enemy.CurrentThreat`. Works on the Volume's runtime
  profile copy so it never dirties the asset.
* **Camera kick** (`PlaneCameraRig.Kick`): a 7 % zoom-out that springs back on level load — the
  island arrives — and a 4 % lean-in on a win.
* **Chapter cards**: "CHAPTER II · EMBER" fades in over the first level of each chapter, and again
  when you come back from the menu.
* **Gem flight**: a picked-up gem flies in an arc from where it was to the HUD counter, which
  punches when it lands.
* **Menu sway**: the preview island breathes ±3.5° behind the title. Off the instant a level binds.
* **Hit-stop**: 90 ms of 12 % slow motion on the frame of an on-screen death. Not on falls, which
  restart in a quarter of a second.
* The enemy is 12 % bigger so it carries on a phone.
* **FLAWLESS!** replaces "LEVEL COMPLETE" when a level is cleared without a death, with a brighter
  chime; consecutive flawless levels show "N FLAWLESS IN A ROW" — the cheapest replay hook a level
  game has.

None of this uses a tween library. The project already had an unconditionally stable spring
integrator (`Spring.cs`), and every bit of motion below is a spring:

* `Punch` — scale kick with overshoot. On rocks (impact squash + dust + thud), the pressure plate
  (flinches when pressed), the gate (flinches when it starts to move), the HUD level label (every
  load) and the gem counter (every pickup).
* `UiButtonJuice` — every button shrinks while the finger is down and pops on release. On a phone
  there is no hover, so this is the only confirmation a tap landed.
* `UiPulse` — PLAY breathes. It sits on a wrapper so it does not fight the press-juice on the
  button itself; two components driving one `localScale` tear.
* The level arrives with a 4° swing and settles (`WorldRotator.introKick`), which demonstrates the
  one verb the game has before the player touches anything.

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

31 PlayMode tests. They assert the promises v2 makes, which are nearly the opposite of v1's:

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
* a moving platform does **not** outlive its level (it detaches from the hierarchy on purpose,
  which is exactly how it leaked one per level load in the first build)
* opening settings during play stops the clock, and closing them starts it again
* restart-all needs two taps, and the second really does wipe progress and go to level 1
* water floats the player (it comes to rest near the surface, alive, not on the bed)
* pouring a pool onto a fire puts it out, and drains the pool doing it
* the player cannot press a plate; a rock can
* an enemy kills on contact; the rock in level 21 bowls it over when you tilt toward the door
* the rock in level 22 breaks the crate; the ball alone, same tilt, does not

The suite snapshots your saved progress before the first test and restores it after the last.
`PhysicsNeverExplodes` legitimately wins levels while sweeping them, and before that guard a test
run left the developer's game at "LEVEL 18 OF 18".

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

## Level select

**LEVELS** on the main menu opens a grid of numbered tiles, cloned at runtime from one template
(`UiRoot.OpenLevelSelect`). Unlocked tiles are bright and tappable; locked ones are dimmed. Five
columns of 150 px tiles inside a `ScrollRect`, so the count can keep growing.

This also answers "why does the phone start on the same level after a new build": progress lives
in `PlayerPrefs` on the device and survives reinstalling the same package (Android auto-backup
restores it). That is what players want; for testing, jump anywhere from the picker, or
**Settings → Restart All Levels** to wipe it.

**Settings → UNLOCK ALL LEVELS (DEV)** opens every tile so a build can be tested from any point.
It is a development convenience: set `UiRoot.showDevUnlock` off (and regenerate the scene) for a
release build.

---

## Ads

`Scripts/Ads/`. Three files, and gameplay code only ever talks to one of them.

* **`IAdProvider`** — what the game needs from a network and nothing more: initialize, is a
  rewarded/interstitial ready, show one with a callback. One adapter per SDK (AdMob, LevelPlay,
  AppLovin…), none written yet.
* **`FakeAdProvider`** — a full-screen "TEST AD" card with a countdown, added automatically when
  no real adapter is present. It is why every placement can be tried on a phone today and tested
  headlessly.
* **`AdsManager`** — owns the provider, the cadence rules and the pause/mute around a showing.

The rules are the design, not a detail — they are what makes ads *improve* the game rather than
tax it:

* **Interstitials only ever follow a win**, on the way to the next level, never a death. A death is
  already the low point and the restart has to be instant (that was a real complaint).
* **None before level 6** (`firstInterstitialLevel = 5`, zero-based). The first minutes are for
  learning the verb; retention is decided there.
* **At most one per 3 wins and one per 150 s**, whichever is later.
* **Rewarded ads are offered, never forced, and only when they help.** *STUCK? SKIP LEVEL* appears
  in the HUD after 3 deaths on the same level — exactly the moment a stuck player would otherwise
  quit — and watching it advances to the next level. `AdPlacement.Hint` is reserved for a rewarded
  "show me the first tilt" once per-level hints are authored.
* **`AdsManager.AdsRemoved`** (a future "remove ads" purchase, stored in PlayerPrefs) turns off
  interstitials only; rewarded offers stay, because they are a favour to the player.

Wiring a real network: add a component implementing `IAdProvider` to `~Systems` next to
`AdsManager` (it picks up whatever `IAdProvider` is on the object, and only falls back to the fake
one when none is), or call `AdsManager.SetProvider(...)`. `OnAdClosed` is the analytics hook.

---

## Known gaps

* **Level design needs a play pass.** See the section above — the layouts are deliberately safe and
  therefore thinner than they should be.
* The **moving platform** is the least-tested mechanic. It is built quite differently from the gate
  on purpose: a gate only has to block, so it is a plain child collider of the compound body, but a
  platform has to *carry* the player, and a child collider of a compound kinematic body has no
  velocity as far as the solver is concerned — the player gets pushed out by penetration resolution
  rather than carried. So the platform owns its own kinematic Rigidbody, detaches from the level
  hierarchy at startup, and recomputes its pose from the level's rotation every FixedUpdate.
* **Fixed: a ticking sound while tilting.** The rotation ratchet played a glass tick every 18° of
  turn - about five a second while dragging - on top of a haptic tap. Under the music it read as
  a fault rather than as feel, and was reported as "a weird sound when I tilt the level" after the
  rolling thuds below were already gone. The ratchet is haptic only now (`ImpactFeedback.HandleTick`);
  `PtwSfx.SpinTick` stays in the bank unused. The only sounds that can still fire on a plain tilt
  are a genuine hop of the ball or a rock (quiet, squared volume) and, on water levels, the pour
  puff while a pool spills over its lip.
* **Fixed: a knocking sound under the music while rolling, and a white orb at speed.** Both had
  one cause. The level is a compound body with one box collider per block, so a ball rolling
  along a flat row raises `OnCollisionEnter` at every seam, and both `PlayerBody` and
  `DynamicProp` measured an impact by `relativeVelocity.magnitude`, which includes the rolling
  speed. Each seam was a 3-6 m/s "landing" about five times a second: a full thud, a world shake,
  a haptic tap and an impact flash on the orb (which, stacked on the speed glow, took the core,
  halo and rim over the bloom threshold together). `Impacts.ClosingSpeed` now measures the
  relative velocity along the contact normal - 0.7-1.8 m/s at a seam, full value on a real
  landing. The impact sound volume is squared so small bumps whisper, and the orb's speed glow,
  impact boost, halo and trail are all toned down. `RollingDoesNotThud` guards the regression.
  The wind ambience was also Perlin noise sampled 7000 lattice cells a second, which is a faint
  7 kHz whistle rather than a hiss, over a 55 Hz floor that pulsed every two seconds; it is
  low-passed white noise over a steady floor now.
* **Fixed: level 21 ("Bowl It Over") could jam.** The enemy sits inside its alert range of the
  spawn, hunts the orb the moment the level loads, and ends up next to the rock. Rock and enemy
  then roll downhill *in contact*: one gentle `OnCollisionEnter` under crush speed, and never
  another, so when the wall stopped the enemy and the rock slammed into it at 11 m/s nothing
  noticed. `DynamicProp.OnCollisionStay` now also feeds `Enemy.Crush`, using the contact impulse
  divided by the rock's mass (the speed the contact took off the rock that step: ~9 m/s for the
  slam, under 1 m/s for a rock resting on or rolling beside an enemy). `RockCrushesTheEnemy`
  reproduces the jam deliberately and passes through the squeeze.
* **Fixed: the portal's interior was a white oval.** The energy fill was additive, and additive
  light over a bright pastel sky can only go whiter, whatever colour it is given. It is
  alpha-blended now, amber, and held under the bloom threshold; the additive halo quad around the
  arch carries the glow. The ring pattern is faint at gameplay scale and could still be pushed.
* **Art is judged against `Assets/PullTheWorld/Art/Mockup/`, not `PicReference/`.** The
  reference sheets in `PicReference/` describe the earlier night direction and are kept for
  history only; see the Theme section for the current targets and how captures are compared.
* **Fixed: the pale line along tilted grass.** It was the grass cap's *front-top chamfer bevel*:
  a 45° strip that faces both up and toward the camera, which makes it the single most-lit surface
  in the scene once the island tilts (dot ≈ 0.95 against 0.88 for the top face), pushed to
  blue-white by a cool ambient. Two changes fixed it: the ambient came down from `#9FB2CE` to
  `#7D90AB`, and the cap's chamfer went from 0.042 to 0.012 so the bevel is a hairline. The body
  block keeps its full chamfer, so the silhouette stays soft.

  Method note, because this cost more time than it should have: do **not** read pixel coordinates
  off a capture in an image viewer. The viewer rescales, and two hand-picked probe points both
  landed on plain grass 130px from the target. What worked was printing a coarse luminance map of
  the real 1080×1920 frame from inside a test, then disabling renderers one at a time and watching
  a single pixel to see whose removal changed it.
* `PicReference/` is now **tracked** (the v1 README said to exclude it). `PALETTE.md` and
  `VISUAL_LANGUAGE.md` in there are the working art direction, and the panel renders are what the
  capture-compare loop is judged against. Note that `VISUAL_LANGUAGE.md` still describes v1's
  fiction — the anchor disc and the always-upright player — and those parts no longer apply.
* `Packages/mcp-unity/` is the Unity MCP bridge — delete it and `.mcp.json` if you do not want it.
  `.mcp.json` contains an absolute path with a Windows username in it.
