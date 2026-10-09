# Mod update format

## Source and trust

The default source is `actemendes/ss14-modlauncher`; the user can change it to a trusted `owner/repository` or clear it. Existing saved settings retain their source. A manual check reads the latest GitHub Release and its exact `mods-manifest.json` asset. No launcher executable update is performed.

The repository and its release authors are trusted code publishers. A SHA-256 value protects against a mismatched download, not a malicious publisher controlling both the DLL and manifest. Do not enter an untrusted repository.

If GitHub rejects the anonymous API request with a primary quota limit (HTTP 403 and `X-RateLimit-Remaining: 0`) or HTTP 429, the launcher tries GitHub's [documented latest-release asset link](https://docs.github.com/en/repositories/releasing-projects-on-github/linking-to-releases). It must first resolve to `github.com/<owner>/<repository>/releases/download/<stable-tag>/mods-manifest.json`; an unrelated repository, direct CDN target without that release identity, prerelease tag or manifest-version mismatch is rejected. Normal authorization failures and invalid API metadata do not trigger this fallback. No GitHub token is required or stored.

## Manifest v0.1

```json
{
  "version": "0.1.1",
  "minLauncherVersion": "0.1.1",
  "mods": [
    {
      "id": "crew-console",
      "file": "CrewConsole.Mod.dll",
      "version": "0.1.1",
      "sha256": "<64 hexadecimal SHA-256 characters>",
      "downloadUrl": "https://github.com/owner/repository/releases/download/v0.1.1/CrewConsole.Mod.dll"
    },
    {
      "id": "hello-world",
      "file": "HelloWorld.Mod.dll",
      "version": "0.1.1",
      "sha256": "<64 hexadecimal SHA-256 characters>",
      "downloadUrl": "https://github.com/owner/repository/releases/download/v0.1.1/HelloWorld.Mod.dll"
    }
  ]
}
```

This example is documentation, not an installable manifest. Generate real hashes from the exact release DLL bytes with `./build.ps1 -Repository owner/repository`.

Publish the manifest and DLLs as separate assets of the same release/tag. Download URLs must remain in the configured GitHub repository's release download namespace. File names and IDs must match the known catalog; paths and arbitrary URLs are not accepted. ZIP installation and dependency resolution are outside this format.

The release version, individual mod versions, and minimum launcher version are explicit. Do not reuse a version for different DLL bytes. If the runtime API or payload contract changes, increase the minimum launcher version and distribute a new launcher package separately.

## Build outputs

The default build emits `mods-manifest.json` with URLs for `actemendes/ss14-modlauncher` using tag `v<Version>`. Set `-Repository owner/repository` to build for another publisher. No network publication happens. The repository owner uploads the assets when ready; see [releasing](releasing.md).

With `-Repository ''`, the build emits `mods-manifest.local.json` with real hashes and empty URLs. It is a local inspection template and must not be renamed/uploaded as a usable update manifest.

## Applying an update

The app validates the manifest and downloads before replacing supported DLLs. Close the game and original launcher first. Apply the resulting selection before launching a new client so its selected hashes match the installed bytes. The bootstrap independently checks each selected DLL's hash.

A rejected update should leave a clear error. Do not bypass validation by manually editing hashes in installation state or selection files.
