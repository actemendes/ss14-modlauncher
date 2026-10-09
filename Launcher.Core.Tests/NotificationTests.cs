using SS14ModLauncher.Core;

internal static class NotificationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public static async Task RunAsync()
    {
        SettingsMigrationAndPersistence();
        await SkippedChecks();
        await NonblockingAndSingleAttempt();
        await FailuresAndCancellation();
        await ContextIsolation();
        await MetadataOnly();
        Console.WriteLine("Startup update settings, background notification, cancellation and context isolation passed.");
    }

    private static void SettingsMigrationAndPersistence()
    {
        using var fixture = new SettingsFixture();
        Assert(AppSettings.Load(fixture.Path).CheckUpdatesOnStartup, "new settings enable startup check");
        File.WriteAllText(fixture.Path, "{\"Version\":1,\"Language\":\"en\",\"UpdateRepository\":\"owner/project\"}");
        var migrated = AppSettings.Load(fixture.Path);
        Assert(!migrated.IsReadOnly && migrated.CheckUpdatesOnStartup, "old settings without property migrate enabled");
        migrated.CheckUpdatesOnStartup = false;
        migrated.Save();
        var disabled = AppSettings.Load(fixture.Path);
        Assert(!disabled.CheckUpdatesOnStartup, "explicit startup opt-out persists");
        disabled.CheckUpdatesOnStartup = true;
        disabled.Save();
        Assert(AppSettings.Load(fixture.Path).CheckUpdatesOnStartup, "re-enabled setting persists");

        const string invalid = "{\"CheckUpdatesOnStartup\":\"false\"}";
        File.WriteAllText(fixture.Path, invalid);
        var broken = AppSettings.Load(fixture.Path);
        Assert(broken.IsReadOnly && broken.LoadError != null, "invalid boolean preserves corrupt settings");
        try { broken.Save(); throw new Exception("Corrupt settings unexpectedly saved."); }
        catch (InvalidOperationException) { }
        Assert(File.ReadAllText(fixture.Path) == invalid, "corrupt original stays unchanged");
    }

    private static async Task SkippedChecks()
    {
        using var fixture = new SettingsFixture();
        File.WriteAllText(fixture.Path, "{broken json");
        var cases = new[]
        {
            WithStartup(Settings(), false),
            new AppSettings { UpdateRepository = "" },
            new AppSettings { UpdateRepository = "https://untrusted.example/project" },
            AppSettings.Load(fixture.Path)
        };
        foreach (var settings in cases)
        {
            var requests = 0;
            using var checker = new AutomaticUpdateChecker((repo, _, _) =>
            {
                requests++;
                return Task.FromResult(Result(repo));
            });
            Assert(await checker.CheckOnceAsync(settings) == null, "disabled or invalid settings skip startup");
            Assert(requests == 0 && !checker.IsChecking && checker.LastError == null, "skip performs no request and reports no error");
        }

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var called = false;
        using var preCancelled = new AutomaticUpdateChecker((repo, _, _) => { called = true; return Task.FromResult(Result(repo)); });
        Assert(await preCancelled.CheckOnceAsync(Settings(), cancelled.Token) == null && !called, "pre-cancelled startup skips request");

        static AppSettings WithStartup(AppSettings settings, bool enabled) { settings.CheckUpdatesOnStartup = enabled; return settings; }
    }

    private static async Task NonblockingAndSingleAttempt()
    {
        var entered = Completion<bool>();
        var release = Completion<bool>();
        var returned = Completion<Task<UpdateCheck?>>();
        var requests = 0;
        var settings = Settings();
        using var checker = new AutomaticUpdateChecker((repo, _, _) =>
        {
            Interlocked.Increment(ref requests);
            entered.TrySetResult(true);
            release.Task.GetAwaiter().GetResult(); // Deliberately synchronous: must never run on the caller.
            return Task.FromResult(Result(repo));
        });
        var invocation = Task.Run(() => returned.TrySetResult(checker.CheckOnceAsync(settings)));
        try
        {
            await entered.Task.WaitAsync(Timeout);
            var pending = await returned.Task.WaitAsync(Timeout);
            Assert(!pending.IsCompleted && checker.IsChecking, "startup returns before a blocking provider finishes");
            Assert(await checker.CheckOnceAsync(settings) == null, "concurrent startup call does not create another request");
            release.TrySetResult(true);
            var result = await pending.WaitAsync(Timeout);
            Assert(result?.HasLauncherUpdate == true && !checker.IsChecking && checker.LastError == null, "successful metadata result is available");
            Assert(await checker.CheckOnceAsync(settings) == null && requests == 1, "one automatic attempt per instance");
        }
        finally { release.TrySetResult(true); await invocation.WaitAsync(Timeout); }
    }

    private static async Task FailuresAndCancellation()
    {
        using (var offline = new AutomaticUpdateChecker((_, _, _) => throw new HttpRequestException("Offline fixture")))
        {
            Assert(await offline.CheckOnceAsync(Settings()) == null, "offline startup returns without throwing");
            Assert(offline.LastError is HttpRequestException && !offline.IsChecking, "offline failure is exposed only as nonmodal state");
        }

        foreach (var useExternalCancellation in new[] { false, true })
        {
            var entered = Completion<bool>();
            using var token = new CancellationTokenSource();
            using var checker = new AutomaticUpdateChecker(async (repo, _, cancellation) =>
            {
                entered.TrySetResult(true);
                await Task.Delay(System.Threading.Timeout.Infinite, cancellation);
                return Result(repo);
            });
            var pending = checker.CheckOnceAsync(Settings(), token.Token);
            await entered.Task.WaitAsync(Timeout);
            if (useExternalCancellation) token.Cancel(); else checker.Cancel();
            Assert(await pending.WaitAsync(Timeout) == null, "startup cancellation completes quietly");
            Assert(checker.LastError == null && !checker.IsChecking, "cancellation is not an update error");
        }

        var ready = Completion<bool>();
        var ignoredCancellation = Completion<UpdateCheck>();
        using var late = new AutomaticUpdateChecker((_, _, _) => { ready.TrySetResult(true); return ignoredCancellation.Task; });
        var lateTask = late.CheckOnceAsync(Settings());
        await ready.Task.WaitAsync(Timeout);
        late.Cancel();
        ignoredCancellation.SetResult(Result("owner/project"));
        Assert(await lateTask.WaitAsync(Timeout) == null && late.LastError == null, "late provider result is discarded after cancellation");
    }

    private static async Task ContextIsolation()
    {
        using var fixture = new SettingsFixture();
        var root = fixture.Folder;
        var changes = new (string Name, Action<AppSettings> Apply)[]
        {
            ("repository", settings => settings.UpdateRepository = "other/project"),
            ("installation", settings => settings.LauncherPath = Path.Combine(root, "other")),
            ("installed version", settings => settings.RecordInstalledVersions(root, [new ModUpdate { Id = "crew-console", Version = "9.0.0" }])),
            ("opt-out", settings => settings.CheckUpdatesOnStartup = false)
        };
        foreach (var (name, change) in changes)
        {
            var settings = Settings();
            settings.LauncherPath = root;
            settings.RecordInstalledVersions(root, [new ModUpdate { Id = "crew-console", Version = "1.0.0" }]);
            var entered = Completion<IReadOnlyDictionary<string, string>>();
            var complete = Completion<UpdateCheck>();
            using var checker = new AutomaticUpdateChecker((_, versions, _) => { entered.TrySetResult(versions); return complete.Task; });
            var pending = checker.CheckOnceAsync(settings);
            var snapshot = await entered.Task.WaitAsync(Timeout);
            change(settings);
            Assert(snapshot["crew-console"] == "1.0.0", "in-flight versions are an immutable snapshot");
            complete.SetResult(Result("owner/project"));
            Assert(await pending.WaitAsync(Timeout) == null && checker.LastError == null, "stale result suppressed after " + name);
        }

        var staleSettings = Settings();
        var started = Completion<bool>();
        var failure = Completion<UpdateCheck>();
        using var staleError = new AutomaticUpdateChecker((_, _, _) => { started.TrySetResult(true); return failure.Task; });
        var staleTask = staleError.CheckOnceAsync(staleSettings);
        await started.Task.WaitAsync(Timeout);
        staleSettings.UpdateRepository = "other/project";
        failure.SetException(new HttpRequestException("Old repository went offline"));
        Assert(await staleTask.WaitAsync(Timeout) == null && staleError.LastError == null, "old-source failure cannot replace current UI state");

        using var foreign = new AutomaticUpdateChecker((_, _, _) => Task.FromResult(Result("other/project")));
        Assert(await foreign.CheckOnceAsync(Settings()) == null && foreign.LastError is InvalidDataException, "foreign result repository is rejected");
    }

    private static async Task MetadataOnly()
    {
        using var fixture = new SettingsFixture();
        var settings = Settings();
        settings.LauncherPath = fixture.Folder;
        settings.Save(fixture.Path);
        var before = File.ReadAllBytes(fixture.Path);
        var gameFile = Path.Combine(fixture.Folder, "unchanged-loader.bin");
        File.WriteAllBytes(gameFile, [1, 2, 3]);
        var checks = 0;
        using var checker = new AutomaticUpdateChecker((repo, _, _) =>
        {
            checks++;
            return Task.FromResult(Result(repo) with
            {
                Mods = [new ModUpdate { Id = "crew-console", File = "CrewConsole.Mod.dll", Version = "9.0.0", DownloadUrl = "https://example.invalid/must-not-download" }]
            });
        });
        var offered = await checker.CheckOnceAsync(settings);
        Assert(offered?.Mods.Count == 1 && checks == 1, "startup offers metadata without a download/apply operation");
        Assert(File.ReadAllBytes(fixture.Path).SequenceEqual(before), "background check does not write settings");
        Assert(File.ReadAllBytes(gameFile).SequenceEqual(new byte[] { 1, 2, 3 }), "background check does not modify installation files");
    }

    private static AppSettings Settings() => new() { UpdateRepository = "owner/project", Language = "en" };
    private static UpdateCheck Result(string repository) => new() { Repository = repository, Version = "9.0.0", HasLauncherUpdate = true };
    private static TaskCompletionSource<T> Completion<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Assert(bool condition, string name) { if (!condition) throw new Exception("FAILED: " + name); }

    private sealed class SettingsFixture : IDisposable
    {
        public string Folder { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SS14ModLauncherNotificationTests-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(Folder, "settings.json");
        public SettingsFixture() => Directory.CreateDirectory(Folder);
        public void Dispose() => Directory.Delete(Folder, recursive: true);
    }
}
