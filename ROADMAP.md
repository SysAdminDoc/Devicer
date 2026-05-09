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

## v0.5.0 — Backup (PC-side) — shipped 2026-05-09

- [x] **Direct adb+root partition backup** — `dd` selected blocks on-device, `adb pull` to host, SHA256-verify each image, write a versioned manifest (no TWRP boot needed). Works against any rooted Android device that exposes `/dev/block/by-name`.
- [x] **EFS/NV warning gate** — red EFS-is-one-way banner is permanently visible on the Backup page; critical partitions (EFS, modem NV, persist, modem-state, FSC/FSG) are pre-selected with a CRITICAL badge and a plain-language "what breaks if you lose this" reason.
- [x] **Versioned backup catalog** at `%LOCALAPPDATA%\Devicer\backups\<serial>\<timestamp>\manifest.json` with per-partition entries (name, file, size, sha256, isCritical) plus device metadata (serial, model, codename, createdUtc).
- [x] Per-partition warnings collected non-fatally — one bad partition doesn't kill the whole run; the user sees exactly which images couldn't be captured.
- [ ] tetherback subprocess wrapper for TWRP nandroid streams (deferred to v0.5.1 — requires TWRP boot, separate workflow)
- [ ] App-data backup orchestration (Neo Backup driver via ADB) — deferred to v0.5.2
- [ ] Restore flow with checksum verification — high risk, deferred to v0.5.3 with explicit double-confirm gating

## v0.6.0 — Magisk / KernelSU / APatch boot patcher — shipped 2026-05-09

- [x] **On-device patcher path** — push boot.img to `/data/local/tmp`, run the installed root manager's bundled patcher via `su`, pull the patched image to host, SHA256-verify. No PC-side Python required; uses the patcher already on the user's rooted device.
- [x] Magisk path — `cd /data/adb/magisk && KEEPVERITY=true KEEPFORCEENCRYPT=true sh boot_patch.sh boot.img`. Output: `/data/adb/magisk/new-boot.img`.
- [x] KernelSU path — `ksud boot-patch -b boot.img`. Output: `kernelsu_patched_*.img`.
- [x] APatch path — `apd patch -b boot.img`. Output: `apatch_patched_*.img`.
- [x] Output staged into a reproducible flash package at `%LOCALAPPDATA%\Devicer\patches\<serial>\<timestamp>\` with SHA256 captured.
- [x] Pre-validation: refuses to run if no root manager detected; surfaces the detected manager + version on the page.
- [ ] PC-side affggh/Magisk_patcher subprocess fallback (deferred to v0.6.1 — required when the connected device has no installed root manager but the user has a known-good boot.img)

## v0.7.0 — Flash (inspector + safety gates) — shipped 2026-05-09

- [x] Odin **.tar.md5 inspector** — `OdinInspectorService` parses `.tar` / `.tar.md5` archives via `System.Formats.Tar`, lists every entry with size + partition guess (`boot.img.lz4` → `boot`).
- [x] **Per-entry checkbox queue** with image-vs-non-image filtering (image entries are checked by default, manifest / metadata entries are listed but unchecked).
- [x] **EFS-Clear gate OFF by default**, big red banner if the user enables it.
- [x] **Knox eFuse warning** auto-derived from the connected device's warranty bit — green "intact, don't lose it" banner if `0`, yellow "already tripped" banner otherwise.
- [x] **Dry-run mode** — produces the full flash plan (each `<entry> → <partition>`) plus the EFS-Clear / Knox status as readable text. No data written.
- [x] Footnote: actual writes deferred until v0.7.1 — see notes below.
- [ ] **Thor subprocess wrapper** for actual writes (deferred to v0.7.1; subprocess preserves MIT against Thor's GPL-3.0)
- [ ] **Heimdall fallback** for the rare cases Thor can't reach the device (deferred to v0.7.2)

## v0.8.0 — Universal mode — shipped 2026-05-09

- [x] **OEM detection** from `ro.product.manufacturer` / `ro.product.brand` — covers Samsung, Google, OnePlus, Xiaomi (incl. Redmi / POCO), Sony, ASUS, Motorola, Nothing, Realme, Oppo, Vivo. Falls back to a generic fastboot profile for unknown OEMs.
- [x] **Per-OEM guidance card** with three sections (unlock procedure, flash path, quirks/warnings) backed by the `OemGuidanceService`. Each step has an optional deep-link button.
- [x] **OEM portal deep-links** — Google Android Flash Tool, OnePlus MSM guide, Mi Unlock portal, Sony developer unlock portal, ASUS support, Motorola unlock portal, Nothing support.
- [x] **Per-OEM quirk profiles** — Pixel anti-rollback + Pixel 7+ init_boot, OnePlus MSM region scoping + OxygenOS/ColorOS cross-flash trap, Xiaomi 7-day unlock wait + ARB + EU/Global/China ROM mixing, Sony DRM-key loss on unlock, Motorola Verizon-locked variants.
- [x] **Universal sidebar tab** added between Flash and Settings.
- [ ] Direct in-app fastboot flash queue with progress bar (deferred to v0.8.1 — uses the same `FastbootService` already in `Devicer.Core`)

## v1.0.0 — Polish + first feature-complete alpha — shipped 2026-05-09

- [x] **Theme audit** — every `App*` token defined in both `CatppuccinMocha.xaml` and `CatppuccinLatte.xaml` (parity diff confirmed empty in both directions). No banned `CornerRadius="999"` / `RoundedCornerShape(50)` / `border-radius:999` anywhere in `src/`.
- [x] **Dependency CVE scan** — `dotnet list package --vulnerable --include-transitive` clean across `Devicer.Core`, `Devicer.App`, `Devicer.Smoke`. CommunityToolkit.Mvvm bumped 8.4.0 → 8.4.2.
- [x] **UX polish** — every nav item now has a real implementation page (Device / Firmware / ROMs / Backup / Patch / Flash / Universal + Settings); legacy stub classes deleted. Per-page diagnostics + status banners + cancel buttons + open-folder helpers consistent across the suite.
- [x] **Portable ZIP build script** at `tools/build-release.ps1` (PowerShell 7) — produces `dist/Devicer-vX.Y.Z-portable-win-x64.zip` + `.sha256` sidecar; `-SelfContained` switch for single-file deploys.
- [x] **GitHub Actions release workflow** at `.github/workflows/release.yml` — manual `workflow_dispatch` trigger, builds + uploads ZIP + SHA256, creates GH Release. Active once the repo gets a remote.
- [ ] **Signed installer** (Inno Setup / WiX) — needs an actual code-signing cert; deferred until the user provides one.
- [ ] **README screenshots** captured DPI-aware (125%) — needs a Win32 capture pass against the running app; deferred to a manual session per the user's screenshots ritual.
- [ ] **Branch protection on `main`** — N/A while the repo is local-only; flag for activation immediately after the first `gh repo create`.

## Stretch (post-1.0)

## Stretch (post-1.0)

- [ ] Linux/macOS via Avalonia port (defer until demand justifies)
- [ ] Webhook/CLI mode for scripted batch flashing
- [ ] Plugin API for niche OEMs (Sony, Nothing, Asus)
