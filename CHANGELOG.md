# Changelog

## 0.1.7 — 2026-10-10

Mod-only feed: launcher 0.1.6 and its ZIP are unchanged; the other four mods keep their published bytes.

- ChemMaster 0.1.4 reworks the AUTO tab. Typing a reagent lists matches below the field; Enter adds the first one. Target amounts are edited in place next to the current buffer stock, with More / Up to buttons for the mode.
- The plan is built automatically after every edit and whenever the machine contents change; the Preview button is gone. A failed plan offers Retry and recalculates by itself once the buffer changes.
- One status row replaces the five permanent buttons: step count and estimated time, a coloured progress bar and only the buttons valid for the current state. Targets, mode and saved recipes are locked while a recipe runs.
- Beaker replacement shows the required and current temperature; Resume confirms the required phase. The cold/hot choice appears only when the client cannot read the temperature.
- After Stop or a failure, Replan continues to the same absolute goals from the current stock. Saved recipes load on selection and can be deleted.
- Missing stock is reported in one message listing every reagent with the amount to add. Sources that need an external apparatus are no longer mentioned.
- Reaction effects no longer block a plan. Execution still verifies the composition after every command and stops on any difference.

## 0.1.6 — 2026-10-10

- New ChemMaster 0.1.3: RU/EN AUTO tab inside the native ChemMaster, reagent search, saved production targets, Make/Ensure modes and ingredient-order preview. Chemistry comes from the current connection's prototypes.
- Cold/hot toggle preserves active recipes, reports measured and required temperatures and offers native empty-beaker eject/insert from AUTO. Phase metadata avoids treating slightly warmed cold beakers as hot; finite superheated temperatures remain supported.
- Prepared cold/hot beaker phases with explicit replacement barriers and remaining-goal replanning from measured temperatures; hot temperature persists locally. Missing ingredient errors no longer get overwritten by unsuitable source reactions such as FiberBreakdown. Gas and popup effects that preserve solution chemistry are supported.
- Separate AUTO settings tab with persistent speed, reagent-switch pauses and random interval probability sliders. Settings apply during execution; statuses and chemistry errors use the selected RU/EN language.
- Execution uses ordinary BUI transfer messages, waits for each resulting composition, supports pause/resume/stop and stops on stale state, closure or timeout. First version mixes in an empty beaker and returns products to the buffer; heating, external mixers, reagent data and pill/bottle packaging are outside its scope. See [ChemMaster](docs/mods/chemmaster.md).
- New Death Rattle 0.1.2 mod: RU/EN radio distress call following the launcher's interface language at native awful health (`HumanHealth` severity 4), once per episode with a 30-second cooldown.
- Optional recent, explicit damage Origin attribution names the attacker in one in-game sentence without a colon or an entity label. Standard replicated damage lacks Origin and falls back to the basic call.
- Launcher 0.1.6 registers the new ID; existing mod and bootstrap versions/locked artifacts remain unchanged. Added policy, Harmony integration and local game API checks; live radio delivery remains pending.

## 0.1.5 — 2026-10-09

- New Conversations 0.1.1 mod: stable game-clock receive timestamps, name and message-text search, multiple selected voices and department filtering by received radio channel in the native chat.
- Collapsible RU/EN panel with native controls and chat button styles; existing channel filters, markup and chat input remain active.
- Launcher 0.1.5 registers the new mod ID. Crew Console, Hello World and bootstrap remain 0.1.2; published mod bytes are reused.
- 56 metadata, filter, clock and Harmony integration checks pass during packaging. Local gameplay smoke checks confirm the native panel, timestamps, message search and reset; broader radio/scaling/audio checks remain pending. See [mod documentation](docs/mods/conversations.md).

## 0.1.4 — 2026-10-09

- A localized first-run setup window discovers SS14 and offers the recommended default Crew Console selection.
- **Set up & play** installs the selected mods, applies the existing default Steam integration policy and opens the original launcher in one action.
- Missing installations can be selected manually, including the game folder above `bin_x64`; a running client or original launcher prompts the user to close it and retry.
- Cancelling before setup installs nothing and dismisses the prompt for that installation. Existing installations skip the automatic popup; **Installation → Quick setup** opens it manually.
- A **Getting started** entry, hover hints and visible action error messages explain common steps; background update errors remain nonmodal.
- Existing profiles and explicit Steam integration opt-outs are retained. Launcher 0.1.4 keeps the published mod DLLs and bootstrap at 0.1.2.

## 0.1.3 — 2026-10-09

- One background update check when ModLauncher opens, enabled by default with a saved opt-out in Updates. Offline failures stay nonmodal; there is no timer or closed-app/tray updater.
- Separate notifications for launcher and individual mod updates. Downloads and installation remain explicit user actions.
- Independent feed, launcher, bootstrap and mod versions. Launcher 0.1.3 keeps the published Crew Console and Hello World 0.1.2 DLLs byte-for-byte.
- Per-mod minimum launcher versions: compatible updates remain installable while blocked mods show the version they require. The global minimum keeps older clients safe.
- Explicit launcher package metadata supports releases containing only mod updates without announcing a new launcher.
- A checked-in mod artifact ledger prevents rebuilding different bytes under an existing published mod version.

## 0.1.2 — 2026-10-09

- Steam integration becomes the default for installations identified by a matching Steam app manifest and library layout. Installing mods or launching them for the first time also installs the bridge; CLI `--install` follows the same policy.
- The loader patch and Steam bridge are applied as one transaction, so a failed install rolls them back together.
- Explicit **Disable integration** and **Enable integration** choices are retained through routine mod updates, reinstalls and restoration. Restoring clean SS14 removes the active bridge while preserving the preference for a future reinstall.
- Standalone installations keep their original launcher entry point by default. A published single-file executable is required only when installation enables the Steam bridge.
- Upgrade the launcher using the 0.1.2 ZIP to receive this change; mod DLL updates alone do not replace the launcher.

## 0.1.1 — 2026-10-09

- Mod update checks can retrieve the latest public release manifest when the anonymous GitHub API quota is exhausted.
- The fallback requires a canonical release asset redirect in the configured repository, validates the stable release tag, and retains bounded downloads and SHA-256 verification.
- Added regression checks for rate limits, unsafe redirects and mismatched release metadata.
- The original 0.1.0 release assets are preserved. Upgrade the launcher using the 0.1.1 ZIP to receive this fix.

## 0.1.0 — 2026-10-09

First public release of **SS14 ModLauncher by actemendes**, evolving the local-mod installer into a Windows x64 launcher.

- Mod selection and saved profiles, with bundled Crew Console and Hello World.
- Russian and English launcher interface and bundled mod labels.
- Original-loader backups and SHA-256 validation for install/restore.
- Optional reversible Steam launch integration, plus explicit adoption of verified upstream files with archived old backups.
- Local diagnostic report export and a one-session launch without mods.
- Manual GitHub Release mod update checks, with manifest and download validation; the default source is `actemendes/ss14-modlauncher`.
- A self-contained Windows package, local tests, and public user/developer documentation.

The existing Crew Console includes sensor health/position history and a draggable damage timeline. Complete web Crew Monitor replay, rooms, zones, recording files, and message history remain outside this release.

Release verification and compatibility limits are recorded in [docs/verification.md](docs/verification.md).
