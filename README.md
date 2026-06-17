<p align="center"><img src="branding/logo.png" width="128" alt="Devicer logo"/></p>

# Devicer

[![Version](https://img.shields.io/badge/version-2.0.0-blue.svg)](CHANGELOG.md)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows-0078d4.svg)](#)
[![.NET](https://img.shields.io/badge/.NET-10-512bd4.svg)](#)
[![Status](https://img.shields.io/badge/status-2.0%20alpha-orange.svg)](#)

> Unified Windows toolkit for managing rooted Android phones — identify, search ROMs, back up, patch, and flash from one shell.

## Status

**v1.2.0 — in-app ROM download.** The ROMs tab now downloads builds directly into `%LOCALAPPDATA%\Devicer\roms\<codename>\` with chunked HTTP + SHA256/MD5 verification, resume support, progress bar, and cancel. Previously the "Download" button opened the browser; it now saves to disk and verifies the hash against the ROM source's published digest. A "Browser" button is still available for users who prefer the browser workflow. Seven functional sidebar pages (Device / Firmware / ROMs / Backup / Patch / Flash / Universal) plus Settings. See [CHANGELOG.md](CHANGELOG.md). Tooling-landscape document at [docs/research.md](docs/research.md), phased build plan at [ROADMAP.md](ROADMAP.md).

## Goals

1. **Identify** device + currently-installed ROM (model, CSC, build fingerprint, BL, baseband, root status, slot, encryption state).
2. **Search & download** stock firmware (Samsung CSC-aware) AND custom ROMs (LineageOS, crDroid, PixelExperience indices) from one search box.
3. **Back up** — partitions, EFS/NV (Samsung-critical, losing it bricks IMEI), userdata, Magisk modules.
4. **Patch + flash** — patch boot.img / init_boot.img with Magisk on the PC side (no phone roundtrip), flash via Odin protocol or fastboot, with EFS-clear and Knox-trip safety gates.

## Why

No single tool covers all four jobs in 2026. The closest existing option, [Thor Flash Utility](https://github.com/Samsung-Loki/Thor), only does firmware download + flash. ROM-search aggregation is still browser work. Backup orchestration from PC is fragmented across TWRP nandroids and per-app tools. Devicer integrates the recommended best-of-breed toolchain ([Bifrost](https://github.com/zacharee/SamloaderKotlin) + Thor + [tetherback](https://github.com/dlenski/tetherback) + [Magisk_patcher](https://github.com/affggh/Magisk_patcher)) under one shell.

## Stack (locked v0.2.0)

C# / .NET 10 WPF, TFM `net10.0-windows10.0.22621.0`, MVVM via `CommunityToolkit.Mvvm`. Catppuccin Mocha theme.

## Architecture (locked v0.2.0)

**Orchestration shell with subprocess wrappers** for every backend tool. `Devicer.App` (WPF UI) sits on top of `Devicer.Core` (services + models). Each external tool — Bifrost, Thor, tetherback, Magisk_patcher — runs as a child process across the OS-level boundary, which keeps Thor's GPL-3.0 from contaminating Devicer's MIT license. Tools are downloaded lazily on first need to `%LOCALAPPDATA%\Devicer\tools\` with version + SHA256 pinning.

## Build

```powershell
dotnet build -c Release Devicer.sln
dotnet run --project src/Devicer.App -c Release
# Backend smoke test (probes connected phone, prints DeviceInfo to stdout):
dotnet run --project tools/Devicer.Smoke -c Release
# Multi-CSC firmware lookup smoke:
dotnet run --project tools/Devicer.Smoke -c Release -- --firmware-regions SM-S938B EUX INS
```

Release exe: `src/Devicer.App/bin/Release/net10.0-windows10.0.22621.0/Devicer.App.exe`.

## Project layout

```
Devicer.sln
src/
  Devicer.Core/         class library — IShellRunner, AdbService, FastbootService,
                        DeviceProbeService, DeviceInfo / RootStatus models
  Devicer.App/          WPF shell — sidebar nav, 7 functional pages (Device / Firmware /
                        ROMs / Backup / Patch / Flash / Universal) + Settings, Catppuccin
                        Mocha + Latte themes, MVVM via CommunityToolkit.Mvvm
tools/
  Devicer.Smoke/        E2E console smoke against the connected device
docs/
  research.md           2026 tooling landscape, foundation document
```

## License

[MIT](LICENSE) — preserved by subprocess architecture; do **not** convert to library linking against GPL tools.
