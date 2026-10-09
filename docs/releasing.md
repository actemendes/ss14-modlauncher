# Preparing a release

The public repository is [actemendes/ss14-modlauncher](https://github.com/actemendes/ss14-modlauncher). No release is published automatically; the repository owner controls publication. The project uses the MIT license. See the [verification record](verification.md) for the evidence associated with **0.1.0**.

## Before the first public release

- Verify the intended repository and its private vulnerability-reporting route in `SECURITY.md`.
- Review README art and screenshots, compatibility statements, third-party notices and changelog.
- Confirm the target SS14 launcher/client versions with a live smoke test. Do not describe fixture-only verification as live game verification.
- Decide whether the optional Steam integration is supported on the tested installation and document any remaining limits.

## Build artifacts

From a clean reviewed checkout:

```powershell
./build.ps1 -Version 0.1.0 -Repository actemendes/ss14-modlauncher
```

The script builds and tests locally. It does not tag, push, create a release, or upload assets.

Inspect `dist/build-info.json` for source revision and SDK version. A dirty checkout is recorded; publish from a clean reviewed commit. Inspect the generated manifest and ensure all download URLs match the intended repository and `v0.1.0` tag.

Upload these files manually to the same release:

- `dist/SS14ModLauncher-0.1.0-win-x64.zip`
- `dist/mod-assets/mods-manifest.json`
- `dist/mod-assets/CrewConsole.Mod.dll`
- `dist/mod-assets/HelloWorld.Mod.dll`
- `dist/SHA256SUMS.txt`
- `dist/build-info.json`

The local-only `mods-manifest.local.json` is not a release update source. Do not upload original game files, local settings, authentication files, fixtures copied from a user's game, or the entire working directory.

`SHA256SUMS.txt` uses asset basenames because GitHub downloads them into one directory. For a local build, the DLLs and manifest remain under `dist/mod-assets/`; when verifying downloaded release assets, place all six files together and check every hash. Example for PowerShell 7:

```powershell
foreach ($line in Get-Content -LiteralPath ./SHA256SUMS.txt) {
    $expected, $file = $line -split '  ', 2
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $expected) {
        throw "Checksum mismatch: $file"
    }
}
```

## Verification checklist

1. Extract the archive into a fresh writable directory and run it on Windows x64 without a separate .NET runtime.
2. Inspect RU and EN UI at the intended DPI settings; verify keyboard navigation and long status text.
3. Use a disposable copy of a compatible installation: inspect, install, apply selected mods and restore. Compare original files before/after with SHA-256.
4. Check altered-backup and altered-installed-file failures; unexpected data must remain intact.
5. Test empty and nonempty profiles with a fresh game process. Verify Crew Console and Hello World separately.
6. Enable optional Steam integration, start through Steam, then restore and start normally again. Verify the original launcher receives its arguments.
7. Exercise a real release manifest: no-update, new update, wrong hash, invalid URL and unsupported version. Do not substitute this with a claim that offline tests prove GitHub publication works.
8. Confirm the ZIP includes documentation, runtime/dependency notices, and the actual chosen source license.
9. Record versions and verification results in the release notes.

## Versioning and updates

Use stable three-part versions such as `0.1.0`. Before changing the build version, update `Program.Version` in `Installer/Program.cs`, `Catalog.LauncherVersion` in `Launcher.Core/Catalog.cs`, the bundled versions in `catalog/mods.json`, project metadata and release documentation. The build rejects a mismatch. The first packaging script versions the app and bundled mods together; coordinate separate mod versions before they become independent repositories.

Replacing the launcher is a manual package upgrade in 0.1.0. Mod update checks distribute DLLs only. If a future change requires a newer bootstrap/Harmony/launcher contract, publish a new launcher package and increase the manifest's minimum launcher version.

## Rollback

Preserve previous release assets. A user's clean-install restoration uses their own verified original SS14 backups, not files taken from the repository. Never distribute another user's original launcher/loader as a rollback payload.
