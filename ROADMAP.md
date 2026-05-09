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

## v0.3.1 — Samsung firmware download (auth + decrypt) — shipped 2026-05-09

- [x] FUS NONCE handshake: `POST NF_DownloadGenerateNonce.do` → `NONCE` response header. AES-CBC decrypt with KEY_1 (32 bytes UTF-8), IV = key[:16], strip padding. Dual-keygen support (current + legacy) — Samsung's CDN serves either depending on route.
- [x] Auth-signature derivation: per-char `KEY_1[nonce[i] % 16]` for i=0..15, append KEY_2 → 32-byte AES-256 key. AES-CBC-encrypt the nonce, IV = key[:16], PKCS#7 padding, base64-encode → goes into `Authorization: FUS signature="…"` header.
- [x] `NF_DownloadBinaryInform.do` — XML POST with model + CSC + target_version + IMEI + LOGIC_CHECK; parses `BINARY_NAME` / `BINARY_BYTE_SIZE` / `LATEST_FW_VERSION` / `LOGIC_VALUE_FACTORY` from the response.
- [x] Streaming download: GET `NF_DownloadBinaryForMass.do?file=…` with optional `Range:` resume; chunked write + SHA256 over the encrypted blob.
- [x] AES-128-ECB firmware decryption (NOT CBC — confirmed against samloader / Bifrost). PKCS#7 unpad on the final block. V4 key = MD5(LOGIC_CHECK(LATEST_FW_VERSION, LOGIC_VALUE_FACTORY)); V2 legacy key = MD5("REGION:MODEL:VERSION").
- [x] Local firmware cache at `%LOCALAPPDATA%\Devicer\firmware\<model>_<region>_<pda>\` with `index.json` manifest.
- [x] IMEI auto-probe via root + manual UI entry (Samsung's late-2024 protocol change requires a real IMEI; the legacy `0000…` fake yields FUS Status 408).
- [ ] Per-CSC search across multiple regions in one query (deferred to v0.3.2)
- [ ] Alternative IMEI read path for Samsung One UI 7+ (`service call iphonesubinfo` returns permission error even with root) — `/efs` parsing or privileged shim app (deferred to v0.3.2)

## v0.4.0 — Custom ROM search — shipped 2026-05-09

- [x] Aggregate LineageOS official update API (`/api/v1/<codename>/<romtype>/*`) — nightly + weekly across the device matrix
- [x] Aggregate crDroid OTA JSON from the `crdroidandroid/android_vendor_crDroidOTA` GitHub repo across branches 16.0 / 15.0 / 14.0
- [x] Filter results by device codename, **auto-derived from the connected device's `ro.product.device`** on the Device tab
- [x] Direct download via deep-link (one-click → opens browser/download manager); SHA256 displayed for every entry so post-download verification is trivial
- [x] Built-in ROMs page with Catppuccin styling, search box, status line, per-build cards (source / kind / version / size / build-date / maintainer / forum link)
- [ ] Optional: PixelExperience, Evolution X indices (deferred — neither maintains a stable public JSON feed in 2026)
- [ ] In-app download with chunked SHA256 verify + ROM cache (deferred to v0.4.1)

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
