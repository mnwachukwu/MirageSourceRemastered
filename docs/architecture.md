# Technical decisions

Choices that are not obvious from reading the code, recorded with the reasoning that
produced them.

| Area | VB6 | C# |
|---|---|---|
| Language / runtime | Visual Basic 6 | C# 14 / .NET 10 |
| Wire protocol | Binary, Chr(255) delimiters | Newline-delimited JSON over TLS (encrypt-only, ephemeral self-signed cert) |
| Data storage | Binary `.dat`, INI files | UTF-8 JSON |
| Rendering | DirectX COM (DirectDraw) | MonoGame 3.8 DesktopGL |
| Music | MIDI via `mciSendString` (Windows only) | OGG Vorbis, gapless streaming via NVorbis |
| Editor | Admin-gated forms inside the client exe | Standalone Avalonia 12 app |
| Concurrency | Single-threaded Winsock callbacks | Async TCP, per-player channel |
| Platform | Windows only | Windows, macOS, Linux |

## Why compiled content is committed

Build outputs do not belong in a source tree. This one is the exception, and it is worth stating why so
nobody "tidies it up" later — or copies the pattern somewhere it does not apply.

**The client's content pipeline cannot run anywhere but Windows, for two unrelated reasons.**

- The effect compiler is `SharpDX.D3DCompiler` plus `libmojoshader_64.dll`. The `mgcb` tool ships
  freetype, FreeImage, nvtt and PVRTexLib as `.so` and `.dylib`, but neither of those two. MonoGame's own
  answer off Windows is to run the compiler under **Wine**.
- Four of the five spritefonts are drawn from **Tahoma**, resolved out of `C:\WINDOWS\Fonts`. Tahoma ships
  with Windows and with nothing else, and it is proprietary, so it cannot be bundled beside the
  descriptions the way `FSEX300.ttf` is.

That leaves three options, and the cost of each is the argument:

| | Cost |
|---|---|
| Require Wine and a font install to build | Breaks "the .NET SDK and nothing else" on two of the three platforms it is claimed for, for a first-time contributor, on a clone |
| Replace Tahoma with a bundled free face | Changes the typeface of every string in the game — a design decision made to satisfy a build constraint |
| **Commit the compiled output** | ~263 KB of binaries in the tree, and a staleness risk |

The third is the one taken. It is 263 KB against a 15 MB spritesheet, every platform renders identically
because they all ship the same atlases, and the staleness risk — the only real objection — is the one
that can be *mechanically* removed: `check-prebuilt-content.mjs` hashes every content source against a
committed manifest and fails the build when they disagree.

The generalizable rule: **committing a build output is defensible when reproducing it requires something
the platform cannot have, and only when a check can prove the committed copy is current.** Without that
second half this would be an ordinary bad idea. See
[Building](building.md#compiled-content-is-committed) for the workflow.

Nothing else in the tree works this way — the seed data is generated but its generators run anywhere, and
`git status` after a regeneration is the same review this uses.

## Per-user file locations (why the map cache is under Local, not Roaming)

> Author's note-to-self: this is written down purely so future-me remembers *why* the split is the way it is — it's an easy detail to second-guess or "tidy up" into one folder later. Don't.

Client and editor writable paths sort into three kinds, each mapped to the OS-conventional location:

| Kind | Purpose | Windows | macOS | Linux (XDG) |
|---|---|---|---|---|
| Config | Settings that follow the user across machines | Roaming `%AppData%\<GameName>\` | `~/Library/Application Support/<GameName>` | `~/.config/<game-name>` |
| Data | Persistent, machine-local app data | Local `%LocalAppData%\<GameName>\` | `~/Library/Application Support/<GameName>` | `~/.local/share/<game-name>` |
| Cache | Regenerable data (the downloaded map cache) | Local `%LocalAppData%\<GameName>\` | `~/Library/Caches/<GameName>` | `~/.cache/<game-name>` |

The client uses Config (its `appsettings.json`) and Cache (the map cache); Data is mainly the editor's authored maps and items.

**So on Windows there are two `<GameName>` folders by design** — Roaming for config, Local for cache/data. The map cache goes to **Local**, not Roaming, because: (1) it's **regenerable** — re-downloaded from the server on demand, so syncing it across machines is pointless; (2) it can get **large**, and Windows copies the whole Roaming profile on login/logout, so a fat cache there would slow logins and bloat (or blow the quota on) the roaming profile; and (3) it's inherently **machine/session-specific** — which maps you've observed, at which revision. Roaming is meant for a few KB of settings you'd want identical on any machine you sign into; dropping a regenerable download cache in it is exactly the anti-pattern the Roaming/Local distinction exists to prevent. (This two-bucket split is Windows-specific: macOS folds Config+Data together and only breaks out Caches, while Linux/XDG splits all three.) The upshot for clearing the map cache: delete `%LocalAppData%\<GameName>\maps\` — the Roaming `appsettings.json` is unaffected.
