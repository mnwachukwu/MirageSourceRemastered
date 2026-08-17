# Mirage Source Remastered — C# Rewrite

[![Build, test and release](https://github.com/mnwachukwu/MirageSourceRemastered/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/mnwachukwu/MirageSourceRemastered/actions/workflows/ci.yml)
[![Latest release](https://img.shields.io/github/v/release/mnwachukwu/MirageSourceRemastered?display_name=tag&color=9aa8f5)](https://github.com/mnwachukwu/MirageSourceRemastered/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/mnwachukwu/MirageSourceRemastered/total?color=9aa8f5)](https://github.com/mnwachukwu/MirageSourceRemastered/releases)

![.NET 10](https://img.shields.io/badge/.NET-10-9aa8f5)
![Windows](https://img.shields.io/badge/Windows-x64-9aa8f5?logo=windows&logoColor=white)
![Linux](https://img.shields.io/badge/Linux-x64-9aa8f5?logo=linux&logoColor=white)
![macOS](https://img.shields.io/badge/macOS-x64-9aa8f5?logo=apple&logoColor=white)

**[Overview](#overview)** · **[Project structure](#project-structure)** · **[Getting started](#getting-started)** · **[Documentation](#documentation)**

## Overview

This is a C# reimplementation (a remastering, if you will) of [Mirage Online v3.0.3](https://github.com/mnwachukwu/mirage-source-v3.0.3) — whose [original site](https://miragesource.net/) is still standing — a 2D tile-based MMORPG engine originally written in Visual Basic 6. The original's mechanics, formulas, and systems are the foundation, but combat, progression, and the economy have been substantially reworked and rebalanced (see [Changes from the VB6 original](docs/changes-from-vb6.md)) — so this is better described as *inspired by* Mirage Online than a faithful reproduction of it. It's a handwritten .NET 10 codebase built on [MonoGame](https://monogame.net/), [Avalonia](https://avaloniaui.net/), and [Serilog](https://serilog.net/) — no VB6 runtime, no transpilation, no auto-conversion tools. The client's game logic carries no MonoGame dependency, so another shell such as [Godot](https://godotengine.org/) could consume `Mirage.Client.Core` unchanged; MonoGame is the shell shipped here.

I don't know why I did this.

---

## Project Structure

| VB6 | C# |
|---|---|
| `server/` | `Mirage.Shared` — shared protocol types and records |
| | `Mirage.Server.Core` — game logic (no transport dependency) |
| | `Mirage.Server.Host` — TCP, DI, entry point |
| `client/` (includes editor forms) | `Mirage.Client.Core` — game state and logic |
| | `Mirage.Client.Shell` — MonoGame rendering and input |
| | `Mirage.Editor` — standalone Avalonia editor |

`Mirage.Shared` is referenced by all three solutions, replacing VB6's duplicated `modTypes.bas` definitions and the server/client divergence they caused.

On disk, that is three top-level folders — `server/`, `client/`, `editor/` — each holding its own `src/` and a satellite `.slnx`, with the root `Mirage.slnx` tying all twenty-one projects together.

Some things people expect to find here live **outside** this repository, because they write into it
rather than build with it: the content generators that produced the seed, the scripts that draw the
app icons and control-scheme images, and the converter that imports an old VB6 world. Those are
published separately — see [Authoring tools](#authoring-tools) below.

The standalone balance simulators are not published. They answer "what would this feel like" against
the shipped formulas, nothing here builds or ships them, and their output is a judgment call that
already lives in the numbers.

Deliberately described rather than enumerated: a previous version of this section named three simulators
by hand and was wrong about all of it within a few months.

---

## Getting Started

**Prerequisite:** [.NET 10 SDK](https://dotnet.microsoft.com/download) (10.x or later)

```sh
git clone https://github.com/mnwachukwu/MirageSourceRemastered.git
cd MirageSourceRemastered
dotnet tool restore
dotnet tool restore --tool-manifest client/.config/dotnet-tools.json
```

There are two tool manifests, and the second command is not redundant: the root one declares `vpk`
(Velopack, used by the publish targets) and `ilspycmd`, while `client/.config/dotnet-tools.json`
declares `mgcb` (the MonoGame content builder). The client manifest sets `"isRoot": true`, which stops
the upward search, so a plain `dotnet tool restore` at the root never reaches it.

Run the server, then the client (and optionally the editor) in separate terminals:
```sh
dotnet run --project server/src/Mirage.Server.Host
dotnet run --project client/src/Mirage.Client.Shell
dotnet run --project editor/src/Mirage.Editor
```

> **Importing VB6 world data:** [MirageSourceRemasteredConverter](https://github.com/mnwachukwu/MirageSourceRemastered.Tools.Public) turns an original VB6 server directory into this JSON format in one pass — all binary `.dat` maps and INI data files, with account passwords hashed on the way through and the source files never modified, so a run costs nothing if the result is not what you wanted. See [Authoring tools](#authoring-tools).

> **Seed data:** `server/src/Mirage.Server.Host/data/` is the shipped default configuration — 10 classes, 558 items, 270 spells, 174 NPCs, 35 conversations, 54 quests and 21 shops. The folder is **not** copied to the build output, so to start from it, copy `data/` next to the server executable before first run (or point the `DataDir` setting at one). Any collection you leave out is created empty and written on first save, so a partial `data/` folder boots fine.
>
> Those counts are checked against the folder by `.github/checks/check-seed-counts.mjs`, which CI runs — they have gone stale twice.
>
> **There are no maps in it, and that is the important caveat.** The seed is a content *library*, not a world: it defines what exists — the items, the bestiary, the townsfolk, the shops they keep and the quests they give — but nothing places any of it on a tile. Start a server against this `data/` and you get the 174 NPCs as definitions and a set of blank maps with none of them standing anywhere. Placing them is map authoring, which is what the editor is for.
>
> **The seed is TEST data, not a game.** It was built to exercise the engine at three specific bands — **levels 1–20, 100–120, and 235–255** — and there is deliberately *nothing in between*. Levels 21–99 and 121–234 have no mobs, no gear and no spells at all: a character leveling normally runs out of world twice. The three bands exist so combat, gearing and party scaling could be measured at the bottom, middle and top of the curve without authoring 255 levels of content to get there.
>
> It is included as a courtesy — enough to start a server and see the systems work, and a worked example of what the record formats look like — but it is not a playable game and was never intended as one. It is regular enough to look machine-written because it is: the generators that wrote it are published, so the seed can be regenerated, retuned, or replaced wholesale rather than treated as fixed. See [Authoring tools](#authoring-tools). Placing any of it on a map is still the editor's job.

---

## Authoring tools

The seed world in `data/` was not hand-authored. It was generated, and the generators that wrote it are
published: **[MirageSourceRemastered.Tools.Public](https://github.com/mnwachukwu/MirageSourceRemastered.Tools.Public)**.

They are there because a seed you cannot regenerate is a seed you can only edit. With them you can retune
the whole economy, rescale the bestiary, or throw the shipped content away and generate your own to the
same shape.

| | |
|---|---|
| **`ContentGenerators/`** | The ten that wrote the seed — spellbook, armory, bestiary, classes, conversations, quests, shops. `run-all.cs` runs them in the order they depend on each other and stops at the first failure. |
| **`ArtGenerators/`** | The app icons and the in-game control-scheme reference images, drawn as geometry rather than exported from a design file. |
| **`MirageSourceRemasteredConverter/`** | Imports an original VB6 Mirage Online server directory into this JSON format — binary `.dat` maps and INI data alike, with account passwords hashed on the way through. The source directory is only ever read. |

Two things worth knowing before running any of them:

- **They compute with this engine's own formulas.** Each one takes a project reference on
  `Mirage.Shared`, so item prices come from `EconomyFormulas`, NPC health from the same
  `GetNpcMaxHp` the server uses, and experience from `ExpFormulas`. A generator cannot drift from the
  engine, because it has no second copy of the rule to drift from. That is also why the tools repository
  expects to sit beside this one — the reference is a relative path.
- **They own their collections outright.** A generator clears its collection before writing, so hand
  edits to `data/items/` are lost the next time the armory generator runs. Author in the editor, or
  author in the generator — not both.

The pipeline reproduces the committed seed byte-identically, which makes `git status` on `data/` after a
run a real check that nothing has drifted.

Not published: the standalone balance simulators. They exist to answer design questions, their output is
already baked into the numbers the generators use, and nothing here builds them.

---

## Known limitation: the client has no name until a server gives it one

The client ships branded **Mirage Source Remastered** — the engine's name. It has no game identity of its
own, because one client is meant to reach every server. On connect, before you log in, the server tells it
the game's name, and the window title, the menu and the HUD show that from then on.

So launching "Mirage Source Remastered" and arriving in "Brightwater" is expected. It is a handshake, not
a rebrand and not a bait and switch: the engine cannot know what to call itself until a server says.

Two things deliberately do **not** follow the server's name:

- **Your settings folder.** It stays under the engine name, so joining a differently-named game never
  moves your configuration or loses your options.
- **The executables.** Server and client filenames are fixed, which is what lets the management window
  find the server it ships beside.

Operators set their game's name in the server window under **Configuration → This server → Game name**,
or as `gameName` in `serverconfig.json`. Leaving it empty keeps the engine's name.

If you want a client that carries your own name and icon from the moment it launches, that is a rebuild
rather than a setting — see [Icons and shipping your own client](docs/branding.md).

---

## Documentation

This file covers what the project is and how to get it running. Everything else lives in
[`docs/`](docs/), one file per subject:

| Document | What it answers |
|---|---|
| [Building, publishing and releasing](docs/building.md) | How a working tree becomes installers, what the version number is bound to, how a tag cuts a release, and which platforms the output runs on |
| [Icons and shipping your own client](docs/branding.md) | Rebranding a fork: the four icon locations, the MonoGame window-icon trap, and repackaging a client without a compiler |
| [Testing](docs/testing.md) | What the four suites cover, how to run one on its own, and why the cross-platform matrix exists |
| [Technical decisions](docs/architecture.md) | Choices that are not obvious from the code, recorded with the reasoning that produced them |
| [Game data conventions](docs/game-data.md) | Rules the authored content is expected to follow, including music loop points |
| [Changes from the VB6 original](docs/changes-from-vb6.md) | Additions, rebalances, bug fixes carried across, and the two features excluded by design |
