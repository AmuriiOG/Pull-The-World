# Palette

Sampled out of `_source/PullTheWorld_ConceptSheet.png` by pixel classification: group
pixels by material role, then split each group into shadow / base / lit by luminance
percentile. Not eyeballed.

## Read this before quoting a number

**1. These are FINAL-FRAME values, not albedo.** The sheet is already bloomed,
vignetted and tonemapped. A render should *land* on these numbers; raw URP material
albedo has to sit brighter and slightly more saturated to end up here. The Unity side
keeps its own albedo table for exactly this reason, and the two are not comparable.
Whenever a colour gets quoted, say which stage it refers to.

**2. Measure per-context, never across the whole sheet.** The six level cards do not
share a ground. L1 and L10 are bright; L20 is fire-lit; L30 is a night card on
`#0D151C`. Averaging all six drags every material dark — an earlier pass of mine did
that and put stone lit at `#8B8E97` when the true bright-card value is `#A6ABB6`. That
is a 27-point error on the most common material in the game. The table below is
**LEVEL 1 only**, corroborated against L10 where the material appears there.

**3. The real arbiter is the capture-compare loop**, not this file: render the Game View
at 1080x1920 and diff it against the sheet. This table is the target, not the tuning
mechanism.

## Materials — bright levels (L1-sourced)

| Role | Shadow | Base | Lit |
|---|---|---|---|
| Grass cap | `#2A562E` | `#598146` | `#7B9C55` |
| Stone | `#494D52` | `#7C8087` | `#A6ABB6` |
| Wood / crate | `#6F4D31` | `#926540` | `#B2885C` |
| Player ivory | `#CDD0DF` | `#DBDEE9` | `#EDEEF3` |

Wood is sampled off L5 (neutral-lit crates). The L20 crate reads warmer
(`#764627 / #A5572B / #B9995A`) because it sits in lava light — that is a lighting
variant, not a different material.

## Emissive / accent

| Role | Dim | Mid | Hot |
|---|---|---|---|
| Anchor cyan — **hot rim core** | — | — | `#86DFFC` |
| Anchor cyan — soft spill | `#5A76A4` | `#7290BB` | `#8BB1DB` |
| Door glow amber | `#CEB469` | `#E4C26C` | `#F8CE61` |
| Fire orange | `#EE6E1E` | `#F48429` | `#FB9D1C` |

The anchor ring has two distinct populations and they must not be conflated: a narrow
hot emissive rim at `#86DFFC`, and a wide soft glow around it that classifies much
duller. **The rim is the brightest cyan in the frame** and nothing may out-glow it — it
marks the fixed point the whole game is built on.

## Backgrounds

| Context | Value |
|---|---|
| Level card ground — **measured** | `#97A3B3`, flat |
| Level card ground — Unity committed | `#9DA9B9` to `#8E9BAB`, a deliberate ~6% vertical gradient |
| Contact shadow under a block | `#939EAD` — only ~4% darker than the ground |
| Dark / void ground | `#0C131B` to `#16202A` |
| Concept sheet outer surround | `#1C2530` |
| Caption bar | `#15222C` |
| Card border | `#202A32` |

The measured ground is **flat**; the slight gradient in the Unity value is a deliberate
art choice so the frame is not dead flat, not a measurement. Worth knowing that the
apparent darkening toward the bottom of a card is mostly **contact shadow**, which is
why the shadow value and the old "gradient bottom" reading land within a point of each
other.

Both sessions independently arrived at `#97A3B3` here. The earlier near-white reading
(`#EDEFF2`) was a simultaneous-contrast error: the cards *look* near-white against the
`#1C2530` surround. On a near-white ground the pale stone goes muddy and the ivory
player stops reading as the brightest thing in frame, which breaks the composition rule
the whole art direction rests on.

## Saturation

Deliberately muted and naturalistic — noticeably less saturated than typical
hyper-casual, and that restraint is much of why it reads premium. Grass is soft
olive-leaning green, not vivid lime. Stone is warm-neutral grey, not blue-white. Only
the three emissives may be genuinely saturated, and the ivory player stays the brightest
non-emissive element.

## Unbuilt mechanics (reference only)

Lower confidence — these panels sit next to each other on the sheet, so the classifier
picked up bleed between the lava, water and magnet hues. Trust the hue, sanity-check the
value. Not shippable; see `prompts/future--not-built/`.

| Role | Dim | Mid | Hot |
|---|---|---|---|
| Water | `#115B99` | `#1B78A8` | `#6DA3CB` |
| Lava | `#D54D1B` | `#E15519` | `#EE6B1F` |
| Rescue NPC yellow | `#E6BA55` | `#F8CE4C` | `#FDE260` |
| Magnet N red | — | `#C04434` | — |
| Magnet S blue | — | `#0A5EBE` | — |
