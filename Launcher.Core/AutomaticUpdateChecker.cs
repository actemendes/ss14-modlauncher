using System.Collections.ObjectModel;

namespace SS14ModLauncher.Core;

/// <summary>
/// One best-effort startup check. It reports metadata only: it cannot download or install updates.
/// The caller owns UI dispatch and may cancel this check when beginning a manual operation.
/// </summary>
public sealed class AutomaticUpdateChecker : IDisposable
{
    private readonly Func<string, IReadOnlyDictionary<string, string>, CancellationToken, Task<UpdateCheck>> _check;
    private readonly object _gate = new();
    private CancellationTokenSource? _cancellation;
    private Exception? _lastError;
    private bool _started;
    private bool _disposed;
    private int _checking;

    public AutomaticUpdateChecker(Func<string, IReadOnlyDictionary<string, string>, CancellationToken, Task<UpdateCheck>> check)
    {
        ArgumentNullException.ThrowIfNull(check);
        _check = check;
    }

    public bool IsChecking => Volatile.Read(ref _checking) != 0;
    public Exception? LastError => Volatile.Read(ref _lastError);

    /// <summary>
    /// Starts at most one request. Disabled, invalid or unreadable settings do not make a request.
    /// A changed repository, installation, installed version or opt-out discards the result.
    /// Failures are returned as null and exposed through LastError, never thrown into the UI.
    /// </summary>
    public Task<UpdateCheck?> CheckOnceAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (cancellationToken.IsCancellationRequested || !TryCapture(settings, out var snapshot))
            return Task.FromResult<UpdateCheck?>(null);

        lock (_gate)
        {
            if (_disposed || _started) return Task.FromResult<UpdateCheck?>(null);
            _started = true;
            _lastError = null;
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Volatile.Write(ref _checking, 1);
            var cancellation = _cancellation;
            // Even an injected implementation with a synchronous preamble cannot block the UI.
            return Task.Run(() => RunAsync(settings, snapshot!, cancellation), CancellationToken.None);
        }
    }

    private async Task<UpdateCheck?> RunAsync(AppSettings settings, Snapshot snapshot, CancellationTokenSource cancellation)
    {
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            var result = await _check(snapshot.Repository, snapshot.InstalledVersions, cancellation.Token).ConfigureAwait(false);
            if (cancellation.IsCancellationRequested || !StillCurrent(settings, snapshot)) return null;
            if (result == null || !result.Repository.Equals(snapshot.Repository, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The automatic update result belongs to another repository.");
            return result;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception error)
        {
            if (!cancellation.IsCancellationRequested && StillCurrent(settings, snapshot))
                Volatile.Write(ref _lastError, error);
            return null;
        }
        finally
        {
            lock (_gate)
            {
                _cancellation = null;
                Volatile.Write(ref _checking, 0);
            }
            cancellation.Dispose();
        }
    }

    public void Cancel()
    {
        CancellationTokenSource? cancellation;
        lock (_gate) cancellation = _cancellation;
        try { cancellation?.Cancel(); }
        catch (ObjectDisposedException) { /* The completed check already released its source. */ }
        catch (AggregateException) { /* A provider's cancellation callback must not interrupt the UI. */ }
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
        Cancel();
    }

    private sealed record Snapshot(string Repository, string LauncherRoot, IReadOnlyDictionary<string, string> InstalledVersions);

    private static bool TryCapture(AppSettings settings, out Snapshot? snapshot)
    {
        snapshot = null;
        if (!settings.CheckUpdatesOnStartup || settings.IsReadOnly || !UpdateService.IsValidRepository(settings.UpdateRepository))
            return false;
        try
        {
            var root = string.IsNullOrWhiteSpace(settings.LauncherPath)
                ? "" : Path.TrimEndingDirectorySeparator(Path.GetFullPath(settings.LauncherPath));
            var versions = root.Length == 0
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : settings.VersionsFor(root).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            snapshot = new(settings.UpdateRepository, root, new ReadOnlyDictionary<string, string>(versions));
            return true;
        }
        catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException or InvalidOperationException)
        {
            // A setting changed during capture, or an incomplete path cannot identify an installation.
            return false;
        }
    }

    private static bool StillCurrent(AppSettings settings, Snapshot original)
    {
        if (!TryCapture(settings, out var current) || current == null
            || !current.Repository.Equals(original.Repository, StringComparison.OrdinalIgnoreCase)
            || !current.LauncherRoot.Equals(original.LauncherRoot, StringComparison.OrdinalIgnoreCase)
            || current.InstalledVersions.Count != original.InstalledVersions.Count) return false;
        return original.InstalledVersions.All(pair => current.InstalledVersions.TryGetValue(pair.Key, out var version)
            && version == pair.Value);
    }
}
