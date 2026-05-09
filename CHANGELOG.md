# Changelog

All notable changes to Devicer are documented here. Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versions follow [SemVer](https://semver.org/).

## v0.8.0 — 2026-05-09 (Universal mode)

### Added
- **`OemKind` enum + `OemKindExtensions.Detect`** — manufacturer/brand → enum classification covering Samsung / Google / OnePlus / Xiaomi (Redmi / POCO) / Sony / ASUS / Motorola (Lenovo) / Nothing / Realme / Oppo / Vivo, plus Other/Unknown fallbacks.
- **`OemGuidanceService`** — produces a populated `OemGuidance` (headline, tooling, unlock steps, flash steps, quirks, optional portal URL+label) for each OEM. Eight first-class profiles + a generic-fastboot fallback.
- **Universal sidebar tab** (between Flash and Settings) with three step-list cards driven by the active OEM profile. Each step is a Catppuccin card with an optional "Open" deep-link button to the relevant portal/article. The header has a primary "Open portal" button bound to the OEM's portal URL when available.
- **OEM auto-detection on device-tab change** — UniversalViewModel re-runs `OemKindExtensions.Detect(device.Manufacturer, device.Brand)` whenever the user picks a different connected device.

### Profile coverage
- **Pixel** — Android Flash Tool deep-link, factory-image fallback, anti-rollback warning, Pixel 7+ init_boot.img patch note.
- **OnePlus** — fastboot flash + MSM tool guide, region-scoped MSM warning, OxygenOS/ColorOS cross-flash trap.
- **Xiaomi** — Mi Unlock 7-day wait, MiFlash steps, ARB warning, EU/Global/China ROM mixing trap, MIUI/HyperOS post-reset lock note.
- **Sony** — developer-portal IMEI unlock, Newflasher .ftf flow, DRM-key loss warning.
- **ASUS** — model-specific Unlock Tool APK, fastboot flash, warranty marker.
- **Motorola/Lenovo** — unlock-data extraction, portal submission, carrier-lock warning (Verizon variants).
- **Nothing** — factory firmware ZIP, AVB / dm-verity note.
- **Samsung** — links the user to the dedicated Firmware + Flash tabs and surfaces the One UI 8 OEM-unlock-toggle removal warning for S25 / Z Fold7 / Z Flip7.
- **Generic** — standard fastboot unlock + per-partition flash, with a "verify the OEM's quirks before flashing" reminder.

### Verified
- dotnet build clean (4 projects, 0/0).
- WPF app launches with the new Universal tab in nav; OEM auto-fills from the connected Samsung S25 Ultra (correctly routes to the Samsung-redirect profile).

### Architecture notes
- v0.8.0 is the **guidance** half of universal mode. Direct in-app fastboot flashing for non-Samsung devices is deferred to v0.8.1 — the underlying `FastbootService` is already in `Devicer.Core` from v0.2.0 and just needs an orchestration ViewModel + UI.

## v0.7.0 — 2026-05-09 (Flash inspector + safety gates)

### Added
- **`OdinInspectorService`** — parses Samsung's Odin firmware archives (.tar and .tar.md5) via `System.Formats.Tar`. Returns an `OdinTarInfo` with per-entry size + a derived partition-name guess (`boot.img.lz4` → `boot`, `cache.img.lz4` → `cache`). The trailing `<md5>  <filename>\n` block on `.tar.md5` files is detected and tolerated by the tar reader.
- **Functional Flash page** (replaces stub):
  - File picker for `.tar.md5` / `.tar` archives, "Inspect" button, package hint surfaced (AP / BL / CP / CSC / HOME_CSC).
  - Per-entry checkbox list with mono name + partition guess + size; image-like entries (`.img` / `.lz4` / `.bin`) are pre-checked, metadata entries listed but unchecked.
  - **Knox eFuse banner** — green "intact, do not lose it by accident" warning when the connected device's warranty bit is `0`; yellow "already tripped" banner otherwise. Drawn from `DeviceInfo.KnoxWarrantyBit` — auto-syncs when the user changes the selected device on the Device tab.
  - **EFS-Clear toggle** — OFF by default, prominently labelled. Square `CornerRadius="6"` per the global pill-ban rule.
  - **Dry-run plan** — text-rendered flash plan (`<entry-name> → <partition>`) plus the EFS-Clear + Knox status, written to a status panel. No writes performed.
- New models: `OdinTarEntry`, `OdinTarInfo`.
- Cleaned out the legacy `FlashPage` / `PatchPage` / `BackupPage` stubs from `StubPage.xaml.cs` — every nav item now has a real implementation page.

### Verified
- dotnet build clean across Debug + Release (4 projects, 0/0).
- WPF app launches without XAML errors.

### Architecture notes
- v0.7.0 deliberately ships **inspect + dry-run only**. Actual writes will land in v0.7.1 via a Thor subprocess wrapper — keeping Thor (GPL-3.0) outside the .NET process boundary so Devicer stays MIT-clean.
- The dry-run plan format already matches what the Thor subprocess flow will execute, so v0.7.1 will be a thin shim that turns "show me what would happen" into "actually do it" with explicit double-confirm gating.

## v0.6.0 — 2026-05-09 (Boot-image patcher)

### Added
- **`BootPatchService`** — orchestrates the full patch pipeline: push boot.img to `/data/local/tmp`, run the installed root manager's patcher via `su`, pull the patched output back, SHA256-record it, write to `%LOCALAPPDATA%\Devicer\patches\<serial>\<timestamp>\`. Three branches:
  - **Magisk**: `cp boot.img /data/adb/magisk/ && cd /data/adb/magisk && KEEPVERITY=true KEEPFORCEENCRYPT=true sh boot_patch.sh boot.img`. Output `/data/adb/magisk/new-boot.img`.
  - **KernelSU**: `ksud boot-patch -b boot.img`. Output `kernelsu_patched_*.img`.
  - **APatch**: `apd patch -b boot.img`. Output `apatch_patched_*.img`.
- **Functional Patch page** (replaces stub): shows the detected device + root manager / version, a "Browse boot.img" picker, status updates with Cancel, output panel with the patched-image path + SHA256 + "Open folder" button. Pre-validation refuses to run if no root manager is installed.
- `PatchResult` model captures input path, output path, SHA256, file size, the root manager that did the patching, and its version — all the data needed to feed the future v0.7.0 flasher.

### Verified
- dotnet build clean (4 projects, 0/0).
- WPF app launches cleanly with the new Patch tab in nav.

### Architecture notes
- Subprocess wrapper boundary preserved: we never link any patcher binary into the .NET process; everything goes across the OS-level shell boundary, keeping Devicer's MIT license clean.
- The on-device-patcher path was chosen over a PC-side Python `Magisk_patcher` subprocess because (a) every rooted user already has the patcher on the device, (b) avoiding a Python runtime dependency keeps the single-binary install promise, (c) the on-device patcher is automatically version-correct (it ships with the user's installed Magisk).
- A v0.6.1 follow-up will add a PC-side patcher fallback for when the connected device has no installed root manager — needed when the user has obtained a boot.img out-of-band.

## v0.5.0 — 2026-05-09 (Partition backup)

### Added
- **Direct adb+root partition backup**:
  - `PartitionInfo` model (name, block-device path, size, isCritical, criticalReason). Critical-name set covers Samsung EFS / modem NV / persist / modem-state / FSC/FSG / DRM / keystore.
  - `AdbService.ListPartitionsAsync` — parses `ls -l /dev/block/by-name` via `su`, resolves each symlink to its block target, bulk-stats sizes via `blockdev --getsize64`. Critical partitions sorted to the top.
  - `BackupService.RunAsync` — for each selected partition: `dd if=… of=/data/local/tmp/devicer_<name>.img` via root, `adb pull`, SHA256 verify, accumulate manifest. Per-partition failures isolated (one bad block doesn't kill the run). Generous timeouts proportional to partition size.
  - `BackupManifest` JSON written to `%LOCALAPPDATA%\Devicer\backups\<serial>\<timestamp>\manifest.json` alongside the `.img` files (serial, model, codename, createdUtc, partitions[]: name/file/size/sha256/isCritical).
  - `AdbService` gains `RunShellAsync` / `RunSuAsync` / `PullAsync` helpers used by the backup orchestration. `Bash.Quote` POSIX shell-quoting helper for safe command construction.
- **Functional Backup page** (replaces stub):
  - Permanent red **EFS / NVRAM IS ONE-WAY** banner above the controls.
  - "Load partitions" button populates a checkbox list; critical partitions are pre-selected and badged.
  - Each row shows the partition name (mono), critical reason (subtle), size (right-aligned mono).
  - "Back up selected" runs the orchestration with status updates + coarse progress bar.
  - "Cancel" cooperative `CancellationToken`.
  - "Open folder" reveals the manifest folder in Explorer once the run completes.
  - Per-partition failure warnings rendered inline beneath the result.
- `Devicer.Smoke --partitions <serial>` flag prints the partition table for offline inspection.

### Verified
- Live partition listing against the connected Samsung S25 Ultra (SM-S938B, Magisk 30.7): 125 partitions enumerated; all 6 expected criticals (efs, fsc, fsg, modemst1, modemst2, persist) flagged correctly with their CRITICAL reasons. Sort order: critical-first, then alphabetical.
- `dotnet build Devicer.sln -c Release` clean (4 projects, 0/0).
- WPF app launches without XAML errors.

### Architecture notes
- We deliberately do NOT depend on tetherback / TWRP boot for v0.5.0. Most users have Magisk root on a working ROM and would rather not boot TWRP just to back up; the `dd`-via-root path covers them. tetherback subprocess integration is a v0.5.1 follow-up.
- Restore flow is deliberately deferred — writing back to block devices is asymmetric in risk vs. reading, and the v0.5.3 release will gate it behind explicit double-confirm + dry-run.

## v0.4.0 — 2026-05-09 (Custom ROM search)

### Added
- **Custom ROM search aggregator**:
  - `RomEntry` model unifies build metadata across sources (`Source`, `Kind`, `Version`, `BuildDate`, `SizeBytes`, `FileName`, `DownloadUrl`, `Sha256`, `Md5`, `Maintainer`, `ForumUrl`).
  - `IRomSource` interface with two production implementations:
    - `LineageOsRomSource` queries the official `https://download.lineageos.org/api/v1/<codename>/<romtype>/*` JSON endpoint across `nightly` + `weekly`. The `id` field is the SHA256 — surfaced verbatim.
    - `CrDroidRomSource` queries `https://raw.githubusercontent.com/crdroidandroid/android_vendor_crDroidOTA/<branch>/<codename>.json` across branches 16.0 / 15.0 / 14.0, capturing `download` URL, SHA256, MD5, build-type, maintainer, and the XDA forum thread.
  - `RomAggregatorService` fans out to every registered source in parallel, isolates per-source failures, merges + sorts newest-first, reports which sources had results.
- **ROMs page** (new sidebar nav item between Firmware and Backup): codename text field auto-fills from the connected device's `ro.product.device`. Search button triggers the aggregator. Each result is a Catppuccin card with Source + Kind chips (no pill backdrops — square `CornerRadius="6"` per global rule), version, filename, build-date, size, maintainer, "Open forum" link (if available) and a "Download" button that deep-links to the official mirror in the user's default browser.
- `Devicer.Smoke` `--roms <codename>` flag prints aggregated results to stdout for debugging / scripted use.

### Verified
- Live aggregation against `cheeseburger` (OnePlus 5): 5 builds returned, 3 LineageOS Nightly (22.2 builds, full SHA256 chain) + 2 crDroid Monthly (10.x + 11.x branches, SHA256 + MD5).
- Auto-fill against the connected S25 Ultra (`pa3q`): 0 builds (device is too new for either aggregator's index) — surfaced cleanly via the diagnostic banner with a "verify codename" hint.
- dotnet build clean across Debug + Release (4 projects, 0/0).

### Architecture notes
- Per-source failure isolation: any HTTP / JSON / timeout in one source returns an empty list rather than poisoning the aggregator. The `SourcesWithResults` list lets the UI tell the user which sources actually returned anything.
- Direct in-app download is deferred to v0.4.1 — for now the user clicks Download and their browser/download manager takes the SHA256-verifiable URL. The hash is displayed inline so post-download verification is a one-command shellout.
- PixelExperience and Evolution X both retired their public JSON feeds in the 2024-2025 cycle; we'll add them back when they re-publish a stable index.

## v0.3.1 — 2026-05-09 (Samsung firmware download — auth + decrypt)

### Added
- **Native FUS protocol implementation** (no Python, no JRE, single-binary preserved):
  - `FusCrypto` — AES-CBC nonce decode, signature derivation (`KEY_1[c % 16] + KEY_2`), AES-CBC PKCS#7 sign, `LOGIC_CHECK` per-char index, MD5 firmware-key derivation (V2 + V4).
  - `FusClient` — POST/GET session with rotating server NONCE + `Authorization: FUS …` header. Handles dual-keygen NONCE responses (Samsung's CDN serves under either `hqzdurufm2c8mf6bsjezu1qgveouv7c7` or the legacy `vicopx7dqu06emacgpnpy8j8zwhduwlh`); we try both and stay consistent across signature derivation.
  - `FirmwareDownloadService` — orchestrates BinaryInform → streaming download → SHA256 verify → AES-128-ECB decrypt → cache-index.
  - `FirmwareCipher` — chunked AES-ECB streaming decrypt, PKCS#7 unpad on the trailing block.
  - `FirmwareCache` — `%LOCALAPPDATA%\Devicer\firmware\<model>_<region>_<pda>\` with `index.json` manifest (encrypted/decrypted size, SHA256, completed timestamp).
- **Firmware-version normalization** (`FirmwareVersion.Normalized` — appends PDA as BOOT for 3-segment feeds; required by FUS BinaryInform).
- **IMEI auto-probe + manual entry**: `AdbService.ReadImeiAsync` calls `service call iphonesubinfo 1 i32 0` via root, parses the Parcel ASCII back to digits. Surfaces as `DeviceInfo.Imei`. Required because Samsung's FUS rejects the legacy `0000…` fake IMEI as of late 2024 (FUS Status 408 / Authentication Failed).
- **Functional Firmware page download flow**: "Download & decrypt" button, IMEI text field (auto-fills from probed device), live progress bar with phase + bytes-processed display, Cancel button (cooperative `CancellationToken`), "Open folder" on completion.
- **Devicer.Smoke `--inform` flag**: opt-in BinaryInform end-to-end probe without burning bandwidth (auth + metadata only).

### Verified
- AES round-trip self-test: `ABCDEFGHIJKLMNOP` encrypt+decrypt cleanly recovers via `--crypto-self-test`.
- Live FUS handshake against Samsung backend (SM-S938B / EUX): `NF_DownloadGenerateNonce.do` returns NONCE under the legacy KEY_1 generation; nonce decodes to a printable 16-char ASCII string (e.g. `zzva67fb8s0117ar`); BinaryInform Authorization header accepted.
- BinaryInform itself is reached on the wire (FUS Status 408 returns when the IMEI is missing/fake — exactly the documented late-2024 server behavior). Will pass with a real IMEI.
- dotnet build clean across Debug + Release (0/0).

### Architecture notes
- Decryption is **AES-128-ECB + PKCS#7 unpad** on the trailing block (NOT AES-CBC as the v0.3.0 ROADMAP entry stated — confirmed against samloader / Bifrost / SamFirm). v0.3.0 ROADMAP wording corrected.
- Samsung's CDN puts the `NONCE` response header into `HttpResponseHeaders.NonValidated` because base64 contains `/` and `+` which trip .NET's strict header validator. We iterate `NonValidated` directly — `TryGetValues` only matches known header descriptors.
- `Content-Type: application/xml` is sent without `charset=utf-8` (Samsung's parser is strict; the .NET `StringContent` ctor adds the charset automatically — we use `ByteArrayContent` to prevent it).
- Cookie jar disabled (`UseCookies = false`): Imperva tracking cookies in the response trigger their bot-detection on subsequent requests.

### Known limitations
- IMEI auto-probe via `service call iphonesubinfo` returns a permission-error Parcel on modern Samsung Android 14/15/16 (One UI 7+). Manual entry on the Firmware page is the practical workflow until we add an alternative read path (e.g., `/efs` parsing or a privileged shim app).

## v0.3.0 — 2026-05-09 (Samsung firmware lookup)

### Added
- **Samsung OTA latest-version lookup** (no auth, no Samsung account, no Python dep). New `Devicer.Core/Services/FirmwareCheckService.cs` queries `https://fota-cloud-dn.ospserver.net/firmware/<csc>/<model>/version.xml`, parses the XML, returns `LatestFirmware` with the latest `FirmwareVersion` (PDA/CSC/CP/Boot) and the upgrade history list.
- New `FirmwareVersion` record with `TryParse` (handles 3- and 4-segment slash-separated strings) and `ComparePda` (lexicographic ordering on Samsung's PDA strings).
- **Functional Firmware page** (replaces stub): auto-fills Model + CSC + currently-installed PDA from the selected device on the Device tab; "Check latest" button hits the OTA endpoint; results card shows latest PDA / CSC / CP / Boot, "Update available" badge if behind, and the full upgrade history list. Handles the 404 / empty-feed case with a warning banner.
- **Samsung PDA extraction**: `DeviceProbeService.ExtractSamsungPda` parses the AP firmware version from the build fingerprint's 5th '/' segment (e.g. `S938BXXS6BYIF_OXM6BYIF` → PDA `S938BXXS6BYIF` + CSC firmware `OXM6BYIF`). Prefers `ro.build.PDA` when present (older Samsungs). Surfaced on the Device tab as a new "Samsung PDA / CSC FW" field.
- `DeviceInfo` gains `SamsungPda` and `SamsungCscVersion` fields.
- Devicer.Smoke now prints OTA latest + behind/ahead/current status when a Samsung device is connected.

### Architecture notes
- Implementation is **Option C** from the v0.3.0 fork analysis: native C# HTTP client + XML parser, no external runtime dep. Latest-version endpoint is unauthenticated, so we ship this in v0.3.0 alpha. The auth-protected `NF_DownloadBinaryInform` and AES-CBC-encrypted download endpoints come in v0.3.1+.
- All firmware HTTP traffic stays on the host; no telemetry; no Samsung account needed.

### Verified end-to-end
- Live test against connected Samsung Galaxy S25 Ultra (SM-S938B / EUX): installed PDA `S938BXXS6BYIF` (Oct 2025) → latest from Samsung `S938BXXS9BZCH` (Mar 2026, build code `o=16`). Devicer correctly reports "BEHIND (update available)" with 16-entry upgrade history.
- dotnet build clean (Release, 0/0).
- WPF launches clean, no crashlog.

## v0.2.3 — 2026-05-09 (First-run wizard)

### Added
- **First-run wizard**: when `settings.firstRunCompleted == false`, a modal `FirstRunWindow` shows on launch with the Devicer logo, environment checks (live adb / fastboot detection with success/error glyphs), step-by-step USB-debugging enablement on the phone, and a privacy note (no data leaves the host). "Re-check" re-runs detection; "Get started" sets `firstRunCompleted = true` and continues to MainWindow.
- App startup converted from `StartupUri` to manual window creation in `App.OnStartup` to gate on first-run.
- Logo bundled into the App assembly via `<Resource Include="..\..\branding\logo.png" Link="Resources\Images\logo.png" />`.

### Verified
- dotnet build clean (Release, 0/0).
- `firstRunCompleted=false` → FirstRunWindow shows, MainWindow does not (process at ~127 MB, wizard-only).
- `firstRunCompleted=true` → MainWindow shows directly, no wizard (process at ~140 MB, full app + hot-plug timer).

## v0.2.2 — 2026-05-09 (Settings + Latte theme)

### Added
- **Settings store**: `AppSettings` (Theme / FirstRunCompleted / ProbeIntervalSeconds / LastDeviceSerial) persisted to `%LOCALAPPDATA%\Devicer\settings.json` with atomic write (`.tmp` → `File.Replace` → `.bak`).
- **Catppuccin Latte light palette**: full `App*` token surface mirrored from Mocha, runtime-swappable via `ThemeManager.Apply(theme)` (replaces merged-dictionary slot 0).
- **Settings page** (replaces stub): live theme picker (Mocha / Latte), probe-interval slider (2–30 s), platform-tools detection (adb + fastboot status with re-check button), about section (version / settings file / crashlog / tool cache paths), "Open data folder" launches Explorer.
- Theme persists across launches; applied before first window paint to avoid theme flash.
- Probe interval changes propagate live into `DeviceViewModel.SetProbeInterval`; throttle dynamically scales to 80% of the configured interval.

### Verified
- dotnet build clean (Release, 0/0).
- Fresh install creates `settings.json` with sensible defaults.
- Latte theme loads without XAML resolution errors (cleared crashlog after Latte launch).
- Hot-plug timer respects user-configured interval.

## v0.2.1 — 2026-05-09 (UX polish)

### Added
- **Hot-plug detection**: 4-second `DispatcherTimer` re-probes adb / fastboot when not already probing, with a 3.5s minimum gap between probes. Plug or unplug a phone and the Device tab updates automatically — no manual Refresh.
- **Probe status indicator**: animated accent stripe + live status text ("Probing adb / fastboot…", "1 device connected", "No devices") next to the heading.
- Selection persistence across refreshes: when the same serial reappears, the previous device stays selected.

### Fixed
- **Removed pill / oval backdrops** from `Badge` styles in `ThemeStyles.xaml` (status badges and root indicators were `CornerRadius="999"`). All badges now use `CornerRadius="4"` with subtle 1 px tinted borders. Variants differentiate by color, not shape. Per global rule.

## v0.2.0 — 2026-05-09 (alpha shell)

### Added
- Solution scaffold: `Devicer.sln` + `src/Devicer.Core` + `src/Devicer.App` + `tools/Devicer.Smoke`.
- **Devicer.Core**: `IShellRunner` + `ShellRunner` (timeout-bounded subprocess), `AdbService`, `FastbootService`, `DeviceProbeService`. Models: `DeviceInfo`, `RootStatus` (Magisk / KernelSU / APatch / Other / None), `ConnectionState` (NotConnected/Adb/Recovery/Sideload/Fastboot/Bootloader/Download/Unauthorized/Unknown).
- **Devicer.App**: WPF shell on .NET 10 (`net10.0-windows10.0.22621.0`), Catppuccin Mocha theme + ThemeStyles, sidebar nav with six pages (Device, Firmware, Backup, Patch, Flash, Settings). Crash logger writes to `%LOCALAPPDATA%\Devicer\crashlog.txt`.
- **Device page**: probes adb + fastboot on launch, surfaces 14+ device fields (model, codename, Android+SDK, build fingerprint, security patch, Samsung CSC + country, bootloader, baseband, slot, A/B flag, encryption, OEM unlock, Knox warranty bit, Magisk/KernelSU/APatch root status). Knox bit shows green badge when `0` (intact), red badge when tripped.
- `Devicer.Smoke` console tool — exercises the Core pipeline against the connected device for CI / local verification independent of the WPF shell.
- DPI-aware app manifest (PerMonitorV2), long-path-aware.

### Decisions (locked)
- **Stack**: C# / .NET 10 WPF, TFM `net10.0-windows10.0.22621.0`, MVVM via `CommunityToolkit.Mvvm` 8.4.0.
- **Architecture**: subprocess wrappers for ALL backend tools (Bifrost, Thor, tetherback, Magisk_patcher). Linking would force Devicer to GPL-3.0 because Thor is GPL-3.0; subprocess across the OS process boundary preserves MIT.
- **Tool delivery**: lazy download to `%LOCALAPPDATA%\Devicer\tools\<tool>\<version>\` with SHA256 pin. Platform-tools detected on PATH; user-prompted if absent.

### Verified
- Build clean: 2 projects, 0 warnings, 0 errors (Debug + Release).
- App launches in <1s, ~130 MB working set, no crashlog.
- Device probe end-to-end against live Samsung Galaxy S25 Ultra (SM-S938B / codename `pa3q`, Android 16, CSC EUX/GB, build fingerprint `samsung/pa3qxxx/pa3q:16/BP2A.250605.031.A3/...`, security patch 2025-10-01, Magisk 30.7 detected via `su -c 'magisk -c'`, Knox bit `0` = intact). All 14 fields populated correctly via `Devicer.Smoke`.

### Notes
- Settings, Firmware, Backup, Patch, Flash pages are stubs that describe their planned scope and target version.
- ROM-search desktop aggregator (still the gap from the research) is v0.4.0 work.

## v0.1.0 — 2026-05-09 (research)

### Added
- Initial repo scaffold (README, LICENSE, .gitignore, CHANGELOG, ROADMAP).
- [docs/research.md](docs/research.md) — 2026 Android-rooted-phone tooling landscape: tool matrix by job, AVOID list, Knox reality check, recommended Samsung toolchain, universal-mode swap.
- ROADMAP for v0.2.0 → v0.8.0 phased build.
- Branding logo prompt placeholders.

### Decisions
- Build = orchestration shell, not from-scratch reimplementation.
- Stack proposal: C# / .NET 10 WPF (locked in v0.2.0).
- Integrated tools: Bifrost + Thor + tetherback + Magisk_patcher + Platform-Tools.
