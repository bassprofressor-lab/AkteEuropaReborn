# Akte Europa Reborn

A reimplementation of the 1997 real-time strategy game **Akte Europa** in
Godot 4 and C#, by **chr1zZo**.

**Website: [openreborn.com](https://openreborn.com/de/)**

*[Deutsche Fassung](README.de.md)* · *[Changelog](CHANGELOG.md)*

The project ships **no data from the original game whatsoever**. It reads the two
original CDs — which you need to own — and derives everything the game needs on
your own machine: maps, unit graphics, fonts, tables, mission briefings. The
shipped executable carries nothing from 1997; the bundled `.pck` is about 200 KB.

---

![Mission 05 "Production"](docs/screenshots/04-mission.png)

Mission briefings appear on the original's own screen — the picture comes out of
`BRIEFG.DAT`, the text out of `BRIEFG.TXT`, and the typeface is the game's own:

![Mission briefing](docs/screenshots/01-briefing.png)

Maps are baked at full size, up to 10160 × 5285 pixels. "River Combat" with 321
units, zoomed all the way out:

![Map "River Combat"](docs/screenshots/03-uebersicht.png)

A base with the side panel and the overview map:

![Base](docs/screenshots/02-basis.png)

---

## Download

Grab the installer from the [releases page](../../releases/latest), run it, and
point it at your CDs on first start.

## Status

Skirmishes and the 33-mission campaign are playable. From the two discs, the
first launch derives:

| | |
|---|---|
| Maps | 44, baked up to 10160 × 5285 pixels |
| Game states | 44 (units, buildings, objectives, deposits, rail lines …) |
| Unit pictures | 4307, composed out of ROBO.CWR |
| Tables | 12, from the map files and GAME.EXE |
| Interface | original typeface, side panel, effects, mission briefings |

The simulation runs on the original's own numbers: prices, hit points, attack
and defence, range, reload time, speed, fuel and ammunition all come from the
game's records rather than from guessed constants. Where something could not be
recovered, the code says so — every assumption of our own is marked as such.

## Coming Soon

- Full Original Campaign
- Reborn Campaign
- Multiplayer
- Better Skirmish Mode
- Ger/Eng Language Support
- UI/UX Improvements
- Reborn Graphics/Sprites Mode (You can switch between original and reborn)
- Website + WIKI
- Discord

## Requirements

* The two original *Akte Europa* CDs (in the drive or as folders)
* Windows; builds for other systems are not set up yet

## Installation

Run the installer from the [releases page](../../releases/latest). On first
start the program asks for the discs or an installation folder and derives the
content from them. This takes a few minutes; the derived files go to your user
profile and the program directory stays untouched.

## Building it yourself

You need Godot 4.7 (Mono/.NET) and the .NET SDK.

```bash
dotnet build "Akte Europa Reborn.csproj"
godot --path . --headless --export-release "Windows Desktop"
```

Importing content without the interface:

```bash
godot --path . --headless -- --import-cd          # discs in the drive
godot --path . --headless -- --import=<folder>    # or from an installation
```

## Self tests

The readers for the original formats are not merely built — they are checked
against the data they read:

```bash
godot --path . --headless -- --selftest-cwp=<toolsdir>
godot --path . --headless -- --selftest-designs
godot --path . --headless -- --selftest-briefings
```

Among the things verified: 13 sprite frames pixel-identical against a second
implementation, 26 map files consumed with zero bytes left over, 4147 unit
pictures identical, 601 design records reproduced exactly, 33 mission texts
character for character.

## How this project works

Every mechanic is reconstructed from the original binary before it is
implemented — even where inventing something would be quicker. A reading only
counts once there is a number behind it: counted across every map, held against
a second implementation, measured in the running engine. What comes from the
data and what comes from us is kept apart and named in the code. Misreadings get
withdrawn rather than overwritten.

The file formats were opened up one at a time along the way: CWP (tiles and
objects), CWM (maps with over 130 sections), CWR (unit sprites), CWD (fonts),
CWA (effects), the CD's InstallShield cabinet, and the tables inside GAME.EXE.

## Legal

Copyright © 2026 **chr1zZo**. The code is licensed under the **GPL-3.0**, see
[`LICENSE`](LICENSE).

*Akte Europa* and all related content belong to their respective rights holders.
This project is neither endorsed by nor affiliated with them, and distributes no
original data. You need the original CDs to play.
