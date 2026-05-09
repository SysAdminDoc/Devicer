![Devicer banner](branding/banner.png)

# Devicer

[![Version](https://img.shields.io/badge/version-0.1.0-blue.svg)](CHANGELOG.md)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows-0078d4.svg)](#)
[![Status](https://img.shields.io/badge/status-research-orange.svg)](#)

> Unified Windows toolkit for managing rooted Android phones — identify, search ROMs, back up, patch, and flash from one shell.

## Status

**v0.1.0 — research phase.** No code yet. The 2026 tooling landscape, gap analysis, and recommended toolchain are documented in [docs/research.md](docs/research.md). Phased build plan lives in [ROADMAP.md](ROADMAP.md).

## Goals

1. **Identify** device + currently-installed ROM (model, CSC, build fingerprint, BL, baseband, root status, slot, encryption state).
2. **Search & download** stock firmware (Samsung CSC-aware) AND custom ROMs (LineageOS, crDroid, PixelExperience indices) from one search box.
3. **Back up** — partitions, EFS/NV (Samsung-critical, losing it bricks IMEI), userdata, Magisk modules.
4. **Patch + flash** — patch boot.img / init_boot.img with Magisk on the PC side (no phone roundtrip), flash via Odin protocol or fastboot, with EFS-clear and Knox-trip safety gates.

## Why

No single tool covers all four jobs in 2026. The closest existing option, [Thor Flash Utility](https://github.com/Samsung-Loki/Thor), only does firmware download + flash. ROM-search aggregation is still browser work. Backup orchestration from PC is fragmented across TWRP nandroids and per-app tools. Devicer integrates the recommended best-of-breed toolchain ([Bifrost](https://github.com/zacharee/SamloaderKotlin) + Thor + [tetherback](https://github.com/dlenski/tetherback) + [Magisk_patcher](https://github.com/affggh/Magisk_patcher)) under one shell.

## Stack (proposed)

C# / .NET 10 WPF — matches the existing Windows-toolkit family (UCX, NVMe Patcher, Images, TeamStation), native DPI, easy dark theme. Thor itself is .NET, so it can be referenced as a library rather than shelled out to. Final stack decision pinned in v0.2.0.

## Architecture (planned)

Orchestration shell, **not** a from-scratch reimplementation. Each integrated tool earned its slot in the [research](docs/research.md). Lower risk, faster MVP, leverages active OSS communities.

## License

[MIT](LICENSE)
