# Devicer ROADMAP

Phased build plan derived from [docs/research.md](docs/research.md). Each version delivers a working increment.

## v0.1.0 — Research (shipped 2026-05-09)

- [x] Survey 2026 Android-flashing tooling landscape
- [x] Identify gaps: no all-in-one, no ROM-search desktop tool, fragmented backup
- [x] Pick recommended toolchain to integrate (Bifrost + Thor + tetherback + Magisk_patcher + Platform-Tools)
- [x] Repo scaffold (README, LICENSE, .gitignore, CHANGELOG, this file)
- [x] **Stack locked: C# / .NET 10 WPF**, `net10.0-windows10.0.22621.0` TFM (matches Snapture / OrganizeContacts / DicomUtilitySuite)
- [x] **Architecture locked: subprocess wrappers for ALL backend tools** (preserves MIT license — linking against Thor as a library would force GPL-3.0 contagion). Tools downloaded lazily on first need to `%LOCALAPPDATA%\Devicer\tools\` with version pinning. Platform-tools detected on PATH; user-prompted install if absent.

## v0.2.0 — Scaffold + device ID (shipped 2026-05-09)

- [x] WPF shell with sidebar (Device · Firmware · Backup · Patch · Flash · Settings)
- [x] Catppuccin Mocha dark theme (Latte deferred to v0.2.x polish)
- [x] adb/fastboot wrapper service: `getprop ro.build.fingerprint`, `ro.csc.sales_code`, `ro.product.model`, slot, BL state, baseband, Knox warranty bit, encryption, OEM unlock
- [x] Magisk + KernelSU + APatch detection via `su -c 'magisk -c'` / `ksud --version` / `apd --version`
- [x] Device dashboard: model, ROM, root, BL, Knox state as glanceable cards
- [x] `tools/Devicer.Smoke` console verifier (CI-friendly E2E against connected device)
- [ ] First-run wizard: detect/install platform-tools v37+, prompt for USB debugging — deferred to v0.2.x

## v0.2.x polish (in progress)

- [ ] First-run wizard (platform-tools detection + install prompt)
- [ ] Catppuccin Latte light theme + runtime swap
- [ ] Settings page implementation (theme, log level, tool paths)
- [x] Loading indicator during probe (v0.2.1 — animated accent stripe + status text)
- [x] Hot-plug detection (v0.2.1 — 4 s polled re-probe with selection persistence)
- [x] Banish pill / oval backdrops from theme (v0.2.1)

## v0.3.0 — Firmware download (Samsung) — **BLOCKED on backend choice**

Three viable backends, mutually exclusive, each with real tradeoffs. **Decision required from user before implementation:**

- **Option A** — Subprocess-wrap Python `samloader` CLI. Pros: real CLI, well-documented protocol, maintained forks. Cons: forces a Python runtime dependency on the user's machine (we'd ship an embeddable Python or require the user to install one).
- **Option B** — Subprocess-wrap a Bifrost / SamloaderKotlin CLI build. Pros: same protocol, maintained, native binary (Kotlin/Native or JVM jlink). Cons: Bifrost is currently GUI-only — needs a CLI fork or upstream PR; jlink path adds ~50 MB JRE bundle.
- **Option C** — Native C# reimplementation of Samsung's Kies / FUS / NF protocol. Pros: zero external dependency, single-binary install, fastest UX. Cons: most work; protocol can change (Samsung has rotated keys / endpoints in past).

**Recommendation**: Option C if we want a clean MIT product. Option A is fastest if Python is acceptable.

Once backend chosen:

- [ ] Per-CSC search, latest + history view, region picker
- [ ] Decrypted streaming download with resume + integrity check
- [ ] Local firmware cache at `%LOCALAPPDATA%\Devicer\firmware\<model>_<csc>_<version>\` with model/CSC/version index

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
