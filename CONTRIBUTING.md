# Contributing

Start with [development](docs/development.md) and [architecture](docs/architecture.md). The project uses the [MIT license](LICENSE). Contributions and bundled third-party code must have compatible redistribution terms.

## Changes

- Keep installation and recovery changes in `Launcher.Core` and cover failure paths with tests.
- Keep UI strings in the launcher's localization dictionary; add both Russian and English entries.
- Preserve the original launcher, engine signature checks, and normal authentication.
- Never commit tokens, account data, local installation paths, player/chat dumps, or executable files from a user's SS14 installation.
- Pin dependency and submodule versions. Document compatibility claims with the tested launcher/client versions.
- Update the relevant document and `CHANGELOG.md` when behaviour changes.

Run `./build.ps1` before requesting review. Describe the visible change, why it is needed, and what was verified. Screenshots of UI changes should show both languages when the layout changes.

## Issues

For a bug, include app version, Windows version, original SS14 launcher version, reproduction steps, expected/actual behaviour, and the installation status shown in the app. Redact account identifiers and personal paths from logs. Never attach launcher authentication files.

For a feature request, describe the player-facing outcome and a concrete example. Compatibility with a specific server fork should name the fork and version without sharing private server credentials.
