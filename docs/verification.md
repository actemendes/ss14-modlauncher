# Verification record — 0.1.x

Date: **2026-10-09**. Platform: **Windows x64**. Build toolchain: **.NET SDK 10.0.400**, self-contained .NET / Windows Desktop runtime **10.0.11**. Live game: **SS14 Launcher 0.40.2**, **Robust 275.0.0**, local Dev/Sandbox server bound to loopback with hub advertising disabled.

Game and Steam checks below cover the 0.1.0 implementation. Version 0.1.1 changes release discovery when the anonymous GitHub API quota is exhausted; mod and installation behaviour are unchanged.

## Completed checks

| Area | Result |
| --- | --- |
| Crew history, timeline, layout and map projection | 26 regression checks passed |
| Installer and restore core | 24/24 cases passed: 21 core scenarios plus real-loader, published Steam handoff and self-contained executable tests |
| Bootstrap selection | Selection validation, path confinement, SHA-256 validation and clean-session bypass passed |
| Catalog, profiles and updates | Settings/profile persistence, semantic versions, HTTP pipeline, manifest/source/download bounds, rate-limit fallback and invalid update rejection passed |
| Self-contained package | Extracted EXE installed, inspected and restored a disposable installation with isolated runtime variables; host trace confirmed use of bundled runtime |
| Original loader compatibility | Install/restore on a copied real SS14 loader restored exact original bytes |
| Live Crew Console, English | Only Crew Console loaded; native console, portrait layout, crew row, map and health card displayed; loss of server telemetry preserved history and showed no telemetry |
| Live Crew Console, Russian | Only Crew Console loaded through the actual Steam chain; current sensor count, health card, map, landscape layout, historical point selection, dragging the timeline and return to Live passed |
| Live Hello World, Russian | Only Hello World loaded; F1 opened and closed its localized window; native crew console remained unchanged |
| Live empty selection | A fresh client connected with no verified mods; bootstrap continued with the standard client and the native crew console |
| Actual Steam integration | Steam Play opened the stable ModLauncher; its Launch action opened the preserved original launcher, which connected the client to the local server |
| Actual Steam restoration | Product restore returned exact original loader and launcher bytes and disabled the bridge; the pre-test legacy installation was then reinstated, matching all 92 baseline files by path and SHA-256; Steam Play then opened the original launcher normally |
| Published executable handoff fixture | Stable launcher handoff and restoration while that UI remained open passed |
| Packaging | Self-contained EXE and ZIP, runtime/dependency notices, MIT license, documentation and artwork present; release asset SHA-256 checksums verified |
| Documentation and source audit | Relative links checked; publication files/history checked for secrets and proprietary game binaries |
| Build version guard | An inconsistent requested version was rejected before output mutations |
| GitHub CI | Windows build and test workflow passed on the published source |

The live sessions used the game's own sensor network and locally spawned test NPCs. A disposable test server with an independent power supply was used after the Dev map's original sensor server lost power. No public multiplayer server was used for these tests. The original installation and ModLauncher settings were restored; the local test server was stopped.

## Publication verification

Release packages are built from the clean Git revision recorded in `build-info.json`. All six public **0.1.0** assets were downloaded anonymously and matched the original build and `SHA256SUMS.txt`. The downloaded ZIP was inspected and its self-contained EXE completed install/restore on an isolated copy with exact original hashes.

The initial anonymous API update check encountered GitHub's shared-IP quota limit (HTTP 403, remaining quota 0). Version **0.1.1** adds a public latest-release asset fallback. Its regression tests cover successful discovery/download, ordinary errors that must not fall back, canonical tag identity, foreign repositories, prereleases, invalid manifests, direct CDN routes, size limits and redirect loops. An independent code review found no blocking issues. Before packaging 0.1.1, the updated production UpdateService was exercised against the public 0.1.0 release: actual API 403 led to the canonical public manifest, both DLLs downloaded and matched the release bytes and SHA-256 values, and a repeated check returned zero updates.

Post-publication results for the current package are recorded in this [online verification record](https://github.com/actemendes/ss14-modlauncher/blob/main/docs/verification.md) and its release notes.

## Scope and remaining compatibility limits

- These results cover the tested Windows installation and engine version, not every server fork, future SS14 update or Steam library layout.
- Russian and English native UI and both crew console layouts were inspected at the desktop's current display settings. All Windows DPI combinations were not tested.
- The self-contained trace confirms the bundled runtime on this computer; a fresh Windows machine without an installed .NET runtime was not available.
- The application is not digitally signed by a public code-signing certificate. Antivirus reputation and SmartScreen behaviour vary by machine.
- Normal authentication and engine signature verification were preserved. No claim is made that every possible argument accepted by the original launcher was individually exercised.

See the [release checklist](releasing.md) for future versions.
