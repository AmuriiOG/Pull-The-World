# Concept sheet layout

Measured off `_source/PullTheWorld_ConceptSheet.png` so the sheet can be rebuilt with
real prototype levels instead of the aspirational ones.

Source canvas: **1536 x 1024**, outer background `#1C2530`.

## Regions

| Region | x | y | w | h |
|---|---|---|---|---|
| Hero panel | 14 | 6 | 432 | 1014 |
| Level card, col 1 | 456 | — | 352 | 410 |
| Level card, col 2 | 812 | — | 352 | 410 |
| Level card, col 3 | 1168 | — | 352 | 410 |
| Card row 1 | — | 4 | — | — |
| Card row 2 | — | 414 | — | — |
| Controls strip | 456 | 838 | 1064 | 182 |

Card pitch is **356 px horizontally, 410 px vertically**. Cards have a `#202A32` border
and rounded corners.

## Caption bar

Inside each 352 x 410 card, in card-local coordinates:

| Property | Value |
|---|---|
| Position | x 10, y 345 |
| Size | 332 x 55 |
| Corner radius | ~10 px |
| Fill | `#15222C` |
| Text | White, **Poppins Bold**, uppercase, generous letter-spacing, centred |
| Format | `LEVEL 3 – TIP IT OVER` (en dash, spaced) |

Text is **composited, never generated**. The prompts forbid text in the image.

## Rebuilding the sheet for the prototype

Six cards, five levels — so the natural 5-level layout is:

```
+----------------+  +-----------+ +-----------+ +-----------+
|                |  |    L1     | |    L2     | |    L3     |
|   HERO / LOGO  |  |  start    | |  start    | | rotated   |
|                |  +-----------+ +-----------+ +-----------+
|   key art      |  +-----------+ +-----------+ +-----------+
|                |  |    L4     | |    L5     | |  L5 gate  |
|                |  |  solved   | |  start    | |   open    |
+----------------+  +-----------+ +-----------+ +-----------+
                    +-------------------------------------+
                    |      SWIPE            ROTATE         |
                    +-------------------------------------+
```

Pick `L3_rotated` over `L3_start` for the card, because the tilted-world image is the
one that explains the game to someone who has never seen it.

## Pinch to zoom is CUT — decided, do not reinstate

The original sheet advertises **PINCH — Zoom the world**. There is no zoom, and there
will not be one. `WorldRig` exposes drag (`focus`) and rotation (`spin`) only; the
orthographic camera and its ortho size are fixed for the whole game.

This is deliberate, not an omission. A locked orthographic framing is what makes a drag
an exact 1:1 *"the spot under my finger stays under my finger"*, and it keeps every
screenshot consistently composed. Zoom would cost both.

So the rebuilt sheet gets a **two-panel** controls strip, not three:

```
+-------------------------------------------+
|     SWIPE                 ROTATE          |
|  Move the world        Rotate the world   |
+-------------------------------------------+
```

Zoom lives with the other future ideas. Do not ship a sheet promising a control the
code does not have.
