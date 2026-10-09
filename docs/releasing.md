# Preparing a release

Repository: [actemendes/ss14-modlauncher](https://github.com/actemendes/ss14-modlauncher). The project uses MIT. Build scripts create local artifacts only; publication remains an explicit maintainer action. See [verification](verification.md) for evidence and pending checks by version.

## Version sources

`release/release.json` separates the feed tag, application version, launcher hosting tag and bootstrap version, with an explicit build mode:

```json
{
  "schemaVersion": 1,
  "mode": "Full",
  "releaseVersion": "0.1.3",
  "launcherVersion": "0.1.3",
  "launcherReleaseVersion": "0.1.3",
  "bootstrapVersion": "0.1.2",
  "repository": "actemendes/ss14-modlauncher"
}
```

`catalog/mods.json` owns each mod's version and minimum launcher requirement. Crew Console and Hello World remain **0.1.2** in launcher **0.1.3**. `release/mod-artifacts.json` pins their published source release, assembly version and SHA-256. Do not change that version's bytes or remove its lock entry just to force a rebuild.

For a launcher change, update `launcherVersion` and matching `Program.Version`, `Catalog.LauncherVersion` and installer project metadata. Set `mode: Full` and point `launcherReleaseVersion` to the new feed tag that will contain the ZIP. Keep unchanged mod and bootstrap versions intact. For a mod change, increase only its catalog version and adjust its minimum launcher if the contract requires it. A bootstrap change needs its own version and compatibility review.

## Full launcher release

From a clean reviewed checkout:

```powershell
./build.ps1 -Mode Full -ReleaseVersion 0.1.3
```

Full mode requires `releaseVersion == launcherReleaseVersion`: this feed is the host of the new ZIP. The application `launcherVersion` is independent and determines the ZIP filename. For example, feed/tag **1.5.0** can host app **0.2.0** as `releases/download/v1.5.0/SS14ModLauncher-0.2.0-win-x64.zip`.

The old `-Version` spelling is only an alias for `-ReleaseVersion`; it overrides the feed tag only. The default `Auto` mode uses the explicit configuration `mode`, not version equality.

Review and publish these assets under `v0.1.3`:

- `dist/SS14ModLauncher-0.1.3-win-x64.zip`
- `dist/mod-assets/mods-manifest.json`
- The mod DLLs under `dist/mod-assets/`
- `dist/SHA256SUMS.txt` and `dist/build-info.json`

For the current two-mod catalog, this is six public assets. `mod-artifacts.next.json` is a local maintainer receipt and is excluded from public checksums; do not upload it as a release asset.

The ZIP contains a self-contained launcher, current documentation, MIT and dependency/runtime notices. Inspect the actual archive and source provenance before uploading.

## Release containing only mod updates

After changing a mod, incrementing its catalog version and verifying its minimum launcher requirement, prepare a new feed while retaining the already published launcher version:

```powershell
./build.ps1 -Mode ModsOnly -ReleaseVersion 0.1.4 -OutputRoot dist/mods-0.1.4
```

This is a future-release example. Persist it with `mode: ModsOnly` and `releaseVersion: 0.1.4`, leaving `launcherVersion` and `launcherReleaseVersion` at 0.1.3. The default `-Mode Auto` follows that configuration. `-ModsOnly` is a shorthand for explicit `-Mode ModsOnly`.

ModsOnly defaults to `dist/mod-release` unless `-OutputRoot` is specified. It emits the manifest, mod DLLs, build metadata and checksums, with **no rebuilt launcher or new launcher ZIP**. For the current catalog those are five public assets. The proposed ledger receipt is local-only and excluded from public checksums. A separate output folder avoids confusing these assets with an earlier full package.

The feed's explicit `launcher` entry uses `version` for the app, `releaseVersion` for its hosting tag and `downloadUrl` for the ZIP. In this example it still points to app 0.1.3 hosted under `v0.1.3`. The global minimum is the maximum mod requirement so older clients remain safe. A mod requiring a launcher newer than the configured published one needs a compatible launcher release first.

Upload only the newly generated public feed assets; keep `mod-artifacts.next.json` local and do not relabel an old launcher ZIP as the feed version. New clients use the explicit launcher version for notifications, so a mod-only feed does not announce a nonexistent launcher.

## Independent release sequence

| Feed/tag | Mode | App version | Launcher hosting tag | Result |
| --- | --- | --- | --- | --- |
| 0.1.3 | Full | 0.1.3 | 0.1.3 | New launcher ZIP |
| 0.1.4 | ModsOnly | 0.1.3 | 0.1.3 | Mod updates, existing ZIP |
| 0.1.5 | Full | 0.1.4 | 0.1.5 | Next app version under the next unused feed tag |

For the final row, use `mode: Full`, `releaseVersion: 0.1.5`, `launcherVersion: 0.1.4` and `launcherReleaseVersion: 0.1.5`. Subsequent mod-only feeds retain the last two launcher fields until a new app is published. The feed version orders publication; it does not reserve or increment the application version.

## Artifact ledger

For an already known mod version, the build searches the SHA-keyed cache and known local artifact locations, then downloads the pinned published DLL if permitted. It validates both the SHA-256 and assembly version. It never recompiles a known version as a fallback. Use `-Offline` to require an existing verified local artifact.

New versions compile with scoped mod properties. The build emits `mod-artifacts.next.json` containing proposed provenance and hashes. After publishing and verifying the actual downloaded assets, review and merge the appropriate entries into `release/mod-artifacts.json`; preserve previous entries. Do not adopt hashes for bytes that were never published.

The independent version and URL contract is described in [updates](updates.md). Options and output paths are listed in [development](development.md).

## Checksums and publication review

`SHA256SUMS.txt` uses asset basenames because GitHub downloads them into one directory. Place the downloaded release assets together and check every listed hash:

```powershell
foreach ($line in Get-Content -LiteralPath ./SHA256SUMS.txt) {
    $expected, $file = $line -split '  ', 2
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $expected) {
        throw "Checksum mismatch: $file"
    }
}
```

Inspect `build-info.json` for the exact source revision, dirty status, component versions, mode and artifact origins. Publish from a clean reviewed commit. Verify manifest URLs resolve to the intended repository/tags and byte hashes match the uploaded assets.

`-Repository owner/repository` changes the output publisher. `-Repository ''` creates a local template with empty URLs and must not be published. Original game files, authentication data, local settings and private test fixtures never belong in release assets.

## Verification checklist

1. Run the relevant test suites and test the actual extracted package; do not count source-only tests as package verification.
2. Inspect RU/EN layouts, notification state and startup-check opt-out. Offline startup must remain usable, with no automatic downloads or installation.
3. Exercise default Steam integration, standalone installation, opt-out persistence and atomic rollback on disposable installations. Verify the intended real Steam launch path separately when authorized.
4. Check original/changed backup recovery and exact restore hashes. Preserve the user's intended installed state after testing.
5. Verify mod selection and compatibility in a fresh client where relevant. Attribute live gameplay evidence to the version actually tested.
6. Check independent launcher versus mod announcements and a mixed feed with compatible/blocked mods. A higher feed version alone must not announce a newer explicit launcher.
7. Compare retained mod DLLs with their ledger hashes; test new mod bytes. Confirm mods-only output does not create or relabel a launcher package.
8. Validate all manifest links, download hashes, licenses, provenance and archive contents after publication. Recheck the public update path.
9. Update the verification record and release notes with actual results, then adopt the verified artifact ledger entries.

## Rollback

Preserve previous releases and ledger entries. Clean-install restoration uses the user's own verified original SS14 backups. Never distribute another user's launcher/loader as rollback data.
