# PULL THE WORLD — prototype

> You stay. The world moves.

An isometric mobile puzzle prototype. The player and camera are **completely fixed**; you drag and
twist the entire world around them. Twisting re-aims gravity relative to the level, which is the
core puzzle mechanic.

Unity **6000.2.9f1**, **URP 17.2**, portrait, built for phones.

---

## Run it

Open `Assets/PullTheWorld/Scenes/Game.unity` and press **Play**.

> **Important:** almost everything that makes this feel good is *runtime* — idle breathing, the
> anchor-ring pulse, drag inertia, dust, fire, bloom pulses, impact squash. Opening the scene
> without pressing Play shows a static diorama and none of it. The scene now ships with Level 1
> placed under `WorldRoot` purely so it *looks* like the game when you open it; that preview is
> deleted on Play and the real level is spawned in its place.

### Controls

| | Mobile | Editor |
|---|---|---|
| Move the world | one finger drag | left-mouse drag |
| Rotate the world | two-finger twist | right-mouse drag, or `Q` / `E` |
| Restart | button, top right | `R` |
| Skip level | — | `N` |

Rotation snaps to 15° on release. Levels 1–2 have rotation locked while they teach dragging;
Level 3 is flagged `teachRotation` and shows an animated two-finger prompt until you twist.

**Haptics** fire on grab, rotation ticks, impacts, plate presses, gate moves, win and fail.
`Assets/Plugins/Android/AndroidManifest.xml` exists purely to declare `VIBRATE` — the haptics go
through `android.os.Vibrator` by reflection (a 12ms tap; `Handheld.Vibrate` is a ~500ms buzz and
would be unbearable on every tick), and Unity's automatic permission detection cannot see that.
Delete the manifest and every vibration silently stops.

## Android builds — read before touching the manifest

**Do not hand-write `Assets/Plugins/Android/AndroidManifest.xml`.** That file *replaces* Unity's
generated manifest rather than merging with it. This project uses **GameActivity**
(`androidApplicationEntry: 2`), so the launcher class is `com.unity3d.player.UnityPlayerGameActivity`
— a hand-written manifest naming `UnityPlayerActivity` produces an APK that installs successfully
and then has **no launcher icon at all**. That exact bug shipped once here.

If you need manifest changes, tick Player Settings → Publishing Settings → **Custom Main Manifest**,
which generates the correct template for the current entry point, then edit that.

The `VIBRATE` permission is obtained instead via a never-called `Handheld.Vibrate()` in
`Haptics.cs` — Unity's manifest generator scans for that API and adds the permission itself.

## Editor tips

* Game view defaults to Free Aspect at window resolution, which is far lower than the captures and
  makes everything look aliased. Run **Pull The World → Add 1080×1920 Game View Size** and pick it.
* The **Scene view does not render post-processing**. Colours there are not the game's colours.

## Custom shaders

Three hand-written URP shaders in `Art/Shaders/`, all SRP-batcher friendly and mobile-cheap:

| Shader | Used by | What it does |
|---|---|---|
| `PTW/Backdrop` | camera backdrop | vertical gradient, a pool of light behind the play area, slow drifting cloud noise |
| `PTW/PortalEnergy` | exit doorway | additive swirling arms + inward rings + breathing pulse; goes cold and dim when the door is locked |
| `PTW/Water` | *(unused)* | depth-driven shallow/deep gradient, shoreline foam, refraction, animated normals with real specular, and a `_Tilt` input so a pool drains and pours off its downhill edge. **Water was cut from the game** — this shader, `Prop_Water`, `M_Water` and `M_Ocean` remain in the project but nothing references them. Delete them if you want it gone. |

All three read **object space**, not UVs — the meshes are procedural and their winding is flipped
to face the camera, which rotates UVs. `PTW/Water` also takes a `_TileOffset` per tile so a
multi-tile pool has one continuous wave pattern instead of an obvious repeating grid.

---

## Regenerating everything

All art, prefabs, levels and the scene are **generated from code** — there are no hand-authored
assets and no store assets. Menu bar → **Pull The World**:

1. `Bootstrap (fonts + TMP)`
2. `Rebuild Art (materials + meshes)`
3. `Rebuild Prefabs`
4. `Rebuild Levels`
5. `Rebuild Scene`

…or just **Build Everything**. It is idempotent — change a colour in `PtwArt.cs`, rerun, done.

Headless equivalent:

```bash
Unity.exe -batchmode -nographics -quit -projectPath . \
  -executeMethod PullTheWorld.EditorTools.PtwBuild.BatchAll
```

### Tests + screenshots

```bash
Unity.exe -runTests -batchmode -projectPath . -testPlatform PlayMode \
  -testResults results.xml -screen-width 1080 -screen-height 1920
```

13 PlayMode tests. They solve all five levels by driving the real rig, assert the player and camera
never move, that walls block, that rotation re-aims gravity, that restart works, and that physics
never explodes. They also write real 1080×1920 Game View PNGs to `/Captures`.

Note: **do not** use `-nographics` for the test run — the captures need a renderer.

---

## Layout

```
Assets/PullTheWorld/
  Scripts/
    Core/      WorldRig, WorldInput, IsometricCameraRig, PlayerAnchor,
               LevelManager, LevelDefinition, GameDirector, Spring
    Gameplay/  ExitPortal, Hazard, PressurePlate, MovingGate, Pushable
    Feel/      PtwAudio, Haptics, ParallaxLayer, WorldMotionDust, ScreenFillQuad
    UI/        HudController
    Editor/    Ptw* generators (art, meshes, prefabs, levels, scene, build)
  Art/         generated meshes, materials, textures, physics materials, Poppins (OFL)
  Prefabs/     Blocks/ Props/ Gameplay/ Levels/
  Scenes/      Game.unity
  Tests/       PtwPlayTests
```

### The one file that matters

`Scripts/Core/WorldRig.cs`. World pose is two values:

* `focus` — the point in world-local space currently sitting on the player's feet
* `spin` — degrees about the **camera view axis**

```
rot = AngleAxis(spin, cameraForward)
pos = anchor - rot * focus
```

Rotating about the camera axis means a twist always reads on screen as a clean spin around the
player **and** always changes gravity's direction relative to the level.

**Physics architecture.** `WorldRoot` carries one kinematic Rigidbody, so every static level
collider becomes a single compound PhysX actor — cheap to teleport, no broadphase churn. Dynamic
props are separate actors parented under it, so a world move carries them rigidly. Uniform gravity
is translation-invariant, so a pure drag is a perfect symmetry of the simulation and literally
cannot jitter. A rotation is a rigid transform too — *except* gravity doesn't rotate with it, and
that single asymmetry is the entire gravity mechanic.

Three non-obvious things this depends on, all learned the hard way:

* **Sleeping bodies ignore gravity.** Re-aiming gravity does nothing to a settled prop unless you
  wake it. `WorldRig` wakes props on any rotation.
* **Abutting tile colliders catch sliding props.** Floor colliders overlap by 4 cm to bury the seam.
* **A box on a 36° slope sits at PhysX's default friction angle.** Crates need a slick physics
  material *and* low angular damping, and even then are less reliable than spheres.

---

## Adding a level

Levels are ASCII maps in `Scripts/Editor/PtwLevels.cs`, auto-centred on `P`:

```csharp
var b = new Builder(6, "My Level", "hint text");
b.Map(
    ". . g g g",
    ". . g D g",
    "# # # # #",
    ". g P g ."
);
b.Save();
```

`g` grass · `s` stone · `d` dark stone · `#` floor+wall · `P` player · `D` door · `X` gate ·
`b` boulder · `c` crate · `f` fire · `k` spikes · `p` plate · `T`/`t` tree · `r` rock · `u` bush

Then `Rebuild Levels` + `Rebuild Scene`. Or edit `Prefabs/Levels/Level_0N.prefab` by hand in the
Editor — they're ordinary prefabs.

**Three design rules the levels obey:**

1. **The player must always have ground beneath them.** Gaps are real obstacles — you cannot drag
   the world out from under yourself. This is what makes island shape the actual puzzle. A level
   is only solvable if there's a 4-connected floor path from `P` to `D`.
2. Only **solid terrain** stops the player horizontally. Loose props can't — the player is a
   kinematic pin and PhysX shoves dynamic bodies aside. Terrain is the maze; props are tools
   (plates, smothering). Prefer a raised grass tier (`G`) over stone (`#`) — a run of `#` reads as
   a grey warehouse wall rather than an island.
3. Rotation is about the camera axis, so **downhill in level space always runs along the (1,0,−1)
   diagonal**. Build rolling channels along +X with a wall on the −Z side to catch the drift.

---

## Levels

| # | Name | Teaches |
|---|---|---|
| 1 | Pull the World | drag; the door comes to you |
| 2 | Around the Wall | you are solid — steer the level around yourself |
| 3 | Tip It Over | twist re-aims gravity; boulder → plate unlocks the door |
| 4 | Smother the Fire | props are tools; drop a boulder on the hazard |
| 5 | The Long Way Round | all of it, plus spikes to avoid |

---

## Known gaps

* Art is close to the reference sheet but **not identical** — the palette is tuned by eye against
  captures rather than converged, and the grass reads flatter than the reference's.
* `Prop_Crate` is **not reliable on slopes** (a cube's friction angle is right at the tilt a 45°
  spin produces, and it also catches on tile seams). Level 5 uses a boulder. Use boulders for
  anything that must roll; the crate prefab is fine as static set dressing or on a plate.
* `ParallaxLayer` factors are **0 on purpose**. A backdrop that slides at a fraction of the
  foreground is the visual signature of a moving *camera* — the opposite of this game's premise.
  Don't "fix" it by turning parallax on.
* The **static ocean** (`Ocean` in the scene, not parented to `WorldRoot`) is load-bearing, not
  decoration. A fixed camera over a moving world and a moving camera over a fixed world are the
  same image; the only thing that separates them is a static object with *visible features*. The
  sea's swell and the island shadows falling on it are that reference. Delete it and the world
  stops reading as the thing in motion.
* The player has **no anchor ring**. It read as a UI decal stuck to the character, which reinforces
  a character-centric camera. Its job is done by the world-attached `GrabMarker` instead.
* The **Scene view does not render post-processing** by default, so colours there are not what the
  game looks like. Judge from the Game view at 1080x1920, or enable post-processing in the Scene
  view toolbar.
* No water/magnet mechanics — deliberately out of scope for this prototype.
* Stock `URP/Lit` throughout; no custom shaders.
* `PicReference/` at the repo root is concept-art reference from a parallel session, not used by
  the build. Exclude it if you `git init`.
* `Packages/mcp-unity/` is the Unity MCP bridge — delete it and `.mcp.json` if you don't want it.
