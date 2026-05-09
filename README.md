<p align="center"><img src="branding/logo.png" width="128" alt="Devicer logo"/></p>

# Devicer

[![Version](https://img.shields.io/badge/version-0.7.0-blue.svg)](CHANGELOG.md)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows-0078d4.svg)](#)
[![.NET](https://img.shields.io/badge/.NET-10-512bd4.svg)](#)
[![Status](https://img.shields.io/badge/status-alpha-orange.svg)](#)

> Unified Windows toolkit for managing rooted Android phones — identify, search ROMs, back up, patch, and flash from one shell.

## Status

**v0.7.0 — alpha.** Device + Firmware + ROMs + Backup + Patch + **Flash** tabs all functional. Flash page reads Samsung Odin `.tar.md5` archives, lists their partition entries, and produces a dry-run plan with the EFS-Clear toggle (OFF by default) plus a Knox-status banner. Actual writes are gated to v0.7.1. Backup performs root `dd` of selected partitions, pulls the images off-device, SHA256-verifies, and writes a versioned manifest to `%LOCALAPPDATA%\Devicer\backups\<serial>\<timestamp>\`. Critical Samsung partitions (EFS, modem NV, persist, modem state, FSC/FSG) are pre-selected and rendered with a CRITICAL badge + plain-language reason. Live-tested partition discovery on the connected S25 Ultra (125 partitions, all 6 critical correctly flagged). No Python, no JRE, no third-party servers. See [CHANGELOG.md](CHANGELOG.md). Tooling-landscape document at [docs/research.md](docs/research.md), phased build plan at [ROADMAP.md](ROADMAP.md).

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
```

Release exe: `src/Devicer.App/bin/Release/net10.0-windows10.0.22621.0/Devicer.App.exe`.

## Project layout

```
Devicer.sln
src/
  Devicer.Core/         class library — IShellRunner, AdbService, FastbootService,
                        DeviceProbeService, DeviceInfo / RootStatus models
  Devicer.App/          WPF shell — sidebar nav, 6 pages (Device functional, others stubs),
                        Catppuccin theme, MVVM via CommunityToolkit.Mvvm
tools/
  Devicer.Smoke/        E2E console smoke against the connected device
docs/
  research.md           2026 tooling landscape, foundation document
```

## License

[MIT](LICENSE) — preserved by subprocess architecture; do **not** convert to library linking against GPL tools.
