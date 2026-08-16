# Changelog

All notable changes to Akte Europa Reborn. The build ships **only the engine** —
terrain, units, maps, tables and now sound are derived on your own machine from
your own copy of the 1997 game.

*Auf Deutsch: [CHANGELOG.de.md](CHANGELOG.de.md).*

## 0.6.0 — unreleased

Everything committed after 0.5.0 goes here. Four areas, in the order they were
chosen:

- **Multiplayer online.** Beyond the LAN: a server as broker, seed distribution
  from the server, lag compensation, checksums and reconnect — and before any of
  that, the question the LAN proof could not answer: float determinism between
  two *different* machines. (The second question that stood here — routing the
  computer players' orders through the command ring — has been answered, see
  below.)
- **The map editor.** Painting single cells, placing units, buildings and track,
  choosing the number of players, opening existing maps — and making a generated
  map fully playable, not just walkable.
- **Skirmish.** The competitive mode it is meant to become: the depot-or-queue
  decision, the economy in the interface, the balance that goes with it.
- **Train and track.** The wagon fine placement in section 44 and the repair
  chain — both done, see below. What remains: the train that explodes at a break,
  and one line on `map_DM_6` whose chain does not match its stated length.
  (The "25 chain cells without a neighbour" that stood here turned out to be a
  misreading of our own counter — there is not one such cell on those maps.)

Campaign stays faithful to the original; skirmish and multiplayer are allowed to
deviate on purpose, and every deviation is marked as ours.

### Units

- ⭐ **A building's ground no longer hides anything.** Reported with
  screenshots: "that piece of track is never visible there, and when a unit
  drives in, the ground graphic covers it. You can still select the unit!" That
  "still selectable" was the key — the vehicle was there, it was only painted
  over.
  ⚠ **The obvious explanation was wrong and is withdrawn:** that the building
  pattern reaches beyond the footprint. Measured, the footprint is **exactly**
  the occupied pattern area (mine 9×6 and pattern out to (8,5), base 7×6 and out
  to (6,5), weapons factory 8×5 and out to (7,4) — **twelve of twelve types
  identical**). There is no overhang.
  What painted over things were the **flat** tiles. A building is mostly made of
  them — base **30 of 37**, weapons factory 29 of 38, power plant 23 of 26 — and
  those are concrete, gravel, shadow and ramp. In the original they are
  **terrain**: the stamping loop at `0x4C97B4` writes them into the map cell with
  the range marker `+10000`, and the building blitter at `0x42B1DE` draws only
  one sprite and then the doors — sixty tiles appear nowhere in it. Here they
  rode along in their building's row bucket and covered whatever stood on them:
  on `map_NET02` **749** such tiles with **80** track cells underneath, on
  `map_DM_4` 489 and 58. They now go in a pass of their own, below everything
  that moves. ⚠ That we **split** flat from raised is ours — the original makes
  do with its second sprite, which we do not have; the threshold sits in the
  measured gap at 24/25 px. Counter-check `--boden-alt`, probe
  `--overdraw-check`.

- **What a building occludes is decided by its DOOR.** Read at the row-bucket
  insert `0x42FD47`: a building **without** a door goes into draw row
  `row + 3`, one **with** a door into `row + the door's row offset`. And the
  picture does not move with it — `0x42FDD5` takes the same amount back off, so
  the position works out identical for every door value. **The door shifts the
  painter's order, never the image.** The values are constant per type, counted
  over all **798** doors of the 23 maps: base (4,2) 73/73, the three factories
  two at (2,3) and (5,3), airfield (5,4) 39/39, mine (5,3) 49/49, shipyard (2,3)
  13/13. So the **base** belongs one bucket earlier than our previous fixed 3,
  and the **airfield** one later. ⚠ On balance this occludes slightly *more*,
  not less (on `map_NET02` 727 → 741 tiles) — the change is faithfulness, not an
  improvement, and it does not fix the point above. Counter-check `--tuer-alt`.

- ⭐ **Units disappear behind buildings.** Until now every unit was drawn over
  every building — a tank stood on the roof of the base. What the original does
  is read: it **occludes**, it does not make anything translucent. It *has* a
  translucent blitter (a 256×256 blend table at `0xA3AFB1`), but that one has
  **exactly one** caller against nine for the normal one, and it hangs off a
  building field, not off whether something stands behind it. Instead the
  original buckets its display list by screen row (dispatcher `@0x42C8C0`, 30
  kinds) — painter's order.
  A unit's body now runs in the same row-by-row pass the track already ran in.
  Selection brackets and order lines stay on top: those are controls, not world
  objects. Counter-probe `--no-unit-occlusion`.
- **A fallen foot soldier no longer vanishes.** Measured across all 24 sets and
  8 facings: the walk cycle (blocks 0–7) and the standing pose (11) are
  complete, the death frames are not — block 12 in 21, block 13 in 69 and block
  14 in 63 of 192 cases each carry at most four pixels. They decode, so the
  export wrote them as valid files and the renderer drew an empty image. Nearly
  empty frames are no longer written at all, and the renderer falls back from
  the time-correct block to the last one that really exists for that facing:
  **192 of 192** (set, facing) pairs have a corpse frame, 0 without.

- **The ball-roller chassis is visible in all eight facings.** It was invisible
  in seven of eight. Counted across the whole part bank of ROBO.CWR: **35 of 36**
  populated components carry their full row of eight at block 0 — **component 9
  alone carries it at block 5**, in all three groups. Looked at, those eight are
  eight clean rotations, while the five single frames before them are different
  *pitches* of one view; that refutes the other old reading. The export now
  *finds* the block instead of assuming it: 64 of 65 chassis complete before,
  **65 of 65** now.

- **A unit that stops to fire now carries on afterwards.** Until now the move
  order was gone the moment a target came into range: the unit stopped, fired —
  and then stood there forever. On map_NET07 that was exactly the one unit in
  forty that stalled two cells short of its goal. Likewise, a unit that finds no
  free path at the moment of the order (because its own comrades are in the way)
  now keeps its goal and tries again a second later instead of silently dropping
  the order.
  ⚠ Whether the original resumes after a firefight is not read — that is our
  setting, and it is marked as such.
- **Eighteen map images still had their buildings baked into the ground.** The
  map baker deliberately leaves a standing building out of the picture — an
  older fix, because otherwise its pixels stay in the terrain forever and a
  destroyed building can never show its ruin. The images for campaigns 16 to 33
  dated from **before** that fix. The round-trip harness found it: it had been
  stuck at 50 of 68 for days, and all eighteen failed **on the image alone**,
  with the data round-tripping perfectly. While the building stands nothing is
  visible — two screenshots of the same spot are pixel-identical, because the
  engine draws the building exactly over it; the difference showed only on the
  minimap, and would only have hurt once a building fell. **A fresh install was
  never affected** — it bakes with current code.
- **A captured works now gets its starting stock.** The report was that taking
  Horni in campaign 3 does nothing. In the original it does something: once the
  slot is yours and one of your units stands on the door cell, the mission script
  writes 180 weapon parts and 127 chassis parts into the building's store; Dolni
  gets 330 and 237. On our side the effect of that rule was simply **empty** —
  the condition was there, correctly read, and did nothing. The same form occurs
  twenty times across five missions, and it ASSIGNS the store rather than adding
  to it — which is why in campaign 33 the very same instruction is a penalty
  instead of a reward. The amounts hang off the mission block's tick counter and
  vary by up to nineteen, as in the original; campaign 25 applies a factor of
  three. Measured: campaign 3 four of four, campaign 6 four of four, campaign 2
  two of four.
  Campaign 33 is now built too: it is the only one that computes
  **quadratically** (the amount depends on the square of the tick counter), and
  all twelve of its stockings hit their value.
- **The original counts cells, not pixels — and the ground slows nobody down.**
  The second half of the reported question about speed has been read out. Every
  unit carries a step counter the game itself calls "kolik"; it grows by the
  unit's speed once per tick, and the cell is done when it reaches 80 — 120 for
  a step across a corner. That is all there is. No terrain, no slope: the 1997
  movement loop never touches the terrain grid, and a unit's speed is changed in
  exactly one place in the whole program — on a hit, where it is halved.
  Our 45 % surcharge for rough ground is therefore **withdrawn**; it was a guess
  and it was wrong. What matters more is what it hid: we drove at a fixed pixel
  speed towards the next cell centre, and in an isometric view the eight
  neighbours are not equally far away. Depending on the compass heading the same
  unit took up to **twice** as long to cross a cell. Now it is the original's one
  number: 1 straight, 1.503 diagonal. Measured on map_NET07 over 8275 completed
  cell steps — rough against free ground 0.999 instead of 1.610, diagonal
  against straight 1.503 instead of 1.386. As a side effect one floating-point
  computation drops out of the network game: an integer now decides when a unit
  arrives.
- **Weapons have a minimum range, and we had missed it.** Checking the ranges
  turned up a field that has always been in the map data and is read exactly
  **once** in the whole 1997 game — twenty-two bytes next to the range, in the
  same decision: too far away rejects the shot, **too close does as well**. The
  maps confirm it without a counter-example: 620 of 4476 units carry such a
  value, and it is smaller than the range in 620 out of 620 cases (3 at range 8,
  5 at 14 …) — and only the long-range ones have one at all. An artillery piece
  now lets go of a target that has come too close instead of keeping it; driving
  closer would only make it worse. Measured on `map_DM_1`: 18 units with a
  minimum range, 30 targets dropped because of it, and zero with
  `--no-min-range`. ⚠ The report also says when a map has no such unit at all —
  there the zero is not a result.


- **A group no longer strands at a bottleneck.** Reported as "selecting a group
  and driving them one behind the other, over a bridge say, stops them dead as
  soon as somebody else is on the bridge". We used to wait 0.7 seconds, look for
  a new route **once**, and throw the route away if that failed — after which
  the unit stood there forever, until the player clicked again by hand. On a
  single-lane bridge the route is occupied at almost every replanning moment, so
  it hit half the group. The 1997 game does it differently, and at the root: its
  movement question has **three** answers, not two — no, *yes but somebody has
  to give way*, free. Waiting in front of a wall is pointless; waiting behind a
  moving unit is exactly right, and we had thrown both into one pot. Now a unit
  waits behind another and keeps its route; in front of something immovable a
  patience counter runs down, and when it expires the route is replanned and the
  counter **re-armed** instead of given up. The numbers are the original's
  (15 + roll%15 on entering a cell, 40 + roll%20 after that, and a one-in-60
  nudge per tick while somebody is in the way). Measured with the new
  `--stuck-check` across three maps where the question is answerable:
  **8 stranded units before, 0 after**; `--stuck-check=alt` reproduces the old
  behaviour inside the same program and fails. The twin stays bit-identical over
  30 seconds.

- **Bombs land on the middle of the hull.** An air attack used to aim at a
  unit's record cell — on a battleship, that is its top-left corner. The 1997
  game shifts the aiming point according to the target's class, and that shift
  is nothing other than the middle of the hull: one cell for the small ones, two
  for the large. It is the same table that decides the hull size.
- **A bomber flies a second run instead of hanging over its target.** It drops,
  turns away to a rolled point **10 to 19 cells** off (each axis with its own
  sign), comes back and attacks again — until the ammunition is gone. On the way
  out it does not drop; that is not our convenience but the original's own lock,
  which ties the drop to an order state it clears when turning away. The 1997
  game names the place itself: "Over target while attack".
  ⚠ **And with that I withdraw a withdrawal of my own.** I had reported that the
  original knows no such loop, and revoked an earlier claim about it. The
  revocation was itself wrong: the only correct part was that the two aiming
  offsets are the middle of the hull and not an overflight. The loop sits
  elsewhere — behind a condition I had not followed up at the time, in a second
  dispatcher the game only enters once the aircraft stands exactly on its target
  cell. Measured: 71 loops in a good two minutes on a map with ten bombers.
- **The railway repairs itself again.** A vehicle with the track attachment
  works twenty ticks on a shot-up piece, makes it whole — and then finds the
  next one on the same line **by itself**. That is how the chain reads in the
  original; it was only waiting for us to have a way of giving it an order.
- **A ship now occupies its whole hull.** The 1997 game checks **four** cells for
  a small ship and **sixteen** for a large one; we checked one. A battleship thus
  occupied a sixteenth of itself — two ships could stand inside each other, and a
  land vehicle drove through three quarters of one. The hull size now lives in
  the navigation grid itself, so claiming and releasing cells cannot drift apart.
- **Supply helicopters fly home when nobody needs supplies any more** — as in the
  original. Until now they hovered over the units they had just serviced. And
  they turn back while the fuel still covers the way home, instead of waiting for
  an empty tank. **Where** they fly was guessed wrong at first: we sent them to
  the supply post. The 1997 game instead rolls **any building of its own** and
  scatters the destination by up to five cells — with no check whatsoever of what
  kind of building it is. That also explains how a helicopter gets home on a map
  with no airfield: it is not looking for one. Measured on a demo map: of 19
  helicopters, 15 used to hang in the air with no order, now **none** — and the
  number standing at home grows from 4 to 9 over the run. ⚠ **Our deviation:** a
  player with no building left keeps its helicopters where they are. The 1997
  game sent them to a rolled map cell in that case — a fault that only goes
  unnoticed there because without buildings the match is over anyway.
- **An aircraft only had half a turn.** The hull stood across the flight arrow,
  and the cause was neither an offset nor a state error but the **export**: an
  aircraft in the 1997 game owns **sixteen** images, and we exported eight of
  them — not every second step but **the first eight**, that is, half a
  revolution. An aircraft could not look backwards at all. Proven from the part
  table itself: the eight air parts lie 16 frames apart, and all 16 differ.
  All sixteen are exported now and the facing is computed in **22.5-degree
  steps** with the original's own formula. Measured: 11 distinct steps in the sky
  at once, 8 of them odd — impossible with eight images.
  ⚠ **A calibration of ours falls with it.** The "offset 2, two independent
  paths" entered earlier is withdrawn: the calibration against the tanks ran on
  the half sprite set, and the second path was not one — the offset in the
  original's formula is the same one our direction computation already had, only
  in sixteenths. Applying both turns 180 degrees too far. The original's
  arithmetic now stands alone.
  ⚠ What **remains** is original and not a fault: a turn takes six degrees per
  tick, a full turn 60 ticks. After a change of target a helicopter therefore
  flies sideways for up to 30 ticks before it has come around.

### Skirmish

- ⭐ **Enemy buildings can be captured — Ctrl+right-click.** Reported as "the
  shipyard and the sea dock cannot be captured, all you can do is attack them"
  and "buildings the AI has captured cannot be captured back, only destroyed".
  The new `--door-check` first showed what it was *not*: the doors are
  reachable (shipyard 1/1 and 2/2, base, factories, airfield, mine and stations
  complete). The cause was the click path — it tries an attack first, and a
  hostile building *is* a target, so the move order never ran and the unit
  could never reach the door tile. Nobody attacks a neutral building, which is
  why it worked there and only there.
  Measured: an enemy base now changes hands **undamaged** (1200/1200). The
  counter-probe `--capture-by-attack` leaves it hostile and at **0/1200** —
  exactly the reported "only destroy".
  ⚠ Sea dock (0 doors in 39 of 39) and power station (0 in 262) stay
  uncapturable; that is the original, and the button now says so instead of
  staying silent. The harbour changes hands with its shipyard.
- **A new unit drives out of the door.** This had been read for a long time
  (`@0x410441`: the spawned unit gets `col + door_col` / `row + door_row`) and
  never built — it appeared at the building's anchor cell, so depending on the
  footprint on the wrong side. Measured: 4 of 4 out of the door.
- **The map preview now says how many bases there are.** Prompted by "the enemy
  AI does something sometimes and nothing at other times — some never start
  building". The cause is not the AI but the map: only a BASE builds, and many
  maps have fewer of them than start slots — `map_DM_4` **2 bases for 5
  slots**, `map_DM_11` 2 for 6, `map_NET07` **none** for 8. Whoever gets none
  watches. That was written nowhere; it now stands under the preview, with a
  warning sign when it does not add up.

- **A build queue.** Ordering the same unit several times used to pay every
  time and merely restart the running build — three clicks, three lots of parts,
  one unit. Orders now line up: paid on entry, at most six waiting (the
  original's own depot size, `cmp al,6` @0x467FBF), **Shift+B** takes the last
  one back and refunds it, and the resource bar shows what is running and what
  is behind it. Measured with `--queue-check=4`: 4 ordered, 80/160/0 paid
  (exactly 4x the price), 4 delivered, queue empty. The counter-probe
  `--no-build-queue` restores the old behaviour and reports 80/160/0 paid
  against a target of 20/40/0 — the reported loss, reproduced and then measured
  away. A queue is OURS; the original has no build time at all, it has a depot.
- **Techstandard now defaults to 8 instead of 1.** At level 1 the airfield only
  releases the two supply helicopters. What is read is that a fresh original
  starts at 1 (@0x4426F4) — not that 1 is a good competitive default. A line
  under the dial now *computes* what the chosen level unlocks. An existing
  install carries the old 1 in its `settings.cfg`; it is lifted **once** and
  never touched again.
- **Research and repair are reachable.** Both have been on keys O and K for a
  long time, but the base window's tabs said "not connected yet". They now show
  state, cost and the next project. Repair needs **no unit** — the building
  repairs itself.
- **The airfield counts parts, not dollars.** It always paid correctly; only its
  heading printed `$150`, which is the supply depot's price. Where the parts
  come from is answered too: **by rail** (type matrix @0x504128).
- **Supply helicopters no longer stall.** An empty one looked only for a supply
  post (type 14) — and **map_NET02 has seven airfields and no post at all**.
  Without one it now reloads at its own player's airfield or base, and says so.
  Where a post exists the post still wins (NET04: 1 at the post, 0 deviations).
- **The resource bar says it is a sum**, with the number of buildings counted —
  and airfields and shipyards now count, since parts are paid out of both.

- **Your own start position is marked on the minimap.** A white diamond with a
  dark outline, where the match began. It does not follow you: the point is
  taken once at the start and never again, so that losing your base does not
  take away the knowledge of where you came from. Ours, like the minimap itself.
- **Every participant starts with a base, the computer included.** The conquest
  maps leave 4 to 8 bases standing neutral, and whoever reaches one first owns
  it: that is a race, not a battle. Each participant is now handed the nearest
  one at the start; the remaining buildings still have to be taken. The handout
  works in **whole cells** and in a fixed order, because it sits in the lockstep
  path and has to come out the same on two machines. Measured on `map_NET04`:
  4 of 4 participants get one, neutral buildings drop from 61 to 57 and neutral
  bases from 8 to 4; `--no-start-base` restores the old behaviour. A deliberate
  departure from the original; the campaign is untouched.

### Campaign and interface

- ⭐ **The original's encyclopedia is in the game.** The menu row was meant to
  link to our wiki. Looking at what the *original* has behind it turned up
  **`ENCYCLOG.TXT` with 106 pages** next to GAME.EXE — chassis, weapons,
  equipment, upgrades, air force, navy, Big Bertha, infantry, buildings, in
  full text and with **149 cross-references**, which are now clickable.
  ⚠ **The encoding is a trap:** `HELPG.TXT` beside it is cp437,
  `ENCYCLOG.TXT` is **Latin-1**. With the wrong reader "Räder" becomes "RΣder".
  The file decides, not the folder.
  ⚠ Without pictures: pages carry an image number up to 97, but `ENCYCLOG.PIC`
  only holds 24 — the mapping is unread and is not guessed at.
- **Credits.** The original's row now leads somewhere. It shows what is
  documented (Virtual X-citement, Eidos Interactive, 1997) and the Reborn side —
  and says what is **not** documented: the names of the 1997 team are in no file
  this build reads. The original's credit roll is probably `34.RPL` — the only
  film outside the 33 mission films, and the only one present on **both** CDs.
  An indication, not proof; we do not play .RPL.

- **"Load game" is centred again.** The screen set its anchors but not its
  offsets, so it kept a zero-size rect in the top-left corner and drew its window
  from there. The same call was wrong in four more places, where it left three
  dimming layers and the end-window's mouse lid doing nothing.

- **The editor overlay stood in the skirmish, and the mission pop-ups stood in
  the main menu.** Two reports, one cause. A scene change replaces only the
  running scene; whatever hangs beside it under the root survives — and two
  helpers hang there on purpose: the map editor's watcher, so it can attach to
  the *next* map, and the help windows' canvas layer, so the camera cannot carry
  them out of frame. Both had a switch to turn them on and none to turn them
  off. The edit mode was never taken back — the method for it existed and was
  called **not once** in the whole program — and the windows were only cleared
  when a map was *loaded*, a path the main menu never takes. The fix now sits at
  the menu's **entrance** instead of its nine exits: whoever stands there has
  left the game world, whichever door they used. With a harness that walks the
  real exit (`--leave-check`) — and a counter-probe that reproduces the old
  behaviour inside the same program (`--leave-check=alt`) and **must** fail,
  because otherwise there is no way to tell whether the counter can see
  anything at all.
- **The hit calculation left out the shooter's elevation.** Reported as "team 2
  hardly takes any damage" — and rightly so: the 1997 game counts elevation on
  **both** sides, we had only one half, and two fields were swapped on top of
  that. A wrong comment set it off. Fixed; the difference that remains is the
  original's elevation rule.
- **The campaign HUD shows the mission's objective.** Until now only the
  sub-missions were there — the main objective is the victory condition, and
  that has no text at all. The original does have one, in `OBJECTG.TXT`; it sits
  on CD 1 in the same archive the briefings and help texts already come from. 33
  missions, 58 objectives, in the 1997 wording.
- **The technical lines and the key legend leave the HUD in play.** Map name,
  grid size, tileset, image size and the two lines of key bindings are facts
  about the file and about the controls, not about the battlefield. They stay in
  the plain map viewer, and `--hud-debug` brings them back everywhere.
- **Supply helicopters** now set their facing on the last step before arrival
  as well. Where one looks *without* an order was read from the original: it
  keeps the facing of its last flight — the air loop never touches facing.

### Map editor

- **Terrain and elevation can be painted.** Two new brushes: one sets a cell's
  terrain class (open, rough, water, blocked), the other raises and lowers it. A
  stroke never changes only the cell that was clicked — the tile key depends on
  the slope, on the four neighbours and on the distance to water, so up to **81
  cells** are pulled along. They take their tile from the same computation as the
  map generator, with the same roll; a cell that was merely pulled along
  therefore gets exactly its old tile back.
  ⚠ **The brush refuses rather than repairs.** The generator resolves elevation
  conflicts by lowering *other* cells. A brush must not do that — it would then
  do something other than what was clicked. It declines and says why.
- **The prober threw out the first brush twice.** Both times it had made the map
  measurably worse: hard seams from 3.4 to 6.9 per cent, one shore cell with an
  inland tile, one complaint in the map check. The cause was the same both times
  — it drew its tile differently from the generator. `--map-edit-check` now puts
  nine numbers **before and after** painting side by side; without them the fault
  would only have shown as a painted patch that "somehow looks different".
- **Players 2 to 5 were never selectable.** The bar said "0..7 = owner", but the
  digits 1 to 4 were already caught earlier as the brush selection — with the
  unit brush no owner other than the first could be set at all. The owner now
  cycles.
- **The "fragmented buildings" were not buildings at all.** The base and factory
  of a generated map are pixel for pixel the same as on a shipped one. What
  looked fragmented were **single building tiles scattered over the terrain as
  vegetation** — including the black interior tiles that only make sense as part
  of a whole. They got there because the 1997 game writes a building's tiles into
  the map grid, so the measured tile table took them for scenery. On the shipped
  maps every building tile lies inside a building's frame — 2094 of 2094 on one,
  160 of 160 on the other, and not a single one stands alone. The map check now
  counts both.
- **Placing units by hand.** Sixteen kinds, from the wheeled scout to the
  battleship, each with the values it actually carries on the shipped maps —
  life, fuel, attack and class read from the raw records rather than chosen. The
  owner is chosen too, and the editor refuses what could not stand there: a ship
  on a meadow is a unit that will never move.
- **The editor has a brush.** You can now place **buildings** by hand (any kind
  the tileset knows — military or civilian, with a choice of owner down to
  unowned and scenery), **objects** and **railway track**. A building appears at
  once; objects and track go into the picture when you save, and until then the
  screen shows a preview and says that it is one. A building only goes on a real
  build site — and where it does not, the screen names the cell and the reason.
  The image of a piece of track is not chosen but derived from its neighbours,
  following the table the 1997 game keeps itself.
- **A generated map is now a conquest map.** It gets neutral buildings like the
  shipped skirmish maps: count, kinds, doors, hit points and spacing are measured
  from seven shipped maps; the distribution and placement are ours. Four
  buildings become over seventy on a large map, airports, factories and bases
  among them, all there to be captured.

### Sound

- **A sound now comes from the left or the right.** Attenuation by distance was
  already there; the panning had been read and deliberately left as a gap. The
  1997 game computes `pan = 200 · dx` and clamps it to DirectSound's own limits —
  the control is therefore at its end at **50 cells** of sideways distance. Built
  the way it has to be: one audio bus per voice with its own panner, twelve of
  them. Sharing a bus would give a shot at the left edge of the map the panning
  of the next shot fired.
  ⚠ **`dx` only.** A sound directly above or below the ear comes from the middle
  however far away it is — the original does not consult `dy` for panning at all.
  An angle computation would be "more correct" and therefore wrong.
  Measured on a large map: 255 objects, values from −1.00 to +1.00, 93 hard left,
  35 hard right.

### Train and track

- ⭐ **The wagons face where the track goes.** Reported with two screenshots
  ("how silly the train often looks"): on a slanted line two coupled wagons
  faced **opposite** ways and three faced three ways, while the track below them
  ran cleanly diagonal.
  The cause was a single expression that knew only **four** of the eight
  directions and, on a diagonal, dropped the x component entirely. Its
  justification sat right next to it and was the actual mistake: that the odd
  pieces are "half steps of a diagonal, which a cell chain does not have". The
  diagonal is very much there — it is laid out as a **staircase**, (±1,0)
  followed by (0,±1).
  The encoding is in the original, picture table `0x539400`: 0 = S, 1 = SW,
  2 = W, 3 = NW, 4 = N, 5 = NE, 6 = E, 7 = SE, opposite direction `+4`, and the
  diagonals are **half** cell steps (±20,±10 against 40 and 20). The direction
  now comes from the central difference across the neighbouring cells.
  Broken down by cause the result was unambiguous — on `map_NET02` **0 of 738**
  straight cells were wrong, but **389 of 389** staircases; on `map_DM_4` 0 of
  469 against **354 of 354**.
  ⚠ The probe `--wagon-facing-check` cannot confirm the fix (it takes its
  expected value from the same computation); what shows it works is a pair of
  screenshots of the same wagon at the same game time, plus the **coupling**,
  which measures from another source: there, only the four pieces f0/f2/f4/f6
  occurred at all before, now all eight do, visible gaps stay at **0 of 45** and
  **0 of 51**, and the gap rate drops by 3 and 8 %. Counter-check
  `--stueck-alt`.

- ⭐ **The train drives the diagonal that is drawn.** Reported a second time
  ("when a rail line is cleanly diagonal … the train zigzags, while the track
  is clean") — and the existing number could not see it: `--rail-check` reports
  the **mean** direction change per tick, and a zigzag alternates by +δ and −δ,
  so the mean cancels it out.
  The new `--rail-zigzag` therefore holds the DRAWN track (connection points
  from the table measured off the artwork) against the DRIVEN path, **one bend
  at a time**. It showed up at once, and split by cause the answer was
  unambiguous: of the places where the track is drawn straight, the path bent
  on NET05 at 7, on NET02 at 24, on DM_4 at 23 — and **every single one at a
  RAMP**. On level track: **0 of about 2700**.
  The cause is in the number: the worst deviation was **15.3 px**, and 15 px is
  exactly one elevation step. The path's height was a **step function per cell**
  (`ElevOf(round(x),round(y))·15`) while the drawn ramp rises **continuously** —
  at every cell boundary of a ramp the wagon jumped a whole step. The height now
  comes from the **artwork**: the table of connection points says per frame and
  side how high the rail sits there (f6/f7 14.7 px, f8/f9 15 px above the level
  value).
  Measured afterwards: bends **0 / 4 / 12** instead of 7 / 24 / 23, worst bend
  **7.3°** instead of 27.5°, shape deviation on ramps **0.2–0.3 px** instead of
  7.4–9.3 px. Counter-probe `--no-rail-lift` restores the old state.
  ⚠ No regression: corners 0 of 949, connection row 45 of 45 flush, wagon gaps
  0 of 48 — all unchanged.
- **`--rail-gap-check`: how far is the last piece of track from its building?**
  For "a small piece of the track is often missing". The old number could not
  see it — it reports "0 of 70 further than **2** cells", and a gap of one cell
  is 40 px. Measured: 20 of 70 (NET05), 20 of 66 (NET02), 16 of 48 (DM_4) ends
  with a gap, **always exactly one cell** and **only at stations and field
  stations**. ⚠ The gap is in the MAP DATA — the first footprint column carries
  no rail cell at all there, the building's own artwork continues the track,
  and vertically it sits flush (32/32, 6/6, 7/7, 0 px). Looked at one of the
  places: no hole. **Not reproduced** — the probe now names map, line, cell and
  building.

- **The wagon fine placement has been read.** Two fields in the wagon record had
  stood there without a name since the beginning. They are the wagon's offset
  **within** its cell, in pixels, and the game sets them from a single quantity:
  the parity of the half-row the wagon stands on. Odd means half a tile down,
  even means half a tile to the right — exactly the **edge midpoints** where the
  rail images have their ends too. Counted on the shipped maps: of 162 wagons
  standing on one of the two starting values, **161** follow the rule. The other
  thousand are intermediate states of a journey, in step with the progress
  counter.
- **The wagons stand on the edge midpoints — measured, and against the
  original's own rule.** Counted across three maps, **not one** of 1193, 1000 and
  1305 path nodes sits on the cell centre; the cell centre is the middle of the
  isometric diamond and lies on neither vertex lattice. Until now our track
  derived the vertex from the neighbouring cell and from the connections measured
  off the images — the 1997 game takes the parity of the half-row instead. The
  two can now be held against each other, and on the cells the cross-check can
  say anything about at all they agree **1271 to 0** — completely.
  ⚠ The first version of this entry said "88 to 93 per cent" and blamed the rest
  on the known gap between the original's track and train structures. That was
  wrong, and the fault was ours: split by image type, **every single**
  disagreement sits on a **corner piece** and none on a straight or a ramp
  (402:0, 473:0, 396:0 against 192:88, 155:66, 353:54). A corner piece joins a
  horizontal to a vertical edge, so it has one connection on *each* of the two
  lattices — a table holding one parity per cell cannot possibly match both. It
  was the bookkeeping, not the track.
  ⚠ The parity **steers nothing**, and must not: it is the second, independent
  account, and making it the rule would leave no cross-check behind.
- **The last wagon of a line stood beside the track.** The free end of a line was
  computed as "the opposite side of the exit". For a straight piece that holds —
  for a **corner piece** it does not: if the rail leaves to the right and
  continues downwards, the opposite side is *left*, and there is no rail there at
  all. The free end is at the bottom. The end node therefore sat half a cell off
  in each axis, about 22 pixels. The right rule is not "the opposite side" but
  "the other side this rail image actually has". **25, 28 and 30** nodes were
  affected on the three maps — and measured, those are **line ends without
  exception**, not one in the middle of a chain. The cross-check against the
  original's rule improves on all three maps (666:94 → 672:88, 667:73 → 674:66,
  853:66 → 865:54); the known figures stay the same number for number. With
  `--rail-lay=altport` the old state can be put alongside — without it "4.12 px
  per tick" would be an assertion; with it, the 4.11 is shown to have been there
  before.

### Multiplayer

- **The computer players do not need to go through the command ring** — measured,
  not assumed. In the 1997 program **exactly one of the 21 targets** of the
  computer-player round reaches the command bus (a group move); production, the
  unit sweep and transport write their fields directly. And because the round
  skips any slot a human sits in, in a network game **every machine runs its own
  computer players**. What was then checked is whether ours survive that: three
  runs with two real processes on *different* player slots, one of them over 6000
  ticks in which both computer players capture a building and build four units
  each — precisely the branch that rolls dice. Both machines arrived at the same
  number on every check tick.
- **The network harness now reports what the computer players did.** It did not
  before, which made its green result worthless: a run in which the computer
  players merely stand there looks exactly like one in which they play. Their
  figures now appear on every check tick and at the end, on both sides.

## 0.5.0 — 2026-08-13

The release in which the campaign starts deciding itself and the railway starts
running. Almost nothing here was invented: the computer players' build
programmes, the schedule that unlocks designs, the missions' own victory
conditions, the rate the mission logic runs at, the shape of every piece of
track — all of it was read out of the 1997 program and checked against **both**
copies of GAME.EXE on the machine. What only one of them yields is a reader's
error, not a finding.

Two habits earned their keep and are worth stating, because they are why the
entries below carry numbers. A test rig that can only push in one direction
only tests one direction — the one that could only *destroy* things had been
confirming three inverted victory conditions for a day. And a test rig that
compares our own derivation against itself tests nothing: `--rail-check`
reported a flawless track while roughly half of it was in the wrong place,
because it was measuring our reconstruction against our reconstruction. The
answer was in the map file all along.

### The campaign decides itself

- **33 missions instead of 15.** Every map had long been imported — only the
  list the engine reads was missing. New switch `--reexport-campaign` writes it
  from the already-imported maps, without the CDs and without re-baking.
- **The missions carry their own logic**, read out of the code rather than
  approximated: **270 rules over 31 missions** — 119 text windows, 61 sounds,
  59 payments, 19 objectives, 8 sales, 4 attack orders. Thirteen effects and
  seven conditions were added in this release, among them `text`, `money`,
  `sound`, `order`, `change_owner`, `set_relation` and `stop_transport`. Read
  on both GAME.EXE: 33 missions identical, 0 differing.
- **The mission logic runs at the original's rate, and that rate is measured.**
  `SetTimer(window, 1, 0x14, NULL)` gives **50 ticks a second**, and the clock
  counts **250 ticks to one game minute** — so a game minute is five real
  seconds. We had been evaluating the rules once per *frame* and treating a
  game minute as a real one, twelve times too slow. Each of the 33 mission
  blocks also turned out to be **in two halves**: a gate on `tick mod 100`
  splits what runs every tick from what runs every second one.
- **Counters have guards.** Three of five counters had lost theirs in
  translation, which is why campaign 2 announced a completed sub-mission 1.7
  seconds after it began, without the player doing anything. It now reports
  0 texts, 0 payments, 0 sounds over the first ten seconds.
- **Mission 1 is the tutorial it is in the original.** Its block opens
  seventeen help windows from HELPG.TXT, sends four attackers, and runs a
  **sub-mission** — "sink the transport ships, 50 $ for each" — with the
  original's own three payments.
- **The missions read the occupancy map.** `imap(column, row)` was the missing
  trigger: campaign 1 starts its attack when a unit stands on cell (39, 4).
  That brought 28 rules in 12 missions with it.
- **Jump targets are readable, so counters are visible.** The original writes a
  counter as the fall-through partner of a jump; a reader that discarded any
  block someone jumps into never saw them, and campaign 1's four attackers
  therefore never started.
- **Three victory conditions were inverted and six were missing.** Two of the
  three were true at mission start — the mission was won in the second it
  began, and the test rig reported that as a success. `OBJECTG.TXT`, which
  states all 33 objectives in plain German, says the opposite.
- **Block variables have producers, and a starting value.** Mission 7 wants a
  counter to reach 2 and raises it exactly once, because the setup starts it at
  1. Without that second half the mission was a riddle.
- **The computer players build to the original's plans** — a programme of up to
  50 lines, one line every 50 ticks, which the game itself calls *vyroba*. It
  is not in any map: it sits as straight code in GAME.EXE, selected by mission
  number. 106 programmes identical on both executables.
- **Designs unlock on the original's schedule.** The file that shipped before
  was measurably incomplete — design 52 from mission 6 in the game against 8 in
  the file — and only agreed again after mission 33, which is why nobody had
  noticed.
- **Infantry was missing from the build list.** The design filter let chassis
  160..175 through and infantry sits on 148 and 149. Since the early campaign
  unlocks almost nothing *but* infantry, that left the computer player exactly
  two buildable designs — a transporter and a chaingun tank. That was the
  reported "the AI only builds transports".
- **On a campaign map nothing marches of its own accord.** The original sends
  no unit anywhere; it attacks what walks into its own sight ring. Anything
  that marches does so because the **mission** gave it a target
  (`add_target`, read from the AI's own target list). Attack waves and building
  grabbers are ours and now run in skirmish only.
- **The balance carries into the next mission.** Every campaign mission used to
  start at $0. The money is never overwritten anywhere in the original — all
  three writes to it *add* — so it runs through, which also matches the
  original's own screenshot ("Missionsbezahlung $320", "Kontostand $470").
- **Mission pay is a fixed amount per mission**, 36 constants read out of
  GAME.EXE, added to the balance when the mission ends. The earlier reading —
  "pay is the sum of the script's own payments" — was marked as ours and was
  wrong.
- **A campaign overview**, 33 tiles: finished missions selectable, the next one
  too, the rest shaded.

### The railway

The transport network went from "lines exist on paper" to a running elevated
railway, over about a dozen steps, most of them prompted by the player looking
at the screen.

- **The lines had no geometry.** The y of a line's ends was being read from
  sec122, which **no** `.CWM` file has — only the three saved games did. It sits
  in sec34 right next to the x. **609 of 609 lines** have a route now.
- **The y overflowed a byte.** It counts half-rows, so on a map over 128 rows
  it wraps: 29 lines lay 128 rows off. Which candidate is right is decided by
  the **end buildings** — a line starts at one and stops at another. 1144 of
  1218 ends now sit on their building, up from 530.
- **There is a track, and it is part 64/65 of ROBO.CWR.** Nobody had looked for
  it, because a wagon's frame index *is* its piece of rail, so track was only
  ever visible where a wagon stood.
- **It is an elevated railway on trestles**, established from a screenshot of
  the original: the piers stand in the landscape and the rail runs above them.
  We had been laying it flat on the ground.
- ⭐ **The route is in the map — we had been inventing it.** This is the biggest
  finding of the release. Section 22 holds the finished list of cells, 3000
  records of 5 bytes: column, row, **frame**, hit points, line number. Measured
  against what our derivation produced on one map: **472 pieces of track that
  are not there, 359 that are and were missed, and 235 of 810 shared cells with
  the wrong frame** — roughly half the network. The player saw it on screen and
  had to report it four times before the right question was asked, which was
  not "is our shape correct?" but *"does the map already carry this?"*.
- **Ramps are part of the vocabulary.** Frames 6..9 are ramps, and every single
  one of them sits on a cell whose terrain byte matches — 147 of 147, 170 of
  170, 180 of 180, 118 of 118. That same byte turned out not to be a flag at
  all but the **slope shape**, which is also what the original's train reads
  when it lifts a wagon by 15 px on a gradient.
- **Trestles stand every sixth cell**, and in one of four variants depending on
  whether the track continues past them — both read, neither a setting of ours
  any more.
- **The line meets the building on its connection row**, +1 or +2 depending on
  the type. 232 of 234 ends are now flush to the pixel, where before **none**
  of the 42 ends on one map were. The old open question "the base is 11 px
  off, constant across all maps" was this same two-row shift.
- **The travel speed is calculated, not chosen** — from the 50 Hz tick, a step
  price of 40 or 28, and a per-tick decrement of 8 that is the same on all
  1439 wagons across all 30 maps. Five ticks per straight step, four per
  diagonal.
- **The wagons hang together.** They were being spaced two route steps apart,
  which not only made the train look like four separate cars but tore the
  track open with it.
- **Half of every journey juddered.** On the return leg the wagon ran
  *backwards* within a cell and jumped two forward at the boundary — net one
  cell, so neither travel time nor route noticed. The largest movement between
  two frames dropped from **77.96 px to 2.21 px** over the same run. It had
  never been measured because the outbound leg is correct, and the earlier
  measurement was taken before the first turnaround.
- **The freight actually moves.** The train unloads at the arrival building and
  loads at the departure building; which goods go which way comes from a 12×12
  matrix in the executable that is byte-identical on both copies. Nothing else
  in the entire program touches those four stock fields.
- **Destructible yes, repairable yes, buildable no** — each with its location
  in the code, so the question does not come back. A single hit cell breaks;
  a whole line never fails. A vehicle repairs it and then looks for the next
  broken piece by itself. Nothing in the original lays a new piece of track.
- ⭐ **The track deck sat 40 px too low — the trestles hung in mid-air.** Both
  GAME.EXE place tiles at `row·20 − height·15 − 50` and track at `− 62`, and our
  **x already matched to the pixel** — which is precisely what proved that only
  y was wrong, and wrong by exactly two cell rows. A counter-proof that needs no
  build: part 65 is 83 rows tall with its foot on row 82, and that foot used to
  land 40 px *below* its own cell. ⚠ The tool that had "confirmed" the old
  number was self-referential — it placed the rail using our own figure.
- **The wagons are coupled: 0 visible gaps instead of 338,000.** First measure
  where it gapes (horizontally in a good half of all ticks, vertically almost
  never), then work out why: a lag of 0/4/7/11 ticks gives 32/24/32 px while the
  sprites are 41/22/22/39 px long. ⚠ **The original shows these seams itself.**
  The decision here went to the picture, and as narrowly as possible: the lag is
  only ever shortened, never lengthened, and never below 12 px — the smallest
  distance the original ever produces. Across 1,010,014 measured wagon pairs not
  one gap is left.
- **"Corners and kinks do not close" is not reproducible** — the counter was
  measuring the wrong thing. Its 4.1 px is the **intended** offset with which a
  corner piece leaves its cell so that the staircase reads as a slope (27.1 ·
  29.5 · 31.9 along the bottom, symmetric, and only on the top/bottom axis). It
  now measures the **hole** between the pixels: 0 of 1119, 0 of 508, 0 of 785 on
  three maps.
- ⭐ **The train runs on the drawn rail, not on cell centres.** An isometric
  diagonal is stored as a staircase of single cells — the train walked the
  staircase while the artwork below it showed a smooth slope. Reported as "it
  still zig-zags". The edge midpoints are in the artwork and are measured, which
  side-steps the old blocker **without breaking it**: the original's half-row
  parity stays unreadable, but is no longer needed. Mean direction change per
  tick **1.6° → 0.7°**. Progress now runs on **arc length** rather than link
  count: the original deducts a fixed amount per tick, so it travels at the same
  speed everywhere.
- **The train sits on the rail instead of above it.** When the deck height was
  corrected, the delta had been taken from the *original's own frame* instead of
  the figure measured off the sprites — exactly the mistake the comment one line
  above warns about.
- **Every crossing was missing a strand**, and on the return leg the whole train
  faced backwards, locomotive first at the wrong end.

### Graphics and animation

- **Mechs and spiders walk.** Field +0x11 is the chassis's walk phase; checked
  against 1360 vehicles across 29 maps with no counter-example. ⚠ The *timing*
  is ours — the original does not play the gait frames back.
- **Buildings were white.** The tile atlas was written as a single column and
  exceeded the maximum texture height on 30 of 35 tilesets; the GPU rejected it
  and returned a texture with a dead handle, which draws as a white rectangle.
  That is why mission 1 looked fine and everything from mission 2 did not. The
  atlas breaks into columns now.
- **The middle building frames are damage stages, not construction steps** —
  `frame = (hp_max − hp) / (hp_max / patterns)`, stamped on top of one another,
  checked over 36 maps and 1451 records.
- **Infantry no longer fires continuously.** It showed the firing pose the
  moment it *had* a target; having one is not the same as shooting at one. The
  pose now lasts a short beat after an actual shot. ⚠ That beat is ours — the
  original counts it in a field we have not read.
- **Hand weapons get no muzzle fireball.** The same ANIM.CWA sequence was being
  played for every weapon, cannon and rifle alike. Infantry needs none at all:
  its firing pose carries the red flash **in the sprite**, verified frame by
  frame.
- **The muzzle flash sits at the barrel, not on the hull.** It was being placed
  with an invented 8-pixel lift that landed on the body. It now starts from the
  same point the original mounts the turret on, which the code already
  computed for the turret's own picture.
- **Supply helicopters no longer produce a hit sprite** when they deliver fuel
  or ammunition. That effect was ours, and the comment that introduced it even
  said "a blast would read as a hit" before reaching for the fireball.
- **The dead no longer stand up.** A hit on a corpse ran all the way through to
  the kill routine, which resets the death timer — so the falling animation
  started over, sound and all. Two of the four paths in had no guard; an
  aircraft holds its target in its own record and kept firing at the same
  corpse indefinitely.
- **Unarmed vehicles carry their equipment and stop shooting.** The weapon
  lookup fell back to a made-up weapon for any unknown component, so
  construction vehicles genuinely fired. The game distinguishes armed from
  unarmed at field +0x0d, established on both executables and checked against
  1226 armed and 218 unarmed units without a counter-example. Equipment
  carriers also had no turret drawn at all; they do now.
- **Type 0 means "no building".** Seven records on one campaign map carry types
  outside 1..16 — mineral deposits and one placeholder — and the placeholder
  was standing in the middle of a town as if it were a base. They stay in the
  list, because the mission scripts query them, but they are no longer drawn,
  selected or counted.
- **Higher ground sees further.** For a land unit the original computes
  `radius = elevation + sight − 1`; it is a larger *circle*, not a line of
  sight — the stamping routine reads no terrain at all. Buildings take a
  literal 10 and ships their own field. The formula was found by its shape in
  both executables, and the `− 1` matters: on flat ground a unit sees one ring
  less than its bare sight value.
- ⭐ **The track casts shadows — 1193 masks that had lain unused since the first
  import.** Every piece of track carries its own shadow mask (frames 10..19),
  offset down and to the right, where light from the upper left puts it. How
  dark is **measured**, not chosen: the original recolours the ground through a
  256-byte table, and over the colours that actually occur in the terrain that
  works out to 0.775/0.831/0.820 per channel with a 3.1 % residual. Taken is the
  single factor 0.809 — black at 19 %.
- ⭐ **The slope poses: 4128 sprites that were never exported.** A part carries
  six blocks per group, and the block is the **tilt** a unit stands at on sloped
  ground. The exporter wrote block 0 only — and the galling part: the **turret
  seat** had long been shifted by the slope class, the sprite had not. The hull
  stayed flat while the ground under it tilted and the turret already slid
  aside.
- **The whole bank is on disk now**: 81 parts, 3535 sprites. The exporter wrote
  only what stands on a map or can be built — but the 601 stored designs reach
  for components that appear on none of the 44 maps: **mine layer, flak gun,
  anti-radar** and seven more. Designing one of those gave you an invisible gun.
- **Part 111 is the rotor's shadow, not a rotor.** The helicopter flickered
  between rotor and black cross at 10 Hz because the mask was drawn as a second
  rotor phase. And the rotor's eight frames are **phases**, not facings — it was
  not turning at all.

### The interface

- **The start menu has its title bar and its demo again.** The original does
  not play a film behind the menu — it loads a finished saved game and lets it
  run, cycling through **thirteen** of them (`1.DM`…`13.DM`), which is what the
  "Naechstes Demo" entry switches. Both the caption "Akte Europa" and its
  position are read; "REBORN" underneath is ours.
- **"Neues Spiel" is now "Kampagne"** and shows all 33 missions.
- **The skirmish screen is ordered by mode and by map**, and the three campaign
  maps are gone from it — a campaign map brings its script, its diplomacy and
  its unlock schedule, and none of that runs in a skirmish. Paging through maps
  with `[` and `]` is also disabled while a game is running.
- **The control panel belongs bottom left**, not bottom right. The original has
  no side panel at all: the map fills the window and the panel sits in the
  corner. Everything else in the original is a floating window with a title bar
  and an X.
- **The mission clock is in the panel**, at the corner the original draws it at
  (x = 23, y = 148 of PANEL.DTA), and it shows **hours:minutes of game time** —
  the same two bytes the statistics page prints. We had been printing real time
  and running twelve times too fast.
- **The objectives are in the status line.** The block had been carrying them
  all along — `v[101+k]` is the state of the k-th objective, `v[131+k]` its
  text number — and nothing ever displayed them. That is why the sub-mission in
  campaign 1 could not be seen, let alone seen to be finished.
- **The base window is a window again**: title bar, energy, status, the four
  tabs (Depot / Produktion / Forschung / Reparatur), the list at the original's
  row height, the three stock figures, four buttons, and a stat block on the
  right. The three resource glyphs are not an invention — the original writes
  them as the plain characters `]`, `[` and `{`, which in its own font *are*
  the three part symbols.
- **The "Erstellung" window** — chassis, superstructure, upgrade, and the
  prices, which match the original screenshot figure for figure. It holds no
  state of its own: every click goes through the same steps the keyboard path
  already used, so the two cannot drift apart.
- **The mission end window** — "MISSION ERFOLGREICH BEENDET": the score table
  over eight columns, mission time, kills and losses, sub-missions, the mission
  pay in orange, the balance, and a **Weiter** button that starts the next
  mission. Kills and losses used to come only from a saved game, so a freshly
  started campaign mission reported 0 and 0.
- **Help windows can be closed and stay closed.** A rule without a guard calls
  "close all windows" and "show text" on the same tick, so a dismissed window
  was rebuilt in the same breath and looked immovable. There is a visible X as
  well — the original says in its own text #001 that right-click or ESC closes
  it, but nobody reads that once they think the window is stuck.
- **Settings in the pause menu**, and the window starts at 1600×900 and can be
  resized.
- **The map editor has a row in the main menu.** It existed already, but only
  behind two command-line switches — that is, behind something a player does not
  have. ⚠ It is a **terrain generator**, not an editor in the full sense: size,
  tileset, ground block, generate, check, view. Painting, placing units and
  opening existing maps are missing, and a generated map cannot be played —
  which is why the button says "view map" and not "play".
- **Skirmish: "all units"** — an option of ours, and the reason is a gap in the
  data. The skirmish maps carry **zero aircraft templates**; the airfield had
  nothing to offer no matter what was in store. With the box ticked all eight
  players get the full roster — **ground 601 designs instead of 65, air 8
  instead of 0, sea 10 instead of 2** — opponents included, or it would be an
  advantage rather than a match. ⚠ It also surfaced that the aircraft templates
  carry **no prices**: aircraft would have been free. They were taken from the
  13 maps that do carry them — one price per type across 104 records, no
  counterexample.

### Gameplay and rules

- **Units are built in the BASE, not in the factory.** The reported symptom was
  a factory's parts counter climbing to 10 and dropping to 0 over and over —
  which is correct behaviour, the train is collecting them. What was wrong was
  where we let the player build. The game says so itself in its own help texts,
  the production button reads the three stock fields of the building whose
  window is open, and the computer player's own routine puts its line into a
  base. The return path base → factory is gone; the original has none, and it
  was draining the base.
- **Supply helicopters are bought at the supply post**, with money, from a
  two-button dialogue — and **no campaign map 1..15 has an airfield at all**,
  which is why campaign 2 was not completable. The post on that map belongs to
  nobody and stays that way; the original's dialogue checks the balance and no
  owner, so neither do we.
- **The panel click actually builds.** It reached the aircraft purchase only
  for the airfield type, so the supply post showed its two rows and the click
  went nowhere. ⚠ The test rig had missed it twice by calling the purchase
  directly instead of clicking.
- **Aircraft have their speed.** The reader took the template from one field
  too far along, so `aircraft.json` carried no speed at all and every aircraft
  flew at 7 px/s while a vehicle drives 24..84. Confirmed from a second,
  independent place in the map files.
- **Sound has a place.** A shot at the far end of the map was as loud as one
  next to you. The original attenuates by distance with a constant of 40 —
  0.4 dB per cell — found by its shape in both executables. ⚠ The panning is
  read and **not** built.

### Under the hood

- **In the shipped executable not a single switch arrived.** `--campaign=3`
  started nothing, silently, with return code 0: Godot only passes on what
  stands **after** `--`. It never showed up in development because `--path .`
  forces the separator anyway. There is now a bridge that finds the switches
  either way.
- **New test rigs**, each built to see a specific class of error rather than to
  confirm a green light: `--rail-check`, `--rail-lay` (the counter-proof),
  `--tick-check`, `--produce-check`, `--econ-check`, `--pay-check`,
  `--unarmed-check`, `--depot-check`, `--sound-check`, `--infdeath-check`,
  `--tutorial-check`, `--script-coverage`, `--skirmish`, `--end-window`,
  `--shot-when=squash`. Several of them were verified by taking the fix back
  out and checking that the rig complains.
- **Partial re-exports**, so a change does not cost a full import:
  `--reexport-campaign`, `--reexport-help`, `--reexport-tables`,
  `--reexport-buildings`, `--reexport-units`, `--reexport-effects`,
  `--reexport-states`.

### Multiplayer, and the determinism it needed first

- ⚠ **The simulation ran on frame time.** Same seed, same map, same *simulated*
  ten seconds — and three different states depending on the frame rate:

  | frames/s | ticks | checksum | hp | shots |
  |---|---|---|---|---|
  | 30 | 300 | `5071A756A80B2634` | 36911 | 58 |
  | 60 | 600 | `8C5F21CD5F8AF503` | 36965 | 55 |
  | 144 | 1440 | `F45A1091E165730F` | 36911 | 58 |

  `_Process(double delta)` passed the real elapsed frame time into the world at
  74 places in one file alone: whoever draws faster rolls dice more often and
  shoots more often. Two twins at 1/60 and 1/30 diverged after **18 ticks**.
  Everything that touches state now sits in a fixed tick, and there is a single
  die instead of a freshly, *randomly* seeded generator per factory.
- **Inputs became commands — read first, then built.** The original has exactly
  one path from input to state, and it is a lockstep command queue: 236-byte
  records in a ring of 1000 slots. Found in **both** GAME.EXE by its shape, not
  by its address (dispatcher `0x4C2262` / `0x4C26E0`, ring `0xB4FA38` /
  `0xB509D8`). A click is now a record and takes effect at the **start** of the
  next tick, never in the middle of a frame.
- **Two real processes play the same game over ENet.** They connect, distribute
  map and seed, exchange commands over the wire for 600 ticks and report the
  same checksum at **every** check tick. The counter-proof — swallow one record
  on one side — trips and names tick, unit and field. The original's lockstep
  protocol was read in full first: opcode 1003 "player is ready", 978 the round
  release, `+0x22` the round number.
- **A LAN lobby, so nobody types an IP.** Deliberately query-and-answer rather
  than a beacon: the searcher knows when its search began, so it can honestly
  say "nothing found after 1200 ms" — with a beacon, "I heard nothing" and "I
  did not listen long enough" are the same state.
- **"Start game" used to quit the program.** Not a network fault: a 20-second
  host timeout that is *right* for a headless test rig (an unbounded wait holds
  the build lock) and simply wrong on the player's path. The two paths are
  separated now.
- **Multiplayer lives in its own main-menu line.** The line "Netzwerkspiel" had
  been there all along (slot 65, help index 106) and did nothing; it is now
  called Multiplayer and leads there. Skirmish shows nothing of the network any
  more, and "watch intro" is gone from the main menu.

### Skirmish

- **The air list hangs on the Techstandard 1..8, not on an option of ours** —
  gate `0x419F30`, measured on three maps (level 1 → 2, 4 → 3, 5 → 4, 6 → 6
  templates; no level yields an empty menu). Two of our own justifications fall
  with it: the skirmish maps do not carry section 120 **at all** (all 23 `.CWM`
  end at section 39, header byte 1 instead of 2 — 23 of 23 against 13 of 13),
  and the EXE templates **do** have prices. A *missing* section overwrites
  nothing, so the original simply keeps the table in the executable.
- **The Techstandard row appears in the screen only now that it works** — a
  switch without an effect is exactly what the player had already stumbled over.
  Nothing about it is ours: the range is the button's, the default is a fresh
  game's, the caption is the original's.
- **A standing resource bar with the growth per second** (key `Q`). This one *is*
  ours, and it is marked as such. The reading behind it: the original has **no
  build time** for factories — all three production paths put the unit into the
  world in the same tick the order is handled. What it has instead is a depot
  with six slots.
- The skirmish air checkbox was imperceptible where it stood and has been
  replaced by the read rule above.

### The campaign

- ⚠ **The clock was a condition, and the rule reader dropped it silently.** A
  reader that returns nothing for a limb it cannot read makes `set_rules` throw
  the **whole rule** away, and nobody hears about it. It now names and counts
  every dropped limb: 207 rules without a readable condition, 118 unreadable
  comparison values, 91 comparisons without a call. Adding the clock and the
  unit index brought **+83 rules across 25 missions**.
- **The campaign places its units and sends them off**: `place_unit` at
  `0x4D0810` — 60 calls in 14 missions, the largest hole in the vocabulary —
  plus the orders that belong to it.
- **Mission 23 is playable.** The "four corner heights" of a build site are not
  heights at all: they are section 2 **classes** (0 impassable, 1 shore/sand, 2
  open land, 3 special), and `corners_carry` demands open land on all four
  corners. The field mine went from **0 to 57** build sites on that map.
- ⚠ **The occupancy map is column-wise** (`column*256 + row`), proven from the
  routine that stamps it. Our reader had it transposed, which affected 29
  conditions in 12 missions; **8 of them could never come true** and mission 7
  was unplayable. The rule vocabulary now also knows **OR**.
- **Withdrawn: "mission 5 does not win."** The defect was in the test rig, which
  handed over an *arbitrary* building — on one map a script slot with no
  building type at all.
- **`--selftest-cwm`'s "22 equal / 4 differing" were four swapped comparison
  files.** `10.CWM` and `10.DM` share a file stem, so exporting both silently
  overwrites the campaign map with a **savegame**. With two clean sets: 26 maps
  equal, 0 differing.

### The map editor

- **The terrain generator's yardstick is the 26 shipped maps.** Every counter had
  been green while the picture was a chequerboard — water share, slope bytes,
  height jumps all in range, and the tiles still did not continue into their
  neighbours. **The seam decides, not the key**: a key says which codes the
  original uses in a *place*, never which of them it puts *next to* which. Hard
  seams went from **8.65 %** to 0.22–3.11 % (median of the shipped maps: 0.58 %).
- ⚠ One core number of that measurement was wrong and gave itself away: the
  parts summed to 607,090 cells where the 26 maps have 605,090. It is **29,990**
  cells with two adjacent higher neighbours, not 31,990 — and the three
  *opposite* cases now come with their location.
- **Generated maps have resource deposits.** In the original the **mission
  script** places them (`add_terra_place`, 50 calls in 8 missions) — a generated
  map has no script, so its ground was empty. The field mine now finds **8, 8
  and 48** build sites where it found none, with the density calibrated against
  the original (0.23 against 0.24 deposits per 1000 walkable cells). ⚠ Any
  distribution there is **our addition** and says so in five places.
- **And a real economy bug that hit the campaign too:** `Entity.Deposit` started
  at −1, so a **built** mine never produced anything. Measured over ten economy
  ticks: 5000 → 4950 in the ground, 50 mined, 50 in the mine's store.

### The unit portraits

- **86 images, from ANIM.CWA sequences 400..403** (frames 1176..1261, gapless,
  recounted at the file header). The allocation is fully accounted for: 0..56
  parts, 57..66 ship hulls, 67..73 aircraft, 74..85 people. Byte **+0x0D** of the
  58-byte part record *is* the image number. They now show in all three places
  the player named: the panel at the bottom left, the modular build screen and
  the base.
- ⚠ **For buildings there is none — and the original has none either.** The
  drawer has exactly six cases and not one of them takes a building; it never
  touches the building table; and a building cannot even *be* the selected
  object (the selection routine knows only land units and airfield slots, and
  the range between them is never written). Four independent measurements, all
  with raw scans.
- ⚠ **Two "byte tables" were jump tables.** At `0x450C98` there are 13 code
  addresses — 12 cases plus the **error branch**, not a 13th unit. At `0x450D60`
  there are ten, and the second one carries the actual permutation, because hull
  151 appears **twice** among the ten designs; "hull − 150" would have given the
  flak launch the light cruiser's rockets and the battleship the little flak
  boat.
- ⚠ **The `yoff` byte is part of the image.** Without it frame 1177 comes out
  51x38 where it is 51x60 on the blit canvas — and that byte is what sets turret
  and chassis against each other.

### Ships and vehicles stand on their own cell

- **Battleship and cruiser: the picture was wrong, not the frame.** Selection
  frame, health bar and owner ring stood half a ship length beside the ship. The
  frame is the footprint out of the occupancy map and is right to the byte (4x4
  for the battleship, top-left cell is the record's cell, no counter-example in
  56 stamped ships). The original draws on the **record's cell** — no drawing
  case carries a term for the size of the unit, the extent sits in the image
  itself. 121 units corrected.
- **The weapon of hulls 157 and 158 is no longer drawn.** They have no mount
  point — and the original has no value either: its ship drawer has a three-case
  switch and everything else falls into `"Wrong chassis of ship"` and then reads
  a stack cell that is **never written**. Faithful is not reproducible here, so
  the player decided: no weapon. The hull images carry their guns anyway.
- **64 land units: a two-cell stamp is not a footprint, it is a step.** Ships
  stamp full rectangles; land units are overwhelmingly single-celled and in the
  exceptional case carry **exactly two** cells, never three. The second cell lies
  in the **facing direction** — a closed compass rose against `facing`, 60 of 64,
  and 55 of 55 for facings 1..7. That is the cell reserved for the next step, not
  a body. Standing point on the visible sprite: **2 of 64 before, 52 of 64
  after**.

### The railway, continued

- **`delka` is the length of the route codes, not the number of cells.**
  Counted over all 30 maps: `delka` minus cell count is **4 in 369 of 371**
  lines, and the 4 is derived rather than guessed — aligning the section-22 chain
  onto the `delka+1` route points always drops exactly five points, two at one
  end and three at the other. The two exceptions are precisely the two maps with
  foreign cells under line number 0, so they are the error the rule makes
  visible, not a counter-example.
- **The missing last piece of track is not missing — it lies under the
  building.** Measured on 476 line ends: 224 (47 %) are covered, 128 of them
  completely, and for factory, mine and airfield it is 166 of 166 because their
  ends sit two rows in. ⚠ **The original covers them the same way**, proven from
  its layered drawing list (track in slot row+2, buildings at row+5). The player
  decided to keep it faithful, and that decision is written into the code so
  nobody later mistakes it for a bug.

### Fixed, and one diagnosis withdrawn

- ⚠ **A `ConfigFile` per read access crashed the program on shutdown.**
  "Leaked unsafe reference to object" in series, then `0xC0000005` in the
  finalizer, return code 139 or 132. The second half of that fault would never
  have surfaced without the crash: every access was a **disk** access, and four
  of the settings are asked during the frame — so the settings file was being
  read up to 60 times a second. Settings holds one instance now; the unit book
  and the campaign release theirs. ⚠ The campaign must **not** hold one:
  `--fresh-campaign` clears that state from *outside*, and a held copy would go
  on claiming the old progress. Proven both ways.
- ⚠ **The 97 leak lines for `JSON`/`Image` are not a missing release.** Probes at
  the real exit point produced 97 unreleased images three different ways —
  thrown away, held in a static list, and the shape of every real load site —
  and every run reported **zero** leak lines and return code 0. It is a race at
  shutdown. The whole candidate list is off the table, and the diagnosis is
  written down so nobody repeats it.

### Known limits

- **The wagons of a line can stand on one cell.** Measured: in about 10 % of
  frames of a travelling line, and in the worst case all four sit on the same
  floating-point position, so you see one wagon instead of four. The cause is
  understood — the wagons share one lead position with an offset, and at a
  terminus they all clamp into the same limit. It is **deliberately not fixed**:
  the original gives each wagon its own counter and its own route pointer, and
  the routine that staggers their departure has not been read yet. Any repair
  before that would be an invention.
- ~~**The trigger rule of campaign 2's sub-mission is not entered.**~~
  **Closed within this release.** The vocabulary knows **OR** now — the original
  writes it as two queries in a row, and three rules that had never made it into
  the file came with it (campaign 2 at `0x498EEC`, campaign 3 at `0x4996B7`,
  mission 15 at `0x49D89D`). Campaign 2's sub-mission starts at all now.
- ~~**Mission 5 is not yet winnable in play.**~~ **Withdrawn** — the defect was
  in the test rig, which handed over an arbitrary building. `--produce-check`
  shows independently that mission 5 wins in six seconds once it has its
  factories.
- **Missions 21 and 28 have no script** — the only two of the 33.
- **Unit classes 1..4 are not told apart** in every place; where a rule needs
  them, the data file says so.
- **The sound panning is read and not built**, and the impact effect for direct
  fire is not read at all. An invented impact on every rifle shot would be
  worse than none.
- **The muzzle reach (14 px) is ours.** The right number is read — it sits in
  SHOOT.CWT, 2400 records of four points — but that file does not run through
  the import yet.
- **The multiplayer is proven between two processes on one machine.** That is
  blind to exactly one class of fault: `Entity.Pos` is `float`, and two different
  machines may not agree on it. Untested. Also, **the player number in the packet
  is trusted**, and the AI still writes to the state directly instead of going
  through the command ring — on one machine both sides compute it identically, so
  the equal checksums say less about an AI game than they look like they do.
- **Large generated maps crash on load.** A 254×254 map got through once and
  then failed six times in a row with the same shutdown race as the leak lines
  above (`Godot.DisposablesTracker`, return code 139). Big maps are therefore not
  reliably testable right now.
- **The content builder writes no aircraft prices**, so the fallback that derives
  them from the payload has to stay — without it aircraft would be free, and that
  is measured, not feared.
- **The terrain generator cannot produce section-2 class 3**, where 32 % of the
  original's deposits sit.

⚠ **Re-import once after updating.** Help texts, the railway cells, the ramp
frames and the repaired tile atlas are all produced at import time; keeping an
old data folder means not getting them. `--reexport-states` and
`--reexport-units` together are enough for the railway.

## 0.4.0 — 2026-08-08

The release that came out of playing 0.3.3. Two dozen reports were worked
through one at a time, and most of the answers were already in the 1997
program — the passability, the alliances, the doors, the ruins, the speed of a
unit. Where the game could be asked, it was asked; where it could not, the entry
says so.

### Graphics

- **A destroyed building shows its ruin.** It used to stand there untouched and
  could still be clicked. The game has a picture for it — the last pattern of
  every type — and to use it the buildings had to stop being **baked into the
  map picture**. They are drawn live now, which is what lets them fall.

- **A wrecked unit leaves a wreck, not a black blob.** The scorch mark under the
  debris is a shading layer, not a picture, and was being drawn as solid black.
  It is blended at 45 % under one of three debris variants now. Dead units also
  stopped swallowing clicks: a corpse on the same cell as a living unit used to
  win the click and eat the order.

- **Buildings have doors.** Sequence 301 of ANIM.CWA, 76 frames = 19 doors x 4
  phases, placed the way the original places them and opened by the original's
  own rule: something in the door cell opens it, an empty cell shuts it.

- **A building can be clicked on its body.** Its hit area was a small square
  beside it; it is the whole footprint now, cell by cell out of the occupancy
  mask.

- **Ships are no longer cut off at the bow.** Every unit picture was drawn onto
  a fixed 64x56 canvas and anything past it was thrown away — 192 of the bank's
  3,439 frames lost visible pixels. The canvas is a minimum now and grows for
  the frames that need it.

- **Every unit was drawn two and a half rows too far north.** Inland nobody
  could see it; at the water's edge it is a ship standing on the beach, which is
  how it was found. Checked by asking, for every water cell on a map, whether
  the pixel a unit is placed on is really blue: 20,340 cells over five maps,
  20,324 of them right, the 16 exceptions being piers.

- **The ships of mission 1 float.** A ship covers four cells and the level file
  names only its corner, so it was drawn half a tile inland.

- **The overview map shows what you can see.** It used to draw the whole
  battlefield whether you had been there or not. It now carries the same fog the
  main map does: never seen is black, remembered is dimmed, watched right now is
  bright.

- **The pause screen sits in the middle.** It hung half off the top-left corner
  — a Control under a CanvasLayer is laid out at size zero, and the panel
  centred itself on that.

- **The settings screen sits where it belongs** and survives a 720p window.

- **The version is in the main menu**, bottom right, out of `project.godot` so
  code and installer cannot drift apart.

### Animation

- **The factories run their conveyor belts, the mines turn their wheels, the
  airfield runs a light along its runway.** Every tileset carries a table of
  cell animations that nothing had ever read — the game calls it "animations of
  the buildings" itself. 207 rows over 23 tilesets, every one of them on a cell
  that really belongs to its building.

- **Doors open and close** in four phases as something enters or leaves the door
  cell.

- **Infantry falls over when it dies** instead of vanishing, and the walk cycle
  was measured rather than assumed: eleven of eleven marching soldiers show all
  eight walk pictures.

- Not yet: **vehicles still glide.** Mechs and spiders show one fixed picture
  while they drive. The field the picture comes from is not an animation counter
  — where the original takes the walk phase from is still unknown. See 0.5.0.

### Gameplay

- **You can take a building.** Drive a unit up to the door of a neutral or enemy
  structure and it changes hands, with everything in it. How long it takes is
  the building's own hit points, so a battered factory falls faster than a fresh
  one — the original's rule, not a number of ours.

- **A rescued building can be taken again.** Stopping an enemy mid-capture used
  to lock the building for good, for everybody. The original resets the whole
  capture state when it is broken off, and now so does this.

- **Neutral units join you when you drive up to them.** The neutral unit looks
  at the ring around itself — three by three, four by four for the wider chassis
  — and only the human player collects them, which is the original's rule.

- **You can build.** Depot, generator and Feld-Rohstoffmine can be raised, with
  the build site tested the way the original tests it.

- **Saving and loading**, for campaign and skirmish, with a round-trip check
  that reloads what it just wrote and compares it.

- **ESC pauses and offers a menu** — continue, restart, save, quit to the main
  menu. Ours from end to end: the 1997 game leaves to the menu straight away.

- **"Rohstoffe: keine · wenige · normal · viele"** — the original's own skirmish
  option, with its own words and its own numbers.

- **A production list in the panel.** Select a factory, a dock or an airfield
  and the display box shows what it can build, what each costs and what it can
  pay for right now; click a line to order it. `I` puts the info text back.

- **The network maps are conquest maps, and the skirmish says so.** They carry
  12 to 42 factories and 4 to 8 bases standing neutral, waiting to be taken.

- **The computer goes for buildings too.** One unit per side at a time, sent to
  the nearest door it does not own.

### Movement and the map

- **Units can cross bridges.** A bridge is drawn as a map object and this remake
  blocked every cell an object stands on. The original asks its own map — the
  **imap** — where a bridge cell is free. Over 23 levels, **13,660 cells that
  used to block are free**.

- **Land units stay out of the water.** The water they drove into came from
  reading the wrong section — a zone map the movement code never touches.

- **Rough ground is only for those who can cross it.** Foot soldiers walk it,
  hovercraft cross it where the ground is level, vehicles go round.

- **Infantry can be driven through and driven over** — friendly infantry is
  driven through, hostile infantry is run over, both the original's rules.

- **The view stops at the edge of the map**, and the minimap's white viewport
  can no longer be dragged out of it — that jump was the only path in the game
  that skipped the camera clamp.

- **A building no longer seems to block the reveal.** It watched from the corner
  cell of its footprint, so the far side of a 7x6 base fell outside its own
  sight circle.

### Rules read out of the original

- **A built unit is no longer a thousand times too fast.** The speed field was
  read as a word where it is a byte: over 601 designs that gave values up to
  48,643 instead of 0..17 — and 0..17 is exactly what the units placed on the
  maps carry. This was the "super speed mode" reported for spiders.

- **A built unit gets the movement class of its chassis.** Production pinned
  every unit to "vehicle", walkers and hovercraft included.

- **The campaign knows who its friends are.** The alliances of all 33 missions
  and the neutral slot are read out of the game's own start-up routine instead
  of being guessed from who owns a base. Player 7 is neutral in every mission —
  measured, not assumed: the only slot allied with everybody in all 33 tables.

- **A building sees ten cells, from a point the game looks up per type.** Both
  numbers used to be ours (six cells, half the footprint). They are the
  original's now, and the table was found by shape rather than by address —
  it sits at different addresses in the two 1997 executables that exist.

- **Only your own units can be selected.** Click and rubber band took anything
  on the map, so the enemy could be picked up and given orders.

- **The campaign stops shooting at bystanders.** Mission 1 has sides that only
  stand there. Sides that own a base are played; the rest are left alone.

### Sound and music

- **The music plays.** It was fully built and simply never started — the six
  MIDI tracks were exported, the volume worked, and nothing ever called play.

  ⚠ Which piece belongs to which mission is **ours**: the original picks by a
  file name assembled at run time, and that assembly has not been read.

- **The menu is quiet again.** Every click played sound 600, which is not an
  interface sound at all but a 1.8-second spoken line the campaign uses to
  announce a finished objective.

- **A dying foot soldier no longer sets off a fire.** The death effect was the
  tall flame that burns on trees, three times the size of the explosion it was
  meant to be. The hit announcement that came with it is not a fault: the
  original announces hits on infantry too.

### Under the hood

- **The .CWP building tables are read completely.** The fourth of them — the
  cell animations — was the last unread block, and both its fields of the type
  record now have a meaning.

- **Every reading is held against the Python reference, byte for byte.** The
  self-test compares 410 type rows, 255,660 pattern cells and 207 animation rows
  between the original files, this engine and the exported content, and the
  shipped binary is tested, not just the development tree.

- **The map baker's test knows what the baker deliberately leaves out.** Since
  buildings are drawn live, the baked picture differs from the 1997 reference
  exactly where a building stands; the test now exempts those pixels and prints
  how many, so the exemption cannot hide anything.

- New harness switches for measuring instead of looking: `--anim-check`,
  `--inf-anim-check`, `--door-check`, `--ruin-check`, `--corpse-check`,
  `--group-check`, `--save-check`, `--selftest-bake=`, and
  `--reexport-buildings=`, which rewrites the tileset files without re-baking a
  single map.

### Notes

- Content imported before this version has no passability block and no door
  field. The game says so when it loads such a map, and the count is printed
  rather than hidden. Re-import, or run `--reexport-states=<source>`, which
  rewrites only the game state and leaves the pictures alone. Campaign levels 16
  to 33 live on the second disc, so that run needs both discs.

- The installer ships **only the engine**. Terrain, units, maps and tables are
  derived on your own machine from your own two CDs on first start.

### Coming in 0.5.0

- **The campaign mission scripts.** This is the big one: alliances that change
  during a mission, reinforcements that arrive, objectives that trigger. The
  command bus the game runs all its orders through has been located — roughly
  295 opcodes — which is the groundwork.
- **Walking vehicles.** Mechs and spiders glide; the source of the walk phase is
  still open.
- **The computer opponent.** It does not move its infantry, and it builds
  transports where it should build an army.
- **The last factory doors.** Two of the door pictures come from a table that is
  filled at run time and was empty in every memory image taken so far.
- **"Radar setzen".** A unit carries a stock of radars and can drop one; it
  reveals ten cells for everybody allied with its owner. Read completely out of
  the original, not built yet.

## 0.3.3 — 2026-08-03

0.3.2 put the turret on the deck with a number of our own, measured from the
art, and said so. The game's own table has been found since, and it replaces it.

### Fixed

- **The turret mount is the original's now, not ours.** The ordinary unit is
  drawn by case 0 of the draw list's switch, and after the hull goes down the
  code moves the pen before the turret:

      x += (t[chassis][0].x + t[chassis][k].x) / 2
      y += (t[chassis][0].y + t[chassis][k].y) / 2

  `t` is a table of **22 chassis × 5 (x, y) pairs** in GAME.EXE, and **k is the
  FLAG byte of the tile the unit stands on** — the fourth byte of the map
  record — with anything above 4 counting as 0. So a unit on flat ground takes
  entry 0 and a unit on a slope is moved with the ground it stands on: 112,509
  of the 1,282,512 tiles across the 44 maps carry such a flag, about one in
  eleven. The table is read out of **your** executable, located by its own
  shape, and the September 1997 disc yields the same numbers as the January
  1998 build.

- **Ships have their own rule and now get it.** Their case of the switch — the
  one whose error path prints *"Wrong chassis of ship"* — puts the turret at
  `x + 0x10`, `y + mount − 0x0c`, with a mount per hull (70 → 15, 71 → 2,
  72 → 12) and no slope in it. Checked by measurement: the shift lands the
  turret's middle within a few pixels of the hull's (41.5 against 41.0 for the
  first, 39.5 against 42.0 for the second), where stacking on the shared anchor
  left it 15 to 20 pixels behind the ship. Hulls 73, 100 and 101 fall into that
  error path in the original itself, so there is no number to copy and they get
  none.

- **⚠ Withdrawn:** the note in 0.3.2 that "the game offsets the turret by +0x10
  in x and mount − 0x0c in y" was the SHIP rule, quoted as if it were the tank's.
  That is why it did not fit. The 45 % of hull height that 0.3.2 shipped is gone.

### Known, and left as it is

A turret on a slope is drawn from a tilted set of frames in the original
(five blocks: 0, 16, 32, 8, 24). The importer writes block 0 only, so a unit on
a slope stands in the right place with the flat turret.

## 0.3.2 — 2026-08-02

Two more things the play test of 0.3.1 turned up: there was no fog of war, and
the turret lay in the tracks instead of on the hull.

### Added

- **Fog of war**, built on the original's own table. The game keeps a circle of
  visibility per sight value at `0x4f8a48` — 20 rows, one per radius up to 19,
  each row the half-width of the circle per scanline. That table is exported and
  stamped: what no unit of yours has ever seen stays black, what you have seen
  but nobody is watching stays dim, what a unit watches right now is clear.
  Every unit's own sight (entity `+0x2c`) is used, the grid is restamped five
  times a second, and enemy vehicles you are not watching are not drawn.
  Checked against the table: radius 6 covers 121 cells, and the engine reports
  121. Switchable in the settings and with **J** in game.

### Fixed

- **The turret sat down in the tracks.** Hull and weapon were stacked on the
  same anchor — that is what this project and its Python tooling had always
  done, and the note that called it "visually correct" had never checked. The
  turret is now set down on the hull's deck, and the composed pictures are built
  the same way, so a standing unit and a moving one look alike.

  ⚠ **The mount point is ours, not the original's.** The game has a rule and we
  have only half of it: the vehicle draw offsets the turret by `x + 0x10`,
  `y + mount − 0x0c` with a mount per chassis (70 → 15, 71 → 2, 72 → 12) and
  gives a weapon component ≤ `0x14` no turret at all — but that branch does not
  fit the frames this importer writes, and the ordinary unit takes another case
  of the draw's switch on entity `+0x47` whose frame source is not settled. So
  the mount is measured from the art instead: the turret's bottom centre goes on
  the middle of the hull's outline at **45 %** of its height, a factor picked by
  laying 25/35/45/55 % side by side over the six commonest chassis. When the
  real rule turns up, this goes.

## 0.3.1 — 2026-08-02

A hotfix for two things a play test of 0.3.0 turned up.

### Fixed

- **Every unit and building was drawn nearly six rows too far north.** The
  campaign's first tank stood in a lake. The map's draw origin was read from a
  field the *Python* baker writes; the importer that runs on your machine writes
  a different one, so the origin silently fell back to zero — 115 pixels on the
  first campaign map. It is taken from the tiles themselves now, which know
  where they were drawn: checked over all 44 maps, constant per map and always
  the same value, no exception.
- **The skirmish looked missing.** The added *Gefecht* entry had been hung on
  the end of the start menu, below *Beenden*, where nobody looks. It sits under
  *Netzwerkspiel* now, where one looks for a game against an opponent; the
  original's own order and spacing are unchanged.
- The skirmish setup lost its title and its hint line on a 720p window. It
  scrolls now.

## 0.3.0 — 2026-08-02

The release that gave the game its voice, and its own start screen.

### Sound

The whole sound bank of the original is readable, and the game plays from it.

- **`SOUNDS.CWN` decoded** — 79,573,697 bytes: a count of 2000, then 2000
  entries of `{offset, length}`, samples from `0x3e84`. 492 slots are filled and
  they tile the file with **no gap and no overlap**, ending exactly on the file
  size. Format read off the game's own `WAVEFORMATEX`: **22050 Hz, 8 bit, mono**.
- A **negative length is a flag**, not damage: the original skips those when it
  preloads. That splits the bank into **202 effects** (3 minutes, held in
  memory) and **290 speech samples** (57 minutes, read when needed). The remake
  keeps the same split.
- **Every weapon has its own report.** A component names a sound class, the
  class picks a row of the game's fire-sound table, and the original plays that
  number *or the next, at random* — so two shots never sound quite the same.
- **The missions are read aloud.** Sounds 501–533 are the spoken briefings, one
  per campaign mission — established by holding each sound's length against the
  text of its briefing: **r = 0.984 over all 33**, at 17–23 characters a second.
- **The units answer** when you pick them, from the band the game's options
  screen calls *Meldungen*, keyed by chassis with three variants each — and they
  report when they are hit, rate-limited by the same clock the original uses.
- Explosions, research, refusals, building work and the briefing screen's own
  two sounds, each traced to the call site it belongs to.
- **Music** plays the way the original plays it: `0.MID`–`5.MID` through
  Windows MCI. Windows only, and the options screen says so.
- `--sound-probe` lists all 492 with their length and what is known about them,
  click to play.

### The start menu of 1997

Rebuilt from the code that draws and hit-tests it, not from a screenshot: nine
entries of 160 × 20 pixels at the original's own offsets, its own captions
(*Neues Spiel · Spiel laden · Netzwerkspiel · Einstellungen · Enzyklopaedie ·
Intro ansehen · Credits · Naechstes Demo · Beenden*) and its own help line under
the pointer. One entry is added and says so: **Gefecht**, which the original had
no equivalent for.

### The briefing screen

- The **radar monitor** is filled: `MAP.DAT` holds 33 groups of ten 202 × 202
  pictures, one group per mission, and the ten frames close a targeting reticle
  on the mission's own place in Europe. Checked against the briefings — mission
  5 sits on the Canaries, 13 on the Pyrenees, 25 on Belgium, 26 in the north.
- The mission is read aloud while you read it.

### Skirmish

- **The start slots are measured and stated** before you begin. Of the eight NET
  maps only three give any slot a building — NET01 four slots with one each,
  NET06 one with four, NET07 three with 1, 6 and 9. The setup screen says how
  many and how lopsided.
- On the five maps where **no** slot owns a structure, the armies the map was
  drawn with now stay. Taking them away there left the player with nothing,
  which is why a skirmish on NET02 used to end in defeat the same second.

### Options

The original's own options screen turned out to be recoverable, so ours now
follows its wording: sound, volume, **MIDI-Musik an/aus**, music volume, and the
two speech switches under the names the game gives them. Where a switch cannot
do anything yet, the screen says which and why.

### Fixed

- **A CD install would have been silent.** The sound reader only looked for
  loose files, which is how an *installation* keeps them — on the discs
  `SOUNDS.CWN`, `MAP.DAT` and the music are all inside `DATA1.CAB`. They are
  unpacked from there now, byte for byte identical to the loose files.
- **`--skirmish=<map>` could start a different map.** A name that was not in the
  picker left the selection on the first entry and started *that* one, silently.
- The game's typeface was exported with its second colour slot forced to black,
  which swallowed letters on a dark background. It is a shading colour, three
  quarters of the text colour, and is exported that way now.
- README screenshots had been ending up inside the shipped `.pck`. The package
  is 73 KB now and holds nothing but this project's own code and art.
- Importing can take several source folders (`--import=a;b`), because an
  installation does not always carry everything.

### Verified

Every reader is checked against the Python tooling that produced the content
before it, not by eye: **sounds 492/492 byte for byte · interface 47/47 ·
unit pictures 4147/0/0 · designs 601/601 · briefings 33/33 · maps 26/26**. The
release was built and then run from both original discs: 44 maps, 44 saved
states, 4307 unit pictures, 492 sounds, 6 pieces of music, 33 missions.

## 0.2.0 — 2026-08-01

- Designs cost and perform what the game's own tables say — price, hit points,
  attack, defence, range, sight, reload, speed, all read out of the executable.
- Mission briefings on the original's own screen (`BRIEFG.DAT`, 640 × 480).
- Overview map with alarms; order queue that takes queued attacks.
- The complete transport graph — all 386 rail lines join two named buildings.
- Buildings use their real footprints, read out of the map's spatial grid.
- A skirmish starts from the base alone; right-drag pans; map preview and an
  options screen in the setup.

## 0.1.0 — 2026-07-31

First Windows build: the engine alone, deriving terrain, units, maps and tables
from the player's own copy on first start.
