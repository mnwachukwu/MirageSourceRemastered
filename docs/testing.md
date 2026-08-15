# Testing

What the suites cover, how to run one, and why the cross-platform matrix exists.

One **NUnit** test project per source portion, each sitting next to the code it exercises; the root `Mirage.slnx` groups them under a `/Tests/` solution folder.

| Test project | Exercises | Runs on |
|---|---|---|
| `Mirage.Server.Tests` | `Mirage.Server.Core` + `Mirage.Server.Host` + `Mirage.Shared` | all three platforms |
| `Mirage.Client.Core.Tests` | `Mirage.Client.Core` — the shell-agnostic core | all three platforms |
| `Mirage.Editor.Tests` | `Mirage.Editor` | all three platforms |
| `Mirage.Client.Shell.Tests` | `Mirage.Client.Shell` — input, panels, HUD | Windows only, see below |

Around 1,250 tests across the four. The count is left approximate on purpose — an exact one here is a
number nothing updates and everything eventually contradicts.

| Scope | Command |
|---|---|
| **Everything, one shot** | `dotnet msbuild Mirage.Test.csproj -t:TestAll` |
| Everything (built-in) | `dotnet test Mirage.slnx` |
| One area | `dotnet msbuild client/Mirage.Client.Test.csproj -t:TestAll` (or `server/Mirage.Server.Test.csproj` / `editor/Mirage.Editor.Test.csproj`) |
| One project | `dotnet test tests/Mirage.Client.Shell.Tests/Mirage.Client.Shell.Tests.csproj` |

**Where the suites live, and why the drivers exist.** All four sit under `tests/`, out of the
`src/` trees they exercise, and each is named for what it covers rather than for where its subject
happens to be. Each area then gets a driver — `Mirage.Server.Test.csproj`, `Mirage.Client.Test.csproj`,
`Mirage.Editor.Test.csproj` — mirroring the publish profiles exactly: publishing the server is
`Mirage.Server.Publish.csproj`, so testing it is `Mirage.Server.Test.csproj`, and neither asks you to
remember a path.

The area drivers deliberately do **not** run on a solution build; only the aggregate
`Mirage.Test.csproj` does. Both would mean every suite running twice — the same double-work that once
turned a 9-second solution build into a 127-second one when the publish projects defaulted to
publishing. `/t:TestAll` is always explicit for an area.

Inside each suite the sources are grouped into folders (`Combat/`, `Ai/`, `Formulas/`, …) so the
project root is the csproj and a handful of directories rather than seventy loose files. The folders
carry no namespace: everything stays in `Mirage.Server.Tests` and friends, because a folder is not a
namespace in C# and renaming them would be churn no reader benefits from.

`Mirage.Test.csproj` runs every suite, **keeps going after a failure** so one red suite doesn't hide the rest, then reports a per-suite breakdown. A bare `dotnet msbuild Mirage.Test.csproj` runs `TestAll` by default, as does a Visual Studio right-click → **Build**; uncheck it in Configuration Manager to keep it out of `Ctrl+Shift+B`.

> Building or testing while the game is running fails to copy the shared DLLs — close the running app first.

**Current coverage.** All four suites are real; the placeholder smoke tests are gone. Roughly 19,300 lines of tests across 137 files, weighted toward the server, where most of the rules live.

What they actually pin, by kind:

- **Formulas** — `StatFormulasTests`, `DeathFormulasTests`, `EconomyFormulasTests`, `GuildWarFormulasTests`, `SeasonFormulasTests`, `TerritoryFormulasTests`, `ItemFormulaTests`. The tuning constants are meant to be retuned, so these pin shape and invariants rather than magic numbers.
- **Parity** — `NpcPlayerFormulaParityTests` holds NPCs and players to the same damage and mitigation math, and `LocalizationParityTests` (present in three suites) holds the four language files to the same key set, so a translation cannot silently go missing.
- **Systems** — one suite per feature area: guilds, quests, mail, market, trade, party, bank, shops, social, weather, time of day, regeneration, conversations, objectives.
- **Seams and geometry** — `DeterminismSeamTests`, `LayerLogicTests`, `NpcChaseRoutingTests`, `NpcPathCacheTests`, `NpcFootprintCombatTests`, `WorldCoordHelperFootprintTests`. The cross-map and two-plane cases are where the engine is easiest to break by accident.
- **Determinism** — `PinnedClockTests` and `PinnedRandomnessTests` keep time and randomness injectable, which is what makes the rest of the suite reproducible.
- **Convention** — `LocalizationConventionTests` reads the editor's own sources to check that anything using localized strings has a refresh hook, and `GamePanelContractTests` / `PanelPolicyTests` hold every panel to one declared behavior table so a new panel cannot silently miss a rule.
- **Performance baselines** — `PerfBaselineTests` and `RenderPerfBaselineTests` catch regressions in the per-frame and per-tick hot paths.
- **Platform** — `UserPathsTests` pins where a per-user file belongs on each OS. It asserts the *current* platform's branch rather than mocking one, because there is nothing to mock: the code switches on `RuntimeInformation.IsOSPlatform`, which no test can lie about.

## Across platforms

CI runs three of the four suites on **Linux, macOS and Windows**; the fourth stays on Windows.

Cross-building for three platforms from one runner proves they *compile* and nothing more — it never
runs a line of code on the other two. That gap is not theoretical here:

- `UserPaths` branches three ways on the operating system. On any single machine, two of those
  branches are unreachable by any test.
- Linux is **case-sensitive**. Every path the persistence and localization loaders build is somewhere
  that difference could bite, and no Windows runner will ever tell you.

`Mirage.Client.Shell.Tests` is the exception, and stays on Windows: it is the only suite that pulls
in MonoGame, and building the client's content — five spritefonts and a shader — needs native tooling
off Windows that is not worth fighting for tests that are not platform-sensitive in the first place.
The other three touch no MonoGame at all, which is what makes the split cheap.

Windows appears in the matrix as well as in the full build job, so the three legs are directly
comparable. It costs a minute of duplicated work and buys a row per platform that is either green or
not, instead of one green row and two a reader has to reason about.

## The documentation is checked too

```sh
node .github/checks/check-doc-links.mjs
```

[`.github/checks/check-doc-links.mjs`](../.github/checks/check-doc-links.mjs) reads every Markdown file in the
repository and follows every link that points inside it — a path, an in-page anchor, or both. It runs
as its own CI job and gates a release alongside the suites.

**Node, in a C# repository, on purpose.** It imports nothing outside Node's standard library, so
there is no `package.json`, no install step and nothing to keep up to date — and every GitHub-hosted
runner ships Node, so the CI job needs no toolchain setup and costs seconds. The first version was a
.NET 10 file-based program, which read better beside the rest of the codebase but required
`setup-dotnet`: .NET 10 is not preinstalled on the runner images, and file-based programs are a .NET
10 feature, so whatever SDK an image carries cannot be assumed. Thirty seconds of setup to check a
few dozen links was the wrong trade for a check meant to be cheap enough that nobody resents it.

It exists because prose rots differently from code. A renamed file or a reorganized document breaks
links that nothing compiles and no test covers, and the person who moved the file is the least likely
to notice. Splitting this documentation out of the README broke four links and left one reference
pointing at a heading that had already stopped existing some time earlier — none of which any other
check here would ever have reported.

Two deliberate choices:

- **External links are not followed.** Reaching the network would make a documentation check depend
  on somebody else's uptime, and a check that fails at random gets ignored.
- **Spelling is compared against real directory entries**, not by asking whether the file exists.
  Windows and macOS answer that question case-insensitively, so `Docs/Building.md` resolves on the
  machine that wrote it and 404s on Linux and on github.com. Same lesson as `UserPaths` above,
  arriving from a different direction.
