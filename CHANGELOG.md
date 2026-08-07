# Changelog

All notable changes to Akte Europa Reborn. The build ships **only the engine** —
terrain, units, maps, tables and now sound are derived on your own machine from
your own copy of the 1997 game.

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
