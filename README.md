![Devicer banner](assets/brand/devicer-readme-banner.png)

# Devicer

[![Version](https://img.shields.io/badge/version-2.2.2-38d9ff.svg)](https://github.com/SysAdminDoc/Devicer/releases/tag/v2.2.2)
[![License](https://img.shields.io/badge/license-MIT-8bd5ca.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-4f7cff.svg)](#requirements)
[![Build](https://img.shields.io/badge/build-self--contained-8b5cf6.svg)](#install)

Devicer puts Android firmware and device tools in one Windows app. Inspect a phone, compare Samsung firmware across regions, or prepare a partition backup before moving to patching and flash plans. It's alpha software for experienced Android users, with the risks shown beside the controls.

[**Download Devicer v2.2.2 for Windows x64**](https://github.com/SysAdminDoc/Devicer/releases/latest/download/Devicer-v2.2.2-win-x64.zip)

## See it in action

![Devicer identifies a sample Samsung phone and reports its software, root, encryption, and Knox state](assets/screenshots/01-device.png)

The Device view gathers the details you usually chase through separate ADB commands. The sample serial makes the representative capture easy to distinguish from a live phone.

| Firmware lookup | ROM discovery |
|---|---|
| ![Devicer compares sample Samsung firmware across two CSC regions](assets/screenshots/02-firmware.png) | ![Devicer lists representative LineageOS builds for a device codename](assets/screenshots/03-roms.png) |

| Critical backup | Flash safety review |
|---|---|
| ![Devicer preselects critical EFS, modem, and persistent partitions](assets/screenshots/04-backup.png) | ![Devicer shows Knox and bootloader warnings before an Odin flash](assets/screenshots/05-flash-safety.png) |

These screenshots come from the production Windows executable on isolated desktops at 125% DPI. Device details, firmware builds, and ROM results are representative data, not a compatibility list or a completed flash. Capture mode uses temporary preferences and an empty IMEI cache, with polling and external tool commands disabled. Settings paths use `%LOCALAPPDATA%` labels. The [capture record](assets/screenshots/capture-report.json) identifies the exact executable and six screenshots by SHA-256.

## What it handles

| Workflow | What Devicer does |
|---|---|
| Device insight | Detects ADB, fastboot, recovery, sideload, bootloader, and Samsung Download mode. Reports model, build, root, encryption, slots, CSC, Knox, and bootloader state. |
| Official firmware | Checks Samsung's public FUS feed by model and CSC, compares the installed PDA, downloads the encrypted package, then decrypts it locally. |
| Custom ROMs | Searches LineageOS and crDroid by codename. Downloads stay in the app, with SHA-256 or MD5 verification when the source publishes a digest. |
| Backup and restore | Finds block partitions through root, preselects EFS and other critical data, verifies each backup, and checks hashes before restore. |
| Boot patching | Sends `boot.img` or `init_boot.img` to Magisk, KernelSU, or APatch. A PC-side Magisk patcher is available when the phone is not rooted. |
| Flash planning | Inspects Odin archives, builds dry runs, gates destructive Thor actions, and manages fastboot image queues with slot and AVB options. |

A ROM result with a published hash is marked "SHA-256 available". Verification happens after the download, not when the search result appears.

## Why use it

- One interface replaces a pile of terminal commands and browser tabs.
- Critical Samsung partitions are called out before a flash, with EFS clear disabled by default.
- External GPL tools run as separate processes. Devicer remains MIT licensed and each backend can be updated independently.
- Device data, cached firmware, manifests, and logs stay under your Windows profile. No Devicer account is required.

## Install

1. Download `Devicer-v2.2.2-win-x64.zip` from the [latest release](https://github.com/SysAdminDoc/Devicer/releases/latest).
2. Verify the matching SHA-256 file if you want to confirm the download.
3. Extract the ZIP and run `Devicer.exe`.
4. Connect a phone with a data-capable USB cable. Enable USB debugging, unlock the phone, and approve the computer when Android asks.

The Windows build is self-contained, so you do not need to install .NET. It is not code signed because no signing certificate is available for this project. Windows SmartScreen may show an unrecognized publisher notice. Check the published SHA-256 digest before running it.

The ZIP includes this guide, its screenshots, and the complete [original artwork archive](assets/brand/concepts/README.md).

## Requirements

- Windows 10 or Windows 11, x64
- Android SDK Platform Tools v37 or newer on `PATH`
- A data-capable USB cable and the correct OEM USB driver
- Root access for raw partition backup, restore, and on-device boot patching

Firmware lookup and ROM search can work without root. Flashing also depends on the target device, an unlocked bootloader where required, and a compatible backend such as Thor, Heimdall, or fastboot.

## Safety model

Devicer cannot make flashing risk-free. It can make the plan visible.

- Dry runs show the archive entries or fastboot images before execution.
- EFS clear starts off and requires an explicit choice.
- Knox, One UI bootloader restrictions, Play Integrity impact, and AVB choices are shown near the controls that matter.
- Restore checks the saved manifest and SHA-256 values before writing partitions.

Back up EFS, modem state, `persist`, and any device-specific calibration data before changing firmware. Never flash an image built for another model or bootloader revision.

Don't treat a downgrade as an unlock method. Samsung's hardware rollback protection can reject older bootloaders, and its Knox warranty bit records unsupported software changes permanently. Read [Samsung's hardware-security explanation](https://docs.samsungknox.com/admin/fundamentals/whitepaper/samsung-knox-mobile-security/system-security/hw-backed-security/) before planning a change. App behavior after modification also depends on the [integrity verdicts](https://developer.android.com/google/play/integrity/verdicts) each app requires.

## Build from source

Devicer uses C# with .NET 10 WPF and CommunityToolkit.Mvvm.

```powershell
dotnet restore Devicer.sln
dotnet test Devicer.sln -c Release
dotnet run --project src/Devicer.App -c Release
```

Build the release package locally:

```powershell
pwsh tools/build-release.ps1 -SelfContained -BuildOnly
```

Capture the built executable on private desktops, without a connected phone:

```powershell
dotnet run --project tools/Devicer.MarketingCapture -c Release -- `
  --app dist/Devicer-v2.2.2-win-x64.exe `
  --output build/marketing-candidate
```

Review the six captures before replacing `assets/screenshots` with the PNGs and report. Then run `pwsh tools/build-release.ps1 -SelfContained -PackageOnly`. Package checks reject stale captures, missing guide artwork, and changes to the archived originals. Use `tools/build-brand-assets.ps1` only when intentionally exporting brand sizes; the selected identity and original concepts are already saved.

Run `pwsh tools/test-marketing.ps1 -Executable dist/Devicer-v2.2.2-win-x64.exe -Version 2.2.2` to exercise the package checks against deliberately invalid fixtures. The test uses temporary copies and leaves the original artwork alone.

## Project layout

```text
src/Devicer.Core/            Device models, services, parsers, and tool wrappers
src/Devicer.App/             WPF application, pages, themes, and view models
tests/Devicer.Core.Tests/    Unit and regression tests
tools/Devicer.Smoke/         Hardware and public-feed smoke commands
tools/Devicer.MarketingCapture/  Private-desktop screenshot runner
assets/brand/                Master identity, banner, social card, and icon family
assets/screenshots/          Current production UI captures
```

## Current limits

Devicer is an alpha release for experienced Android users. A wrong image, interrupted write, locked bootloader, or anti-rollback rule can leave a phone unbootable. Samsung firmware downloads still require a valid IMEI, and the availability of third-party tools can vary by device family.

## License

Devicer is available under the [MIT License](LICENSE). Thor Flash Utility and other optional tools keep their own licenses and run outside the Devicer process.
