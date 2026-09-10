# PicReference

Art reference and image-generation prompts for **Pull The World**.

This folder lives at the **project root, outside `Assets/`**, on purpose: Unity never
imports it, it never lands in a build, and it generates no `.meta` files. Nothing in
here is a game asset.

## What is in here

| Path | What it is |
|---|---|
| `_source/PullTheWorld_ConceptSheet.png` | The original concept sheet, verbatim. The single source of truth for style. |
| `_source/panels/` | That sheet sliced into its individual panels at 2x, so each one can be used as a standalone style reference. |
| `VISUAL_LANGUAGE.md` | The style bible: camera, grid, asset vocabulary, lighting, composition rules. |
| `PALETTE.md` | Colour values **measured out of the concept sheet pixels**, not eyeballed. |
| `LEVELS.md` | The level roster and what each level has to show. |
| `SHEET_LAYOUT.md` | Measured geometry of the concept sheet itself, for rebuilding it with real levels. |
| `prompts/*.txt` | One standalone image prompt per picture. Paste a whole file into an image model. |
| `prompts/future--not-built/` | Prompts for mechanics that are **NOT in the prototype**. Clearly fenced off. |

## How to use the prompts

Each `prompts/*.txt` is **self-contained** — scene, style, camera, palette and negatives
are all inlined, so you paste one whole file and get one image. They deliberately repeat
the shared style block rather than referencing it, because image models do not follow
links.

If you change the style, change `VISUAL_LANGUAGE.md` first, then regenerate the prompt
files so they stay consistent.

## Ground rules

1. **The concept sheet wins on style. `LEVELS.md` wins on content.** The sheet shows 30
   levels with water, magnets and a rescue NPC. The prototype has 5 levels and none of
   those mechanics. Style from the sheet, content from the roster.
2. **Anything under `future--not-built/` is not shippable art.** Do not let it leak into
   store screenshots or the level list.
3. Generated images belong in `output/` (create it as needed). Keep the prompt that made
   each image next to it.

## Status

Prompts are written and ready. **No images have been generated yet** — this session has
no image model wired up. The OpenArt MCP connector is installed but unauthenticated; run
`/mcp` and authenticate "claude.ai OpenArt" to enable generation, then these prompts can
be run straight through.
