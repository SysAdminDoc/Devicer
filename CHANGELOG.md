# Changelog

All notable changes to Devicer are documented here. Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versions follow [SemVer](https://semver.org/).

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
