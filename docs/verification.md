# Verification record — 0.1.0

Date: **2026-10-09**. Platform: **Windows x64**. Build toolchain: **.NET SDK 10.0.400**, self-contained .NET / Windows Desktop runtime **10.0.11**.

This record describes the publication candidate prepared locally. No GitHub release was published.

## Completed checks

| Area | Result |
| --- | --- |
| Crew history, timeline, layout and map projection | 26 regression checks passed |
| Installer and restore core | 23 cases passed, including exact restore, invalid payload/path rejection, changed backup protection, transaction recovery, Steam bridge state and upstream-baseline adoption |
| Bootstrap selection | Offline checks passed for selection validation, path confinement, SHA-256 validation and clean-session bypass |
| Catalog, profiles and updates | Offline checks passed for settings/profile persistence, semantic versions, manifest/source/download bounds and invalid update rejection |
| Original loader compatibility | Install/restore roundtrip exercised on a copied real original SS14 loader; original bytes restored |
| Published Steam bridge | Published executable handed off to the stable launcher; restoration exercised while that stable UI remained open |
| Packaging | Self-contained EXE and ZIP created; runtime/dependency notices and artwork present; release-asset SHA-256 checksums verified |
| Documentation | Relative Markdown links checked; Crew Console limits preserved separately from historical port research |
| Build version guard | An inconsistent requested version was rejected before output mutations |

The UI has native Russian and English screenshots under `docs/assets/`. Those renders document appearance; they are not evidence of a successful multiplayer session.

## Limits of this verification

- The final new bootstrap/mod selection path has not yet been rechecked in a live game session. Existing compatibility experience with Launcher 0.40.2 and Robust 275.0.0 does not prove every new release path or every server fork.
- The executable handoff test is not a claim that the actual Steam Play button was exercised on every installation layout.
- Update tests used controlled offline responses. A live public GitHub Release, download redirects and publisher configuration still need a smoke test after the owner publishes the intended assets.
- Fresh-machine execution without an installed .NET runtime, all Windows DPI settings, antivirus reputation and code-signing behaviour have not been exhaustively tested.
- The application is not digitally signed by a public code-signing certificate.

Before publication, follow the remaining relevant steps in the [release checklist](releasing.md). Update this record with actual results when those checks are performed; do not silently convert pending checks into compatibility claims.
