# Level roster

Authoritative content list, from the lead Unity build session. **The prototype ships 5
levels.** The concept sheet showing levels up to 30 is aspirational — style reference
only, not a content plan.

Mechanic set is **LOCKED** for this prototype:

- **In:** drag, rotate-gravity, physics props, pressure plate + gate, fire/spike hazards.
- **Out:** water, magnets, rescue NPC, ice, conveyor, teleporter, wind, timed doors.

## The five levels

### L1 — "Pull the World"
- **Teaches** dragging. Rotation **locked**.
- One long grass causeway, player at one end, door at the other, nothing in between.
- Hint: *"Drag anywhere. You stay — the world moves."*
- Pictures: `L1_pull-the-world_start.txt`
- The image has to say *there is nothing to do here but pull*. Any obstacle is a bug.

### L2 — "Around the Wall"
- **Teaches** that the player is a solid pin the world cannot pass through. Rotation
  **locked**.
- Door sits in an alcove behind a stone wall. Dragging straight is physically blocked
  because world geometry cannot pass through the player, so the route must go down,
  across and back up.
- Hint: *"The world cannot pass through you. Go around."*
- Pictures: `L2_around-the-wall_start.txt`
- The dog-leg route must be the obvious single readable path.

### L3 — "Tip It Over"
- **Teaches** rotation = gravity. Rotation **unlocks here**.
- A boulder in a stone cradle plugs the corridor; dragging cannot shift it. Twist the
  world, the cradle tips, the boulder rolls out and falls away.
- Hint: *"Two fingers to twist. Gravity always points down."*
- Pictures: `L3_tip-it-over_start.txt`, `L3_tip-it-over_rotated.txt`
- The **rotated** image is the most important picture in the whole set — it is the one
  that sells the hook. World rolled around the player like a wheel, player dead upright,
  gravity still down-screen.

### L4 — "Smother the Fire"
- **Teaches** that physics props defeat hazards.
- Fire in the corridor, boulder on a ledge above. Rotate to roll the boulder off the
  ledge onto the fire; it goes out. Then drag across.
- Hint: *"Drop something heavy on it."*
- Pictures: `L4_smother-the-fire_start.txt`, `L4_smother-the-fire_solved.txt`
- Start and solved must be the same layout so the change reads as cause and effect.

### L5 — "The Long Way Round"
- **Combines everything.** No hint text.
- Rotate to drop a crate onto a pressure plate, which retracts a stone gate; then drag
  the player around a wall, past spikes, and bring the door in.
- Pictures: `L5_long-way-round_start.txt`, `L5_long-way-round_gate-open.txt`
- Biggest diorama in the set, and the one most at risk of reading as visual noise. The
  gate-open image should let the eye trace one continuous path start-to-door.

## Non-level pictures

| Prompt | Use |
|---|---|
| `KEYART_hero.txt` | Store listing / title screen. Dark background, world orbiting the player, room left at the top for the logo. |

## Coverage

9 prototype pictures: 5 start states, 3 solved/rotated states, 1 key art. Every locked
mechanic appears in at least one picture, and every mechanic that needs a before/after to
be understood has both.
