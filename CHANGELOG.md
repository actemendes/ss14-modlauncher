# Changelog

## 0.1.0 — 2026-10-09

First public release of **SS14 ModLauncher by actemendes**, evolving the local-mod installer into a Windows x64 launcher.

- Mod selection and saved profiles, with bundled Crew Console and Hello World.
- Russian and English launcher interface and bundled mod labels.
- Original-loader backups and SHA-256 validation for install/restore.
- Optional reversible Steam launch integration, plus explicit adoption of verified upstream files with archived old backups.
- Local diagnostic report export and a one-session launch without mods.
- Manual GitHub Release mod update checks, with manifest and download validation; the default source is `actemendes/ss14-modlauncher`.
- A self-contained Windows package, local tests, and public user/developer documentation.

The existing Crew Console includes sensor health/position history and a draggable damage timeline. Complete web Crew Monitor replay, rooms, zones, recording files, and message history remain outside this release.

Release verification and compatibility limits are recorded in [docs/verification.md](docs/verification.md).
