using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;
using SS14ModLauncher.Core;

var tests = new List<(string Name, Action Run)>
{
    ("install, selection, exact restore, unknown mod retained", RoundTrip),
    ("repeat install patches original only once", RepeatInstall),
    ("restore then update and reinstall retains verified ownership", Reinstall),
    ("invalid payload, traversal and selection fail before any write", InvalidInputs),
    ("foreign mod filename collision is never overwritten", ForeignPayload),
    ("modified payload cannot be blessed by selection", ModifiedPayload),
    ("damaged backup prevents every restore write", DamagedBackup),
    ("unknown loader update preserves both Steam and loader state", ChangedLoader),
    ("Steam roundtrip, disable and wrapper upgrade", SteamRoundTrip),
    ("Steam repair to recorded original is recognized", SteamRepair),
    ("unknown Steam update never restores stale executable", ChangedSteam),
    ("legacy installation upgrades and restores", Legacy),
    ("locked loader rolls back staged payload and state", LockedLoaderRollback),
    ("interrupted transaction recovers before restore", InterruptedTransaction),
    ("damaged recovery snapshot stops without writes", DamagedJournal),
    ("malicious recovery paths never escape game root", MaliciousJournal),
    ("unsupported loader is rejected without backup", Unsupported),
    ("explicit adoption archives previous originals after Steam verification", RebaseAfterUpdate),
    ("adoption refuses a still-patched loader or active wrapper", UnsafeRebase),
    ("verified updated mods survive restore and tampered bytes are never adopted", VerifiedInstalledMods),
    ("Steam integration refuses an unrelated executable", UnrelatedLauncher),
    ("Steam detection requires matching library layout and app manifest", SteamDetection),
    ("default Steam installation patches and enables the launcher together", SteamDefaultInstallation),
    ("invalid default bridge fails before any installation writes", SteamDefaultPreflightFailure),
    ("locked Steam executable rolls back the entire default installation", SteamDefaultAtomicFailure),
    ("explicit Steam disable survives installs, selections, updates and restore", SteamDisablePreference),
    ("restore preserves default Steam enable for explicit reinstall", SteamRestoreReinstallDefault),
    ("mods-only legacy install adopts default only through explicit install", SteamLegacyDefault),
    ("standalone default installation never replaces its executable", StandaloneDefault),
    ("invalid Steam preference fails without adopting a default", InvalidSteamPreference),
};
var realLoaderIndex = Array.IndexOf(args, "--real-loader");
if (realLoaderIndex >= 0 && realLoaderIndex + 1 < args.Length)
    tests.Add(("read-only real SS14 loader copy install/restore", () => RealLoaderRoundTrip(args[realLoaderIndex + 1])));
var publishedIndex = Array.IndexOf(args, "--published-launcher");
if (publishedIndex >= 0 && publishedIndex + 1 < args.Length)
    tests.Add(("published executable Steam handoff in isolated fixture", () => PublishedHandoff(args[publishedIndex + 1])));
var selfContainedIndex = Array.IndexOf(args, "--self-contained-launcher");
if (selfContainedIndex >= 0 && selfContainedIndex + 1 < args.Length)
    tests.Add(("self-contained CLI standalone/Steam defaults, restore and explicit opt-out", () => SelfContainedCli(args[selfContainedIndex + 1])));
var failures = 0;
foreach (var (name, run) in tests)
{
    try { run(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + name + "\n" + ex); }
}
var installationFailures = failures;
try { await CatalogUpdateTests.RunAsync(); }
catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL catalog/update tests\n" + ex); }
try { await NotificationTests.RunAsync(); }
catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL automatic update notification tests\n" + ex); }
try { SetupTests.Run(); }
catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL first-run setup tests\n" + ex); }
try { BundledPayloadTests.Run(); }
catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL bundled payload tests\n" + ex); }
var liveRepositoryIndex = Array.IndexOf(args, "--live-repository");
if (liveRepositoryIndex >= 0 && liveRepositoryIndex + 1 < args.Length)
{
    var releaseAssetsIndex = Array.IndexOf(args, "--release-assets");
    var releaseAssets = releaseAssetsIndex >= 0 && releaseAssetsIndex + 1 < args.Length ? args[releaseAssetsIndex + 1] : null;
    try { await LiveUpdateTests.RunAsync(args[liveRepositoryIndex + 1], releaseAssets); }
    catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL live public release update test\n" + ex); }
}
Console.WriteLine($"Installation tests: {tests.Count - installationFailures}/{tests.Count} passed; failures including catalog: {failures}");
return failures == 0 ? 0 : 1;

static void RoundTrip()
{
    using var f = new Fixture();
    Equal("clean", Installation.Inspect(f.Root).State);
    f.Install();
    Equal("installed", Installation.Inspect(f.Root).State);
    Bytes(f.OriginalLoader, File.ReadAllBytes(f.Path("loader/SS14LocalMods/SS14.Loader.original.dll")));
    var selection = Installation.ReadSelection(f.Root)!;
    Equal("ru", selection.Language);
    Equal("Test.Mod.dll", selection.EnabledMods.Single().File);
    Equal(Hash(f.Payload["Test.Mod.dll"]), selection.EnabledMods.Single().Sha256);
    f.Write("loader/SS14LocalMods/Mods/Other.Mod.dll", [9, 8, 7]);
    Installation.SetSelection(f.Root, [], "en");
    Equal(0, Installation.ReadSelection(f.Root)!.EnabledMods.Count);
    Installation.Restore(f.Root);
    Equal("clean", Installation.Inspect(f.Root).State);
    Bytes(f.OriginalLoader, File.ReadAllBytes(f.Path("loader/SS14.Loader.dll")));
    True(File.Exists(f.Path("loader/SS14LocalMods/Mods/Other.Mod.dll")));
    True(!File.Exists(f.Path("loader/SS14LocalMods/installation.json")));
    True(!File.Exists(f.Path("loader/SS14LocalMods/SS14.Loader.original.dll")));
    Installation.Restore(f.Root); // Idempotent.
}

static void RepeatInstall()
{
    using var f = new Fixture(); f.Install(); f.Install();
    using var assembly = AssemblyDefinition.ReadAssembly(f.Path("loader/SS14.Loader.dll"));
    var run = assembly.MainModule.GetType("SS14.Loader.Program").Methods.Single(m => m.Name == "Run");
    Equal(1, run.Body.Instructions.Count(i => i.Operand is string value && value == "SS14LocalMods/SS14LocalMods.Bootstrap.dll"));
    Bytes(f.OriginalLoader, File.ReadAllBytes(f.Path("loader/SS14LocalMods/SS14.Loader.original.dll")));
}

static void Reinstall()
{
    using var f = new Fixture(); f.Install(); Installation.Restore(f.Root);
    f.Payload["SS14LocalMods.Bootstrap.dll"] = [50, 51, 52];
    f.Payload["Test.Mod.dll"] = [90, 91, 92];
    f.Install();
    Equal("installed", Installation.Inspect(f.Root).State);
    Bytes(f.Payload["Test.Mod.dll"], File.ReadAllBytes(f.Path("loader/SS14LocalMods/Mods/Test.Mod.dll")));
}

static void InvalidInputs()
{
    using var f = new Fixture();
    f.Payload["../outside.Mod.dll"] = [1];
    Code("payload-invalid", () => f.Install());
    f.Payload.Remove("../outside.Mod.dll");
    Code("selection-invalid", () => Installation.Install(f.Root, f.Payload, ["../Test.Mod.dll"], "ru"));
    Code("language-invalid", () => Installation.Install(f.Root, f.Payload, [], "xx"));
    Code("selection-invalid", () => Installation.Install(f.Root, f.Payload, ["Test.Mod.dll", "Test.Mod.dll"], "en"));
    Code("mod-missing", () => Installation.Install(f.Root, f.Payload, ["Missing.Mod.dll"], "en"));
    True(!Directory.Exists(f.Path("loader/SS14LocalMods")));
    Bytes(f.OriginalLoader, File.ReadAllBytes(f.Path("loader/SS14.Loader.dll")));
}

static void ForeignPayload()
{
    using var f = new Fixture();
    f.Write("loader/SS14LocalMods/Mods/Test.Mod.dll", [100]);
    Code("payload-conflict", () => f.Install());
    Bytes([100], File.ReadAllBytes(f.Path("loader/SS14LocalMods/Mods/Test.Mod.dll")));
    True(!File.Exists(f.Path("loader/SS14LocalMods/installation.json")));
}

static void ModifiedPayload()
{
    using var f = new Fixture(); f.Install();
    f.Write("loader/SS14LocalMods/Mods/Test.Mod.dll", [100]);
    var before = File.ReadAllBytes(f.Path("loader/SS14LocalMods/selection.json"));
    Code("payload-conflict", () => Installation.SetSelection(f.Root, ["Test.Mod.dll"], "en"));
    Bytes(before, File.ReadAllBytes(f.Path("loader/SS14LocalMods/selection.json")));
    Equal("recovery", Installation.Inspect(f.Root).State);
    Installation.Restore(f.Root); // Modified inactive mods do not prevent original loader restore.
    Bytes([100], File.ReadAllBytes(f.Path("loader/SS14LocalMods/Mods/Test.Mod.dll")));
}

static void DamagedBackup()
{
    using var f = new Fixture(); f.Install(); f.EnableSteam();
    f.Write("loader/SS14LocalMods/SS14.Loader.original.dll", [0]);
    var before = File.ReadAllBytes(f.Path("loader/SS14.Loader.dll"));
    Code("backup-invalid", () => Installation.Restore(f.Root));
    Bytes(before, File.ReadAllBytes(f.Path("loader/SS14.Loader.dll")));
    Bytes(f.Wrapper, File.ReadAllBytes(f.Path("SS14.Launcher.exe")));
}

static void ChangedLoader()
{
    using var f = new Fixture(); f.Install(); f.EnableSteam();
    f.Write("loader/SS14.Loader.dll", [99, 98]);
    Equal("changed", Installation.Inspect(f.Root).State);
    Code("loader-changed", () => Installation.Restore(f.Root));
    Code("loader-changed", () => f.Install());
    Bytes([99, 98], File.ReadAllBytes(f.Path("loader/SS14.Loader.dll")));
    Bytes(f.Wrapper, File.ReadAllBytes(f.Path("SS14.Launcher.exe")));
    True(File.Exists(f.Path("SS14.Launcher.clean.exe")));
}

static void SteamRoundTrip()
{
    using var f = new Fixture(); f.Install(); f.EnableSteam();
    True(Installation.IsSteamEnabled(f.Root));
    Equal(f.Path("SS14.Launcher.clean.exe"), Installation.GetLaunchExecutable(f.Root));
    Bytes(f.OriginalLauncher, File.ReadAllBytes(f.Path("SS14.Launcher.clean.exe")));
    Bytes(f.Wrapper, File.ReadAllBytes(f.Path("SS14ModLauncher/SS14ModLauncher.exe")));
    Installation.DisableSteam(f.Root);
    True(!Installation.IsSteamEnabled(f.Root));
    Equal(f.Path("SS14.Launcher.exe"), Installation.GetLaunchExecutable(f.Root));
    Bytes(f.OriginalLauncher, File.ReadAllBytes(f.Path("SS14.Launcher.exe")));
    f.Wrapper[10] = 77;
    f.EnableSteam(); // Old stable executable ownership survives disable.
    Bytes(f.Wrapper, File.ReadAllBytes(f.Path("SS14ModLauncher/SS14ModLauncher.exe")));
    Installation.Restore(f.Root);
    Equal("clean", Installation.Inspect(f.Root).State);
    Bytes(f.OriginalLauncher, File.ReadAllBytes(f.Path("SS14.Launcher.exe")));
    Bytes(f.OriginalLoader, File.ReadAllBytes(f.Path("loader/SS14.Loader.dll")));
}

static void SteamRepair()
{
    using var f = new Fixture(); f.Install(); f.EnableSteam();
    f.Write("SS14.Launcher.exe", f.OriginalLauncher);
    f.Write("loader/SS14.Loader.dll", f.OriginalLoader);
    Equal("clean", Installation.Inspect(f.Root).State);
    True(!Installation.IsSteamEnabled(f.Root));
    f.Install(); f.EnableSteam();
    True(Installation.IsSteamEnabled(f.Root));
    f.Write("SS14.Launcher.exe", f.OriginalLauncher);
    f.Write("loader/SS14.Loader.dll", f.OriginalLoader);
    Installation.Restore(f.Root);
    Bytes(f.OriginalLauncher, File.ReadAllBytes(f.Path("SS14.Launcher.exe")));
    Bytes(f.OriginalLoader, File.ReadAllBytes(f.Path("loader/SS14.Loader.dll")));
}

static void ChangedSteam()
{
    using var f = new Fixture(); f.Install(); f.EnableSteam();
    var patched = File.ReadAllBytes(f.Path("loader/SS14.Loader.dll"));
    f.Write("SS14.Launcher.exe", [8, 9, 10]);
    Equal("changed", Installation.Inspect(f.Root).State);
    Code("steam-changed", () => Installation.Restore(f.Root));
    Code("steam-changed", () => Installation.DisableSteam(f.Root));
    Code("steam-changed", () => f.EnableSteam());
    Bytes(patched, File.ReadAllBytes(f.Path("loader/SS14.Loader.dll")));
    Bytes([8, 9, 10], File.ReadAllBytes(f.Path("SS14.Launcher.exe")));
}

static void Legacy()
{
    using var f = new Fixture(); f.Install();
    var record = new { OriginalHash = Hash(f.OriginalLoader), PatchedHash = Hash(File.ReadAllBytes(f.Path("loader/SS14.Loader.dll"))) };
    f.Write("loader/SS14LocalMods/installation.json", JsonSerializer.SerializeToUtf8Bytes(record));
    File.Delete(f.Path("loader/SS14LocalMods/ownership.json"));
    Equal("legacy-installation", Installation.Inspect(f.Root).Detail);
    Code("upgrade-required", () => Installation.SetSelection(f.Root, [], "en"));
    f.Install();
    Equal("installed", Installation.Inspect(f.Root).State);
    Installation.Restore(f.Root);
    Bytes(f.OriginalLoader, File.ReadAllBytes(f.Path("loader/SS14.Loader.dll")));
}

static void LockedLoaderRollback()
{
    if (!OperatingSystem.IsWindows()) return; // FileShare delete semantics are a Windows integration guarantee.
    using var f = new Fixture();
    using (var held = new FileStream(f.Path("loader/SS14.Loader.dll"), FileMode.Open, FileAccess.Read, FileShare.Read))
    {
        var error = Throws<Exception>(() => f.Install());
        True(error is IOException or UnauthorizedAccessException);
        Bytes(f.OriginalLoader, File.ReadAllBytes(f.Path("loader/SS14.Loader.dll")));
        True(!File.Exists(f.Path("loader/SS14LocalMods/installation.json")));
        True(!File.Exists(f.Path("loader/SS14LocalMods/SS14.Loader.original.dll")));
        True(!File.Exists(f.Path("loader/SS14LocalMods/Mods/Test.Mod.dll")));
        True(!Directory.Exists(f.Path(".ss14-modlauncher-transaction")));
    }
    f.Install();
    Equal("installed", Installation.Inspect(f.Root).State);
}

static void InterruptedTransaction()
{
    using var f = new Fixture();
    var after = new byte[] { 10, 11, 12 };
    f.Write("loader/SS14.Loader.dll", after);
    f.Journal("loader/SS14.Loader.dll", f.OriginalLoader, after);
    Equal("recovery", Installation.Inspect(f.Root).State);
    Installation.Restore(f.Root);
    Bytes(f.OriginalLoader, File.ReadAllBytes(f.Path("loader/SS14.Loader.dll")));
    True(!Directory.Exists(f.Path(".ss14-modlauncher-transaction")));
}

static void DamagedJournal()
{
    using var f = new Fixture();
    var after = new byte[] { 10, 11, 12 };
    f.Write("loader/SS14.Loader.dll", after);
    f.Journal("loader/SS14.Loader.dll", f.OriginalLoader, after);
    f.Write(".ss14-modlauncher-transaction/0.bak", [0]);
    Code("backup-invalid", () => Installation.Restore(f.Root));
    Bytes(after, File.ReadAllBytes(f.Path("loader/SS14.Loader.dll")));
}

static void MaliciousJournal()
{
    using var f = new Fixture();
    f.Journal("../outside.dll", f.OriginalLoader, [1]);
    Code("path-invalid", () => Installation.Restore(f.Root));
    Bytes(f.OriginalLoader, File.ReadAllBytes(f.Path("loader/SS14.Loader.dll")));
}

static void Unsupported()
{
    using var f = new Fixture(); f.Write("loader/SS14.Loader.dll", [0, 1, 2]);
    Code("loader-unsupported", () => f.Install());
    True(!File.Exists(f.Path("loader/SS14LocalMods/SS14.Loader.original.dll")));
}

static void RebaseAfterUpdate()
{
    using var f = new Fixture(); f.Install(); f.EnableSteam();
    using var updated = new Fixture();
    var newLauncher = f.OriginalLauncher.ToArray(); newLauncher[60] = 71;
    f.Write("loader/SS14.Loader.dll", updated.OriginalLoader);
    f.Write("SS14.Launcher.exe", newLauncher);
    var archive = Installation.RebaseAfterPlatformUpdate(f.Root);
    True(Directory.Exists(archive));
    Bytes(f.OriginalLoader, File.ReadAllBytes(System.IO.Path.Combine(archive, "SS14.Loader.original.dll")));
    Bytes(f.OriginalLauncher, File.ReadAllBytes(System.IO.Path.Combine(archive, "SS14.Launcher.clean.exe")));
    True(File.Exists(System.IO.Path.Combine(archive, "adoption.json")));
    Bytes(updated.OriginalLoader, File.ReadAllBytes(f.Path("loader/SS14.Loader.dll")));
    Bytes(newLauncher, File.ReadAllBytes(f.Path("SS14.Launcher.exe")));
    Equal("clean", Installation.Inspect(f.Root).State);
    f.Install(); f.EnableSteam(); Installation.Restore(f.Root);
    Bytes(updated.OriginalLoader, File.ReadAllBytes(f.Path("loader/SS14.Loader.dll")));
    Bytes(newLauncher, File.ReadAllBytes(f.Path("SS14.Launcher.exe")));
}

static void UnsafeRebase()
{
    using var f = new Fixture(); f.Install(); f.EnableSteam();
    Code("verify-game-first", () => Installation.RebaseAfterPlatformUpdate(f.Root));
    f.Write("loader/SS14.Loader.dll", f.OriginalLoader);
    Code("verify-game-first", () => Installation.RebaseAfterPlatformUpdate(f.Root));
    True(File.Exists(f.Path("loader/SS14LocalMods/installation.json")));
    Bytes(f.Wrapper, File.ReadAllBytes(f.Path("SS14.Launcher.exe")));
}

static void VerifiedInstalledMods()
{
    using var f = new Fixture(); f.Install();
    f.Payload["Test.Mod.dll"] = [54, 55, 56];
    f.Install();
    Bytes([54, 55, 56], Installation.ReadVerifiedInstalledMods(f.Root)["Test.Mod.dll"]);
    Installation.Restore(f.Root);
    Bytes([54, 55, 56], Installation.ReadVerifiedInstalledMods(f.Root)["Test.Mod.dll"]);
    f.Write("loader/SS14LocalMods/Mods/Untracked.Mod.dll", [1]);
    True(!Installation.ReadVerifiedInstalledMods(f.Root).ContainsKey("Untracked.Mod.dll"));
    f.Write("loader/SS14LocalMods/Mods/Test.Mod.dll", [123]);
    Code("payload-conflict", () => Installation.ReadVerifiedInstalledMods(f.Root));
    f.Payload["Test.Mod.dll"] = [123]; // A caller cannot bless altered bytes by passing them back.
    Code("payload-conflict", () => f.Install());
}

static void RealLoaderRoundTrip(string path)
{
    // Source is read exactly once. Every installation write is under Fixture.Root.
    var original = File.ReadAllBytes(path);
    using var f = new Fixture();
    f.Write("loader/SS14.Loader.dll", original);
    f.Install();
    Equal("installed", Installation.Inspect(f.Root).State);
    using (var assembly = AssemblyDefinition.ReadAssembly(f.Path("loader/SS14.Loader.dll")))
    {
        var run = assembly.MainModule.GetType("SS14.Loader.Program").Methods.Single(m => m.Name == "Run" && m.Parameters.Count == 0);
        Equal(1, run.Body.Instructions.Count(i => i.Operand is string text && text == "SS14LocalMods/SS14LocalMods.Bootstrap.dll"));
    }
    Installation.Restore(f.Root);
    Bytes(original, File.ReadAllBytes(f.Path("loader/SS14.Loader.dll")));
}

static void UnrelatedLauncher()
{
    using var f = new Fixture(); f.Install();
    f.Write("SS14.Launcher.exe", Encoding.ASCII.GetBytes("MZ" + new string('X', 126)));
    Code("steam-unsupported", () => f.EnableSteam());
    True(!File.Exists(f.Path("SS14.Launcher.clean.exe")));
    True(!File.Exists(f.Path("SS14ModLauncher/steam.json")));
}

static void SteamDetection()
{
    using var f = new Fixture(steam: true);
    True(Installation.IsSteamInstallation(f.Root));
    True(Installation.ShouldEnableSteamByDefault(f.Root));
    var originalManifest = File.ReadAllText(f.ManifestPath!);
    File.WriteAllText(f.ManifestPath!, originalManifest.Replace("\"1482520\"", "\"12345\""));
    True(!Installation.IsSteamInstallation(f.Root));
    File.WriteAllText(f.ManifestPath!, originalManifest.Replace("\"installdir\"\t\t\"Space Station 14 Playtest\"", "\"installdir\"\t\t\"Other game\""));
    True(!Installation.IsSteamInstallation(f.Root));
    File.WriteAllText(f.ManifestPath!, originalManifest.Replace("\"installdir\"\t\t\"Space Station 14 Playtest\"", "\"installdir\"\t\t\"../Space Station 14 Playtest\""));
    True(!Installation.IsSteamInstallation(f.Root));
    File.WriteAllText(f.ManifestPath!, originalManifest.Replace("\"StateFlags\"", "\"installdir\""));
    True(!Installation.IsSteamInstallation(f.Root));
    File.Delete(f.ManifestPath!);
    True(!Installation.IsSteamInstallation(f.Root));
    using var standalone = new Fixture();
    standalone.Write("appmanifest_1482520.acf", Encoding.UTF8.GetBytes(originalManifest));
    True(!Installation.IsSteamInstallation(standalone.Root));
}

static void SteamDefaultInstallation()
{
    using var f = new Fixture(steam: true);
    f.InstallDefaults();
    Equal("installed", Installation.Inspect(f.Root).State);
    True(Installation.IsSteamEnabled(f.Root));
    True(Installation.ShouldEnableSteamByDefault(f.Root));
    Bytes(f.Wrapper, File.ReadAllBytes(f.Path("SS14.Launcher.exe")));
    Bytes(f.OriginalLauncher, File.ReadAllBytes(f.Path("SS14.Launcher.clean.exe")));
    True(File.Exists(f.Path("SS14ModLauncher/preferences.json")));
}

static void SteamDefaultPreflightFailure()
{
    using var f = new Fixture(steam: true);
    f.Write("bad-wrapper.exe", [1, 2, 3]);
    Code("steam-unsupported", () => Installation.InstallWithDefaults(f.Root, f.Payload, ["Test.Mod.dll"], "en", f.Path("bad-wrapper.exe")));
    Bytes(f.OriginalLoader, File.ReadAllBytes(f.Path("loader/SS14.Loader.dll")));
    Bytes(f.OriginalLauncher, File.ReadAllBytes(f.Path("SS14.Launcher.exe")));
    True(!Directory.Exists(f.Path("loader/SS14LocalMods")));
    True(!Directory.Exists(f.Path("SS14ModLauncher")));
}

static void SteamDefaultAtomicFailure()
{
    if (!OperatingSystem.IsWindows()) return;
    using var f = new Fixture(steam: true);
    using (var held = new FileStream(f.Path("SS14.Launcher.exe"), FileMode.Open, FileAccess.Read, FileShare.Read))
    {
        var error = Throws<Exception>(() => f.InstallDefaults());
        True(error is IOException or UnauthorizedAccessException);
        Bytes(f.OriginalLoader, File.ReadAllBytes(f.Path("loader/SS14.Loader.dll")));
        Bytes(f.OriginalLauncher, File.ReadAllBytes(f.Path("SS14.Launcher.exe")));
        True(!File.Exists(f.Path("loader/SS14LocalMods/installation.json")));
        True(!File.Exists(f.Path("loader/SS14LocalMods/SS14.Loader.original.dll")));
        True(!File.Exists(f.Path("SS14.Launcher.clean.exe")));
        True(!File.Exists(f.Path("SS14ModLauncher/SS14ModLauncher.exe")));
        True(!File.Exists(f.Path("SS14ModLauncher/preferences.json")));
        True(!Directory.Exists(f.Path(".ss14-modlauncher-transaction")));
    }
    f.InstallDefaults();
    True(Installation.IsSteamEnabled(f.Root));
}

static void SteamDisablePreference()
{
    using var f = new Fixture(steam: true);
    // An explicit disable also works before the first installation.
    Installation.DisableSteam(f.Root);
    True(!Installation.ShouldEnableSteamByDefault(f.Root));
    Installation.InstallWithDefaults(f.Root, f.Payload, ["Test.Mod.dll"], "en", f.Path("does-not-exist.exe"));
    True(!Installation.IsSteamEnabled(f.Root));
    f.EnableSteam();
    True(Installation.ShouldEnableSteamByDefault(f.Root));
    Installation.DisableSteam(f.Root);
    f.Payload["Test.Mod.dll"] = [91, 92, 93];
    f.InstallDefaults();
    f.Install();
    Installation.SetSelection(f.Root, [], "en");
    True(!Installation.IsSteamEnabled(f.Root));
    True(!Installation.ShouldEnableSteamByDefault(f.Root));
    Installation.Restore(f.Root);
    f.InstallDefaults();
    True(!Installation.IsSteamEnabled(f.Root));
    Bytes(f.OriginalLauncher, File.ReadAllBytes(f.Path("SS14.Launcher.exe")));
    f.EnableSteam();
    True(Installation.IsSteamEnabled(f.Root));
    True(Installation.ShouldEnableSteamByDefault(f.Root));
}

static void SteamRestoreReinstallDefault()
{
    using var f = new Fixture(steam: true);
    f.InstallDefaults();
    Installation.Restore(f.Root);
    True(!Installation.IsSteamEnabled(f.Root));
    True(Installation.ShouldEnableSteamByDefault(f.Root));
    f.InstallDefaults();
    True(Installation.IsSteamEnabled(f.Root));
    Installation.Restore(f.Root);
    File.Delete(f.Path("SS14ModLauncher/preferences.json")); // Pre-0.1.2 restore footprint.
    True(Installation.ShouldEnableSteamByDefault(f.Root));
    f.InstallDefaults();
    True(Installation.IsSteamEnabled(f.Root));
}

static void SteamLegacyDefault()
{
    using var f = new Fixture(steam: true);
    f.Install();
    Installation.SetSelection(f.Root, [], "en");
    True(!Installation.IsSteamEnabled(f.Root));
    True(!File.Exists(f.Path("SS14ModLauncher/preferences.json")));
    True(Installation.ShouldEnableSteamByDefault(f.Root));
    f.InstallDefaults();
    True(Installation.IsSteamEnabled(f.Root));
}

static void StandaloneDefault()
{
    using var f = new Fixture();
    True(!Installation.IsSteamInstallation(f.Root));
    True(!Installation.ShouldEnableSteamByDefault(f.Root));
    Installation.InstallWithDefaults(f.Root, f.Payload, ["Test.Mod.dll"], "ru", f.Path("does-not-exist.exe"));
    Equal("installed", Installation.Inspect(f.Root).State);
    True(!Installation.IsSteamEnabled(f.Root));
    Bytes(f.OriginalLauncher, File.ReadAllBytes(f.Path("SS14.Launcher.exe")));
    True(!File.Exists(f.Path("SS14ModLauncher/preferences.json")));
}

static void InvalidSteamPreference()
{
    using var f = new Fixture(steam: true);
    f.Write("SS14ModLauncher/preferences.json", "{}"u8.ToArray());
    Code("state-invalid", () => f.InstallDefaults());
    Bytes(f.OriginalLoader, File.ReadAllBytes(f.Path("loader/SS14.Loader.dll")));
    Bytes(f.OriginalLauncher, File.ReadAllBytes(f.Path("SS14.Launcher.exe")));
    True(!File.Exists(f.Path("loader/SS14LocalMods/installation.json")));
}

static void SelfContainedCli(string published)
{
    using var f = new Fixture();
    var executable = System.IO.Path.GetFullPath(published);
    void Run(string operation, Fixture? fixture = null)
    {
        var target = fixture ?? f;
        var emptyRuntime = target.Path("empty-runtime");
        var extraction = target.Path("bundle-extraction");
        Directory.CreateDirectory(emptyRuntime);
        var start = new System.Diagnostics.ProcessStartInfo(executable)
        {
            UseShellExecute = false, WorkingDirectory = target.Root, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
        };
        start.ArgumentList.Add(operation); start.ArgumentList.Add(target.Root);
        start.Environment["DOTNET_ROOT"] = emptyRuntime;
        start.Environment["DOTNET_ROOT_X64"] = emptyRuntime;
        start.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
        start.Environment["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = extraction;
        start.Environment["DOTNET_HOST_TRACE"] = "1";
        start.Environment["DOTNET_HOST_TRACEFILE"] = target.Path(operation.TrimStart('-') + "-host-trace.txt");
        using var process = System.Diagnostics.Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true); process.WaitForExit(5_000);
            throw new Exception("Self-contained CLI timed out: " + operation);
        }
        Task.WaitAll(output, error);
        if (process.ExitCode != 0) throw new Exception($"Self-contained CLI {operation} failed ({process.ExitCode}): {output.Result} {error.Result}");
    }
    Run("--install");
    Equal("installed", Installation.Inspect(f.Root).State);
    True(!Installation.IsSteamInstallation(f.Root));
    True(!Installation.IsSteamEnabled(f.Root));
    Bytes(f.OriginalLauncher, File.ReadAllBytes(f.Path("SS14.Launcher.exe")));
    Equal("CrewConsole.Mod.dll", Installation.ReadSelection(f.Root)!.EnabledMods.Single().File);
    var mods = Installation.ReadVerifiedInstalledMods(f.Root);
    Equal(Catalog.Bundled.Count, mods.Count);
    foreach (var mod in Catalog.Bundled)
    {
        using var bytes = new MemoryStream(mods[mod.File]);
        using var assembly = AssemblyDefinition.ReadAssembly(bytes);
        Equal(System.IO.Path.GetFileNameWithoutExtension(mod.File), assembly.Name.Name);
    }
    var trace = File.ReadAllText(f.Path("install-host-trace.txt"));
    True(trace.Contains("Detected Single-File app bundle", StringComparison.Ordinal));
    True(trace.Contains("Executing as a self-contained app", StringComparison.Ordinal));
    True(trace.Contains("is_framework_dependent=0", StringComparison.Ordinal));
    Run("--status");
    Run("--restore");
    Equal("clean", Installation.Inspect(f.Root).State);
    Bytes(f.OriginalLoader, File.ReadAllBytes(f.Path("loader/SS14.Loader.dll")));
    Bytes(f.OriginalLauncher, File.ReadAllBytes(f.Path("SS14.Launcher.exe")));
    var publishedHash = Hash(File.ReadAllBytes(executable));
    using var steam = new Fixture(steam: true);
    True(Installation.IsSteamInstallation(steam.Root));
    Run("--install", steam);
    Equal("installed", Installation.Inspect(steam.Root).State);
    True(Installation.IsSteamEnabled(steam.Root));
    Equal(publishedHash, Hash(File.ReadAllBytes(steam.Path("SS14.Launcher.exe"))));
    Equal(publishedHash, Hash(File.ReadAllBytes(steam.Path("SS14ModLauncher/SS14ModLauncher.exe"))));
    Run("--restore", steam);
    Bytes(steam.OriginalLauncher, File.ReadAllBytes(steam.Path("SS14.Launcher.exe")));
    Bytes(steam.OriginalLoader, File.ReadAllBytes(steam.Path("loader/SS14.Loader.dll")));
    True(Installation.ShouldEnableSteamByDefault(steam.Root));
    Run("--install", steam);
    True(Installation.IsSteamEnabled(steam.Root));
    Installation.DisableSteam(steam.Root);
    True(!Installation.ShouldEnableSteamByDefault(steam.Root));
    Run("--install", steam);
    True(!Installation.IsSteamEnabled(steam.Root));
    Bytes(steam.OriginalLauncher, File.ReadAllBytes(steam.Path("SS14.Launcher.exe")));
    Run("--restore", steam);
    Run("--install", steam);
    True(!Installation.IsSteamEnabled(steam.Root));
    True(!Installation.ShouldEnableSteamByDefault(steam.Root));
    Run("--restore", steam);
    Bytes(steam.OriginalLauncher, File.ReadAllBytes(steam.Path("SS14.Launcher.exe")));
    Bytes(steam.OriginalLoader, File.ReadAllBytes(steam.Path("loader/SS14.Loader.dll")));
    Console.WriteLine($"Self-contained EXE SHA-256: {publishedHash}");
}

static void PublishedHandoff(string published)
{
    using var f = new Fixture(); f.Install();
    Installation.EnableSteam(f.Root, System.IO.Path.GetFullPath(published));
    var settings = f.Path("handoff-settings.json");
    var capture = f.Path("handoff.png");
    var start = new System.Diagnostics.ProcessStartInfo(f.Path("SS14.Launcher.exe"))
    {
        UseShellExecute = false, WorkingDirectory = f.Root, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
    };
    start.ArgumentList.Add("--help"); // Forwarded game argument must not become a ModLauncher operation.
    using var wrapper = System.Diagnostics.Process.Start(start)!;
    try
    {
        if (!wrapper.WaitForExit(15_000)) throw new Exception("The Steam entry-point process did not hand off and exit.");
        Equal(0, wrapper.ExitCode);
        var deadline = DateTime.UtcNow.AddSeconds(20);
        System.Diagnostics.Process? stable = null;
        while (stable is null && DateTime.UtcNow < deadline)
        {
            foreach (var candidate in System.Diagnostics.Process.GetProcessesByName("SS14ModLauncher"))
            {
                try
                {
                    if (string.Equals(candidate.MainModule?.FileName, f.Path("SS14ModLauncher/SS14ModLauncher.exe"), StringComparison.OrdinalIgnoreCase))
                    { stable = candidate; break; }
                }
                catch (InvalidOperationException) { }
                candidate.Dispose();
            }
            if (stable is null) Thread.Sleep(100);
        }
        True(stable is not null);
        using (stable)
        {
            True(stable!.WaitForInputIdle(15_000));
            // Enabling from the currently running stable copy is a content no-op.
            Installation.EnableSteam(f.Root, f.Path("SS14ModLauncher/SS14ModLauncher.exe"));
            True(Installation.IsSteamEnabled(f.Root));
            // The stable UI remains open while its original Steam entry point is restored.
            Installation.Restore(f.Root);
            True(!stable.HasExited);
            stable.CloseMainWindow();
            True(stable.WaitForExit(10_000));
        }
        Bytes(f.OriginalLauncher, File.ReadAllBytes(f.Path("SS14.Launcher.exe")));
        var captureStart = new System.Diagnostics.ProcessStartInfo(f.Path("SS14ModLauncher/SS14ModLauncher.exe"))
        { UseShellExecute = false, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden, WorkingDirectory = f.Root };
        foreach (var argument in new[] { "--launcher-root", f.Root, "--settings", settings, "--capture", capture, "--lang", "en" }) captureStart.ArgumentList.Add(argument);
        using var captureProcess = System.Diagnostics.Process.Start(captureStart)!;
        True(captureProcess.WaitForExit(20_000));
        Equal(0, captureProcess.ExitCode);
        True(File.Exists(capture));
    }
    finally
    {
        if (!wrapper.HasExited) wrapper.Kill();
        // Only terminate processes whose executable is the unique, disposable fixture copy.
        foreach (var process in System.Diagnostics.Process.GetProcessesByName("SS14ModLauncher"))
        {
            using (process)
            {
                try
                {
                    if (string.Equals(process.MainModule?.FileName, f.Path("SS14ModLauncher/SS14ModLauncher.exe"), StringComparison.OrdinalIgnoreCase))
                    { process.Kill(); process.WaitForExit(5_000); }
                }
                catch (InvalidOperationException) { }
            }
        }
    }
}

static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
static void True(bool value) { if (!value) throw new Exception("Assertion failed."); }
static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}."); }
static void Bytes(byte[] expected, byte[] actual) => True(expected.AsSpan().SequenceEqual(actual));
static void Code(string code, Action action) { var error = Throws<InstallationException>(action); Equal(code, error.Code); }
static T Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T ex) { return ex; }
    throw new Exception($"Expected {typeof(T).Name}.");
}

sealed class Fixture : IDisposable
{
    private string Sandbox { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ss14-modlauncher-core-tests", Guid.NewGuid().ToString("N"));
    public string Root { get; }
    public string? ManifestPath { get; }
    public byte[] OriginalLoader { get; }
    public byte[] OriginalLauncher { get; } = Encoding.ASCII.GetBytes("MZSS14.Launcher.dll" + new string('A', 110));
    public byte[] Wrapper { get; } = Encoding.ASCII.GetBytes("MZ" + new string('W', 126));
    public Dictionary<string, byte[]> Payload { get; } = new()
    {
        ["SS14LocalMods.Bootstrap.dll"] = [1, 2, 3], ["0Harmony.dll"] = [4, 5, 6], ["Test.Mod.dll"] = [7, 8, 9]
    };
    public Fixture(bool steam = false)
    {
        Root = steam ? System.IO.Path.Combine(Sandbox, "steamapps", "common", "Space Station 14 Playtest", "bin_x64") : Sandbox;
        if (steam)
        {
            ManifestPath = System.IO.Path.Combine(Sandbox, "steamapps", "appmanifest_1482520.acf");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ManifestPath)!);
            File.WriteAllText(ManifestPath, "\"AppState\"\n{\n\t\"appid\"\t\t\"1482520\"\n\t\"name\"\t\t\"Space Station 14 Playtest\"\n\t\"installdir\"\t\t\"Space Station 14 Playtest\"\n\t\"StateFlags\"\t\t\"4\"\n\t\"UserConfig\"\n\t{\n\t\t\"language\"\t\t\"english\"\n\t}\n}\n");
        }
        using var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("SS14.Loader", new Version(1, 0)), "SS14.Loader", ModuleKind.Dll);
        var type = new TypeDefinition("SS14.Loader", "Program", TypeAttributes.Class | TypeAttributes.Public, assembly.MainModule.TypeSystem.Object);
        assembly.MainModule.Types.Add(type);
        var run = new MethodDefinition("Run", MethodAttributes.Static | MethodAttributes.Public, assembly.MainModule.TypeSystem.Void);
        type.Methods.Add(run); run.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        using var buffer = new MemoryStream(); assembly.Write(buffer); OriginalLoader = buffer.ToArray();
        Write("SS14.Launcher.exe", OriginalLauncher);
        Write("SS14.Launcher.dll", [1]);
        Write("loader/SS14.Loader.dll", OriginalLoader);
    }
    public string Path(string relative) => System.IO.Path.Combine(Root, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
    public void Write(string relative, byte[] bytes) { var path = Path(relative); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes); }
    public void Install() => Installation.Install(Root, Payload, ["Test.Mod.dll"], "ru");
    public void InstallDefaults() { Write("test-wrapper.exe", Wrapper); Installation.InstallWithDefaults(Root, Payload, ["Test.Mod.dll"], "ru", Path("test-wrapper.exe")); }
    public void EnableSteam() { Write("test-wrapper.exe", Wrapper); Installation.EnableSteam(Root, Path("test-wrapper.exe")); }
    public void Journal(string target, byte[] before, byte[] after)
    {
        Write(".ss14-modlauncher-transaction/0.bak", before);
        Write(".ss14-modlauncher-transaction/journal.json", JsonSerializer.SerializeToUtf8Bytes(new
        {
            Version = 1,
            Entries = new[] { new { Path = target, BeforeHash = Convert.ToHexString(SHA256.HashData(before)), AfterHash = Convert.ToHexString(SHA256.HashData(after)), Snapshot = "0.bak" } }
        }));
    }
    public void Dispose()
    {
        var full = System.IO.Path.GetFullPath(Sandbox);
        var allowed = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ss14-modlauncher-core-tests")) + System.IO.Path.DirectorySeparatorChar;
        if (!full.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe test cleanup path.");
        Directory.Delete(full, recursive: true);
    }
}
