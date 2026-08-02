# Changelog

All notable changes to Akte Europa Reborn. The build ships **only the engine** —
terrain, units, maps, tables and now sound are derived on your own machine from
your own copy of the 1997 game.

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
