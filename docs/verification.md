# Verification record — 0.1.x

Date: **2026-10-09**. Platform: **Windows x64**. Build toolchain: **.NET SDK 10.0.400**, self-contained .NET / Windows Desktop runtime **10.0.11**. Live game: **SS14 Launcher 0.40.2**, **Robust 275.0.0**, local Dev/Sandbox server bound to loopback with hub advertising disabled.

Live gameplay and the original complete Steam-to-game sessions cover the 0.1.0 implementation. Later launcher, package and local Steam bridge checks are recorded separately by version. Version 0.1.1 changes release discovery when the anonymous GitHub API quota is exhausted; mod and installation behaviour are unchanged in that version. Version 0.1.2 changes the default Steam installation policy.

## 0.1.10 — published and verified / 2026-10-11

- Distress Call **0.1.3** observes actual injury totals as well as awful alerts. Healing, improved severity and critical/dead state cancel pending calls; recovery requires 10 net damage from its best value, including a fresh 5-damage series. A 500 ms settlement, 3-second freshness limit and shared 30-second cooldown prevent recovery/jitter/stale-injury shouts. Attaching to an already injured body stays quiet.
- Verified Robust's authoritative state timing: `StartStateApplication` sets both `ApplyingState = true` and `IsFirstTimePredicted = false`. Damage/healing in this state is accepted, while actual prediction replays are ignored. Radio attempts are prohibited during state application/replay.
- Optional directed natural-melee fauna swing/damage correlation names a single likely attacker with “похоже” / “I think”. It requires close direction/range/parent checks and an 800 ms event window, works in both event orders, drops healing/ambiguous evidence and never claims confirmed Origin. Instant critical/dead characters cannot send a postmortem call.
- **126 policy checks and 31 isolated Harmony integration scenarios** cover recovery, serious relapse, sustained worsening, small fluctuations, stale cooldown expiry, switching bodies, native radio prefix, RU/EN text, state application, replays, fauna ambiguity, other targets/parents, held weapons, humanoids and failure containment. Actual SS220 / Robust **277.2.1.0** passed the expanded API contract script and accepted all five hooks.
- Stable release: [v0.1.10](https://github.com/actemendes/ss14-modlauncher/releases/tag/v0.1.10), from clean implementation commit `498a184987f051acb0f519097d7907fd7c9a1e37`, with `testsRun: true`, `sourceDirty: false`, `launcherBuilt: false`. Complete packaging regression suites passed, including **30/30 installation cases**. The exact packaged DLL passed all 126 policy checks and 31 isolated cases again and installed all five hooks against actual SS220 assemblies.
- All **nine public assets** downloaded anonymously and matched the local package and GitHub digests; all **eight checksum entries** and six assembly identities/versions matched. Production `UpdateService` discovered 0.1.10 through its rate-limit fallback (anonymous GitHub API returned 403), downloaded all six mods with correct hashes, retained launcher 0.1.8 and suppressed repeated updates for current versions.
- Published Distress Call **0.1.3** is locked with SHA-256 `A9A28D71D476CE31DCD9F47AB86FE2734B8D161E0361D0AEF044E02C9429AA6D`. Launcher **0.1.8**, bootstrap **0.1.2** and the five other mod DLLs retain their published versions/bytes; no launcher ZIP was rebuilt. Publication did not modify the user's game installation.
- GitHub Actions passed for the [implementation commit on main](https://github.com/actemendes/ss14-modlauncher/actions/runs/38092748540) and [v0.1.10 release tag](https://github.com/actemendes/ss14-modlauncher/actions/runs/38092840327). Live radio delivery after treatment or a spider attack has not been tested; fixture dispatch and real-game hook installation are the recorded compatibility evidence.

## 0.1.11 — published and verified / 2026-10-11

- Replaces the mistakenly implemented health status icons with the actual medical HUD **health bar above each character**. The native `EntityHealthBarOverlay` supplies bar geometry, colours, damage/critical/death threshold calculation and visibility filtering. Profession icons remain an independent option.
- **135 lifecycle/rendering/UI/Harmony checks** passed: native bar registration, healthy/critical/dead progress, visibility, missing containers, medical visibility prototype, scoped configuration restoration after renderer exceptions, equipment container/prototype preservation, no duplicate overlays, glasses removal, reset/disconnect and RU/EN labels. Actual SS220 / Robust **277.2.1.0** passed **92 API checks** and installed the required Harmony hooks.
- This correction is Debug Vision **0.1.5**, in mods-only feed **0.1.11**, retaining launcher **0.1.8** and the published Distress Call **0.1.3** from feed 0.1.10. Release 0.1.9 was returned to draft; previously downloaded 0.1.4 installations can upgrade normally. New bars have not been visually verified in a live game.
- Stable [v0.1.11](https://github.com/actemendes/ss14-modlauncher/releases/tag/v0.1.11) was built from clean commit `e66dfaf630cc4e91e9482697c33a6744191440e7`, with `sourceDirty: false` and `testsRun: true`. Full packaging suites passed, including **30/30 installation cases**. The final packaged DLL installed its required hooks against actual SS220 game assemblies.
- All **nine public assets** were downloaded anonymously and matched local bytes and GitHub digests; all **eight checksum entries** matched. Production `UpdateService` exercised GitHub API quota fallback, discovered v0.1.11, verified all six downloads and assembly versions, and suppressed already-current updates. Launcher remains **0.1.8**; the five other mod artifacts retain their published hashes.
- Debug Vision **0.1.5** is locked in the artifact ledger with SHA-256 `D686B10F93FCF75677743EBDF78FDEB7E47A5C9A7163B4BF696B024134A95768`. The user's game installation was not changed by publication.
- GitHub Actions passed for both [main](https://github.com/actemendes/ss14-modlauncher/actions/runs/38093247851) and [v0.1.11](https://github.com/actemendes/ss14-modlauncher/actions/runs/38093249726).

## 0.1.9 — withdrawn / 2026-10-11

The 0.1.9 release was withdrawn after the user clarified that the requested medical HUD means a health bar above the character. Debug Vision 0.1.4 incorrectly enabled only health status icons. The original publication evidence below is historical; the replacement is Debug Vision 0.1.5 in feed 0.1.11. Release 0.1.10 remains intact.

- Two independent, localized F1 panel toggles enable native health status and job icons without HUD equipment. Both default off, persist when the panel closes, and clear on reset/disconnect. Missing data stays missing; native icon visibility rules remain in effect.
- **130 lifecycle/rendering/UI/Harmony checks** passed, including native health states, missing health/job data, independent buttons, no duplicate job icons, preservation of other HUDs and equipment container sets, equipment changes, exception restoration, reset and reconnect. Actual SS220 assemblies (Robust **277.2.1.0**) passed **80 API checks** and accepted the required Harmony hooks.
- This is a mods-only feed retaining launcher **0.1.8** and the five previous mod artifacts. HUD rendering in a live game has not been visually verified; automated fixtures and installation against real game DLLs are the recorded evidence.
- Stable release: [v0.1.9](https://github.com/actemendes/ss14-modlauncher/releases/tag/v0.1.9), built from clean commit `37953b8b657a5ba0f9d1b7632a0e56672c1330bb` with `sourceDirty: false` and `testsRun: true`. Full packaging regression suites passed, including **30/30 installation cases**. No launcher ZIP was rebuilt.
- All **nine public assets** downloaded anonymously and matched the local build and GitHub digests; all **eight checksum entries** matched. The production `UpdateService` discovered v0.1.9, downloaded and verified all six mod DLLs and their assembly versions, retained launcher 0.1.8, and suppressed updates for already current installed versions.
- Published Debug Vision **0.1.4** is locked in the artifact ledger with SHA-256 `FD732A87DE72E09045F2C02DB36D141B8E8B674C3B3203DC16067D9498A628CB`. The user's game installation was not changed by publication.
- GitHub Actions passed on the published [v0.1.9 tag](https://github.com/actemendes/ss14-modlauncher/actions/runs/38091839894).

## 0.1.8 — published and verified / 2026-10-11

### Published package

- Stable release: [v0.1.8](https://github.com/actemendes/ss14-modlauncher/releases/tag/v0.1.8), built from clean source commit `4c54f44ce71da57fe2cab51c9181ff564ca3846b`; public provenance records `sourceDirty: false` and `testsRun: true`. Launcher 0.1.8 includes Debug Vision 0.1.3; the five existing mod DLLs retain their published bytes and bootstrap remains 0.1.2, rebuilt with current source provenance.
- Full packaging suites passed, including **109 Debug Vision checks**, **26 crew checks**, **56 Conversations checks**, Death Rattle integration/policy, **108 chemistry checks** and installation/catalog/update/setup/version tests. The final self-contained EXE passed **33/33 installation checks**, including isolated Steam handoff, standalone/Steam defaults and a read-only real-loader fixture. Released Debug Vision passed **70 actual SS220 API checks** and installed all **25 Harmony hooks**.
- All **10 public assets** downloaded anonymously and matched local bytes, GitHub digests and the nine-entry checksum file. ZIP contents, notices, clean provenance and the downloaded portable CLI install/restore on an isolated real-loader fixture were verified. The production updater discovered v0.1.8, downloaded all six mod DLLs with matching hashes and suppressed repeated updates for current installed versions.
- Updater verification above uses launcher 0.1.8. Launcher 0.1.7 and older validate manifests against their smaller embedded catalog and cannot accept the six-mod feed; they require a manual ZIP upgrade. Release notes provide the manual upgrade route. This limitation cannot be changed inside already distributed executables.
- The published Debug Vision 0.1.3 artifact is locked in the ledger with SHA-256 `73E4D9D871B930165933B87839552DD0CF06DC6CBF6514688CD20618E305FA7A`. The annotated tag resolves to the source commit above. GitHub Actions passed for [main](https://github.com/actemendes/ss14-modlauncher/actions/runs/38087138852) and [v0.1.8](https://github.com/actemendes/ss14-modlauncher/actions/runs/38087260761).
- Current RU/EN launcher library captures were inspected and saved: all six mod rows are visible, including Debug Vision 0.1.3. User-reported acceptance covers the locally installed 0.1.3 before publication; no detailed flash/welding or English in-game test sequence was recorded. Remote-server visibility, other forks and future engines still require separate compatibility checks. Publication did not restart or modify the running game installation.

### Release candidate / 2026-10-11

- The user confirmed that the installed Debug Vision 0.1.3 works and authorized publication. Read-only inspection confirmed installed assembly 0.1.3.0 and SHA-256 parity with the local effects package. This is user-reported gameplay acceptance, not a recorded sequence of flash/welding or English UI scenarios.
- The release includes launcher 0.1.8, Debug Vision 0.1.3 and the five previous mod DLLs at their existing immutable versions, with bootstrap 0.1.2. Publication evidence is recorded after final package and public download checks.

### Debug Vision 0.1.3 visual effect protection / 2026-10-11

- Added Ctrl+B and a RU/EN panel switch for visual effect protection, independent of FOV/fullbright/shadows. Seven known overlays are suppressed: flash, blindness, blurry vision (including welding injury), drunk, drowsiness, rainbows and damage shading. Native effect updates/state continue; disabling protection restores ongoing effects. Reset/disconnect clears protection. Unrelated overlays are untouched.
- Blindness may have disabled lighting before protection is enabled. Its draw prefix restores only the native render flags/light manager; `IsBlind` and `EyeDamage` remain unchanged. Damage, slowdowns, stuns and server-side visibility limits are not removed.
- **109 lifecycle/rendering/UI/Harmony checks** passed, including real byref-like draw arguments, all seven effects, enabling during blindness, native lighting recovery, frame updates, unrelated overlays, active-effect restoration, Ctrl+B text focus/repeats, panel state and lifecycle reset. Actual SS220 assemblies passed **70 API checks**. Live flash/welding testing remains pending; this is not evidence of gameplay immunity.
- Full offline packaging checks passed. `dist/debug-vision-effects/SS14ModLauncher-0.1.8-win-x64.zip` includes Debug Vision 0.1.3; its DLL SHA-256 matches the manifest and all **25 Harmony hooks** installed against actual SS220 assemblies. The game installation has not been updated automatically.

### Debug Vision 0.1.2 zoom fix

- User-reported live testing of 0.1.1 confirmed F1/Ctrl mode shortcuts and native zoom binding compatibility, but one press could zoom out excessively. Actual Robust input code replays the native handlers inside a past-prediction area; a regression with 20 such replays reproduced the extra multiplication before the fix.
- Local targets now step only when `IGameTiming.IsFirstTimePredicted` is true. Original handlers continue running during replays. The displayed scale smoothly approaches each native target at 8/s, with consistent timing across render frame rates; reset cancels the transition.
- **74 lifecycle/rendering/UI/Harmony checks** and **52 actual SS220 API checks** passed, including single-step prediction replay, repeated real presses, zoom reversal during animation, convergence, frame-rate independence and invalid frame times. Live testing of 0.1.2 remains pending.
- Full offline packaging suites passed; `dist/debug-vision-stepped/SS14ModLauncher-0.1.8-win-x64.zip` contains Debug Vision 0.1.2 with a DLL hash matching its manifest. The packaged DLL installed all **18 Harmony hooks** against actual SS220 assemblies. The running server/client were retained; installation into the user's game is still a separate manual step.

### Debug Vision 0.1.1 update

- Ctrl+N/L/H/R shortcuts and permanently available extended zoom passed **63 lifecycle/rendering/UI/Harmony checks**. The fixture's remapped zoom bindings run the original native handlers and then update the shared local camera scale beyond the native clamp. Mouse/native controls, native reset, render bounds, other/null sessions, text focus, exact modifiers and shortcut repeats/releases are covered.
- Actual SS220 client assemblies passed **48 API checks** and accepted all **17 Harmony hooks**. The native session-handler signatures and zoom-step constant are verified in Content.Shared. The original 0.1.0 test session remains open; the updated package uses `dist/debug-vision-hotkeys` so its executable can be built without replacing a running launcher. No current game client/server is restarted for this build.
- The user subsequently confirmed working mode shortcuts and native zoom binding compatibility in a live session. Excessive zoom stepping was reported next and is addressed by 0.1.2 above.
- Preparing the next local session exposed that reinstall always preferred the old installed mod DLL. The merge now selects newer bundled assemblies while preserving equal/newer independent updates and unknown metadata; **6 regression checks** and the **30/30 installation suite** passed. The rebuilt portable launcher requires explicit reinstall to move the user's installed Debug Vision from 0.1.0 to 0.1.1. The loopback SS220 server was restarted and reported Ready; this does not establish live hotkey behaviour.

### Original 0.1.0 build

- Full offline build produced `dist/SS14ModLauncher-0.1.8-win-x64.zip` with Debug Vision **0.1.0**, bootstrap **0.1.2** and the five existing mod DLLs reused byte-for-byte. Nothing was published or installed into the user's game.
- **45 Debug Vision checks** passed: actual Harmony patches on contract fixtures, independent switches, camera replacement, latest native Zoom/Scale restoration, reset/disconnect, RU/EN window events, mouse zoom controls, text focus, modifier shortcuts and matched key releases. F1 opens only Debug Vision when the actual Test Mod is also installed; other keys retain native actions.
- Actual SS220 client assemblies with **Robust.Shared 277.2.1.0** passed **41 API checks**; the packaged mod installed all **11 Harmony hooks** successfully in the assembly smoke harness.
- Full packaging suites passed: release version/artifact checks, **26 crew checks**, **56 Conversations checks**, Death Rattle policy/integration scenarios, **108 chemistry checks**, **30/30 installation checks**, bootstrap/catalog/update/setup checks. All six packaged DLL hashes matched the generated manifest; the five existing DLLs matched the immutable ledger.
- The built launcher rendered the six-row Russian catalog including **Дебаг-видение 0.1.0** in an isolated capture session. Live game rendering, FOV around walls and remote-server PVS limits remain unverified; API/hook installation is not a gameplay smoke test.

## 0.1.7 — published and verified / 2026-10-10

- Stable release: [v0.1.7](https://github.com/actemendes/ss14-modlauncher/releases/tag/v0.1.7), built from clean implementation commit `2e76f089f820e5a5b0db9e401e5ca02a8b0d86bb`; public provenance records `sourceDirty: false` and `testsRun: true`. Launcher **0.1.7** has a compact searchable library, localized display names and offline mod details; Auto Chemistry (ChemMaster) **0.1.4** is built from source. Bootstrap **0.1.2** and the other four mods retain their published versions and bytes. Both READMEs feature the supplied Auto Chemistry artwork near the top; both artwork and gameplay screenshot are included in the ZIP.
- Full packaging regression suites passed: **26 crew checks**, **56 Conversations checks**, Death Rattle policy and integration checks, **108 chemistry checks**, **30/30 core installation checks**, and catalog/update/setup suites. The actual self-contained EXE passed **33/33 installation checks**, including isolated Steam handoff, standalone/Steam CLI install and restore, opt-out persistence and a real-loader fixture. Full SS220 chemistry rules passed **124 checks**, actual SS220 assemblies passed **131 API contracts**, and all seven hooks installed against the released DLL.
- All **nine public assets** downloaded anonymously and matched local sizes, SHA-256 hashes and the eight-entry checksum file. The downloaded ZIP, provenance, licenses and portable CLI install/restore were verified on an isolated real-loader fixture. Production `UpdateService` discovered v0.1.7 through the rate-limit fallback, verified downloads of all five DLLs and suppressed repeated updates for current versions. GitHub's latest-asset redirect briefly retained v0.1.6 immediately after publication, then resolved correctly to v0.1.7; no update code change was required.
- The publicly verified ChemMaster **0.1.4** artifact was added to the ledger with SHA-256 `37A163FEF67723648354DEE2B54D4670C8AD839EE3FA331EDA415C8812871192`. No running game client or server was restarted during publication.
- RU/EN actual WinForms UI checks passed: all five 64-pixel rows, localized categories and details, search across both display-name languages, empty/cleared search, persisted selection in the active profile and minimum-window layout. All five descriptions include localized usage instructions; the unchanged **685 × 689** supplied screenshot loads from the executable's embedded resources in both languages. Documentation screenshots come from the actual UI on a disposable installation fixture.
- Live check on the local loopback SS220 Dev server through the QA client host, not through an installed ModLauncher package: the user exercised the reworked AUTO tab, automatic replanning after loading a missing reagent, the combined missing-stock message and the stopped completion timer, and accepted the result. The session was not recorded step by step.
- Not verified in this release: the English layout in game, beaker-replacement flow in the new tab and Replan after a failure with a non-empty beaker. Existing live UI evidence is separate from packaged install/restore verification.
- Behaviour change to review before publishing: reactions are no longer rejected because of their effects, so recipes with EMP, explosion or entity-spawning effects are planned and executed like any other. Execution still stops when a resulting composition differs from the plan.

## 0.1.6 — published and verified / 2026-10-10

- Stable release: [v0.1.6](https://github.com/actemendes/ss14-modlauncher/releases/tag/v0.1.6). The tag resolves to clean implementation commit `8bccb851f81e6c4fd14ba5a66b96908f337bed38`; public `build-info.json` records this revision, `sourceDirty: false` and completed tests. Launcher **0.1.6**, ChemMaster **0.1.3**, Death Rattle **0.1.2**, Conversations **0.1.1**, Crew Console/Hello World/bootstrap **0.1.2**.
- Full clean-checkout build and regression suites passed. The actual packaged EXE passed **33/33 installation checks**. SS220 checks passed: **114 chemistry assertions**, **111 ChemMaster API contracts**, all seven ChemMaster Harmony hooks and Death Rattle native API contracts.
- GitHub Actions succeeded for [main](https://github.com/actemendes/ss14-modlauncher/actions/runs/38006071735) and [v0.1.6](https://github.com/actemendes/ss14-modlauncher/actions/runs/38006074556).
- All **nine public assets** downloaded anonymously and matched local sizes, SHA-256 hashes and the flat checksum manifest. The ZIP, provenance, licenses and portable CLI install/restore on an isolated real-loader fixture were verified. Production `UpdateService` discovered v0.1.6, downloaded all five mod DLLs with matching hashes and suppressed updates for current versions.
- Verified immutable artifact entries were added for ChemMaster **0.1.3** (`A86A92814B0F07B7EC020A934763209672D34CA26235207A99FF631CB1641FF9`) and Death Rattle **0.1.2** (`3C389101AB2DF80ACD49F48230B0C909CB2405DB4146C9B7131251DEB2FA27D6`); the three existing mod DLLs retain their previous published bytes. These are canonical release artifacts; the earlier local ChemMaster package below used a different build path and has its own recorded hash.
- No running game client or server was restarted during publication. Existing live ChemMaster evidence and remaining compatibility limits are recorded below; publication does not claim full live Aglomorphine completion or radio distress delivery.

## ChemMaster 0.1.3 — beaker selection recovery / 2026-10-10

- Read-only process snapshots independently confirmed the same **58,706.812 K** solution temperature in the client and existing SS220 server. The user's stopped Aglomorphine plan had **29 actions** left, beginning with a cold-beaker barrier. The old cold checkbox called the recipe-edit handler and cleared active execution.
- **114 chemistry checks passed** against the full SS220 export, including a captured buffer fixture reproducing the exact 29-action barrier and a 28-transfer recovery plan that yields **100 u** after a 295.5 K cold replacement. The standalone suite has **98 checks**. Explicit cold/hot phase metadata prevents misclassifying a slightly warmed cold beaker; persisted finite superheated temperatures have no arbitrary upper cutoff.
- Actual SS220 assemblies pass **111 API checks** and all seven Harmony hook installations, including native exclusive toggle controls and the ordinary item-slot BUI event. The full isolated 0.1.6 package passed the existing suites and **33/33 packaged installation checks**. No release was published.
- Installed ChemMaster **0.1.3** transactionally in the existing Steam installation; installed SHA-256 `B2DE91C38D03FA702436EEFD2B317DBCBC326B25EF10B8E12754080CE4EC908F`. Other mod bytes and selections were preserved. Saved the original Aglomorphine goal as an **Ensure 100 u** recovery recipe. Only the developer client restarted; server PID **32416** and its start time stayed unchanged.
- Live native UI verification on the user's prepared machine: the exclusive Cold/Hot buttons render and switch; measured **58,706.81 K** stays independent of selection. Loaded the saved Ensure goal and reproduced **29 actions** from the exact live buffer. Starting reaches the first cold-beaker barrier without reagent transfers. Switching Hot then Cold, ejecting and reinserting the same empty beaker through AUTO, and attempting Resume with the wrong actual temperature all retain the active **0/29 AwaitingBeaker** state. Read-only snapshots confirm the original intermediate stock and beaker identity were retained. The client is left at the prepared machine for the user's physical cold replacement; full live completion is not claimed.

## ChemMaster 0.1.2 — SS220 temperature phases / 2026-10-10

- **106 chemistry checks passed** with an offline export of the current SS220 checkout (`0fade9370721a7d638f252cabaae1306e95aebcd`, Robust **277.2.1.0**): 454 reagents and 346 reaction definitions. The export is test data only; the mod still reads live server prototypes. The standalone core suite contains **93 checks**.
- Aglomorphine **100 u** from sufficient raw stock produces a validated plan of **168 actions with three beaker replacement barriers**, using a 293.15 K cold beaker and a prepared 988 K hot beaker. A second execution simulation replans from changed measured temperatures and replacement identities and retains the original absolute goal. This is offline evidence, not a claim of completed live Aglomorphine production.
- Regression coverage includes competing hot reactions during intermediate production, missing raw stock without misleading alternative-recipe errors, exact server mixer categories, gas/popup effect support, unknown-effect rejection, explicit replacement confirmation, no sends or command timeout during replacement, wrong-temperature rejection, changed machine/player/inventory/mode/capacity rejection, stopping a replacement wait and persisted hot temperature.
- The actual SS220 assemblies passed **104 API checks and all seven Harmony hook installations**. The mod and developer client host build with zero warnings. The developer host starts the game on an STA thread for Windows support.
- The full local 0.1.6 package embeds ChemMaster **0.1.2**. Existing launcher/bootstrap/regression suites passed, and the packaged executable passed **33/33 installation checks**, including copied real-loader and self-contained CLI fixtures. No release was published.
- Installed ChemMaster 0.1.2 in the user's Steam installation using the transactional installer; installed SHA-256 is `A4DBF942600ED95DB8C095899DF089235DE77D46D06302AD8470F9234FC450FD`. Existing other mod bytes and enabled selections were preserved. The hot-beaker profile was set to the user's **988 K** preparation.
- The previous upstream QA server was stopped. A separate local SS220 Dev/Sandbox server runs with TCP/UDP bound to `127.0.0.1:1212`, hub advertising disabled and local admin access. Only its developer client is restarted for this update; the prepared server world is retained. Live cooking with the new phase barriers remains to be checked in the prepared ChemMaster.

## ChemMaster 0.1.1 — local implementation / 2026-10-10

- **71 core checks passed** with the optional external SS220 rules fixture (65 without it). The fixture covers Inaprovaline, Bicaridine, Dylovene, Kelotane, Dexalin and Tricordrazine. It is test data only; shipping chemistry is read from the current connection.
- Timing checks cover faster repeated reagent transfers, an additional reagent-switch pause, shorter/longer random intervals, probability misses, once-per-command sampling, live speed changes, pause/resume, persistence, invalid settings, beaker changes during timed waits and unchanged confirmation timeouts. RU/EN statuses and planner errors are checked explicitly.
- **104 reflected API checks passed** against local Robust **275.0.0.0** / upstream Content assemblies. All seven actual Harmony hooks installed successfully. The mod builds without warnings.
- The 0.1.0 native tab, reagent search, adding targets and missing-beaker rejection were observed in the isolated QA client. The user subsequently confirmed successful Bicaridine production in-game. This is user-reported production evidence for 0.1.0; it does not claim a new live timing/slider or English layout run for 0.1.1.
- The local 0.1.6 package including ChemMaster 0.1.1 was built under `dist/chem-master/` and later installed for the user's test. The subsequent 0.1.2 installation and SS220 checks are recorded above. No release is published. Compatibility with other forks and new engine versions requires separate verification. See [AUTO usage and settings](mods/chemmaster.md).
- Full offline packaging and the existing regression suites passed in an isolated source copy because the live QA process held the workspace Harmony DLL. The actual packaged EXE passed **31/31 installation cases**, including standalone/Steam CLI install and exact restoration in disposable fixtures. All five mod hashes match the manifest; the packaged ChemMaster DLL also passed the seven-hook integration check. This package remains unpublished.

## 0.1.6 — local Death Rattle build / 2026-10-10

- Death Rattle 0.1.2: **32 policy/Harmony checks and 14 isolated integration scenarios passed**, including RU/EN replies, language-change spam protection, humanoid/entity attribution, radio prefix, repeated alerts, healing, self/other damage, replay, unknown origins, optional failure, send failure, polling and unsupported content. Distress replies use one sentence without a colon or an entity label and follow `SS14_MOD_LANGUAGE` from the launcher selection.
- `Test-DeathRattleApi.ps1` passed against the local game DLLs with **Robust 275.0.0.0**. An isolated process loaded the actual game assemblies and successfully installed all four Harmony hooks (sync, clear, update and damage Origin).
- Actual `DamageableHandleState` IL supplies an empty nullable Origin; ordinary replicated damage therefore falls back to the basic distress message. Attribution availability is a protocol limitation.
- Full offline packaging passed release-build checks, **26 Crew Console checks, 56 Conversations checks, 30/30 installation cases** and catalog/update/setup/bootstrap suites. Windows x64 ModLauncher 0.1.6 includes the new DLL and keeps all earlier published mod bytes intact.
- The local package is `dist/SS14ModLauncher-0.1.6-win-x64.zip`. No release was published and the user's installed game/profile was not changed. Live distress transmission, receipt by other players and compatibility with other forks remain **pending**. See [Death Rattle](mods/death-rattle.md).

## 0.1.5 — implementation and publication

- Conversations 0.1.1 adds native chat timestamps, separate name and message-text searches, selected voices and department/radio-channel filtering. Existing mod DLLs and bootstrap remain 0.1.2.
- **56 Conversations checks passed**, including native OOC/LOOC/dead-chat markup, real Harmony hooks on a test game API fixture, UI events, history repopulation, channel filtering, exception cleanup, case-insensitive word/phrase search, combined filters and newly received messages.
- `Test-ConversationsApi.ps1` passed against local Content.Client and Robust.Client **275.0.0.0** DLLs, checking the actual reflected members used by the adapter.
- **26 Crew Console checks and 30/30 installation cases passed**, along with catalog/update/setup and release-build checks.
- A self-contained local Windows x64 package was built under `dist/conversations-0.1.5`; nothing was published. Local installation and startup checks are recorded below.
- Multi-speaker/radio, scaling and scroll/audio verification remain **pending** beyond the live smoke checks below. The API fixture and DLL contract checks do not establish compatibility with every server fork. See [Conversations](mods/conversations.md).

### Local installation and connection — 2026-10-09

Conversations was installed and enabled alongside the existing Crew Console in the user's Steam installation using the transactional installer API. Installed Conversations SHA-256 matches the built DLL; existing Crew Console bytes were preserved. The active launcher profile includes the new mod and the Steam launcher bridge was updated to 0.1.5.

The local Dev/Sandbox server runs on loopback port 1212. The original launcher connected its client successfully; server logs confirm the session and character transfer. Runtime logs confirm both selected plugins loaded into Content.Client without Conversations errors. The live game screenshot shows the native Conversations panel and message timestamps. Multi-speaker/radio scenarios, scaling and audio behaviour remain pending beyond this startup smoke test.

The installed mod was subsequently updated to Conversations 0.1.1 and the client reconnected to the same local server. Its DLL matches the package SHA-256 `3766F7301D640A17262752A77BC0A9131655321A527C7BD59F2486F03058110C`; the existing Crew Console DLL was preserved. Live UI verification confirmed the separate message-search field: entering `welcome` retained the welcome message with its timestamp and hid unrelated admin messages; Reset cleared the field and restored the history. Russian `зомби`/`ЗОМБИ` punctuation and speaker-preservation cases passed in the automated fixture suite.

### Published 0.1.5 — passed

- Release: [v0.1.5](https://github.com/actemendes/ss14-modlauncher/releases/tag/v0.1.5), built from clean source commit `94a1dbca2f2aeb6ff421d1941dadf1209715f181`; `sourceDirty: false`. [Windows CI](https://github.com/actemendes/ss14-modlauncher/actions/runs/37950961455) passed for that exact source.
- The complete self-contained launcher passed **33/33 installation/executable checks**, including real-loader roundtrip, Steam handoff and isolated-runtime CLI installation. Package verification caught and fixed a hardcoded two-mod payload list; runtime/mod payload discovery now follows the catalog.
- All seven public assets downloaded anonymously and matched the clean build, GitHub digests and flat checksums. Manifest URLs, three embedded mods, archive contents, notices and source provenance passed. The downloaded EXE installed and restored a disposable legacy installation with exact original bytes.
- ZIP SHA-256: `A85A310F669ED545623F6CA53B3B80367233A47DB7934A67F708186E56A21C16`. Published Conversations 0.1.1 SHA-256: `7A0107C3A3C4519A081B7BB64EB1F5E7154108C481049D70DB78AA8ECA742ED4`.
- Production UpdateService discovered the public 0.1.5 feed after the GitHub latest-download cache refreshed. Anonymous API rate limiting used the canonical public fallback; all three mod downloads matched the manifest and local bytes, and a repeated check offered no current mod or launcher updates.
- The verified Conversations artifact was added to the immutable ledger. Crew Console and Hello World published bytes and prior releases remain unchanged. Live game smoke checks above refer to the earlier local build of the same mod source; no additional radio/scaling/audio pass is claimed.

## 0.1.4 — local checks completed and release published

This change adds first-run setup to launcher 0.1.4. The bundled mod DLLs and bootstrap remain 0.1.2.

- **30/30 installer core cases passed** with the catalog, bootstrap, update and startup-notification regression suites.
- New setup checks passed for discovery/root normalization, persisted per-root completion/dismissal, missing-folder context, installed/restored suppression, changed-loader handling and corrupt settings.
- Running/uninspectable-process readiness and retry checks passed using the existing injected exception contract. These tests do not start or kill game processes.
- **20 actual WinForms setup scenarios passed:** fresh RU/EN setup, missing-folder and retry flows, Later/window-close dismissal, language switching, read-only settings, recovery, native 96 DPI with simulated 125%/150% scaling, minimum width, existing-installation suppression, disabled onboarding and update coexistence.
- The UI harness exercised the production install, settings save and `StartGame` path against a disposable fixture with a benign launcher stub. Failed-launch retry passed. This confirms the launch handoff, not live gameplay.
- The existing **eight update-notification UI scenarios passed**. The setup suite also passed after final focus/colour polish.
- **33/33 installer and packaged-executable checks passed**, including a copied real loader roundtrip, Steam bridge handoff, restoration while the stable UI remained open, self-contained standalone/Steam CLI installation and restoration, and a durable integration opt-out.
- The actual release EXE's `--capture` mode with fresh isolated settings exited successfully without opening onboarding or writing settings; its PNG was inspected.
- Documentation relative links and whitespace checks passed. Bootstrap project, mod catalog versions and published artifact ledger remain unchanged.
- Release: [v0.1.4](https://github.com/actemendes/ss14-modlauncher/releases/tag/v0.1.4), built from clean source commit `6e86be8afc759bd68d9f0a0b9b716e986a0e9544`; `sourceDirty: false`. [Windows CI](https://github.com/actemendes/ss14-modlauncher/actions/runs/37938819604) passed for that exact source.
- All six public assets downloaded anonymously and matched the local bytes and SHA-256 checksums. The public tag resolves to the clean build source above. Manifest metadata preserves the independent 0.1.2 mod versions and their locked DLL hashes.
- The anonymously downloaded ZIP passed inspection; its self-contained EXE installed and restored a disposable installation with exact original-file hashes.
- Release ZIP SHA-256: `A0B9B8228C73B1306E7B1710392DE74D96C9403F9D0ACF6F2A7DA8F6038D1E10`. EXE SHA-256: `F998BA18E1F555DC0F6A964BD765C05BE806B0D73E5FAA634DA72118D2587756`.
- The local Steam bridge was upgraded to 0.1.4 with integration retained as enabled. Comparing all 99 installation files showed only the two bridge EXEs and two ownership JSON files changed; mod DLLs, settings, selection, original files and backups were preserved. No app processes were killed.
- After GitHub's public cache refreshed, production UpdateService discovered the actual latest 0.1.4 release. An anonymous API quota failure (HTTP 403) used the canonical public manifest fallback; both mod downloads matched the release bytes and SHA-256, and a repeated check reported no mod or launcher updates for current versions.
- The release-tag [Windows CI](https://github.com/actemendes/ss14-modlauncher/actions/runs/37939074715) also passed.

At the time of this verification, **0.1.4** is the latest stable release. Previous release assets are preserved.

No new live gameplay pass is claimed for 0.1.4. Historical results below remain associated with their recorded versions.

## 0.1.3 — completed checks and publication

This change adds startup notifications and independently versioned releases. Launcher 0.1.3 targets the unchanged published 0.1.2 mod DLLs; prior live-game evidence remains associated with 0.1.0. No new live gameplay pass is claimed.

- **33/33 installer and packaged-executable checks passed**, together with startup-notification and update-schema suites. This includes the copied real loader, published Steam handoff and self-contained CLI installation/restoration.
- Startup tests cover default-on settings migration, saved opt-out, corrupt-file preservation, a nonblocking single attempt, quiet offline errors, cancellation and stale-result suppression for repository, installation, version and preference changes. Metadata checking does not download/apply updates or mutate installation/settings files.
- Update-schema checks cover independent launcher/mod versions, compatible versus blocked mods, per-mod requirements and conservative global requirements for older clients.
- **Eight actual WinForms UI scenarios passed:** RU/EN mod-only notifications, mixed compatibility, launcher-only notification, offline status, disabled checking, cancellation and preservation of an unsaved installation path. Updated RU/EN layouts were also inspected.
- Synthetic build tests passed for a new mod version **2.4.0** while bootstrap stays **0.1.2**, and for reuse of locked published mod bytes even when their old source is deliberately unbuildable.
- Documentation links and whitespace checks passed.

- An actual isolated mod-only build used feed **1.5.1**, launcher **0.2.0** hosted under **v1.5.0**, and mod **2.4.0** with Bootstrap **0.1.2**. It emitted no launcher ZIP and left existing payload files untouched. A separate build reused the real published 0.1.2 mod DLLs byte-for-byte.
- Update test fixtures use explicit installed-version baselines. The entire core suite also passed in an isolated copy with both catalog mod versions set to **2.4.0**, confirming that future mod versions do not invalidate the fake older release fixtures. This test-only follow-up does not change published binaries.
- Release: [v0.1.3](https://github.com/actemendes/ss14-modlauncher/releases/tag/v0.1.3), source commit `6d2b231e15d65408813fc68fabc52bd06b857e8c`; `sourceDirty: false`. The public tag matches this commit and [Windows CI](https://github.com/actemendes/ss14-modlauncher/actions/runs/37934149654) passed.
- All six public assets downloaded anonymously and matched the local bytes, sizes and checksums. The manifest correctly separates launcher **0.1.3**, mod versions **0.1.2**, and minimum launcher **0.1.2**; both mod hashes match the existing published artifact ledger.
- The downloaded ZIP passed archive/license/provenance inspection. Its self-contained EXE installed mods on a disposable legacy copy, then restored exact original loader and launcher bytes with shared runtime discovery disabled.
- ZIP SHA-256: `D63A2A1A9799C1C279A6D8BE27ED15B44083453699DB0120157B7237D0CD9241`. EXE SHA-256: `EEED011130720AD9F53F02604C22211FD7439AA880F6DD8C17C4459A6340244C`.
- Production UpdateService read the actual latest 0.1.3 manifest, verified both mod downloads against the release bytes and SHA-256, then reported no repeated mod or launcher updates for current versions.
- The local Steam bridge was upgraded to 0.1.3 while its processes were stopped. Before/after hashes of all 99 files showed only the two bridge EXEs and two ownership JSON files changed; original launcher, loader, mods, selection, preferences and backups remained unchanged. Steam integration stayed enabled.

At the time of this verification, 0.1.3 was the latest stable version. Previous release assets were preserved.

## 0.1.2 — completed installation and package checks

The new default enables the Steam bridge together with the mod patch on a recognized Steam installation, unless the user explicitly opted out. Standalone installs keep their original entry point.

- **33/33 installer and packaged-executable tests passed**, including 30 core cases, a copied real SS14 loader, published Steam handoff and actual self-contained CLI execution.
- The actual release EXE's `--install` enabled both wrapper copies in a fixture with a matching Steam manifest/layout. Standalone installation kept its original executable. GUI install and first mod launch use the same policy entry point, confirmed by independent source review.
- Invalid bridge preflight and a locked Steam executable left the installation unchanged: loader and bridge writes roll back together.
- Explicit disable survived updates, reinstall and restore/reinstall. Missing preferences, including legacy ownership files, enabled the default on explicit installation; restoration retained the saved preference.
- The published wrapper handed off to the stable launcher, which stayed open while restoration replaced the entry point. Restored original loader and launcher bytes matched exactly.
- **26 Crew Console checks passed**; bootstrap, profiles, catalog, semantic version, bounded update and rate-limit fallback suites passed. Independent review found no blocking defects.
- Russian and English installation-page captures were inspected; the new default-integration explanation fit within the existing layout.

The current local Steam integration is retained as enabled at the user's request. Prior test cleanup statements below refer to their recorded historical sessions. No new live gameplay run is claimed for 0.1.2; game and mod behaviour were unchanged by this installation-policy change.

## Completed checks — 0.1.0 / 0.1.1

| Area | Result |
| --- | --- |
| Crew history, timeline, layout and map projection | 26 regression checks passed |
| Installer and restore core | 24/24 cases passed: 21 core scenarios plus real-loader, published Steam handoff and self-contained executable tests |
| Bootstrap selection | Selection validation, path confinement, SHA-256 validation and clean-session bypass passed |
| Catalog, profiles and updates | Settings/profile persistence, semantic versions, HTTP pipeline, manifest/source/download bounds, rate-limit fallback and invalid update rejection passed |
| Self-contained package | Extracted EXE installed, inspected and restored a disposable installation with isolated runtime variables; host trace confirmed use of bundled runtime |
| Original loader compatibility | Install/restore on a copied real SS14 loader restored exact original bytes |
| Live Crew Console, English | Only Crew Console loaded; native console, portrait layout, crew row, map and health card displayed; loss of server telemetry preserved history and showed no telemetry |
| Live Crew Console, Russian | Only Crew Console loaded through the actual Steam chain; current sensor count, health card, map, landscape layout, historical point selection, dragging the timeline and return to Live passed |
| Live Hello World, Russian | Only Hello World loaded; F1 opened and closed its localized window; native crew console remained unchanged |
| Live empty selection | A fresh client connected with no verified mods; bootstrap continued with the standard client and the native crew console |
| Actual Steam integration | Steam Play opened the stable ModLauncher; its Launch action opened the preserved original launcher, which connected the client to the local server |
| Actual Steam restoration | Product restore returned exact original loader and launcher bytes and disabled the bridge; the pre-test legacy installation was then reinstated, matching all 92 baseline files by path and SHA-256; Steam Play then opened the original launcher normally |
| Published executable handoff fixture | Stable launcher handoff and restoration while that UI remained open passed |
| Packaging | Self-contained EXE and ZIP, runtime/dependency notices, MIT license, documentation and artwork present; release asset SHA-256 checksums verified |
| Documentation and source audit | Relative links checked; publication files/history checked for secrets and proprietary game binaries |
| Build version guard | An inconsistent requested version was rejected before output mutations |
| GitHub CI | Windows build and test workflow passed on the published source |

The live sessions used the game's own sensor network and locally spawned test NPCs. A disposable test server with an independent power supply was used after the Dev map's original sensor server lost power. No public multiplayer server was used for these tests. The original installation and ModLauncher settings were restored; the local test server was stopped.

## Publication verification

Release packages are built from the clean Git revision recorded in `build-info.json`. All six public **0.1.0** assets were downloaded anonymously and matched the original build and `SHA256SUMS.txt`. The downloaded ZIP was inspected and its self-contained EXE completed install/restore on an isolated copy with exact original hashes.

The initial anonymous API update check encountered GitHub's shared-IP quota limit (HTTP 403, remaining quota 0). Version **0.1.1** adds a public latest-release asset fallback. Its regression tests cover successful discovery/download, ordinary errors that must not fall back, canonical tag identity, foreign repositories, prereleases, invalid manifests, direct CDN routes, size limits and redirect loops. An independent code review found no blocking issues. Before packaging 0.1.1, the updated production UpdateService was exercised against the public 0.1.0 release: actual API 403 led to the canonical public manifest, both DLLs downloaded and matched the release bytes and SHA-256 values, and a repeated check returned zero updates.

### Published 0.1.1 — passed

- Release: [v0.1.1](https://github.com/actemendes/ss14-modlauncher/releases/tag/v0.1.1), source commit `1a00eeba5bb8e82a9efe38793e4eedf5e29f7ced`; `sourceDirty: false`.
- [Windows CI](https://github.com/actemendes/ss14-modlauncher/actions/runs/37925681859) succeeded for that exact commit. The final extracted package passed all 24 installer/packaged-executable checks and the bootstrap, catalog and update suites.
- All six public assets downloaded without authentication and matched the local build. Checksums, manifest URLs/hashes, archive contents, licenses and source provenance passed.
- ZIP SHA-256: `E0AC42064B61F97AC0DB5B41B806DC42E5FFCE117E983D450586537810F24109`.
- The downloaded self-contained EXE installed both bundled mods on a disposable legacy installation, then restored the original loader and launcher bytes. Embedded DLLs matched the public release assets.
- Production UpdateService successfully checked the actual latest 0.1.1 release, downloaded both mod DLLs with matching SHA-256 and local bytes, then reported zero updates for current installed versions. The public fallback worked while the anonymous API quota remained exhausted.
- The published tag resolves to the build's source commit. Original 0.1.0 assets remained unchanged; 0.1.1 was the latest stable release at that verification.

### Published 0.1.2 — passed

- Release: [v0.1.2](https://github.com/actemendes/ss14-modlauncher/releases/tag/v0.1.2), source commit `7a602676963954c3c28b30dba6f8f569fd419b1f`; `sourceDirty: false`. The published tag resolves to this exact commit.
- [Windows CI](https://github.com/actemendes/ss14-modlauncher/actions/runs/37928401127) succeeded for that source. The locally packaged EXE passed the 33 installation/executable checks above.
- All six public assets downloaded anonymously and matched the build, sizes and SHA-256 checksums. Manifest URLs/hashes, archive contents, licenses and source provenance passed.
- ZIP SHA-256: `E87B4AE9329EA7141A1481F03665E02CF1F6332AF85FD2AB90A70F052DF64025`. EXE SHA-256: `A942FEEE3E6BAF96CA770358D5352A3F3EA46A8A728294273EBC5A7063671D25`.
- The downloaded self-contained EXE installed bundled mods on a disposable legacy installation with shared runtime discovery disabled. Embedded mod bytes matched the public DLLs, and restoration returned exact original loader and launcher bytes.
- Production UpdateService checked the actual latest 0.1.2 release, downloaded both mod DLLs with matching SHA-256 and local bytes, then returned no updates for current versions.
- 0.1.2 is the latest stable release at this verification. Prior release assets remain unchanged.

## Scope and remaining compatibility limits

- These results cover the tested Windows installation and engine version, not every server fork, future SS14 update or Steam library layout.
- Russian and English native UI and both crew console layouts were inspected at the desktop's current display settings. All Windows DPI combinations were not tested.
- The self-contained trace confirms the bundled runtime on this computer; a fresh Windows machine without an installed .NET runtime was not available.
- The application is not digitally signed by a public code-signing certificate. Antivirus reputation and SmartScreen behaviour vary by machine.
- Normal authentication and engine signature verification were preserved. No claim is made that every possible argument accepted by the original launcher was individually exercised.

See the [release checklist](releasing.md) for future versions.
