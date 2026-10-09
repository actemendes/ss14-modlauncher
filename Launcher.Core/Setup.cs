using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Launcher.Core.Tests")]

namespace SS14ModLauncher.Core;

public sealed record SetupReadiness(
    string Root,
    string InstallationState,
    string InstallationDetail,
    string Issue,
    bool CanInstall,
    bool SteamEnabledOnInstall,
    bool HasInstallationHistory);

/// <summary>Read-only first-run setup policy. Only explicit AppSettings methods record a decision.</summary>
public static class Setup
{
    private static readonly string[] HistoryFiles =
    [
        "loader/SS14LocalMods/installation.json", "loader/SS14LocalMods/ownership.json",
        "loader/SS14LocalMods/selection.json", "loader/SS14LocalMods/SS14.Loader.original.dll",
        "loader/SS14LocalMods/SS14LocalMods.Bootstrap.dll", "SS14ModLauncher/steam.json",
        "SS14ModLauncher/launcher.json", "SS14ModLauncher/preferences.json"
    ];

    /// <summary>Accept either bin_x64 itself or a game's parent directory containing bin_x64.</summary>
    public static string ResolveRoot(string? root)
    {
        if (string.IsNullOrWhiteSpace(root)) return "";
        var normalized = NormalizePath(root.Trim().Trim('"'));
        var child = Path.Combine(normalized, "bin_x64");
        return !HasLauncherLayout(normalized) && HasLauncherLayout(child) ? child : normalized;
    }

    public static string[] DiscoverRoots() => NormalizeDiscovered(Installation.Discover());

    public static SetupReadiness Prepare(string? root) => Prepare(root, Installation.CheckNotRunning);

    internal static SetupReadiness Prepare(string? root, Action<string> checkNotRunning)
    {
        string resolved;
        try { resolved = ResolveRoot(root); }
        catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException)
        { return new("", "missing", "root-invalid", "root-invalid", false, false, false); }
        if (resolved.Length == 0) return new("", "missing", "root-missing", "root-missing", false, false, false);
        var state = Installation.Inspect(resolved);
        var history = state.State == "installed" || state.Detail is "legacy-installation" or "untracked-patch" or "original-restored-by-platform"
            || HistoryFiles.Any(file => File.Exists(Path.Combine(resolved, file)));
        var canInstall = state.State is "clean" or "installed" || state.Detail == "legacy-installation";
        if (!canInstall)
        {
            var issue = state.Detail == "root-invalid" ? "root-invalid" : state.Detail;
            return new(resolved, state.State, state.Detail, issue, false, false, history);
        }
        var steam = false;
        try
        {
            steam = Installation.ShouldEnableSteamByDefault(resolved);
            checkNotRunning(resolved);
        }
        catch (InstallationException error)
        { return new(resolved, state.State, state.Detail, error.Code, false, steam, history); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return new(resolved, state.State, state.Detail, "cannot-read-installation", false, steam, history); }
        return new(resolved, state.State, state.Detail, "", true, steam, history);
    }

    /// <summary>Automatic prompting only; an explicit setup action always bypasses this policy.</summary>
    public static bool ShouldPrompt(AppSettings settings, string? root = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.IsReadOnly) return false;
        var input = root ?? settings.LauncherPath;
        if (string.IsNullOrWhiteSpace(input)) return !settings.SetupDismissedWithoutRoot;
        try
        {
            var resolved = ResolveRoot(input);
            if (settings.SetupStateFor(resolved) is not null) return false;
            var readiness = Prepare(resolved);
            if (readiness.HasInstallationHistory || readiness.InstallationState == "installed") return false;
            return readiness.InstallationState == "clean" || readiness.Issue == "root-invalid";
        }
        catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException)
        { return true; } // A first-time invalid path should lead to choosing a valid folder.
    }

    internal static string[] NormalizeDiscovered(IEnumerable<string> roots) => roots.Select(root =>
        {
            try { return ResolveRoot(root); }
            catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException) { return ""; }
        })
        .Where(root => root.Length > 0 && HasLauncherLayout(root)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    internal static string NormalizePath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    internal static bool IsValidStoredRoot(string? root)
    {
        if (root is null || root.Length is 0 or > 4096 || root.Any(char.IsControl) || !Path.IsPathFullyQualified(root)) return false;
        try { _ = NormalizePath(root); return true; }
        catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException) { return false; }
    }

    private static bool HasLauncherLayout(string root) => File.Exists(Path.Combine(root, "SS14.Launcher.exe"))
        && File.Exists(Path.Combine(root, "loader", "SS14.Loader.dll"));
}
