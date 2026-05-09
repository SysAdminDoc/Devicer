# Changelog

All notable changes to Devicer are documented here. Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versions follow [SemVer](https://semver.org/).

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
