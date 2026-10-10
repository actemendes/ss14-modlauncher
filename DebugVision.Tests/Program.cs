extern alias HelloWorld;
using System.Numerics;
using System.Reflection;
using HarmonyLib;
using SS14LocalMods;
using Content.Client.Gameplay;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Content.Client.Movement.Systems;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Graphics;
using Robust.Shared.IoC;
using Robust.Shared.Timing;

var checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
var eyeManager = new EyeManager(); var light = new LightManager(); var ui = new UserInterfaceManager();
var player = new PlayerManager(); var nativeZoom = new ContentEyeSystem();
var timing = new GameTiming();
var lerper = new Content.Client.Eye.EyeLerpingSystem();
void SettleZoom() { lerper.FrameUpdate(1); lerper.FrameUpdate(1); }
IoCManager.Services[typeof(IEyeManager)] = eyeManager;
IoCManager.Services[typeof(ILightManager)] = light;
IoCManager.Services[typeof(IUserInterfaceManager)] = ui;
IoCManager.Services[typeof(IPlayerManager)] = player;
IoCManager.Services[typeof(IGameTiming)] = timing;
// Missing game types must leave the engine completely unpatched.
try { Mod.Install(typeof(string).Assembly); throw new Exception("Unsupported client was accepted"); }
catch (TypeLoadException) { Check(Harmony.GetPatchInfo(AccessTools.PropertyGetter(typeof(Eye), "DrawFov")) == null, "Missing contract leaves no partial patches"); }
Mod.Install(Assembly.GetExecutingAssembly()); Mod.Install(Assembly.GetExecutingAssembly());
Check(Harmony.GetPatchInfo(AccessTools.PropertyGetter(typeof(Eye), "DrawFov"))!.Postfixes.Count == 1, "Install is idempotent");
var game = new GameplayState(); game.Startup();
var eye = eyeManager.CurrentEye; var other = new Eye();
Check(eye.DrawFov && light.DrawLighting && light.DrawShadows && Mod.ExpandedZoom && Mod.Zoom == 1, "Expanded zoom is enabled automatically; debug switches start off");
Mod.SetOption("omni", true);
Check(!eye.DrawFov && other.DrawFov, "Omnivision affects only the main camera");
eye.DrawFov = true;
Check(!eye.DrawFov, "Native FOV writes cannot cancel active override");
Mod.SetOption("omni", false);
Check(eye.DrawFov, "Disabling FOV override restores native value");
eye.DrawFov = false; Mod.SetOption("omni", true); Mod.SetOption("omni", false);
Check(!eye.DrawFov, "Existing observer FOV is preserved"); eye.DrawFov = true;
Mod.SetOption("light", true); Mod.SetOption("shadows", true);
Check(!light.DrawLighting && !light.DrawShadows && new LightManager().DrawLighting, "Lighting overrides target the resolved renderer");
light.DrawLighting = false; Mod.SetOption("light", false);
Check(!light.DrawLighting, "Native disabled lighting is preserved"); light.DrawLighting = true;

eye.Zoom = new(1.2f, 1.6f); Mod.SetZoom(8); SettleZoom();
Check(eye.Zoom == new Vector2(8) && eye.Scale == new Vector2(0.125f), "Zoom changes both projection and reported scale");
other.Zoom = new(2);
Check(other.Zoom == new Vector2(2), "Secondary cameras retain native zoom");
eye.Zoom = new(0.75f, 1.25f);
Check(eye.Zoom == new Vector2(8), "Eye lerping updates cannot overwrite local zoom");
game.Shutdown();
Check(eye.Zoom == new Vector2(0.75f, 1.25f), "Restores latest native zoom including aspect ratio");
game.Startup(); Mod.SetZoom(4); eye.Scale = new(2, 4); game.Shutdown();
Check(eye.Zoom == new Vector2(0.5f, 0.25f), "Native scale writes are tracked for restoration");
game.Startup(); Mod.SetZoom(8); SettleZoom();
other.Zoom = new(1.5f); eyeManager.CurrentEye = other;
Check(eye.Zoom == new Vector2(0.5f, 0.25f) && other.Zoom == new Vector2(8), "Camera switch restores old eye and applies to new eye");
game.Shutdown();
Check(other.Zoom == new Vector2(1.5f), "New eye's native zoom is restored");
game.Startup();
foreach (var invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0, -1 })
{
    var before = Mod.Zoom; Mod.SetZoom(invalid); Check(Mod.Zoom == before, "Invalid zoom rejected");
}
Mod.SetZoom(1000); Check(Mod.Zoom == Mod.MaximumZoom, "Large zoom has finite render bound");
Mod.SetZoom(0.001f); Check(Mod.Zoom == Mod.MinimumZoom, "Small zoom cannot produce singular matrix");

var input = new InputManager();
// The physical bindings are owned by the game and can be changed freely.
input.Bindings["F7"] = () => nativeZoom.RunCommand("out", player.LocalSession);
input.Bindings["F6"] = () => nativeZoom.RunCommand("in", player.LocalSession);
input.Bindings["F5"] = () => nativeZoom.RunCommand("reset", player.LocalSession);
void Tap(string key) { input.KeyDown(new() { Key = key }); input.KeyUp(new() { Key = key }); }
Environment.SetEnvironmentVariable("SS14_MOD_LANGUAGE", "ru"); Tap("F1");
Check(DefaultWindow.Last!.IsOpen && DefaultWindow.Last.Title.Contains("Дебаг"), "F1 creates native Russian window");
Check(input.Downs == 0 && input.Ups == 0, "Both halves of window hotkey consumed");
input.KeyDown(new() { Key = "F1", IsRepeat = true }); input.KeyUp(new() { Key = "F1" });
Check(DefaultWindow.Last.IsOpen, "F1 repeats do not close window");
var panel = DefaultWindow.Last.Contents.Children.Single();
var toggle = (Button)panel.Children[0]; toggle.Click();
Check(Mod.Omnivision && !other.DrawFov, "Actual button event toggles omnivision");
Tap("F1"); Check(!DefaultWindow.Last.IsOpen && Mod.Omnivision, "Closing window keeps settings active");
Mod.SetZoom(1);
var zoomButtons = panel.Children.OfType<BoxContainer>().First().Children.OfType<Button>().ToArray();
zoomButtons[1].Click(); Check(Mod.Zoom == 1.5f, "Mouse button zooms out without a number pad");
zoomButtons[0].Click(); Check(Mod.Zoom == 1, "Mouse button zooms in without a number pad");
var presets = panel.Children.OfType<BoxContainer>().Skip(1).First().Children.OfType<Button>().ToArray();
Mod.SetZoom(3); presets.Single(b => b.Text == "1").Click(); Check(Mod.Zoom == 1, "Mouse preset restores scale one");
var nativeDowns = input.Downs;
Tap("NumpadNum4"); Tap("NumpadNum5"); Tap("NumpadNum6"); Tap("F8");
Check(input.Downs == nativeDowns + 4 && Mod.Zoom == 1, "Unassigned keys keep native actions");
var calls = nativeZoom.NativeCalls;
Mod.SetZoom(8); Tap("F7"); Check(Mod.Zoom == 12 && nativeZoom.TargetZoom == 1, "Remapped native zoom-out extends past the native maximum from the panel's scale");
Tap("F6"); Check(Mod.Zoom == 8, "Remapped native zoom-in uses the same camera scale");
Tap("F5"); Check(Mod.Zoom == 1, "Remapped native reset returns to default scale");
Check(input.Downs == nativeDowns + 7 && nativeZoom.NativeCalls == calls + 3, "Original input and native command handlers all execute");
Mod.SetZoom(1); Tap("F7");
var originalCalls = nativeZoom.NativeCalls;
timing.IsFirstTimePredicted = false;
for (var replay = 0; replay < 20; replay++) nativeZoom.RunCommand("out", player.LocalSession);
timing.IsFirstTimePredicted = true;
Check(Mod.Zoom == 1.5f, "Twenty prediction replays of one press must still produce exactly one native zoom step");
Check(nativeZoom.NativeCalls == originalCalls + 20, "Prediction filtering retains every original native handler call");
Mod.Reset();
Tap("F7");
Check(other.Zoom == Vector2.One && Mod.Zoom == 1.5f, "One press selects one target step without instantly moving the camera");
lerper.FrameUpdate(1f / 60);
Check(other.Zoom.X > 1 && other.Zoom.X < 1.5f, "Native frame hook smoothly approaches the first step");
var displayed = other.Zoom;
timing.IsFirstTimePredicted = false;
for (var replay = 0; replay < 20; replay++) nativeZoom.RunCommand("out", player.LocalSession);
timing.IsFirstTimePredicted = true;
Check(Mod.Zoom == 1.5f && other.Zoom == displayed, "Prediction replays neither accumulate steps nor snap an animating camera");
Tap("F7"); Tap("F7"); Check(Mod.Zoom == 3.375f, "Separate real presses each advance exactly one native step");
Tap("F6"); Check(Mod.Zoom == 2.25f, "Zoom-in reverses one step even during an unfinished transition");
SettleZoom(); Check(other.Zoom == new Vector2(2.25f), "Camera finishes the selected discrete target");
Mod.Reset(); Mod.SetZoom(2);
for (var frame = 0; frame < 30; frame++) lerper.FrameUpdate(1f / 60);
var at60Fps = other.Zoom.X;
Mod.Reset(); Mod.SetZoom(2);
for (var frame = 0; frame < 15; frame++) lerper.FrameUpdate(1f / 30);
Check(MathF.Abs(at60Fps - other.Zoom.X) < 0.00001f, "Zoom response is independent of render frame rate");
displayed = other.Zoom;
foreach (var delta in new[] { 0, -1, float.NaN, float.PositiveInfinity }) lerper.FrameUpdate(delta);
Check(other.Zoom == displayed, "Invalid frame times cannot move or corrupt the camera");
Mod.Reset(); Check(other.Zoom == Vector2.One && Mod.Zoom == 1, "Reset cancels an unfinished zoom transition");
Mod.SetZoom(0.05f); Tap("F6"); Check(Mod.Zoom == 0.05f, "Native zoom-in respects only the renderer's positive bound");
Mod.SetZoom(32); Tap("F7"); Check(Mod.Zoom == 32, "Native zoom-out respects the renderer's finite bound");
Mod.SetZoom(4); nativeZoom.RunCommand("out", new Session()); nativeZoom.RunCommand("in", null);
Check(Mod.Zoom == 4, "Other and missing sessions never change the local view");
Mod.SetZoom(1);
var downs = input.Downs;
ui.KeyboardFocused = new LineEdit(); Tap("F1"); Tap("NumpadNum6");
Check(input.Downs == downs + 2 && Mod.Zoom == 1, "Text fields keep native input");
ui.KeyboardFocused = new TextEdit(); Tap("F1"); Check(input.Downs == downs + 3, "Multiline text fields keep native input");
ui.KeyboardFocused = null;
foreach (var key in new[] { new KeyEventArgs { Key = "F1", Alt = true }, new KeyEventArgs { Key = "F1", Control = true }, new KeyEventArgs { Key = "F1", Shift = true } })
{
    downs = input.Downs; input.KeyDown(key); input.KeyUp(key); Check(input.Downs == downs + 1, "Modified shortcuts pass through");
}
Mod.Reset();
Check(!Mod.Omnivision && !Mod.Fullbright && !Mod.NoShadows && Mod.ExpandedZoom && Mod.Zoom == 1, "Reset clears switches but leaves extended zoom available");
// Shortcuts work with the panel closed, toggle once and preserve matched releases.
void Combo(string key, bool repeat = false) => input.KeyDown(new() { Key = key, Control = true, IsRepeat = repeat });
downs = input.Downs; var ups = input.Ups;
Combo("N"); Check(Mod.Omnivision, "Ctrl+N enables omnivision without opening the panel");
Combo("N", true); Check(Mod.Omnivision, "Held Ctrl+N toggles only once");
input.KeyUp(new() { Key = "N" });
Check(input.Downs == downs && input.Ups == ups, "Shortcut consumes its release even after Control was released");
Combo("N"); input.KeyUp(new() { Key = "N" }); Check(!Mod.Omnivision, "Second Ctrl+N disables omnivision");
Combo("L"); input.KeyUp(new() { Key = "L" }); Check(Mod.Fullbright, "Ctrl+L toggles fullbright");
Combo("H"); input.KeyUp(new() { Key = "H" }); Check(Mod.NoShadows, "Ctrl+H toggles shadows");
Mod.SetZoom(8); Combo("R"); input.KeyUp(new() { Key = "R" });
Check(!Mod.Fullbright && !Mod.NoShadows && Mod.Zoom == 1 && Mod.ExpandedZoom, "Ctrl+R resets switches and zoom without disabling extended range");
foreach (var field in new object[] { new LineEdit(), new TextEdit() })
{
    ui.KeyboardFocused = field; downs = input.Downs; Combo("N"); input.KeyUp(new() { Key = "N" });
    Check(!Mod.Omnivision && input.Downs == downs + 1, "Ctrl combinations are left to focused text input");
}
ui.KeyboardFocused = null;
foreach (var shortcut in new[] { new KeyEventArgs { Key = "N" }, new KeyEventArgs { Key = "N", Control = true, Alt = true }, new KeyEventArgs { Key = "N", Control = true, Shift = true } })
{
    downs = input.Downs; input.KeyDown(shortcut); input.KeyUp(shortcut);
    Check(!Mod.Omnivision && input.Downs == downs + 1, "Only the exact Control combination is claimed");
}
other.Zoom = new Vector2(1.5f);
Mod.SetZoom(4); Mod.SetOption("omni", true); Mod.SetOption("light", true);
game.Shutdown();
Check(!Mod.Omnivision && !Mod.Fullbright && !Mod.ExpandedZoom && other.Zoom == new Vector2(1.5f) && DefaultWindow.Last.Disposed, "Disconnect resets overrides and disposes window");
downs = input.Downs; Tap("F1"); Check(input.Downs == downs + 1, "Menu leaves F1 alone");
Environment.SetEnvironmentVariable("SS14_MOD_LANGUAGE", "en"); game.Startup(); Tap("F1");
Check(DefaultWindow.Last!.Title.Contains("Debug Vision") && DefaultWindow.Last.IsOpen && !Mod.Omnivision, "Reconnect creates clean English panel");
// Install the real, independently released competing mod after Debug Vision.
// Harmony ordering must give only one panel ownership of each F1 press/release.
HelloWorld::SS14LocalMods.Mod.Install(Assembly.GetExecutingAssembly());
var visionWindow = DefaultWindow.Last;
Tap("F1");
Check(!visionWindow.IsOpen && ReferenceEquals(DefaultWindow.Last, visionWindow), "F1 closes only Debug Vision with Test Mod also selected");
Tap("F1");
Check(visionWindow.IsOpen && ReferenceEquals(DefaultWindow.Last, visionWindow), "F1 opens only Debug Vision with Test Mod also selected");
Check(input.Ups == input.Downs, "Competing mod does not leak unmatched key releases");
// Real byref-like overlay arguments exercise the Harmony wrappers without boxing.
Mod.Reset();
var blindOverlay = new Content.Client.Eye.Blinding.BlindOverlay();
Overlay[] effects = [new Content.Client.Flash.FlashOverlay(), blindOverlay,
    new Content.Client.Eye.Blinding.BlurryVisionOverlay(), new Content.Client.Drunk.DrunkOverlay(),
    new Content.Client.Drowsiness.DrowsinessOverlay(), new Content.Client.Drugs.RainbowOverlay(),
    new Content.Client.UserInterface.Systems.DamageOverlays.Overlays.DamageOverlay()];
foreach (var effect in effects) { effect.Render(); Check(effect.Draws == 1, "Protection starts off: native effect draws " + effect.GetType().Name); }
Check(!light.Enabled && blindOverlay.Blindable.LightSetup, "Native blindness has already disabled lighting before protection is enabled");
Combo("B"); Combo("B", true); input.KeyUp(new() { Key = "B" });
Check(Mod.NoEffects && !Mod.Omnivision && !Mod.Fullbright && !Mod.NoShadows, "Ctrl+B toggles only visual protection and held repeats do not retoggle it");
foreach (var effect in effects)
{
    effect.FrameUpdate(); effect.Render();
    Check(effect.Draws == 1 && effect.Frames == 1, "Protection skips drawing but retains native updates for " + effect.GetType().Name);
}
Check(light.Enabled && !blindOverlay.Blindable.LightSetup && !blindOverlay.Blindable.GraceFrame,
    "Enabling protection during blindness restores the lighting disabled by its earlier draw");
Check(blindOverlay.Blindable.IsBlind && blindOverlay.Blindable.EyeDamage == 9,
    "Visual protection does not remove blindness or welding eye damage");
var unrelated = new UnrelatedOverlay(); unrelated.Render();
Check(unrelated.Draws == 1, "HUD/world overlays outside the explicit effect list keep drawing");
ui.KeyboardFocused = new LineEdit(); Combo("B"); input.KeyUp(new() { Key = "B" });
Check(Mod.NoEffects, "Focused text fields keep Ctrl+B without toggling protection"); ui.KeyboardFocused = null;
Combo("B"); input.KeyUp(new() { Key = "B" });
foreach (var effect in effects) { effect.Render(); Check(effect.Draws == 2, "Disabling protection resumes current native effect " + effect.GetType().Name); }
Check(!light.Enabled && blindOverlay.Blindable.LightSetup, "Blindness reinstates its native lighting state when protection is disabled");
Mod.SetOption("effects", true); blindOverlay.Blindable.IsBlind = false; blindOverlay.Render();
Check(light.Enabled && !blindOverlay.Blindable.GraceFrame && blindOverlay.Draws == 2,
    "Native recovery from blindness runs while protection is on without a stale grace frame");
Tap("F1");
var protectionButton = DefaultWindow.Last!.Contents.Children.Single().Children.OfType<Button>()
    .Single(button => button.Text.Contains("Ctrl+B"));
Check(protectionButton.Pressed && protectionButton.Text.Contains("Visual effect"), "English panel mirrors the protection hotkey state");
protectionButton.Click(); Check(!Mod.NoEffects, "Native panel button switches visual protection off");
protectionButton.Click(); Check(Mod.NoEffects, "Native panel button switches visual protection on");
Combo("R"); input.KeyUp(new() { Key = "R" }); Check(!Mod.NoEffects && !protectionButton.Pressed, "Reset clears visual protection and its panel state");
Mod.SetOption("effects", true);
game.Shutdown();
Check(!Mod.NoEffects, "Disconnect clears visual protection");
effects[0].Render(); Check(effects[0].Draws == 3, "Menu restores normal effect rendering after disconnect");
game.Startup(); Tap("F1");
var healthHud = new Content.Client.Overlays.ShowHealthIconsSystem();
var jobHud = new Content.Client.Overlays.ShowJobIconsSystem();
var jobs = new Content.Client.Access.Systems.JobStatusSystem(jobHud);
var originalContainers = healthHud.DamageContainers;
Check(!Mod.HealthHud && !Mod.JobHud && healthHud.Collect("Biological").Count == 0 && jobs.Collect("Doctor").Count == 0,
    "HUD overrides start off without equipment");
var hudButtons = DefaultWindow.Last!.Contents.Children.Single().Children.OfType<Button>().Where(b => b.Text.StartsWith("HUD:")).ToArray();
Check(hudButtons.Length == 2 && !hudButtons[0].Pressed && !hudButtons[1].Pressed, "Independent HUD buttons are initially off");
hudButtons[0].Click();
foreach (var state in new[] { "Healthy", "Critical", "Dead", "Rotting" })
    Check(healthHud.Collect("Biological", state).SequenceEqual([state]), "Native health status is retained: " + state);
Check(!Mod.JobHud && jobs.Collect("Doctor").Count == 0, "Health HUD does not enable job HUD");
Check(!healthHud.IsActive && ReferenceEquals(originalContainers, healthHud.DamageContainers) && originalContainers.Count == 0,
    "Collection restores native activation and original container set");
Check(healthHud.Collect(null).Count == 0 && healthHud.Collect("Unknown").Count == 0, "Missing/unsupported health data is not invented");
hudButtons[1].Click();
Check(jobs.Collect("Doctor").SequenceEqual(["Doctor"]) && !jobHud.IsActive, "Native job icon is collected without changing equipment state");
Check(jobs.Collect(null).Count == 0, "Missing job icon is not invented");
jobs.CrewHud = true;
Check(jobs.Collect("Doctor").SequenceEqual(["Doctor", "Crew border"]), "Other native HUD icons remain intact and job icon is not duplicated");
foreach (var collect in new Action[] { () => healthHud.Collect("Biological", fail: true), () => jobs.Collect("Doctor", fail: true) })
{
    try { collect(); throw new Exception("Expected native failure"); } catch (InvalidOperationException) { }
    Check(!healthHud.IsActive && !jobHud.IsActive && ReferenceEquals(originalContainers, healthHud.DamageContainers), "Finalizer restores HUD state after native exception");
}
healthHud.SetEquipment(true); originalContainers.Add("Machine"); jobHud.SetEquipment(true);
Check(healthHud.Collect("Machine").Count == 1 && healthHud.Collect("Biological").Count == 1 && healthHud.IsActive,
    "Equipped damage containers are preserved alongside debug medical HUD");
healthHud.SetEquipment(false); jobHud.SetEquipment(false);
Check(healthHud.Collect("Biological").Count == 1 && !healthHud.IsActive, "Equipment removal while enabled does not cancel debug HUD");
Tap("F1"); Check(Mod.HealthHud && Mod.JobHud, "Closing panel retains both HUD options");
Mod.Reset();
Check(!Mod.HealthHud && !Mod.JobHud && !hudButtons[0].Pressed && !hudButtons[1].Pressed && healthHud.Collect("Biological").Count == 0,
    "Reset clears HUD options and panel buttons");
healthHud.SetEquipment(true); jobHud.SetEquipment(true);
Check(healthHud.Collect("Machine").Count == 1 && jobs.Collect("Doctor").Count == 2, "Reset leaves equipped HUDs working");
Mod.SetOption("health", true); Mod.SetOption("job", true); game.Shutdown();
Check(!Mod.HealthHud && !Mod.JobHud && healthHud.IsActive && jobHud.IsActive && ReferenceEquals(originalContainers, healthHud.DamageContainers),
    "Disconnect clears overrides and preserves native equipment state");
game.Startup(); Check(!Mod.HealthHud && !Mod.JobHud, "Reconnect starts with HUD overrides off"); game.Shutdown();
Console.WriteLine($"Debug Vision: {checks} lifecycle, rendering, UI and Harmony checks passed.");
