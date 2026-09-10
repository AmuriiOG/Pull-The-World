# Pull The World — Monetization Design

**Status:** Design only. Nothing in this document is implemented, and nothing should be implemented
until the prototype has been shown to the team and the gates in [§7](#7-decision-gates) are met.

**Date:** 2026-09-09

**Scope note:** The absence of ad code in this repo is deliberate, not an oversight. Unity Ads is
disabled in `ProjectSettings/UnityConnectSettings.asset` (`UnityAdsSettings.m_Enabled: 0`), there is
no ad SDK in `Packages/manifest.json`, and there is no currency, shop, perk or revive system in
`Assets/PullTheWorld/Scripts`. That is the correct state for a presentation prototype.

---

## 1. What the game is, mechanically

This matters more than any ad-network comparison, because the mechanic decides which placements are
even possible.

- The **player does not move.** `PlayerAnchor` is fixed at the anchor point; it breathes, leans and
  braces, but it does not travel. `WorldRig` moves the *world* under it.
- The player **drags the world** (`WorldRig.BeginDrag` / `UpdateDrag` / `EndDrag`) and, from level 3
  onward, **rotates** it (`AddSpin`, snapped to `spinSnapStep = 15°`). Rotation is locked off in
  levels 1–2 via `LevelDefinition.allowRotation` — that is the teaching ramp.
- Rotating the world re-aims gravity for every dynamic body (`WorldRig.LocalGravityDirection`,
  `RefreshBodies`, `DynamicBodyCount`), so the puzzle is "arrange the world so physics does what I
  need".
- **Win** is `ExitPortal` calling `LevelManager.ReportWin()` — the door has been pulled to the player.
- **Fail** is `Hazard` calling `LevelManager.ReportFail()` — something nasty reached the player.
- Drag range is authored per level by `LevelDefinition.useBounds` / `boundsCenter` / `boundsSize`
  (default `14 × 1 × 14`).

Two consequences fall straight out of this and drive everything below.

### 1a. The puzzle is deterministic

No timer, no score, no randomness, no combat. A level has a correct arrangement, and the player
either sees it or doesn't. **The product being sold to the player is the moment of insight.** Any ad
reward that shortcuts that moment sells the player a worse experience, not a boost.

### 1b. Failure currently costs nothing

`LevelManager.FailRoutine()` waits `failRestartDelay` (0.85s) and calls `Restart()`. Win waits
`winCelebrateTime` (1.35s) and calls `Next()`, which loops back to level 0 at the end of the list
rather than dead-ending the demo. There are no lives, no energy, no retry limit, no progress loss.

This is the most important fact for monetization: **there is no scarcity to sell against.** "Extra
life", "revive", "continue", "extra attempt" and "refill energy" — the standard rewarded-ad
inventory — have nothing to attach to here. Manufacturing that scarcity (adding a lives system so
ads can refill it) means making the game worse in order to sell the fix. That is the move this
document recommends against most strongly.

---

## 2. Placement assessment

| Placement | Verdict | Why |
|---|---|---|
| **Remove-ads IAP** | **Recommended** | Clean value exchange, no design damage. Only meaningful if ads exist at all. |
| **Cosmetics** (anchor skins, world palettes, ring/trail VFX) | **Recommended** | `PlayerAnchor` already drives `body` / `ring` / `contactShadow` as separate transforms, so skinning is contained and touches no puzzle logic. Zero effect on solutions. |
| **Skip level**, only after repeated failure | **Acceptable with guardrails** | Genuine relief for a stuck player. Costs the insight on *one* level they had already given up on. See §3. |
| **Hint** | **Acceptable, weakest of the acceptable set** | `LevelDefinition.hint` exists but is a teaching string ("Drag anywhere to pull the world"), not a solution. A real hint means authoring a *second* per-level asset and replaying a ghost pull — new burden per level, and it sells away exactly the insight from §1a. |
| **Undo last pull** | **Reject as a paid item** | Dragging is continuous and instantly reversible — the player can already drag back. Selling it is selling nothing. Fine as a free convenience. |
| **Slow-motion attempt** | **Reject here** | Would help a twitch-timing game. This isn't one: `WorldRig`'s settle thresholds and snapped spin exist precisely to make the puzzle deliberate rather than reflex-based. |
| **Lives / energy + ad refill** | **Reject** | Requires inventing the scarcity described in §1b. |
| **Extra pull range / stronger pull / any ability boost** | **Reject** | Breaks levels. See §4. |
| **2× currency** | **N/A** | There is no currency, and no reason to add one for a 5-level prototype. |

---

## 3. Guardrails for the two acceptable placements

**Skip level**

- Never offer on the first failure. Gate on a fail counter — 3+ fails on the same level index.
- Offer it *in place of* the automatic restart, never as a modal that interrupts a player who is
  still trying. `LevelManager.OnLevelFailed` is the right signal; the fail counter does not exist yet.
- One skip per level, ever. Skipping must never apply to the teaching levels (1–2): a player who
  skips the drag tutorial cannot play level 3.
- Offer a free skip as well, after enough failures. A player permanently walled off from your content
  is worth less than one ad impression.

**Hint**

- Requires a new authored asset per level (a recorded solution pose, replayed as a ghost). Budget
  that authoring cost before committing — trivial for 5 levels, significant for 60.
- Show the *first move*, not the whole solution.

---

## 4. Why "ads that grant power / abilities / perks" breaks this game specifically

This was the original question, so it deserves a direct answer.

Every level's difficulty lives in authored numbers: `boundsSize` limits how far the world can be
pulled, `startFocus` and `startSpin` set the opening pose, `allowRotation` gates the second verb. A
puzzle is solvable *because* the world can only be pulled so far.

A "bigger pull range" or "stronger pull" reward therefore does not make the player better at the
game — it **deletes the constraint the puzzle was built from.** Concretely: a level designed so the
player must rotate the world to bring a far platform within reach becomes solvable by dragging
further, and the rotation lesson never lands. The level still reports a win, so it looks like it
works, while the design it was carrying quietly stops functioning.

There is also a QA cost. Every purchasable modifier multiplies the states each level must be tested
in — every level must hold up both with and without the boost. For a puzzle game with authored
solutions, that is a poor trade for the revenue.

**Rule of thumb: sell how the game *looks*, and sell *relief* when a player is stuck. Never sell the
puzzle's constraints.**

---

## 5. Prerequisites the build does not have yet

None of this is buildable today, independent of the design decisions above. Missing:

1. **A UI layer.** No HUD, no menus, no modal system. `Assets/PullTheWorld/Scenes` and
   `Prefabs/Gameplay` are still empty. Every placement needs a prompt surface.
2. **Persistence.** No save system, so no "levels completed", no fail counts across sessions, no
   remove-ads entitlement, no owned cosmetics.
3. **A fail counter.** `LevelManager` tracks `LevelState` but not *how many times* the current level
   was failed. The skip gate needs it.
4. **Analytics.** No funnel data, so any tuning would be guesswork. Instrument before monetizing (§7).
5. **An SDK decision.** Nothing is targeted. Unity **LevelPlay** is the lower-friction default for a
   Unity mobile title (mediation included, less external plumbing); **AdMob** pays better at scale but
   costs more integration and policy work. Decide after the demo, not before.

---

## 6. Implementation shape, when it is time

Recorded here so the eventual build isn't improvised.

- **One interface, owned by the game, not the SDK:** an `IAdService` with `IsReady(placement)` and
  `Show(placement, onReward, onDismiss)`, plus an editor/dev stub that grants the reward instantly.
  The game must stay fully playable and testable with no SDK present — critical for a project whose
  main artifact is a demo build.
- **The core must not know ads exist.** `WorldRig`, `PlayerAnchor` and `Spring` should never
  reference an ad type. `LevelManager` already exposes `OnLevelLoaded` / `OnLevelWon` /
  `OnLevelFailed`, so a separate presenter can subscribe and drive prompts with no change to the
  puzzle layer.
- **Single-owner rule:** `LevelManager.cs` is the shared seam and is actively being written. One
  person or agent edits it at a time.
- **Rewards must be idempotent and dismissal-safe.** A closed ad, a network failure, or a
  backgrounded app must never consume a skip or leave a level half-skipped.
- **If interstitials are ever added:** never after a fail (that is the moment a player quits), never
  mid-puzzle, and capped per session. The win-celebrate beat is the only non-destructive slot, and
  even there it damages the "one more level" flow this game depends on.

---

## 7. Decision gates

Do not build any of this until all four hold:

1. The team has played the prototype and the core pull mechanic is confirmed fun.
2. The level count is past the prototype's 5, and the content pipeline is real.
3. Retention is measured. If players don't come back, ad placements only change *how* they leave.
4. Funnel instrumentation exists: per-level attempt counts, fails-before-win, and drop-off level.
   Fails-before-win is the number that tells you whether a skip offer is mercy or a tollbooth.

**Instrument first, monetize second.** The highest-value early metric here is not ARPDAU — it is
per-level drop-off, because that tells you which levels are badly tuned. Fixing a bad level earns
more than selling a skip past it.

---

## 8. Open questions for the owner

- **Business model:** free-to-play with ads, or paid/premium? A deterministic, insight-driven puzzle
  game is one of the few genres where premium still works — and that answer would make most of this
  document moot.
- **Target scale:** a 5-level demo, or a 60+ level commercial title? This decides whether per-level
  hint authoring is affordable.
- **Is F2P a stakeholder requirement rather than a design choice?** If so, say so before the next
  milestone: the cosmetic pipeline (skinnable anchor, world palettes) is far cheaper to plan now than
  to retrofit later.
