# Development

## Prerequisites and full build

Use Windows x64, PowerShell 7 and .NET SDK 10. The first NuGet restore needs network access. Published mod artifacts also need downloading unless verified bytes are already cached.

```powershell
git clone https://github.com/actemendes/ss14-modlauncher.git
cd ss14-modlauncher
./build.ps1
```

The default `Auto` mode reads the explicit `mode` (`Full` or `ModsOnly`) from `release/release.json`; it does not infer the mode from version equality. Full mode builds the launcher from source. Already released mod versions are reused byte-for-byte from the artifact ledger; newly versioned mods are built from source.

Current versions:

| Component | Version | Source of truth |
| --- | --- | --- |
| Release/feed | 0.1.4 | `release/release.json`: `releaseVersion` |
| ModLauncher | 0.1.4 | `release/release.json`: `launcherVersion`, checked against launcher source/project metadata |
| Launcher hosting release | 0.1.4 | `release/release.json`: `launcherReleaseVersion`, the tag containing that ZIP |
| Bootstrap | 0.1.2 | `release/release.json`: `bootstrapVersion`, checked against its project |
| Crew Console | 0.1.2 | `catalog/mods.json` |
| Hello World | 0.1.2 | `catalog/mods.json` |

Full-build output under `dist/`:

- `SS14ModLauncher/`: self-contained app, documentation and notices.
- `SS14ModLauncher-0.1.4-win-x64.zip`: portable launcher package.
- `mod-assets/`: individual DLLs and `mods-manifest.json`.
- `SHA256SUMS.txt` and `build-info.json`: checksums and source/component provenance.
- `mod-artifacts.next.json`: local maintainer receipt for ledger review after publication; not a public release asset.

Nothing is tagged, pushed or published by the script.

## Build options and mods-only releases

```powershell
# Full release: feed version must match launcherReleaseVersion, not the app version.
./build.ps1 -Mode Full -ReleaseVersion 0.1.4

# After updating a mod's source and catalog version, reuse the existing launcher ZIP.
./build.ps1 -Mode ModsOnly -ReleaseVersion 0.1.5 -OutputRoot dist/mods-0.1.5
```

The second command is an example for a future feed. It does not create or rebuild a 0.1.5 launcher. Keep `launcherVersion: 0.1.4` and `launcherReleaseVersion: 0.1.4`; its manifest points to that existing ZIP. Set configuration `mode: ModsOnly` and `releaseVersion: 0.1.5` when making this the persisted release plan.

A later launcher **0.1.5** can ship in feed/tag **0.1.6**: set `mode: Full`, `releaseVersion: 0.1.6`, `launcherVersion: 0.1.5`, and `launcherReleaseVersion: 0.1.6`. Its filename is `SS14ModLauncher-0.1.5-win-x64.zip` under tag `v0.1.6`. Mod-only releases therefore do not consume application version numbers.

| Option | Meaning |
| --- | --- |
| `-Mode Auto\|Full\|ModsOnly` | Auto uses configuration `mode`; explicit modes override it, and `-ModsOnly` selects ModsOnly |
| `-ReleaseVersion` | Override only the feed/tag version; legacy `-Version` is an alias |
| `-Repository owner/repository` | Override the output publisher; default comes from release configuration |
| `-Repository ''` | Produce a local template with empty URLs, not a publishable manifest |
| `-OutputRoot` | Output directory inside the repository; Full defaults to `dist`, ModsOnly to `dist/mod-release` |
| `-Offline` | Forbid downloading published mod artifacts; verified local bytes must exist |
| `-SkipTests` | Fast local packaging only; publication builds must run tests |

`-Offline` applies to artifact retrieval, not to NuGet's own restore process. Populate dependency caches separately if a fully disconnected build is needed.

Known mod versions are resolved from `.tools/artifact-cache/<sha>/<file>`, `payload/<file>`, `dist/mod-assets/<file>`, then their pinned source-release URL. Every accepted DLL must match the ledger hash and assembly version. A published version is never rebuilt as a fallback. An unknown/new version must be higher than its earlier ledger versions before source compilation.

New mods use scoped `ModBuildId` / `ModBuildVersion` properties. Do not pass a global `-p:Version` to force every project and bootstrap to the launcher version. See [releasing](releasing.md) for the publication and ledger workflow.

Build output is ignored by Git. Do not commit `payload/`, `dist/`, `.tools/`, `bin/` or `obj/`.

## Tests

The tests are console harnesses; failed assertions exit nonzero:

```powershell
dotnet run --project tests/CrewConsole.Tests.csproj -c Release
dotnet run --project Launcher.Core.Tests/Launcher.Core.Tests.csproj -c Release
```

The normal build runs both. Coverage includes installation/recovery, bootstrap selection, profiles, update parsing/downloading and startup-notification coordination. Live compatibility and actual Steam behaviour still require a smoke test; consult the versioned [verification record](verification.md).

## Mod contract

A mod DLL exports:

```csharp
namespace SS14LocalMods;
public static class Mod
{
    public static void Install(System.Reflection.Assembly content)
    {
        // content is Content.Client; resolve and patch supported types here.
    }
}
```

Bootstrap calls it after client content loads. Fail clearly when required types or methods are absent. Do not infer private game state from missing data or bypass engine/authentication checks. Reflection integrations are version-sensitive.

Bundled mods use Harmony. Runtime libraries live beside bootstrap, with mod DLLs in `Mods/`. Selection and hash validation are required before loading; merely placing a file there does not enable it.

## Add or change a mod

1. Implement the contract and tests, including missing-type behaviour.
2. Maintain its stable ID, filename, individual version, minimum launcher version, RU/EN descriptions and compatibility metadata in `catalog/mods.json`.
3. For a new mod ID, add the project/build mapping and payload/catalog integration. Dropping unknown DLLs into `Mods/` is not a supported installation mechanism.
4. Increase the mod version when changing shipped bytes. Keep unchanged mods and their ledger entries intact.
5. Review the generated manifest and proposed ledger, test the actual DLLs, and document compatibility.

Launcher-only changes do not require a mod version bump. Mod-only changes do not require rebuilding the launcher when the existing runtime contract remains compatible.

## Future Git submodules

The initial mods remain in this repository. Once a separate mod repository exists, pin a reviewed commit:

```powershell
git submodule add <actual-repository-url> mods/sources/<mod-id>
git -C mods/sources/<mod-id> checkout <reviewed-commit>
git add .gitmodules mods/sources/<mod-id>
```

Then update its project/build mapping and catalog. A submodule pins source code; users receive release DLLs, not an automatic `git pull`.

## Startup notifications and UI

`AppSettings.CheckUpdatesOnStartup` defaults to true for new and older settings; false is persisted. Corrupt settings remain read-only and skip automatic checking.

`AutomaticUpdateChecker` accepts an injected metadata-check callback and offers `CheckOnceAsync`, `IsChecking`, `LastError`, `Cancel` and `Dispose`. It starts at most one background attempt and drops stale/cancelled results. The UI also guards its repository/installation/version context before applying a result. This service must never download DLLs or mutate an installation.

Keep both RU and EN strings complete. Check long labels, scaling, errors, notification state and mixed compatible/blocked mod lists. The visual reference remains `ss14-crew-monitor`. README artwork is branding; screenshots show the real UI.

## Command line

```powershell
./SS14ModLauncher.exe --status "C:\path\to\bin_x64"
./SS14ModLauncher.exe --install "C:\path\to\bin_x64"
./SS14ModLauncher.exe --restore "C:\path\to\bin_x64"
```

`--uninstall` aliases `--restore`. Maintenance commands return 0 on success and a nonzero code on failure. Close the client and original launcher first.

Installation uses `InstallWithDefaults`: matching Steam manifest/layout enables the bridge unless explicitly disabled, and patch/bridge writes share one transaction. Use the published single-file app when the policy enables integration. Lower-level `Install` and `SetSelection` leave integration unchanged for mods-only operations. Detection and `SS14ModLauncher/preferences.json` handling belong in the core; never infer an opt-out from old ownership files.

For an isolated UI session or screenshot:

```powershell
./SS14ModLauncher.exe --launcher-root "C:\path\to\bin_x64" --lang en
./SS14ModLauncher.exe --lang ru --settings "C:\temp\modlauncher-settings.json" --capture "C:\temp\launcher-ru.png"
```

`--settings` selects a separate settings file. `--capture` renders the native form and exits, with automatic startup checking disabled for the capture. The internal Steam bridge switch is not a public installation command.
