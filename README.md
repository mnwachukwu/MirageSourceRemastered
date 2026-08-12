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

On disk, that is three top-level folders — `server/`, `client/`, `editor/` — each holding its own `src/` and a satellite `.slnx`, with the root `Mirage.slnx` tying all fifteen projects together.

Two things people expect to find here live **outside** this repository, in a sibling folder checked out beside it:

| Where | What | Why not here |
|---|---|---|
| `../MirageSourceRemastered.Tools/Simulations/` | `CombatSim`, `OscSim`, `KiteBiasSim` — standalone balance simulators | No dependency on the engine; nothing here builds or ships them |
| `../MirageSourceRemastered.Tools/vb6-to-cs-converter/` | The VB6 → JSON migration tool | Referenced only when importing an old world; it reaches back to `Mirage.Shared` by relative path, so the two folders must stay siblings |

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

> **Importing VB6 world data:** if you have an existing VB6 server directory, run the standalone converter before starting the server. It lives **outside this repository**, in the sibling `MirageSourceRemastered.Tools/vb6-to-cs-converter/` folder:
> `dotnet run --project ../MirageSourceRemastered.Tools/vb6-to-cs-converter/src/Mirage.Vb6Converter -- --migrate <vb6-server-path> [<data-output-path>]`
> It converts all binary `.dat` maps and INI data files to JSON in one pass (account passwords are hashed during conversion). Source files are never modified.

> **Seed data:** `server/src/Mirage.Server.Host/data/` is the shipped default configuration. It is currently **empty** — a fresh checkout boots an empty world, and the server initializes each collection and writes it out on first save. The folder is **not** copied to the build output, so to start from pre-populated content, copy a `data/` directory next to the server executable before first run (or point the `DataDir` setting at one).

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
| [Changes from the VB6 original](docs/changes-from-vb6.md) | Additions, rebalances, bug fixes carried across, and the gaps that remain |

Design notes and balance working papers are deliberately **not** in this repository — they live in a
sibling `_mirage-reference/` folder, because they describe intent rather than the shipped engine and
go stale in ways code review would not catch.
