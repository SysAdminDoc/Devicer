# Changelog

All notable changes to Devicer are documented here. Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versions follow [SemVer](https://semver.org/).

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
