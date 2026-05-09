# Devicer ROADMAP

Phased build plan derived from [docs/research.md](docs/research.md). Each version delivers a working increment.

## v0.1.0 — Research (current)

- [x] Survey 2026 Android-flashing tooling landscape
- [x] Identify gaps: no all-in-one, no ROM-search desktop tool, fragmented backup
- [x] Pick recommended toolchain to integrate (Bifrost + Thor + tetherback + Magisk_patcher + Platform-Tools)
- [x] Repo scaffold (README, LICENSE, .gitignore, CHANGELOG, this file)
- [ ] Lock primary stack (proposed: C#/.NET 10 WPF)
- [ ] Decide bundling strategy: ship integrated tool binaries vs. download-on-first-run

## v0.2.0 — Scaffold + device ID

- [ ] WPF shell with sidebar (Device · Firmware · Backup · Patch · Flash · Settings)
- [ ] Catppuccin Mocha dark theme + light option
- [ ] adb/fastboot wrapper service: `getprop ro.build.fingerprint`, `ro.csc.sales_code`, `ro.product.model`, slot, BL state, baseband
- [ ] Magisk + KernelSU detection via `su -c magisk --version` / `ksud --version`
- [ ] Device dashboard: model, ROM, root, BL, Knox state as glanceable cards
- [ ] First-run wizard: detect/install platform-tools v37+, prompt for USB debugging

## v0.3.0 — Firmware download (Samsung)

- [ ] Bifrost protocol wrapper (or library reference if .NET binding viable)
- [ ] Per-CSC search, latest + history view, region picker
- [ ] Decrypted streaming download with resume + integrity check
- [ ] Local firmware cache with model/CSC/version index

## v0.4.0 — Custom ROM search

- [ ] Aggregate LineageOS wiki, lineageosdevices.com, XDA forum tags
- [ ] Optional: crDroid, PixelExperience, Evolution X indices
- [ ] Filter results by device codename auto-derived from Job 1
- [ ] Direct download where mirror policy permits, deep-link otherwise
- [ ] Checksum verification on download complete

## v0.5.0 — Backup (PC-side)

- [ ] tetherback wrapper for TWRP nandroid streams over ADB
- [ ] Mandatory EFS/NV warning gate before any AP flash on Samsung
- [ ] App-data backup orchestration (Neo Backup driver via ADB)
- [ ] Versioned backup catalog with timestamps and partition manifests
- [ ] Restore flow with checksum verification

## v0.6.0 — Magisk patch

- [ ] Wrap [affggh/Magisk_patcher](https://github.com/affggh/Magisk_patcher) or [Magisk-Boot-Patcher](https://github.com/0xsharkboy/Magisk-Boot-Patcher)
- [ ] Patch boot.img / init_boot.img on PC, no phone roundtrip
- [ ] KernelSU patch path via `ksud boot-patch`
- [ ] Output staged into a reproducible flash package

## v0.7.0 — Flash

- [ ] Thor library integration as primary; Heimdall fallback
- [ ] Odin .tar.md5 inspector + per-partition flash queue
- [ ] EFS-Clear gate **OFF by default**, red banner if user enables
- [ ] Knox eFuse warning before any custom AP flash
- [ ] Dry-run mode that validates the queue without writing

## v0.8.0 — Universal mode

- [ ] Pixel/OnePlus/Xiaomi via fastboot pipeline
- [ ] OEM portal links: Google Android Flash Tool, OnePlus MSM, Xiaomi MiFlash
- [ ] Per-OEM quirk profiles (Pixel anti-rollback, Xiaomi unlock-wait, OnePlus EDL)

## v1.0.0 — Polish + first public release

- [ ] Full UX pass per `directive-ux-polish.md`
- [ ] Theme audit per `directive-theming.md`
- [ ] Dependency CVE scan per `directive-dependency-scan.md`
- [ ] Signed installer + portable ZIP via GH Actions release workflow
- [ ] README screenshots captured DPI-aware (125%)
- [ ] Branch protection on `main`

## Stretch (post-1.0)

- [ ] Linux/macOS via Avalonia port (defer until demand justifies)
- [ ] Webhook/CLI mode for scripted batch flashing
- [ ] Plugin API for niche OEMs (Sony, Nothing, Asus)
