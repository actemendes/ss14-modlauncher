using System.Text.Json;
using SS14ModLauncher.Core;

internal static class SetupTests
{
    public static void Run()
    {
        MissingAndDiscovery();
        DecisionsSurviveRestart();
        ExistingAndRestored();
        ProcessReadiness();
        ChangedInstallation();
        CorruptSettings();
        Console.WriteLine("First-run setup discovery, per-root decisions, restored-installation suppression and process readiness passed.");
    }

    private static void MissingAndDiscovery()
    {
        var settings = new AppSettings();
        Equal("root-missing", Setup.Prepare(null).Issue);
        Check(!Setup.Prepare("").CanInstall);
        Check(Setup.ShouldPrompt(settings));
        settings.SuppressSetupFor(null);
        Check(!Setup.ShouldPrompt(settings));
        using var steam = new Fixture(steam: true);
        var parent = Directory.GetParent(steam.Root)!.FullName;
        var ready = Setup.Prepare(parent);
        Equal(steam.Root, ready.Root);
        Check(ready.CanInstall && ready.SteamEnabledOnInstall && !ready.HasInstallationHistory);
        Check(Setup.ShouldPrompt(settings, parent)); // A missing-root dismissal is not a global opt-out.
        var roots = Setup.NormalizeDiscovered([parent, steam.Root, "", steam.Path("not-an-installation")]);
        Equal(1, roots.Length);
        Equal(steam.Root, roots[0]);
        Equal("root-invalid", Setup.Prepare(steam.Path("missing")).Issue);
        Equal("root-invalid", Setup.Prepare("bad\0path").Issue);
        Check(Setup.ShouldPrompt(new AppSettings { LauncherPath = steam.Path("missing") }));
        Check(!File.Exists(steam.Path("loader/SS14LocalMods/installation.json")));
    }

    private static void DecisionsSurviveRestart()
    {
        using var first = new Fixture(steam: true);
        using var second = new Fixture();
        var path = first.Path("settings.json");
        var settings = AppSettings.Load(path);
        var parent = Directory.GetParent(first.Root)!.FullName;
        settings.SuppressSetupFor(parent);
        settings.Save();
        var restored = AppSettings.Load(path);
        Check(!restored.IsReadOnly);
        Equal("dismissed", restored.SetupStateFor(first.Root));
        Check(!Setup.ShouldPrompt(restored, first.Root));
        Check(Setup.ShouldPrompt(restored, second.Root));
        // Explicit reopening is possible regardless of a saved dismissal.
        Check(Setup.Prepare(first.Root).CanInstall);
        restored.CompleteSetupFor(first.Root + Path.DirectorySeparatorChar);
        restored.Save();
        var completed = AppSettings.Load(path);
        Equal("completed", completed.SetupStateFor(parent));
        Equal(1, completed.SetupByRoot.Count);
        Check(!Setup.ShouldPrompt(completed, first.Root));
        Check(Setup.ShouldPrompt(completed, second.Root));
        completed.SuppressSetupFor("");
        completed.Save();
        var noRoot = AppSettings.Load(path);
        Check(noRoot.SetupDismissedWithoutRoot && !Setup.ShouldPrompt(noRoot, ""));
        Check(Setup.ShouldPrompt(noRoot, second.Root));
        Throws<ArgumentException>(() => completed.CompleteSetupFor(""));
    }

    private static void ExistingAndRestored()
    {
        using var fixture = new Fixture();
        var settings = new AppSettings { LauncherPath = fixture.Root };
        Check(Setup.ShouldPrompt(settings));
        fixture.Install();
        var installed = Setup.Prepare(fixture.Root);
        Check(installed.CanInstall && installed.HasInstallationHistory);
        Equal("installed", installed.InstallationState);
        Check(!Setup.ShouldPrompt(settings));
        Installation.Restore(fixture.Root);
        var restored = Setup.Prepare(fixture.Root);
        Equal("clean", restored.InstallationState);
        Check(restored.CanInstall && restored.HasInstallationHistory);
        Check(!Setup.ShouldPrompt(new AppSettings(), fixture.Root));
        Equal(0, settings.SetupByRoot.Count); // Reading existing history never silently writes preferences.
        using var old = new Fixture();
        old.Write("loader/SS14LocalMods/SS14LocalMods.Bootstrap.dll", [1, 2, 3]);
        Check(!Setup.ShouldPrompt(new AppSettings(), old.Root)); // A legacy clean restore left inert payloads.
    }

    private static void ProcessReadiness()
    {
        using var fixture = new Fixture();
        var before = File.ReadAllBytes(fixture.Path("loader/SS14.Loader.dll"));
        var busy = Setup.Prepare(fixture.Root, root =>
        {
            Equal(fixture.Root, root);
            throw new InstallationException("game-running", "Close SS14 first.");
        });
        Equal("game-running", busy.Issue);
        Check(!busy.CanInstall);
        var unknown = Setup.Prepare(fixture.Root, _ => throw new InstallationException("process-check-failed", "Cannot inspect a process."));
        Equal("process-check-failed", unknown.Issue);
        Check(!unknown.CanInstall);
        var retried = Setup.Prepare(fixture.Root, _ => { });
        Check(retried.CanInstall && retried.Issue.Length == 0);
        Check(before.AsSpan().SequenceEqual(File.ReadAllBytes(fixture.Path("loader/SS14.Loader.dll"))));
        Check(!File.Exists(fixture.Path("loader/SS14LocalMods/installation.json")));
    }

    private static void ChangedInstallation()
    {
        using var fixture = new Fixture();
        fixture.Install();
        fixture.Write("loader/SS14.Loader.dll", [9, 8, 7]);
        var changed = Setup.Prepare(fixture.Root);
        Equal("changed", changed.InstallationState);
        Equal("loader-changed", changed.Issue);
        Check(!changed.CanInstall && changed.HasInstallationHistory);
        Check(!Setup.ShouldPrompt(new AppSettings(), fixture.Root));
    }

    private static void CorruptSettings()
    {
        using var fixture = new Fixture();
        var path = fixture.Path("damaged-settings.json");
        File.WriteAllText(path, "{bad json");
        var damaged = AppSettings.Load(path);
        Check(damaged.IsReadOnly && !Setup.ShouldPrompt(damaged, fixture.Root));
        Throws<InvalidOperationException>(() => damaged.SuppressSetupFor(fixture.Root));
        Throws<InvalidOperationException>(() => damaged.CompleteSetupFor(fixture.Root));
        Equal("{bad json", File.ReadAllText(path));
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            Version = 1, SetupByRoot = new Dictionary<string, string> { [fixture.Root] = "unknown-state" }
        }));
        Check(AppSettings.Load(path).IsReadOnly);
        File.WriteAllText(path, "{\"Version\":1}");
        var legacy = AppSettings.Load(path);
        Check(!legacy.IsReadOnly && legacy.SetupByRoot.Count == 0 && !legacy.SetupDismissedWithoutRoot);
    }

    private static void Check(bool value) { if (!value) throw new Exception("Setup assertion failed."); }
    private static void Equal<T>(T expected, T actual)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}."); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
}
