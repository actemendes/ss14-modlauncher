# Дебаг-видение / Debug Vision 0.1.4

Enable **Дебаг-видение / Debug Vision** in ModLauncher 0.1.8, apply the profile and enter the game. Press **F1** to open or close the native, movable window. Labels follow the launcher's RU/EN language. Extended zoom is always enabled during gameplay; the debug vision switches start off.

| Option | Behaviour |
| --- | --- |
| Omnivision · Ctrl+N | Disables the main camera's field-of-view mask, including wall occlusion. |
| Fullbright · Ctrl+L | Disables the lighting buffer; field of view remains an independent switch. |
| Disable shadows · Ctrl+H | Disables light shadows independently of lighting and FOV. |
| Visual effect protection · Ctrl+B | Hides flash, blindness, welding blur, intoxication/rainbow/drowsiness distortion and damage screen shading. Starts off; works independently of FOV and lighting. Gameplay damage, blindness state, slowdown and other debuffs remain. |
| HUD: health status | Shows native medical HUD icons beside characters, including critical, dead and rotting states where supplied by the game. Works without medical glasses. Starts off; toggle in the F1 panel. |
| HUD: job icon | Shows the native profession icon beside characters with a known job icon. Works without HUD glasses. Starts off; toggle independently in the F1 panel. |
| Extended camera zoom (always enabled) | The game's Zoom in / Zoom out / Reset zoom bindings and panel buttons all adjust the same local camera scale. Custom keyboard/mouse bindings remain supported. Preset 1 restores scale one. No number pad is needed. |
| Reset all · Ctrl+R | Turns debug vision switches off and resets the scale to 1 while keeping extended zoom available. |

Zoom values describe the visible area: 0.5 is closer, 2 is further away. Presets range from 0.25 to 8; repeated buttons or native zoom commands allow 0.05–32. The finite positive bounds protect the renderer from singular or extreme projection matrices. The zoom step comes from the game's native command system (1 → 1.5 → 2.25 on SS220). Each command advances one discrete target; the camera approaches it smoothly at the native eye lerper's response rate. Prediction replays do not add extra local steps. Change the game's Zoom in / Zoom out bindings in its normal control settings to use any supported keys or mouse buttons.

Closing the panel keeps switches active. **Reset all** clears debug switches and resets the local view to scale 1; extended zoom remains enabled. Leaving gameplay restores the latest native camera values and disposes the panel. On a body/camera change, the old camera is restored and the current options follow the new main camera. Secondary/admin cameras retain their own zoom and FOV.

F1 and the exact Ctrl+N/L/H/B/R combinations are intercepted only during gameplay. Shortcuts also work with the panel closed; all actions remain available through mouse buttons. Focused text inputs, unmodified letters, Ctrl+F1 and combinations with Alt or Shift are passed through. A held shortcut toggles once and consumes its matching release even if Control is released first. The mod does not intercept or replace the game's physical zoom bindings. When Test Mod is also selected, Debug Vision takes priority on F1 during gameplay, so one press opens only this panel. Test Mod's 0 shortcut remains available.

The mod's overrides change local rendering only and do not request additional server visibility/PVS or permissions. Native zoom commands still execute their usual game behaviour. Areas/entities outside the data sent by the server remain unavailable. Visual protection suppresses only the named effect overlays; HUD, world objects and other overlays keep drawing. Unlisted effects and server-side mechanics are unaffected. Follow the chosen server's rules for client mods.

Visual protection can be enabled during an active flash or blindness effect. Disabling it resumes any still-active native visual effect. Reset and leaving gameplay turn it off. It is visual protection, not immunity to welding damage or a cure for blindness.

The two HUD switches use the game's existing status-icon renderer, health states and profession prototypes. Missing health/job data is not inferred. Native icon visibility settings, stealth/container rules and FOV still apply. Equipped HUDs continue working when a debug switch is off; closing the panel keeps both switches active, while Reset all and disconnect clear them.

## Integration and verification

The adapter uses Harmony on Robust's `Eye.DrawFov` getter, `Eye.Zoom`/`Scale` setters, `EyeManager.CurrentEye` setter, `LightManager.DrawLighting`/`DrawShadows` getters, input KeyDown/KeyUp and gameplay Startup/Shutdown. Prefix/postfix hooks on `SharedContentEyeSystem.ZoomIn`/`ZoomOut`/`ResetZoom` capture the current local target before the original session handler runs, then apply one native step afterwards only when `IGameTiming.IsFirstTimePredicted` is true. Replayed commands still run their original handler but cannot accumulate local render steps. A postfix on `EyeLerpingSystem.FrameUpdate(float)` interpolates the rendered scale toward the target using a frame-rate-independent 8/s response. This preserves the game's binding dispatch and original command behaviour, including when the original target is clamped. Other/null sessions cannot change the local override. The whole required contract is resolved before patching; a patch failure removes all hooks belonging to this mod. Native writes are preserved for restoration when the camera changes or gameplay ends. Settings are connection-scoped and are not saved to disk.

```powershell
dotnet run --project DebugVision.Tests/DebugVision.Tests.csproj -c Release
./scripts/Test-DebugVisionApi.ps1 -GameDirectory <Content.Client-output-directory>
dotnet run --project DebugVision.Smoke/DebugVision.Smoke.csproj -c Release -- <game-directory> <DebugVision.Mod.dll>
```

Visual protection prefixes `Draw(in OverlayDrawArgs)` on `FlashOverlay`, `BlindOverlay` and `BlurryVisionOverlay`, plus optional `DrunkOverlay`, `DrowsinessOverlay`, `RainbowOverlay` and `DamageOverlay` when present. Overlay updates, registration and BeforeDraw remain native; the byref-like draw args are never boxed. If blindness has already disabled the light manager, the blind draw prefix restores only its `LightSetup`/`GraceFrame` render bookkeeping and lighting, without changing `IsBlind` or `EyeDamage`. Missing optional overlays are logged and do not disable the core camera features.

The regression harness exercises the actual Harmony hooks with contract fixtures, including camera switches, native updates, reset, disconnect, hotkeys and localized button events. The smoke harness verifies installation against actual game DLLs. Live rendering, remote-server visibility and future forks still require an in-game check.

Health/job HUD prefixes wrap only `ShowHealthIconsSystem.OnGetStatusIconsEvent` and `JobStatusSystem.OnGetStatusIconsEvent`. Activation is temporarily enabled for native icon collection; the health container set is copied with the default `Biological` container included. Harmony finalizers restore the original activation and container object even if native collection throws. No components are added to players or equipment, and native equipment refresh/detach behaviour stays intact.
