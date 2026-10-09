# Mod update format

## Source, checks and trust

The default source is `actemendes/ss14-modlauncher`; the user can change it to a trusted `owner/repository` or clear it. Existing saved sources are retained. Version 0.1.3 checks once when the app opens, unless `CheckUpdatesOnStartup` is false. A manual check is also available. Both use the latest stable GitHub Release and its exact `mods-manifest.json` asset.

Startup checks fetch metadata only. There is no automatic DLL download, installation, launcher replacement, periodic timer or closed-app/tray service. Offline failures do not interrupt startup; the Updates page exposes retry/status. Changed source, installation, installed versions or an opt-out invalidate an in-flight automatic result.

The repository and its release authors are trusted code publishers. SHA-256 protects against a mismatched download, not a publisher controlling both DLL and manifest. Do not enter an untrusted repository.

If GitHub rejects the anonymous API request with a primary quota limit (HTTP 403 and `X-RateLimit-Remaining: 0`) or HTTP 429, the launcher tries GitHub's [documented latest-release asset link](https://docs.github.com/en/repositories/releasing-projects-on-github/linking-to-releases). It must resolve first to the configured repository's canonical stable-tag manifest asset. Foreign repositories, a direct CDN target without release identity, prerelease tags and manifest-version mismatches are rejected. Normal authorization failures and invalid API metadata do not trigger fallback. No GitHub token is required or stored.

## Independent versions

| Field | Meaning |
| --- | --- |
| Manifest `version` | Release/feed version, matching the stable GitHub tag |
| `launcher.version` | Actual launcher package version; determines the launcher-update notification |
| `launcher.releaseVersion` | Release tag hosting that ZIP, independent of the app and current feed versions |
| `launcher.downloadUrl` | That package's explicit ZIP URL; it can belong to an earlier release |
| Each mod's `version` | The individual DLL version, independent of launcher and feed |
| Each mod's `minLauncherVersion` | Minimum launcher able to use that mod |
| Global `minLauncherVersion` | Conservative compatibility requirement for older launchers that do not understand per-mod minimums |

All version fields use stable semantic versions. The global minimum must be **at least the highest per-mod minimum**; the build writes that maximum. The advertised launcher must satisfy it. This prevents an older client from ignoring a newer mod's requirement.

New clients split available updates into compatible `Mods` and incompatible `BlockedMods`. Compatible mods remain installable even when another mod in the feed is blocked. Blocked entries display the required launcher version and are never passed to the download/apply path.

For older manifests, a missing mod minimum falls back to the global minimum. A missing `launcher` entry uses the feed version for the launcher notification and the release page as its upgrade route. If a launcher entry omits `releaseVersion`, its hosting tag falls back to `launcher.version` for compatibility with earlier manifests. Publishers should emit explicit independent metadata.

## Manifest example

This describes launcher 0.1.3 with unchanged 0.1.2 mods:

```json
{
  "version": "0.1.3",
  "minLauncherVersion": "0.1.2",
  "launcher": {
    "version": "0.1.3",
    "releaseVersion": "0.1.3",
    "downloadUrl": "https://github.com/owner/repository/releases/download/v0.1.3/SS14ModLauncher-0.1.3-win-x64.zip"
  },
  "mods": [
    {
      "id": "crew-console",
      "file": "CrewConsole.Mod.dll",
      "version": "0.1.2",
      "minLauncherVersion": "0.1.2",
      "sha256": "<64 hexadecimal SHA-256 characters>",
      "downloadUrl": "https://github.com/owner/repository/releases/download/v0.1.3/CrewConsole.Mod.dll"
    },
    {
      "id": "hello-world",
      "file": "HelloWorld.Mod.dll",
      "version": "0.1.2",
      "minLauncherVersion": "0.1.2",
      "sha256": "<64 hexadecimal SHA-256 characters>",
      "downloadUrl": "https://github.com/owner/repository/releases/download/v0.1.3/HelloWorld.Mod.dll"
    }
  ]
}
```

This example is not installable. Generate the real hashes from exact release DLL bytes with the [build workflow](development.md). The 0.1.3 package retains the previously published 0.1.2 mod bytes instead of rebuilding and relabeling them.

Publish the manifest and DLLs as separate assets. Mod filenames and IDs must match the known catalog. Download URLs must remain in the configured repository's release asset namespace. The launcher URL must identify `SS14ModLauncher-<launcher.version>-win-x64.zip` under tag `v<launcher.releaseVersion>`. The runtime checks that tag against the explicit hosting release, not against the application version. ZIP mod installation and dependency resolution are outside this format.

A mod-only feed 0.1.4 can retain `launcher.version: 0.1.3` and `launcher.releaseVersion: 0.1.3`, pointing to the existing ZIP. A subsequent full feed 0.1.5 can publish app 0.1.4 with `launcher.releaseVersion: 0.1.5`; its URL ends in `/v0.1.5/SS14ModLauncher-0.1.4-win-x64.zip`. Only `launcher.version` determines whether a newer app exists.

## Artifact identity and build output

`release/release.json` records explicit `mode`, feed `releaseVersion`, app `launcherVersion`, its hosting `launcherReleaseVersion`, and `bootstrapVersion`. Auto build mode follows the configured mode. `catalog/mods.json` owns individual mod versions and compatibility minimums. `release/mod-artifacts.json` pins already published mod bytes by ID, version, source release and SHA-256. Never reuse a published mod version for different bytes.

The default repository is recorded in the release configuration; `-Repository owner/repository` overrides it for another publisher. Mod URLs refer to the selected feed tag; the launcher URL uses its explicit hosting tag and application-version filename. `-Repository ''` creates a local manifest template with empty download URLs, unsuitable for publication. See [releasing](releasing.md) for full and mods-only releases.

## Applying an update

Close the game and original launcher before applying mods. The app validates the complete compatible selection, downloads into memory and checks each SHA-256 before installation. Blocked mods are not downloaded. Selection hashes are updated for the next client session; the bootstrap checks the selected DLL bytes independently.

Routine mod updates retain the installation's explicit Steam integration preference. An earlier **Disable integration** choice is not reset. Launcher features require its own ZIP upgrade, not a DLL update. A rejected update should remain an actionable error; never bypass it by editing stored hashes.
