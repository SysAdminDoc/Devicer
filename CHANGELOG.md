# Changelog

All notable changes to Devicer are documented here. Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versions follow [SemVer](https://semver.org/).

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
