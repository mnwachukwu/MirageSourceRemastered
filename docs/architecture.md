# Technical decisions

Choices that are not obvious from reading the code, recorded with the reasoning that
produced them.

| Area | VB6 | C# |
|---|---|---|
| Language / runtime | Visual Basic 6 | C# 13 / .NET 10 |
| Wire protocol | Binary, Chr(255) delimiters | Newline-delimited JSON over TLS (encrypt-only, ephemeral self-signed cert) |
| Data storage | Binary `.dat`, INI files | UTF-8 JSON |
| Rendering | DirectX COM (DirectDraw) | MonoGame 3.8 DesktopGL |
| Music | MIDI via `mciSendString` (Windows only) | OGG Vorbis, gapless streaming via NVorbis |
| Editor | Admin-gated forms inside the client exe | Standalone Avalonia 11 app |
| Concurrency | Single-threaded Winsock callbacks | Async TCP, per-player channel |
| Platform | Windows only | Windows, macOS, Linux |

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
