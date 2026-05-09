# Devicer — 2026 Tooling Research

**Date**: 2026-05-09
**Scope**: Windows-side tools for managing a rooted Samsung Android phone (with universal-mode notes for Pixel/OnePlus/Xiaomi). Foundation document for the Devicer build plan.

The user wants ONE tool, or the smallest possible toolchain, that covers all four jobs:

1. **Identify** the device + currently-installed ROM/firmware (model, CSC, build fingerprint, bootloader, baseband, root status, Magisk/KernelSU presence, slot, encryption state).
2. **Search for / download** official firmware AND custom ROMs for that exact model — stock Samsung firmware (per CSC), LineageOS / crDroid / PixelExperience-style custom ROMs, kernels, recovery images.
3. **Back up** the phone before flashing — partitions, EFS/NV (critical for Samsung — losing EFS bricks IMEI), userdata, Magisk modules, app data.
4. **Patch + flash** — patch boot.img with Magisk, flash via Odin OR a friendlier alternative, with safety nets (don't wipe EFS, don't trip Knox unnecessarily, verify checksums).

---

## Tool matrix by job

| Tool | License / cost | Status (2026) | Job 1: ID | Job 2: FW search | Job 2: FW download | Job 2: Custom ROM | Job 3: Backup | Job 4: Patch | Job 4: Flash | Samsung-only? | Knox impact |
|---|---|---|---|---|---|---|---|---|---|---|---|
| [Bifrost (SamloaderKotlin)](https://github.com/zacharee/SamloaderKotlin) | OSS GPL, free | **ACTIVE** v2.1.0 (2026-03-16) | — | yes (model+CSC) | yes (decrypts inline) | — | — | — | — | Samsung | none (download only) |
| [Samloader](https://xdaforums.com/t/tool-samloader-samfirm-frija-replacement.4105929/) (Python CLI) | OSS GPL, free | semi-active forks; original `nm111` account deleted — use a maintained fork | — | yes | yes | — | — | — | — | Samsung | none |
| [Frija](https://xdaforums.com/t/tool-frija-samsung-firmware-downloader-checker.3910594/) | freeware, Win-only, closed src | works but stale; no source | — | yes | yes | — | — | — | — | Samsung | none |
| [Samfirm AIO](https://samfirms.com/samfirm-tool) / [SamFw Tool](https://samfw.com/frp) | freeware + paid credits | active but **AVOID** for legit work | — | yes | yes | — | — | — | yes (Odin shell) | Samsung | FRP/Knox-removal features = grey-market; clones routinely bundle malware |
| SmartSwitch | freeware Samsung | active | partial (model only) | stock only | yes | — | partial (apps/contacts, no partitions) | — | OTA-style only | Samsung | none |
| [Odin3](https://en.wikipedia.org/wiki/Odin_(firmware_flashing_software)) (leaked) | closed, free | works on Win; no source | — | — | — | — | — | — | yes | Samsung | won't trip Knox if BL still locked & official BL flashed; trips on custom AP |
| [Thor Flash Utility](https://github.com/Samsung-Loki/Thor) | OSS, .NET 7, free | **ACTIVE**, Win/Linux/macOS | — | — | yes (built-in fw download+flash) | — | — | — | yes (.tar/.tar.md5/.lz4) | Samsung | same as Odin |
| [odin4](https://xdaforums.com/t/tool-linux-odin4-open-source-samsung-flashing-tool-odin-alternative.4781517/) | OSS, Linux-only | active | — | — | — | — | — | — | yes (CLI) | Samsung | same as Odin |
| [Galaxy Flasher](https://codeberg.org/ethical_haquer/Galaxy-Flasher) | OSS GPLv3, Flatpak | ACTIVE (Linux only — moved to Codeberg) | — | — | — | — | — | — | yes (frontend for Thor/odin4/Heimdall) | Samsung | same |
| [Heimdall (Grimler fork)](https://git.sr.ht/~grimler/Heimdall) | OSS, free | v2.0.2 — fork active; upstream Dobell repo dormant since ~2017 | — | — | — | — | — | — | yes | Samsung | same |
| [SDK Platform-Tools v37](https://developer.android.com/tools/releases/platform-tools) + `getprop` / `fastboot getvar` | free, Google | current (Apr 2026) | yes (build fp/slot/BL) | — | — | — | — | — | yes (fastboot) | universal | none |
| [tetherback](https://github.com/dlenski/tetherback) | OSS Python, free | maintained (niche) | — | — | — | — | yes (TWRP partition images→PC, md5 verified) | — | — | universal | none |
| [Magisk-Boot-Patcher (0xsharkboy)](https://github.com/0xsharkboy/Magisk-Boot-Patcher) / [Magisk_patcher (affggh)](https://github.com/affggh/Magisk_patcher) | OSS, free | active | — | — | — | — | — | yes (PC-side patches AP/boot.img) | — | universal | n/a |
| [Magisk app v30.7](https://topjohnwu.github.io/Magisk/install.html) on phone (canonical) | OSS, free | active (Feb 2026) | — | — | — | — | — | yes | — | universal | n/a |

**ROM aggregators are still browser work** — no Windows desktop app aggregates LineageOS / crDroid / PixelExperience together. Closest things are the [LineageOS wiki](https://wiki.lineageos.org/devices/), [lineageosdevices.com](https://lineageosdevices.com/), and the [XDA sortable list](https://xdaforums.com/t/list-of-lineageos-devices-presented-in-a-convenient-way-sortable-filterable.3941113/). **This is an explicit gap that Devicer should fill.**

---

## AVOID list (2026)

- **Samfirm AIO / SamFw paid** — legit-ish core, but the ecosystem is FRP-bypass / Knox-removal / IMEI-repair grey market; clones bundle malware ([details](https://drfone.wondershare.com/bypass-samsung-frp/samfirm-aio-and-alternative.html)). Not appropriate for managing your own personal phone.
- **Z3X / Octoplus / Chimera** — carrier-unlock dongles; overkill and licensed per-credit.
- **Heimdall mainline (Benjamin-Dobell)** — dormant since ~2017; use Grimler's fork or skip to Thor.
- **LegacyThor** — archived by author; superseded by current Thor rewrite.
- **SamFw "Free Tool"** for newer firmware — exploit patched since Jan 2023; success rate on Android 14+ near zero.
- **Random "SamFirm Tool" mirrors on YouTube/AndroidFileHost** — frequently trojaned.
- **One UI 8 / Android 16 on S25 / Z Fold7 / Z Flip7** — Samsung removed the OEM unlock toggle entirely; do not OTA up if you want to stay rooted ([XDA reference](https://xdaforums.com/t/april-17-2026-platform-tools-v37-0-0-unlock-bootloader-root-pixel-7-pro-cheetah-stable-firmware-play-integrity.4502805/page-29)).

---

## Knox reality check

Tripping is **one-way, hardware eFuse, only PBA replacement reverses it** ([Chainfire writeup](https://chainfire.eu/articles/796/More_on_KNOX_warranty_void), [Samsung Knox docs](https://docs.samsungknox.com/admin/knox-platform-for-enterprise/faq/)).

- **Trigger** = unlocked bootloader + custom AP/kernel.
- Flashing **stock** firmware via Odin/Thor with BL still locked does **not** trip.
- Once tripped: Pay / Secure Folder / Knox attestation gone permanently.

Devicer must surface Knox state prominently and gate any action that risks tripping it behind an explicit confirm.

---

## Recommended 2026 toolchain

**Three tools cover all four jobs cleanly:**

1. **[Bifrost](https://github.com/zacharee/SamloaderKotlin)** — Job 2 (firmware lookup + decrypted download per CSC). Active, OSS, GUI, cross-platform. Replaces Frija / SamFirm / Samloader.
2. **[Thor Flash Utility](https://github.com/Samsung-Loki/Thor)** — Job 4 flashing (BL/AP/CP/CSC tar, .lz4 native, EFS-Clear gated, can also auto-download fw). Active, OSS, Win/Linux/macOS. Replaces Odin/Heimdall.
3. **[SDK Platform-Tools v37](https://developer.android.com/tools/releases/platform-tools) + [tetherback](https://github.com/dlenski/tetherback) + [Magisk-Boot-Patcher](https://github.com/0xsharkboy/Magisk-Boot-Patcher)** — Job 1 (`adb shell getprop ro.build.fingerprint`, `getprop ro.csc.sales_code`, `fastboot getvar current-slot`, `magisk --version` via `adb shell su`), Job 3 (tetherback streams TWRP partition images to host with md5 verification — **including EFS** if you specify it; explicitly back up `/efs` and modem NV before any AP flash), and Job 4 patching (PC-side patch of stock AP boot.img / init_boot.img — replaces the "push to phone, patch in app, pull back" dance).

### Closest single tool

**Thor Flash Utility** — the only one that does firmware download *and* flash *and* PIT inspection *and* DevInfo (model/CSC/serial) in one binary. Does not do backups or Magisk patching, so a Thor-only flow leaves Jobs 1 (partial) and 3 + Magisk patch uncovered. **This is the gap Devicer fills.**

### Canonical Magisk workflow (2026)

Still: extract AP from firmware → patch boot.img → flash via Thor/Odin. The "push to phone, patch in app, pull back" step is now optional — `Magisk-Boot-Patcher` and `affggh/Magisk_patcher` automate it on the host. KernelSU's `ksud boot-patch` does the same natively if you switch root solutions ([KernelSU docs](https://kernelsu.org/guide/installation.html)).

---

## Universal mode (non-Samsung)

Thor / Bifrost / Samloader are Samsung-only (download-mode protocol). For Pixel / OnePlus / Xiaomi / etc., Platform-Tools + tetherback + Magisk-Boot-Patcher all carry over; replace Thor with `fastboot flash` / `fastboot update`, replace Bifrost with the OEM's factory image portal ([Google Android Flash Tool](https://flash.android.com/), OnePlus MSM, Xiaomi MiFlash).

Devicer's v0.8.0 universal mode reuses the same shell with a different backend pipeline.

---

## Implications for Devicer's design

1. **Orchestration over reimplementation** — every backend tool above earned its slot; don't rewrite samloader/Heimdall/Magisk in C#.
2. **Samsung-first, universal later** — Odin protocol is the hard problem; solve it well before generalizing.
3. **Fill the ROM-search gap natively** — there is no desktop aggregator. Devicer's ROM-search panel is differentiating value.
4. **Backup-before-flash is non-negotiable** — Samsung EFS loss = bricked IMEI. UI must enforce the order.
5. **Surface Knox state on every relevant screen** — eFuse trip is permanent; never let a user flash a custom AP without seeing it.

---

## Sources

- [Thor Flash Utility — GitHub](https://github.com/Samsung-Loki/Thor)
- [Thor on XDA](https://xdaforums.com/t/dev-thor-flash-utility-the-new-samsung-flash-tool.4597355/)
- [Galaxy Flasher — Codeberg](https://codeberg.org/ethical_haquer/Galaxy-Flasher)
- [odin4 — XDA](https://xdaforums.com/t/tool-linux-odin4-open-source-samsung-flashing-tool-odin-alternative.4781517/)
- [Heimdall — Benjamin-Dobell GitHub](https://github.com/Benjamin-Dobell/Heimdall) / [Grimler fork](https://git.sr.ht/~grimler/Heimdall)
- [Bifrost / SamloaderKotlin](https://github.com/zacharee/SamloaderKotlin)
- [Samloader XDA thread](https://xdaforums.com/t/tool-samloader-samfirm-frija-replacement.4105929/)
- [Frija XDA thread](https://xdaforums.com/t/tool-frija-samsung-firmware-downloader-checker.3910594/)
- [SamFw Tool review](https://www.tuneskit.com/unlock-android/samfw-tool-review.html)
- [Samfirm AIO review](https://drfone.wondershare.com/bypass-samsung-frp/samfirm-aio-and-alternative.html)
- [tetherback](https://github.com/dlenski/tetherback)
- [Magisk-Boot-Patcher](https://github.com/0xsharkboy/Magisk-Boot-Patcher) / [affggh/Magisk_patcher](https://github.com/affggh/Magisk_patcher)
- [Magisk install docs](https://topjohnwu.github.io/Magisk/install.html) / [KernelSU install](https://kernelsu.org/guide/installation.html)
- [Knox warranty void mechanism — Chainfire](https://chainfire.eu/articles/796/More_on_KNOX_warranty_void) / [Samsung Knox FAQ](https://docs.samsungknox.com/admin/knox-platform-for-enterprise/faq/)
- [LineageOS wiki devices](https://wiki.lineageos.org/devices/) / [lineageosdevices.com](https://lineageosdevices.com/) / [XDA sortable list](https://xdaforums.com/t/list-of-lineageos-devices-presented-in-a-convenient-way-sortable-filterable.3941113/)
- [Platform-Tools v37 — XDA](https://xdaforums.com/t/april-17-2026-platform-tools-v37-0-0-unlock-bootloader-root-pixel-7-pro-cheetah-stable-firmware-play-integrity.4502805/page-29)
