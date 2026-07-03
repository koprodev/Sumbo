# Sumbo

> Mirror any window into a single always-on-top view — crop it, fade it, click through it.

**Sumbo** (숨보, from Korean *"숨겨서 보자"* — "keep it hidden, keep an eye on it") is a
free, open-source Windows utility that live-mirrors any window using native DWM
thumbnails. Keep a chat, a video, a dashboard, or a game map floating above your work —
with **zero impact on the source window** (no capture, no injection, no performance cost
to the mirrored app).

🇰🇷 [한국어 안내는 README.ko.md](README.ko.md)

## Screenshots

> Interface shown in English with sample data.

**Watch a video while you game** — pin it on the left, with zero impact on the game window.

![Sumbo showing a video pinned over a game](docs/screenshots/hero-gaming.jpg)

![Sumbo mirroring a window with the display panel open](docs/screenshots/hero-display.jpg)

|  |  |
| --- | --- |
| ![Pick a target window](docs/screenshots/targets.jpg) | ![Crop a region of the mirror](docs/screenshots/region.jpg) |
| ![Save and apply profiles](docs/screenshots/profiles.jpg) | ![Language and startup settings](docs/screenshots/settings.jpg) |

## Features

- **Single-window mirror** — the main window *is* the mirror. Pick a target from the
  side rail and the whole canvas becomes a live view of it.
- **Region crop** — drag-select just the part you need; saved regions use relative
  coordinates, so they survive source-window resizing.
- **Opacity 10–100%** — see your work through the mirror.
- **Click forwarding** — click on the mirror, the click lands on the source window,
  no window switching.
- **Click-through** — the mirror ignores *all* input so you can work underneath it
  (escape with `Ctrl+Alt+C`).
- **Size presets & anchoring** — original / ½ / ¼ / fullscreen, 9-point screen
  anchors, position lock.
- **Profiles** — save target + region + opacity + placement, reapply in one click.
- **Group switching** — register several windows and cycle through them (`Ctrl+Alt+G`).
- **Overlay mode (UI hide)** — chrome disappears, only the mirrored content stays.
- **Tray resident** — closing (X) hides to the tray; exit from the tray menu.
  Optional start-with-Windows.
- **English / Korean UI**, switchable at runtime.

## Download & install

Pick one from [Releases](https://github.com/koprodev/Sumbo/releases):

| Asset | Size | For |
|---|---|---|
| `Sumbo-<version>-win-x64.msi` | ~38 MB | **Installer** — per-user (no admin/UAC), Start-menu shortcut, uninstall from Apps & Features. Runtime bundled. |
| `Sumbo-<version>-win-x64.zip` | ~45 MB | **Portable** — unzip anywhere and run `Sumbo.exe`. Runtime bundled, nothing to install. |
| `Sumbo-<version>-win-x64-lite.zip` | ~0.2 MB | **Lite portable** — same app without the bundled runtime. Requires the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (Windows prompts with a download link on first run if missing). |

*(Optional)* Verify a download: compare `Get-FileHash <file>` (PowerShell) against
`SHA256SUMS.txt` attached to the release.

> **SmartScreen note**: release binaries are not code-signed. On first run Windows
> may show "Windows protected your PC" — choose *More info → Run anyway*. If you
> prefer, build from source instead (below).

### System requirements

- Windows 10 / 11, 64-bit (x64), with desktop composition (DWM) enabled — it is by
  default on all supported Windows versions.

## Default hotkeys

| Hotkey | Action |
|---|---|
| `Ctrl+Alt+S` | Show / hide the mirror window |
| `Ctrl+Alt+W` | Pick a target window |
| `Ctrl+Alt+C` | Toggle click-through |
| `Ctrl+Alt+↑` / `↓` | Opacity up / down |
| `Ctrl+Alt+R` | Select a region |
| `Ctrl+Alt+G` | Switch to the next window in the group |

## Privacy

- **No telemetry, no accounts, no network access.** Sumbo never phones home.
- Updates are manual — check the
  [Releases page](https://github.com/koprodev/Sumbo/releases) for new versions.

## Known limitations

- Click forwarding cannot reach elevated (administrator) windows — Windows blocks
  input from a non-elevated process; Sumbo shows a one-time notice instead.
- Multi-monitor per-monitor-DPI transitions have had limited real-hardware
  verification. Reports are welcome via
  [Issues](https://github.com/koprodev/Sumbo/issues).

## Building from source

```powershell
winget install Microsoft.DotNet.SDK.10   # then reopen the terminal
dotnet build -c Debug                    # build
dotnet test                              # unit tests
dotnet run --project src/Sumbo.App       # run

# Release single-file publish (what the release zip contains)
dotnet publish src/Sumbo.App -c Release -r win-x64 --self-contained `
  /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true `
  /p:DebugType=embedded -o artifacts/publish/win-x64-single

# Per-user MSI (what the release .msi contains) — WiX 5: dotnet tool install --global wix --version 5.0.2
wix build installer/Package.wxs -arch x64 -d Version=<version> `
  -d PublishDir=artifacts/publish/win-x64-single -d RepoDir=. `
  -o artifacts/publish/Sumbo-<version>-win-x64.msi
```

Project layout: `src/Sumbo.App` (WinForms UI) · `src/Sumbo.Core` (domain logic,
UI-independent) · `src/Sumbo.Native` (P/Invoke wrappers for DWM/User32) ·
`tests/Sumbo.Core.Tests` (xUnit).

## Support ☕

Sumbo is free and always will be — no ads, no feature gating. If it saves you some
screen space and a few Alt+Tabs a day, consider supporting development:

**[github.com/sponsors/koprodev](https://github.com/sponsors/koprodev)**

## License

MIT — see [LICENSE](LICENSE). Release binaries bundle the .NET runtime; see
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

The window-mirroring approach was inspired by
[OnTopReplica](https://github.com/LorenzCK/OnTopReplica); Sumbo is an independent,
from-scratch implementation with no code reuse.
