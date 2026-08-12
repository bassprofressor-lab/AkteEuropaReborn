# Changelog

All notable changes to Akte Europa Reborn. The build ships **only the engine** —
terrain, units, maps, tables and now sound are derived on your own machine from
your own copy of the 1997 game.

*Auf Deutsch: [CHANGELOG.de.md](CHANGELOG.de.md).*

## 0.5.0 — 2026-08-12

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

### Known limits

- **The wagons of a line can stand on one cell.** Measured: in about 10 % of
  frames of a travelling line, and in the worst case all four sit on the same
  floating-point position, so you see one wagon instead of four. The cause is
  understood — the wagons share one lead position with an offset, and at a
  terminus they all clamp into the same limit. It is **deliberately not fixed**:
  the original gives each wagon its own counter and its own route pointer, and
  the routine that staggers their departure has not been read yet. Any repair
  before that would be an invention.
- **The trigger rule of campaign 2's sub-mission is not entered.** It needs an
  OR and the rule vocabulary only has AND. The sub-mission therefore does not
  start at all — a visible gap instead of a silent, wrong reward.
- **Mission 5 is complete on the script side but not yet winnable in play.**
  It needs a running production; the test rig supplies one.
- **Missions 21 and 28 have no script** — the only two of the 33.
- **Unit classes 1..4 are not told apart** in every place; where a rule needs
  them, the data file says so.
- **The sound panning is read and not built**, and the impact effect for direct
  fire is not read at all. An invented impact on every rifle shot would be
  worse than none.
- **The muzzle reach (14 px) is ours.** The right number is read — it sits in
  SHOOT.CWT, 2400 records of four points — but that file does not run through
  the import yet.

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
