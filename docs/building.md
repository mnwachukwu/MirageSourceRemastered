# Building, publishing and releasing

How a working tree becomes installers, what the version number is bound to, and which
platforms the result runs on.

## Building and distributing

```sh
dotnet build Mirage.slnx -c Release
```

Publish a single project (each ships `win-x64`, `linux-x64`, and `osx-x64` profiles; output lands in `dist/<app>/<platform>/`):
```sh
dotnet publish server/src/Mirage.Server.Host/Mirage.Server.Host.csproj /p:PublishProfile=win-x64
dotnet publish client/src/Mirage.Client.Shell/Mirage.Client.Shell.csproj /p:PublishProfile=linux-x64
dotnet publish editor/src/Mirage.Editor/Mirage.Editor.csproj /p:PublishProfile=osx-x64
```

Publish everything at once with Velopack installers:
```sh
msbuild Mirage.Publish.csproj /t:PublishAll         # all apps, all platforms
msbuild Mirage.Publish.csproj /t:PublishAll_Win     # all apps, Windows only
msbuild Mirage.Publish.csproj /t:PublishAll_Linux
msbuild Mirage.Publish.csproj /t:PublishAll_Osx
msbuild server/Mirage.Server.Publish.csproj /t:PublishAll   # one app, all platforms
```

Packaging runs **only for an explicit `/t:PublishAll*` target**. A plain `dotnet build` — of a publish project, or of `Mirage.slnx` — prints a one-line reminder and packages nothing. It used to package on every build, so an ordinary solution build ran nine `dotnet publish` passes plus `vpk`/`mgcb`/`tar`, taking 127s instead of ~9s. In Visual Studio, right-click → **Build** on a publish project therefore no longer packages; use the explicit target.

Installer artifacts (`.exe`, AppImage, `.app.tar.gz`) land in `dist/installers/`. `dotnet tool restore` runs automatically on solution open. The server publishes as a single self-contained executable; the client and editor cannot be single-file because MonoGame and SkiaSharp native libraries must sit on disk beside the binary.

## Versioning and releases

The version lives in `MajorVersion` / `MinorVersion` / `PatchVersion` in [`Directory.Build.props`](../Directory.Build.props), and flows from there into `AssemblyVersion`, `FileVersion`, `InformationalVersion` and Velopack's `--packVersion` — so it is what the executables report and what the installers are named for.

**A release is a tag, and the tag has to match that version.** Pushing `v1.2.0` requires the three values to read 1, 2, 0; CI checks this immediately after restore and fails the run otherwise, before spending a build on it. The order is: bump the three values, commit, then tag.

```sh
git tag v1.0.0
git push origin v1.0.0
```

That publishes all three apps for all three platforms and attaches the twelve installer artifacts to a GitHub Release — which is what the [download page](https://mirage.pluperfect.dev/downloads) reads. An ordinary push builds and runs the tests and stops there; the **Run workflow** button does everything except create the release, so it is a rehearsal.

Rolling back means moving the tag rather than pressing a button; the release job replaces an existing release's assets rather than failing:

```sh
git tag -f v1.0.0 && git push --force origin v1.0.0
```


## Output naming

`GameName` and its slug `GameNameSlug` in [`Directory.Build.props`](../Directory.Build.props) drive all executable and installer filenames. The same string is declared as `Constants.GameName` in [`Mirage.Shared/Constants.cs`](../server/src/Mirage.Shared/Constants.cs) for in-game UI (window title, chat, HUD). **Keep the two in sync manually** — renaming the game requires updating both.

## Platform support

Windows, Linux and macOS, all x64, on .NET 10. This section is the source of truth for that.

> **The README's badges restate it and nothing checks them.** The `.NET 10`, `Windows`, `Linux` and
> `macOS` badges use shields.io's `/badge/` endpoint, which renders whatever text it is handed — they
> are hand-written labels, not readings. Dropping a platform or moving to a new .NET means editing
> them by hand, exactly like `GameName` above; otherwise they go on advertising something that
> stopped being true. Only the top row of badges reports live state: the first is GitHub's own CI
> status, and the other two read the Releases API.

**Linux** — minimal installations may need X11 libraries:
```sh
sudo apt-get install libx11-6 libice6 libsm6    # Debian/Ubuntu
sudo dnf install libX11 libICE libSM            # Fedora/RHEL
```

**macOS** — clear the Gatekeeper quarantine flag before first launch:
```sh
xattr -rd com.apple.quarantine dist/client/osx-x64/
```

> **Apple Silicon:** profiles use `osx-x64`, which runs under Rosetta 2 on M-series Macs. MonoGame 3.8 ships universal libraries, so no ARM profile is needed.

> **Forking:** each app's `installer/Info.plist` sets `CFBundleIdentifier` to a `com.example.*` placeholder. Change it to your own reverse-DNS id before distributing — it's the one identity string not derived from `GameName`.

Android needs a separate shell project targeting Android with touch input; `Mirage.Client.Core` needs no changes. Browser play needs a different rendering shell — MonoGame has no stable WASM support, but `Mirage.Client.Core` can be consumed by a Godot 4 C# project and exported to HTML5/WASM.
