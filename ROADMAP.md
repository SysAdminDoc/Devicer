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

## v0.2.x polish (shipped)

- [x] First-run wizard (v0.2.3 — modal env-check + USB-debugging walkthrough)
- [x] Catppuccin Latte light theme + runtime swap (v0.2.2)
- [x] Settings page implementation (v0.2.2 — theme, probe interval, tool detection, about)
- [x] Loading indicator during probe (v0.2.1 — animated accent stripe + status text)
- [x] Hot-plug detection (v0.2.1 — 4 s polled re-probe with selection persistence; v0.2.2 — user-configurable interval)
- [x] Banish pill / oval backdrops from theme (v0.2.1)

## v0.3.0 alpha — Samsung firmware lookup (shipped 2026-05-09)

**Decision: Option C** — native C#, no Python or JRE dep. Single-binary install preserved.

- [x] FirmwareVersion model (4-segment + 3-segment parser, lexicographic PDA compare)
- [x] FirmwareCheckService — public OTA endpoint at `fota-cloud-dn.ospserver.net/firmware/<csc>/<model>/version.xml`, no auth
- [x] Samsung PDA extraction from build fingerprint (5th '/' segment, '_'-split)
- [x] FirmwareViewModel + functional Firmware page (autofill from Device tab, current vs latest, upgrade history, "Update available" badge)
- [x] Verified end-to-end against live S25 Ultra EUX (installed `S938BXXS6BYIF` → latest `S938BXXS9BZCH` correctly flagged as behind)
- [ ] Per-CSC search across multiple regions in one query (v0.3.1)

## v0.3.1 — Samsung firmware download (auth + download)

- [ ] FUS NONCE handshake: `POST NF_DownloadGenerateNonce.do` → `Set-Cookie: NONCE=...` + `NONCE: ...` header. XOR-decode each byte with `0x70`.
- [ ] Auth-signature derivation: AES-128-CBC encrypt of `nonce + key2`, IV = first 16 bytes of `key2`. `key2` derived from decoded nonce via the per-char transform.
- [ ] `NF_DownloadBinaryInform.do` — XML POST with model+CSC+target_version; returns `BINARY_NAME` / `BINARY_BYTE_SIZE` / `LOGIC_VALUE_FACTORY`.
- [ ] Streaming download: `POST NF_DownloadBinaryForMass.do` returning the encrypted blob; SHA256 verification on the way down.
- [ ] AES-CBC firmware decryption (key derived from version string + IMEI prefix); zero-byte padding strip.
- [ ] Local firmware cache at `%LOCALAPPDATA%\Devicer\firmware\<model>_<csc>_<version>\` with index.

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
