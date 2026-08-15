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

On disk, that is three top-level folders — `server/`, `client/`, `editor/` — each holding its own `src/` and a satellite `.slnx`, with the root `Mirage.slnx` tying all eighteen projects together.

Two things people expect to find here live **outside** this repository, in a sibling `MirageSourceRemastered.Tools` folder:

| What | Why not here |
|---|---|
| Standalone balance simulators — they answer "what would this feel like" against the shipped formulas | No dependency on the engine; nothing here builds or ships them |
| The content generators that produced the seed — the armory, bestiary, spellbook, classes, conversations, quests and shops each have one | They write `data/` and are never referenced by it; the world is the artifact, not the script |
| The VB6 → JSON migration tool | Referenced only when importing an old world; it reaches back to `Mirage.Shared` by a relative path, so the two folders must stay siblings |

> ### ⚠️ That repository is **not currently published**
>
> It is a private authoring toolchain. Everything it produces — the world in `data/`, the app icons,
> the control-scheme images — is committed **here**, so nothing in this repository depends on having
> it, and nothing you can do with this repository requires it. But the paths above are not links, and
> a `../MirageSourceRemastered.Tools` in an instruction anywhere in these docs is describing how
> something was made, not telling you to go and run it.
>
> It may open up later. Until it does, the honest summary is: **you get the artifacts, not the
> factory.**

Deliberately described rather than enumerated: the previous version of this table named three simulators
by hand and was wrong about all of it within a few months.

---

## Getting Started

**Prerequisite:** [.NET 10 SDK](https://dotnet.microsoft.com/download) (10.x or later)

```sh
git clone <repo-url>
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

> **Importing VB6 world data:** a converter exists that turns an original VB6 server directory into this JSON format in one pass — all binary `.dat` maps and INI data files, with account passwords hashed on the way through and the source files never modified. It lives in the **unpublished** tools repository described above, so it is not something you can currently run. Writing your own is tractable — the target format is plain JSON, `server/src/Mirage.Shared/Records/` defines every record with its fields documented, and `server/src/Mirage.Server.Host/data/` is 1,122 worked examples. If you have a VB6 world you want migrated, open an issue.

> **Seed data:** `server/src/Mirage.Server.Host/data/` is the shipped default configuration — 10 classes, 558 items, 270 spells, 174 NPCs, 35 conversations, 54 quests and 21 shops. The folder is **not** copied to the build output, so to start from it, copy `data/` next to the server executable before first run (or point the `DataDir` setting at one). Any collection you leave out is created empty and written on first save, so a partial `data/` folder boots fine.
>
> Those counts are checked against the folder by `.github/checks/check-seed-counts.mjs`, which CI runs — they have gone stale twice.
>
> **There are no maps in it, and that is the important caveat.** The seed is a content *library*, not a world: it defines what exists — the items, the bestiary, the townsfolk, the shops they keep and the quests they give — but nothing places any of it on a tile. Start a server against this `data/` and you get the 174 NPCs as definitions and a set of blank maps with none of them standing anywhere. Placing them is map authoring, which is what the editor is for.
>
> **The seed is TEST data, not a game.** It was built to exercise the engine at three specific bands — **levels 1–20, 100–120, and 235–255** — and there is deliberately *nothing in between*. Levels 21–99 and 121–234 have no mobs, no gear and no spells at all: a character leveling normally runs out of world twice. The three bands exist so combat, gearing and party scaling could be measured at the bottom, middle and top of the curve without authoring 255 levels of content to get there.
>
> It is included as a courtesy — enough to start a server and see the systems work, and a worked example of what the record formats look like — but it is not a playable game and was never intended as one. Building an actual world means authoring your own content in the editor. It is regular enough to look machine-written because it is, but the generators that wrote it are part of the unpublished toolchain above, so the editor is the path.

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
