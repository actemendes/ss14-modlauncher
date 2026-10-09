# Architecture

## Responsibilities

| Component | Purpose |
| --- | --- |
| `Installer/` | Windows desktop app, RU/EN UI, orchestration, embedded payload |
| `Launcher.Core/` | Installation inspection, patch/restore transactions, Steam bridge management, profiles/catalog, update validation |
| `Bootstrap/` | Runtime entry point, selection/hash checks, isolated per-mod failure logging |
| `CrewConsole/` | Native monitoring UI, sensor history and routes |
| `HelloWorld/` | Small Harmony-based example mod |
| `tests/` | Crew history, map/layout, and timeline regression harness |
| `Launcher.Core.Tests/` | Core regression harness with temporary fixtures |
| `docs/` | User guide, contracts, recovery and release information |

The directory name `Installer` is retained for source continuity; its published app is `SS14ModLauncher.exe`.

## First-run setup

The desktop app presents a localized setup window for a new installation, discovers SS14 and displays the active profile; a fresh default profile selects Crew Console. Its primary action uses the same transactional installation and saved Steam integration policy as normal installation, then starts the original launcher. The folder resolver accepts a valid launcher directory or a game parent containing `bin_x64`. Running game processes are reported for manual close/retry, never terminated.

Setup completion/dismissal is saved per normalized installation root in `SetupByRoot`; `SetupDismissedWithoutRoot` suppresses only the missing-folder context. Existing installation or restoration history and read-only/corrupt settings suppress the automatic prompt. Cancelling performs no installation. **Installation → Quick setup** reopens the wizard explicitly, bypassing automatic prompt policy. Existing profiles and an explicit Steam opt-out are retained.

## Launch sequence

```mermaid
flowchart LR
    A[Direct launch or default Steam bridge] --> B[ModLauncher]
    B --> C[Original SS14 launcher]
    C --> D[Patched SS14 loader]
    D --> E[Local bootstrap]
    E --> F[Validated enabled mods]
    D --> G[Normal engine and Content.Client]
    G --> F
```

A small call is added to `SS14.Loader.dll`. Normal authentication and engine verification remain in the original code. The bootstrap registers local dependency resolution and installs enabled mods when `Content.Client` is available. It does not change downloaded server content.

## Installation files

Paths below are relative to the selected SS14 launcher directory:

```text
SS14.Launcher.exe                    original apphost, or enabled Steam bridge
SS14.Launcher.clean.exe              original apphost backup for Steam integration
SS14ModLauncher/
  SS14ModLauncher.exe               stable installed mod launcher
  steam.json                        active Steam integration hashes/state
  launcher.json                     retained stable launcher ownership
  preferences.json                  retained explicit Steam integration choice
loader/
  SS14.Loader.dll                   original or patched loader
  SS14LocalMods/
    installation.json               original/patched hashes and installation state
    SS14.Loader.original.dll        original loader backup
    selection.json                  versioned language + enabled DLL/hash list
    ownership.json                  retained payload ownership for safe reinstall
    SS14LocalMods.Bootstrap.dll
    0Harmony.dll
    Mods/
      CrewConsole.Mod.dll
      HelloWorld.Mod.dll
```

The original launcher apphost is kept in its original directory so it can resolve its existing launcher DLL/runtime files. No Steam library configuration is rewritten.

## Steam installation policy

Since 0.1.2, the launcher recognizes `steamapps/common/<installdir>/bin_x64` only when a matching `appmanifest_<appid>.acf` exists with the same internal `appid` and `installdir`. A Steam-like directory name alone is insufficient. `InstallWithDefaults` prepares the loader patch and Steam bridge for one transaction. The GUI install action, first mod launch that needs installation, and CLI `--install` use this policy. Standalone installations retain their original entry point.

The explicit choice is stored in `SS14ModLauncher/preferences.json` with a boolean `SteamIntegration` and `Version: 1`, for example `{"SteamIntegration":true,"Version":1}`. Disable records false, including before the first installation; enable records true. Routine mod updates and reinstall preserve that decision. Restore removes the active bridge while retaining the preference for future installation. Missing preference means a detected Steam installation defaults to on; retained launcher ownership or stable executable files must not be interpreted as an explicit opt-out. Lower-level `Install` and `SetSelection` update mods without changing integration.

When this policy calls for a bridge, the application must supply a published single-file executable. That requirement is specific to installing/updating the bridge; standalone installation and an explicitly disabled Steam integration do not acquire it.

Per-user settings live in `%LOCALAPPDATA%/SS14ModLauncher/settings.json`. Runtime mod logs remain in `%LOCALAPPDATA%/SS14LocalMods/mods.log`.

## Integrity and failure behaviour

Installation state records SHA-256 values. Existing and backup files are checked before changing or restoring them. Unknown changes produce an error rather than being overwritten. The core uses a transaction journal and rollback for installation mutations. When Steam integration is enabled, installation stages the mod patch and bridge together, and failure rolls both back. Restoration checks the loader and installed Steam integration together.

Selection is explicit. Missing or corrupt selection data does not enable arbitrary DLLs. Bootstrap loads only selected, hash-matching filenames, and catches individual mod failures. This is integrity checking, not a security sandbox.

After Steam has verified a new upstream build, explicit `RebaseAfterPlatformUpdate` archives the previous backups and state in `loader/SS14LocalMods/History/<id>/` and adopts the new compatible originals as the baseline. It rejects an active patch/bridge. This requires a deliberate user confirmation; structural checks are not publisher authentication.

## Update notifications and independent versions

`AppSettings.CheckUpdatesOnStartup` defaults to true, including when upgrading older settings without that field. False is persisted. Corrupt settings remain read-only and do not initiate an automatic check.

`AutomaticUpdateChecker` starts at most one background metadata request per app session. It supports cancellation, returns failures as nonmodal state, and rejects results when the source repository, installation root, installed versions or opt-in setting changed. The UI also checks its captured context before displaying a notification. No timer, tray process, automatic DLL download or automatic installation is involved.

`UpdateService` validates only the configured GitHub release source. The feed/tag version, explicit launcher application version, its hosting release tag, bootstrap version and individual mod versions are independent. In 0.1.4, the launcher is 0.1.4 while the bundled mods and bootstrap remain 0.1.2. The checked-in artifact ledger preserves released mod bytes by version and SHA-256.

The manifest's explicit `launcher.version` determines whether a launcher update exists; `launcher.releaseVersion` identifies the hosting tag of its ZIP. Build configuration records `mode: Full|ModsOnly`; Auto follows this field. Full requires the feed tag to match the ZIP's hosting tag, not the app version, so intervening mod-only releases do not consume launcher version numbers. Compatible mod updates are returned in `Mods`; updates needing a newer launcher are returned in `BlockedMods` with their minimum version. A blocked mod does not prevent applying compatible mods from the same feed. The global minimum remains at least the highest per-mod minimum to keep older clients safe. Downloads are checked before replacement. See [updates](updates.md) for the full schema and [releasing](releasing.md) for artifact handling.

## Supported scope

Windows x64, a compatible existing SS14 installation, and the bundled catalog. Linux/macOS, workshop distribution, arbitrary plugin dependency solving, and launcher self-update are outside version 0.1.4. Profile changes take effect on a newly launched game client.

The [Crew Console reference](mods/crew-console.md) describes telemetry limits. [Conversations](mods/conversations.md) extends native chat independently using receive timestamps, voice names and radio channels. Its new catalog ID requires launcher 0.1.5. The older [port research](crew-monitor-port.md) is preserved as historical design context.
