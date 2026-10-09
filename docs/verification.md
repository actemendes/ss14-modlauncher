# Verification record — 0.1.x

Date: **2026-10-09**. Platform: **Windows x64**. Build toolchain: **.NET SDK 10.0.400**, self-contained .NET / Windows Desktop runtime **10.0.11**. Live game: **SS14 Launcher 0.40.2**, **Robust 275.0.0**, local Dev/Sandbox server bound to loopback with hub advertising disabled.

Game and Steam checks below cover the 0.1.0 implementation. Version 0.1.1 changes release discovery when the anonymous GitHub API quota is exhausted; mod and installation behaviour are unchanged in that version. Version 0.1.2 changes the default Steam installation policy; its separate checks are recorded below.

## 0.1.4 — core and UI checks completed; package checks pending

This change adds first-run setup to launcher 0.1.4. The bundled mod DLLs and bootstrap remain 0.1.2.

- **30/30 installer core cases passed** with the catalog, bootstrap, update and startup-notification regression suites.
- New setup checks passed for discovery/root normalization, persisted per-root completion/dismissal, missing-folder context, installed/restored suppression, changed-loader handling and corrupt settings.
- Running/uninspectable-process readiness and retry checks passed using the existing injected exception contract. These tests do not start or kill game processes.
- **20 actual WinForms setup scenarios passed:** fresh RU/EN setup, missing-folder and retry flows, Later/window-close dismissal, language switching, read-only settings, recovery, native 96 DPI with simulated 125%/150% scaling, minimum width, existing-installation suppression, disabled onboarding and update coexistence.
- The UI harness exercised the production install, settings save and `StartGame` path against a disposable fixture with a benign launcher stub. Failed-launch retry passed. This confirms the launch handoff, not live gameplay.
- The existing **eight update-notification UI scenarios passed**. The setup suite also passed after final focus/colour polish; package/publication checks remain separate.
- Documentation relative links and whitespace checks passed. Bootstrap project, mod catalog versions and published artifact ledger remain unchanged.

Still pending: packaged executable installation/restoration, final artifact hashes, source provenance and public download verification.

No new live gameplay or publication check is claimed for 0.1.4. Historical results below remain associated with their recorded versions.

## 0.1.3 — completed checks and publication

This change adds startup notifications and independently versioned releases. Launcher 0.1.3 targets the unchanged published 0.1.2 mod DLLs; prior live-game evidence remains associated with 0.1.0. No new live gameplay pass is claimed.

- **33/33 installer and packaged-executable checks passed**, together with startup-notification and update-schema suites. This includes the copied real loader, published Steam handoff and self-contained CLI installation/restoration.
- Startup tests cover default-on settings migration, saved opt-out, corrupt-file preservation, a nonblocking single attempt, quiet offline errors, cancellation and stale-result suppression for repository, installation, version and preference changes. Metadata checking does not download/apply updates or mutate installation/settings files.
- Update-schema checks cover independent launcher/mod versions, compatible versus blocked mods, per-mod requirements and conservative global requirements for older clients.
- **Eight actual WinForms UI scenarios passed:** RU/EN mod-only notifications, mixed compatibility, launcher-only notification, offline status, disabled checking, cancellation and preservation of an unsaved installation path. Updated RU/EN layouts were also inspected.
- Synthetic build tests passed for a new mod version **2.4.0** while bootstrap stays **0.1.2**, and for reuse of locked published mod bytes even when their old source is deliberately unbuildable.
- Documentation links and whitespace checks passed.

- An actual isolated mod-only build used feed **1.5.1**, launcher **0.2.0** hosted under **v1.5.0**, and mod **2.4.0** with Bootstrap **0.1.2**. It emitted no launcher ZIP and left existing payload files untouched. A separate build reused the real published 0.1.2 mod DLLs byte-for-byte.
- Update test fixtures use explicit installed-version baselines. The entire core suite also passed in an isolated copy with both catalog mod versions set to **2.4.0**, confirming that future mod versions do not invalidate the fake older release fixtures. This test-only follow-up does not change published binaries.
- Release: [v0.1.3](https://github.com/actemendes/ss14-modlauncher/releases/tag/v0.1.3), source commit `6d2b231e15d65408813fc68fabc52bd06b857e8c`; `sourceDirty: false`. The public tag matches this commit and [Windows CI](https://github.com/actemendes/ss14-modlauncher/actions/runs/37934149654) passed.
- All six public assets downloaded anonymously and matched the local bytes, sizes and checksums. The manifest correctly separates launcher **0.1.3**, mod versions **0.1.2**, and minimum launcher **0.1.2**; both mod hashes match the existing published artifact ledger.
- The downloaded ZIP passed archive/license/provenance inspection. Its self-contained EXE installed mods on a disposable legacy copy, then restored exact original loader and launcher bytes with shared runtime discovery disabled.
- ZIP SHA-256: `D63A2A1A9799C1C279A6D8BE27ED15B44083453699DB0120157B7237D0CD9241`. EXE SHA-256: `EEED011130720AD9F53F02604C22211FD7439AA880F6DD8C17C4459A6340244C`.
- Production UpdateService read the actual latest 0.1.3 manifest, verified both mod downloads against the release bytes and SHA-256, then reported no repeated mod or launcher updates for current versions.
- The local Steam bridge was upgraded to 0.1.3 while its processes were stopped. Before/after hashes of all 99 files showed only the two bridge EXEs and two ownership JSON files changed; original launcher, loader, mods, selection, preferences and backups remained unchanged. Steam integration stayed enabled.

This release is the latest stable version at verification. Previous release assets are preserved.

## 0.1.2 — completed installation and package checks

The new default enables the Steam bridge together with the mod patch on a recognized Steam installation, unless the user explicitly opted out. Standalone installs keep their original entry point.

- **33/33 installer and packaged-executable tests passed**, including 30 core cases, a copied real SS14 loader, published Steam handoff and actual self-contained CLI execution.
- The actual release EXE's `--install` enabled both wrapper copies in a fixture with a matching Steam manifest/layout. Standalone installation kept its original executable. GUI install and first mod launch use the same policy entry point, confirmed by independent source review.
- Invalid bridge preflight and a locked Steam executable left the installation unchanged: loader and bridge writes roll back together.
- Explicit disable survived updates, reinstall and restore/reinstall. Missing preferences, including legacy ownership files, enabled the default on explicit installation; restoration retained the saved preference.
- The published wrapper handed off to the stable launcher, which stayed open while restoration replaced the entry point. Restored original loader and launcher bytes matched exactly.
- **26 Crew Console checks passed**; bootstrap, profiles, catalog, semantic version, bounded update and rate-limit fallback suites passed. Independent review found no blocking defects.
- Russian and English installation-page captures were inspected; the new default-integration explanation fit within the existing layout.

The current local Steam integration is retained as enabled at the user's request. Prior test cleanup statements below refer to their recorded historical sessions. No new live gameplay run is claimed for 0.1.2; game and mod behaviour were unchanged by this installation-policy change.

## Completed checks — 0.1.0 / 0.1.1

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

### Published 0.1.1 — passed

- Release: [v0.1.1](https://github.com/actemendes/ss14-modlauncher/releases/tag/v0.1.1), source commit `1a00eeba5bb8e82a9efe38793e4eedf5e29f7ced`; `sourceDirty: false`.
- [Windows CI](https://github.com/actemendes/ss14-modlauncher/actions/runs/37925681859) succeeded for that exact commit. The final extracted package passed all 24 installer/packaged-executable checks and the bootstrap, catalog and update suites.
- All six public assets downloaded without authentication and matched the local build. Checksums, manifest URLs/hashes, archive contents, licenses and source provenance passed.
- ZIP SHA-256: `E0AC42064B61F97AC0DB5B41B806DC42E5FFCE117E983D450586537810F24109`.
- The downloaded self-contained EXE installed both bundled mods on a disposable legacy installation, then restored the original loader and launcher bytes. Embedded DLLs matched the public release assets.
- Production UpdateService successfully checked the actual latest 0.1.1 release, downloaded both mod DLLs with matching SHA-256 and local bytes, then reported zero updates for current installed versions. The public fallback worked while the anonymous API quota remained exhausted.
- The published tag resolves to the build's source commit. Original 0.1.0 assets remained unchanged; 0.1.1 was the latest stable release at that verification.

### Published 0.1.2 — passed

- Release: [v0.1.2](https://github.com/actemendes/ss14-modlauncher/releases/tag/v0.1.2), source commit `7a602676963954c3c28b30dba6f8f569fd419b1f`; `sourceDirty: false`. The published tag resolves to this exact commit.
- [Windows CI](https://github.com/actemendes/ss14-modlauncher/actions/runs/37928401127) succeeded for that source. The locally packaged EXE passed the 33 installation/executable checks above.
- All six public assets downloaded anonymously and matched the build, sizes and SHA-256 checksums. Manifest URLs/hashes, archive contents, licenses and source provenance passed.
- ZIP SHA-256: `E87B4AE9329EA7141A1481F03665E02CF1F6332AF85FD2AB90A70F052DF64025`. EXE SHA-256: `A942FEEE3E6BAF96CA770358D5352A3F3EA46A8A728294273EBC5A7063671D25`.
- The downloaded self-contained EXE installed bundled mods on a disposable legacy installation with shared runtime discovery disabled. Embedded mod bytes matched the public DLLs, and restoration returned exact original loader and launcher bytes.
- Production UpdateService checked the actual latest 0.1.2 release, downloaded both mod DLLs with matching SHA-256 and local bytes, then returned no updates for current versions.
- 0.1.2 is the latest stable release at this verification. Prior release assets remain unchanged.

## Scope and remaining compatibility limits

- These results cover the tested Windows installation and engine version, not every server fork, future SS14 update or Steam library layout.
- Russian and English native UI and both crew console layouts were inspected at the desktop's current display settings. All Windows DPI combinations were not tested.
- The self-contained trace confirms the bundled runtime on this computer; a fresh Windows machine without an installed .NET runtime was not available.
- The application is not digitally signed by a public code-signing certificate. Antivirus reputation and SmartScreen behaviour vary by machine.
- Normal authentication and engine signature verification were preserved. No claim is made that every possible argument accepted by the original launcher was individually exercised.

See the [release checklist](releasing.md) for future versions.
