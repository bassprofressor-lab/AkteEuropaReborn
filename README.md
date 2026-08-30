<div align="center">

# Akte Europa Reborn

**The 1997 real-time strategy game *Akte Europa*, rebuilt in Godot 4 and C#.**

[![Release](https://img.shields.io/github/v/release/bassprofressor-lab/AkteEuropaReborn?label=download&style=for-the-badge&color=2b7)](../../releases/latest)
[![Discord](https://img.shields.io/badge/Discord-join%20us-5865F2?style=for-the-badge&logo=discord&logoColor=white)](https://discord.gg/MVMfRWrMKv)
[![Website](https://img.shields.io/badge/openreborn.com-visit-orange?style=for-the-badge)](https://openreborn.com/de/)
[![License](https://img.shields.io/badge/license-GPL--3.0-blue?style=for-the-badge)](LICENSE)

*[Deutsche Fassung](README.de.md)* · *[Changelog](CHANGELOG.md)* · *[Open questions](OFFENE_FRAGEN.md)*

</div>

---

> **No original data is shipped — not one byte.** The installer contains the
> engine and nothing else. On first start it reads the two original CDs, which
> you have to own, and derives everything on your own machine: maps, unit
> sprites, fonts, tables, briefings, sound. The bundled `.pck` is about 500 KB.

![A landing operation in mission 5](docs/screenshots/04-mission.png)

---

## Download

**[→ Get the installer from the releases page](../../releases/latest)**

Run it, point it at your CDs on first start, and wait a few minutes while the
content is derived. The derived files go into your user profile; the program
directory stays untouched.

## What it looks like

Mission briefings run on the original's own screen — the picture comes out of
`BRIEFG.DAT`, the text out of `BRIEFG.TXT`, and the typeface is the game's own.
The intro films play before it, decoded from the original's own video format.

![Mission briefing](docs/screenshots/01-briefing.png)

Maps are baked at full size, up to 10160 × 5285 pixels. Here "The Dam" zoomed
out, with rail lines, power poles and the dam itself:

![Map overview](docs/screenshots/03-uebersicht.png)

A base with the side panel, the resource bar and the overview map:

![Base](docs/screenshots/02-basis.png)

## Status

The **33-mission campaign** and skirmish games are playable. From the two
discs, the first launch derives:

| | |
|---|---|
| Maps | 44, baked up to 10160 × 5285 pixels |
| Game states | 44 (units, buildings, objectives, deposits, rail lines …) |
| Unit pictures | 4307, composed out of `ROBO.CWR` |
| Tables | 12, from the map files and `GAME.EXE` |
| Films | 35, decoded from the original's own container |
| Interface | original typeface, side panel, effects, mission briefings |

The simulation runs on the original's own numbers: prices, hit points, attack
and defence, range, reload time, speed, fuel and ammunition all come from the
game's records rather than from guessed constants. Where something could not be
recovered, the code says so — every assumption of our own is marked as such.

## Road to 0.7.0

**0.7.0 ships when every one of the 33 campaign missions has been played
through individually and found clean.** Not before.

The reason is a lesson this project paid dearly for: **a single mission played
attentively found more defects than a hundred automated harnesses combined.**
Mission 1 alone produced over twenty findings in one evening — from faction
colours through missing debris and explosions to units sinking into the ground.
A harness measures what you asked it to measure; a person at the screen also
sees what nobody thought to ask about.

### How it works

For each mission, in order:

1. **Play it**, with an expectation sheet next to you, generated from the
   extracted mission script — which help windows should appear, which
   sub-missions exist, what the victory condition actually requires.
2. **Report every discrepancy**, however small: a sound in the wrong place, a
   tile that looks wrong under fog, an enemy that never comes.
3. **Read the original, do not guess.** Every fix carries the address in
   `GAME.EXE` it came from — and wherever we had to decide something ourselves,
   the code says so in as many words.
4. **Measure, do not assert.** Every fix comes with a number and a null model,
   and every switch with a counter-test that restores the previous behaviour.
5. **Only then the next mission.**

### Where to watch the progress

Everything found and fixed goes into **[CHANGELOG.md](CHANGELOG.md)**; whatever
stays open or needs input goes into **[OFFENE_FRAGEN.md](OFFENE_FRAGEN.md)**
(German) — including the cases where an earlier explanation of ours turned out
to be wrong. Both are kept as we go, not written afterwards.

⭐ **Where we stand:** missions 1 and 2 have been played through several times;
mission 2 became completable for the first time on 2026-08-30. Those two
missions alone account for more than forty documented fixes — among them the
bridge railing under fog, the seven attackers that never arrived because the
pathfinder crashed, and a group move order that left a unit with no goal and no
retry.

**Contributions are explicitly welcome.** If you play a mission and something
feels different from the original, please say so on
**[Discord](https://discord.gg/MVMfRWrMKv)** — especially if it seems like a
small thing. That is exactly what progress hangs on.

## What is next

- Online multiplayer (LAN lockstep already runs)
- Map editor, finished
- A competitive skirmish mode
- English/German language switch
- A "Reborn" graphics mode you can toggle against the original sprites
- Wiki

Come talk about it on **[Discord](https://discord.gg/MVMfRWrMKv)**.

## Requirements

* The two original *Akte Europa* CDs (in the drive or as folders)
* Windows; builds for other systems are not set up yet

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

Beyond the readers, the *rules* are checked in the running engine — the
campaign against both original executables (33 missions, no difference), the
tick rate, the economy, the rail network, transports, groups, saves. Every check
prints numbers, and every one of them can go red.

## How this project works

Every mechanic is reconstructed from the original binary before it is
implemented — even where inventing something would be quicker. A reading only
counts once there is a number behind it: counted across every map, held against
a second implementation, measured in the running engine. What comes from the
data and what comes from us is kept apart and named in the code. Misreadings get
withdrawn rather than overwritten.

The file formats were opened up one at a time along the way: CWP (tiles and
objects), CWM (maps with over 130 sections), CWR (unit sprites), CWD (fonts),
CWA (effects), RPL (films), the CD's InstallShield cabinet, and the tables
inside `GAME.EXE`.

## Legal

Copyright © 2026 **chr1zZo**. The code is licensed under the **GPL-3.0**, see
[`LICENSE`](LICENSE).

*Akte Europa* and all related content belong to their respective rights holders.
This project is neither endorsed by nor affiliated with them, and distributes no
original data. You need the original CDs to play.
