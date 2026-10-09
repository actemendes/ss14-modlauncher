# Development

## Prerequisites and build

Use Windows x64, PowerShell 7, and .NET SDK 10. The first restore needs access to NuGet.

```powershell
git clone https://github.com/actemendes/ss14-modlauncher.git
cd ss14-modlauncher
./build.ps1
```

Outputs:

- `dist/SS14ModLauncher/`: self-contained runnable app, documentation and notices.
- `dist/SS14ModLauncher-0.1.2-win-x64.zip`: portable release package.
- `dist/mod-assets/`: individual mod DLLs and a manifest.
- `dist/SHA256SUMS.txt`: SHA-256 checksums of release assets.
- `dist/build-info.json`: SDK, source revision and build metadata.

Default builds generate manifest URLs for `actemendes/ss14-modlauncher` without publishing. The explicit release command is:

```powershell
./build.ps1 -Version 0.1.2 -Repository actemendes/ss14-modlauncher
```

Use `-Repository ''` for a local manifest template with empty download URLs, or specify your own repository when building a fork.

Build output is ignored by Git. Do not commit `payload/`, `dist/`, `bin/`, or `obj/`. The script rebuilds and verifies a fixed DLL payload before embedding it.

## Tests

The tests are console harnesses: a failed assertion exits nonzero. Run them directly with:

```powershell
dotnet run --project tests/CrewConsole.Tests.csproj -c Release
dotnet run --project Launcher.Core.Tests/Launcher.Core.Tests.csproj -c Release
```

The normal build runs both. `-SkipTests` is available only for fast local packaging after a verified run; a publication build must run tests. Live game compatibility and Steam launch behaviour still require a manual smoke test on the intended installation.

## Mod contract

A mod DLL exports this method:

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

The bootstrap calls it after client content is loaded. Fail clearly when a required type or method is absent. Do not infer private game state from missing data or bypass engine/authentication checks. Runtime integrations using reflection are version-sensitive.

The bundled mods use Harmony. Core libraries are stored beside the bootstrap and mod DLLs in `Mods/`. A selected DLL is validated against `selection.json` before loading. Installing a file alone does not enable it.

## Add a mod

1. Implement and test the contract, including missing-type behaviour.
2. Add a stable mod ID, filename, version, descriptions and compatibility metadata to the catalog.
3. Add it to the build's explicit payload list and release manifest generator.
4. Include both RU and EN launcher text; separately test the mod's own localization.
5. Add targeted tests and user documentation.

Version 0.1.2 deliberately uses a known catalog. Dropping an unknown DLL into `Mods/` is not a supported installation mechanism.

## Future Git submodules

The initial mods remain in this repository. Once an independent mod repository exists, add it explicitly and pin a reviewed revision:

```powershell
git submodule add <actual-repository-url> mods/sources/<mod-id>
git -C mods/sources/<mod-id> checkout <reviewed-commit>
git add .gitmodules mods/sources/<mod-id>
```

Then update its project/build reference and the catalog. A submodule pins source code; users receive built DLL release assets, not an automatic `git pull`. Avoid tracking a moving branch as the reproducibility guarantee.

## Localization and appearance

The launcher's language dictionary must have the same keys for RU and EN. Do not embed user-facing strings in event handlers when they belong in that dictionary. Check long Russian labels, English labels, common Windows scaling, empty/error states and disabled actions.

The palette follows the separate `ss14-crew-monitor` project: dark surfaces, muted borders, cyan accents and compact information panels. README art is branding; screenshots must show the real app and should be refreshed when the UI changes.

## Command line

These maintenance commands return exit code 0 on success and a nonzero code on failure:

```powershell
./SS14ModLauncher.exe --status "C:\path\to\bin_x64"
./SS14ModLauncher.exe --install "C:\path\to\bin_x64"
./SS14ModLauncher.exe --restore "C:\path\to\bin_x64"
```

`--uninstall` is an alias for `--restore`. CLI operations have the same compatibility and hash checks as the UI; close the client and original launcher first. In 0.1.2, `--install` uses the default Steam policy: a matching Steam app manifest and library layout enable the bridge unless the user explicitly opted out. The loader patch and bridge share one transaction. Supply the published single-file app when the policy enables Steam integration; standalone installs and an explicit opt-out keep the original entry point.

Use the core `InstallWithDefaults` path for CLI installation, GUI installation and the first mod launch. Lower-level `Install` and `SetSelection` leave integration unchanged for mods-only operations. Keep detection and `SS14ModLauncher/preferences.json` handling in the core. Do not infer an opt-out from an old stable launcher or ownership record. Explicit disable/enable and restoration must preserve the documented preference lifecycle; see [architecture](architecture.md).

For a controlled UI session or screenshot:

```powershell
./SS14ModLauncher.exe --launcher-root "C:\path\to\bin_x64" --lang en
./SS14ModLauncher.exe --lang ru --settings "C:\temp\modlauncher-settings.json" --capture "C:\temp\launcher-ru.png"
```

`--settings` selects an isolated settings file. `--capture` renders the native form to an image and exits; it is intended for documentation and UI checks. The internal Steam bridge switch is not a public installation command.
