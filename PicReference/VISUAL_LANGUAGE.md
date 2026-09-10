# Visual language

Style bible for Pull The World. Derived from `_source/PullTheWorld_ConceptSheet.png` and
reconciled with the committed Unity art direction.

## The one idea the art has to carry

**You stay. The world moves.**

Every image has to make that legible without a caption. The two devices that do it:

1. The player is **always perfectly upright**, and **always standing on a glowing cyan
   anchor disc**, never directly on the terrain. The disc is the fiction: it is the fixed
   point of the universe.
2. When the world rotates, **everything else tilts and the player does not**. Gravity
   still reads straight down the *screen*, never down the tilted island.

If a picture would read the same with the character walking around a static level, the
picture has failed.

## Camera

| Property | Value |
|---|---|
| Projection | **Orthographic** — no perspective convergence at all |
| Rotation | euler `(36, 45, 0)` — classic 45 degree isometric |
| Ortho size | ~7 |
| Movement | **None, ever.** The camera is fixed for the entire game |
| Composition | Portrait, 1080x1920 in game; generate reference at 1024x1536 (2:3) |

Consequences to hold onto when judging a generated image: every 1x1 cube is the same
on-screen size regardless of depth, and **every vertical edge is exactly vertical and
parallel**. Any perspective convergence is a reject.

## Grid and silhouette

- **1.0 unit cubes.** Ground block tops sit at `y = 0`; the feet of the player are at
  `y = 0`. Walls occupy `y 0..1`. This is load-bearing for the player collision capsule,
  so the art must keep reading as unit cubes.
- ~**0.06 chamfer** on all cube edges. Soft rounded-cube look, not razor sharp — and not
  Minecraft-blocky, not voxel.
- A grass block is a stone base with a **grass cap ~0.18 tall** on top, overhanging the
  base by a hair.

## Materials

URP/Lit, flat colour, **metallic 0, smoothness ~0.15**, roughly 20 shared materials.
Flat-shaded low-poly geometry, procedurally generated. **No textures, no normal maps.**
Colour comes from albedo only.

The single exception in the reference set: the magnet cubes are glossy. They are also
`future--not-built`, so in practice nothing shipping is glossy.

## Lighting and post

- Soft single key from upper-left-front. Diffuse, gentle, **no hard-edged shadows**.
- Soft ambient occlusion in block crevices. Contact shadows under the diorama are **very
  subtle** — soft, wide, only ~4% darker than the ground (`#97A3B3` to `#939EAD`). Never
  dark pools.
- Bloom on **emissives only**: anchor ring, door glow, fire.
- Post stack: Bloom + Vignette + Neutral tonemapping + mild colour adjustments.

## Colour restraint

The ground is a **mid cool blue-grey** (`#97A3B3`), not near-white. The palette is
**muted and naturalistic** — noticeably less saturated than a typical hyper-casual
mobile game, and that restraint is a large part of why the reference reads as premium
rather than cheap. State this explicitly to anyone or anything generating art, because
the default drift is always toward candy colours.

- Grass is a soft **olive-leaning** green, not vivid lime.
- Stone is a **warm-neutral** grey, not blue-white.
- Only the three emissives — cyan ring, amber door, orange fire — may be genuinely
  saturated.
- The **ivory player stays the brightest non-emissive thing in frame.** This is the
  composition rule everything else serves: on a near-white ground the player stops
  winning that contest and the image loses its subject.

Numbers in `PALETTE.md`.

## Composition

- The player figure sits at the **optical centre** of frame.
- The level is a **small floating island** on a flat background — no horizon, no skybox,
  no clouds, no full-bleed environment. Generous empty margin all around.
- Dressing props get pushed to the flanks. The solution path stays uncluttered.
- Light discipline: cyan at the player, amber at the door, orange at hazards. Keep those
  three reading as distinct lights and never let anything out-glow the anchor ring.

## Typography

**Poppins Bold / SemiBold** (SIL OFL), already vendored at
`Assets/PullTheWorld/Art/Fonts/`. Captions are uppercase, generously letter-spaced,
white on the dark navy bar. See `SHEET_LAYOUT.md`.

Captions are **composited afterwards, never generated** — image models cannot be trusted
with text. Every prompt in `prompts/` forbids text in the image for exactly this reason.

## Asset vocabulary

Prefab names are the ones the Unity build is actually using — use these exact names when
briefing art so a picture maps onto a real object.

### Blocks
| Prefab | Reads as |
|---|---|
| `Block_Grass` | 1x1x1 stone cube with a green grass cap on top, slight overhang lip |
| `Block_Grass_Half` | Same, half height — used for steps and ledges |
| `Block_Stone` | Plain pale grey chamfered stone cube, no grass. Walls are made of these |
| `Block_Stone_Dark` | Darker grey variant. Gates, plinths, anything that should read as built rather than terrain |
| `Block_Ramp` | Wedge, one cube footprint |

### Static props (dressing — no physics)
| Prefab | Reads as |
|---|---|
| `Prop_Tree` | Faceted angular medium-green foliage on a short brown trunk, ~2 cubes tall |
| `Prop_TreeSmall` | Same language, ~1 cube tall |
| `Prop_Bush` | Small round faceted green blob |
| `Prop_RockDeco` | Small pale grey faceted pebble. Scatter on the background |
| `Prop_Fence` | Angular wooden posts and rails. Dresses an open lip |
| `Prop_Crystal` | Faceted translucent crystal cluster. Accent only |

### Dynamic props (physics — these are puzzle pieces, not dressing)
| Prefab | Reads as |
|---|---|
| `Prop_Boulder` | Big pale grey faceted rock, ~1.4 cubes across. Chunky, obviously heavy |
| `Prop_Crate` | 1x1 faceted cube of warm honey-brown planks with darker frame edges |

A dynamic prop must always be **visually distinguishable from dressing**. If the player
cannot tell at a glance which rock is a puzzle piece and which is scenery, the art is
wrong. `Prop_Boulder` is much larger and paler than `Prop_RockDeco`.

### Gameplay
| Prefab | Reads as |
|---|---|
| `ExitPortal_Door` | Carved pale grey stone archway with a keystone, filled with a warm amber-gold glowing doorway that spills golden light forward. The goal — the warmest thing in frame |
| `Hazard_Fire` | Angular low-poly orange flame tongues on a scorched, blackened stone cube. Emissive, throws warm orange onto neighbours |
| `Hazard_Spikes` | Row of five pale grey metal spikes jutting up from one stone cube top |
| `PressurePlate` | Square stone slab set slightly into the floor, thin cyan inlay ring on its face. Ring **dark** when un-pressed, **bright cyan** when pressed and flush |
| `Gate_Stone` | Heavy darker-grey slab filling a 1-wide, 2-high doorway, vertical carved grooves. Retracts downward into the floor, leaving a cyan-lit threshold |
| `PlayerRig` | Chunky faceted humanoid, **no face and no features at all** — ball head, capsule body, stubby arms, short legs. Off-white ivory, ~1.6 cubes tall |
| `AnchorRing` | Flat dark disc with a bright glowing cyan rim, spilling cyan light onto whatever is below. The player stands on this, **never** on terrain |
