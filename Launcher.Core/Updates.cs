using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SS14ModLauncher.Core;

public sealed record ModUpdate
{
    public string Id { get; init; } = "";
    public string File { get; init; } = "";
    public string Version { get; init; } = "";
    public string MinLauncherVersion { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public string DownloadUrl { get; init; } = "";
}

public sealed record UpdateCheck
{
    public string Repository { get; init; } = "";
    public string Version { get; init; } = "";
    public string ReleaseUrl { get; init; } = "";
    public string LauncherVersion { get; init; } = "";
    public string LauncherReleaseVersion { get; init; } = "";
    public string LauncherDownloadUrl { get; init; } = "";
    public bool HasLauncherUpdate { get; init; }
    public bool RequiresLauncherUpdate { get; init; }
    public IReadOnlyList<ModUpdate> Mods { get; init; } = Array.Empty<ModUpdate>();
    public IReadOnlyList<ModUpdate> BlockedMods { get; init; } = Array.Empty<ModUpdate>();
}

/// <summary>Explicit, bounded GitHub release checks. No network activity occurs before CheckAsync.</summary>
public sealed class UpdateService : IDisposable
{
    public const int MaximumModBytes = 32 * 1024 * 1024;
    public const int MaximumManifestBytes = 1024 * 1024;
    private readonly HttpClient _http;
    private sealed class GitHubRateLimitException : Exception { }
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Regex RepositoryPattern = new(@"\A[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})/[A-Za-z0-9_.-]{1,100}\z", RegexOptions.CultureInvariant);
    private sealed record Manifest
    {
        public string Version { get; init; } = "";
        public string MinLauncherVersion { get; init; } = "";
        public LauncherManifest? Launcher { get; init; }
        public List<ModUpdate> Mods { get; init; } = [];
    }
    private sealed record LauncherManifest
    {
        public string Version { get; init; } = "";
        public string? ReleaseVersion { get; init; }
        public string DownloadUrl { get; init; } = "";
    }

    public UpdateService() : this(new HttpClientHandler { AllowAutoRedirect = false }) { }

    // A handler can be supplied by offline tests; production still validates every requested URL.
    public UpdateService(HttpMessageHandler handler)
    {
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SS14ModLauncher/" + Catalog.LauncherVersion);
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public static bool IsValidRepository(string? repository) => repository != null && RepositoryPattern.IsMatch(repository)
        && !repository.Split('/')[1].StartsWith('.') && !repository.Split('/')[1].EndsWith('.')
        && !repository.Split('/')[1].Contains("..", StringComparison.Ordinal);

    public Task<UpdateCheck> CheckAsync(string repository, CancellationToken cancellationToken = default) => CheckAsync(repository, null, cancellationToken);

    public async Task<UpdateCheck> CheckAsync(string repository, IReadOnlyDictionary<string, string>? installedVersions, CancellationToken cancellationToken = default)
    {
        ValidateRepository(repository);
        byte[] releaseBytes;
        try
        {
            releaseBytes = await GetBytesAsync(new Uri($"https://api.github.com/repos/{repository}/releases/latest"), MaximumManifestBytes, true, cancellationToken);
        }
        catch (GitHubRateLimitException)
        {
            return await CheckLatestAssetAsync(repository, installedVersions, cancellationToken);
        }
        using var release = JsonDocument.Parse(releaseBytes, new JsonDocumentOptions { MaxDepth = 32 });
        var root = release.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean())
            throw new InvalidDataException("The release is not a stable public release.");
        var releaseVersion = root.GetProperty("tag_name").GetString() ?? "";
        if (releaseVersion.StartsWith('v')) releaseVersion = releaseVersion[1..];
        var parsedRelease = SemanticVersion.Parse(releaseVersion);
        if (parsedRelease.IsPrerelease) throw new InvalidDataException("The release version is not stable.");
        var releaseUrl = root.GetProperty("html_url").GetString() ?? "";
        if (!IsReleasePageUrl(releaseUrl, repository)) throw new InvalidDataException("Invalid release page URL.");
        var assets = root.GetProperty("assets").EnumerateArray()
            .Where(asset => asset.GetProperty("name").GetString() == "mods-manifest.json").ToArray();
        if (assets.Length != 1) throw new InvalidDataException("The release must include exactly one mods-manifest.json asset.");
        if (assets[0].TryGetProperty("size", out var assetSize) && (assetSize.GetInt64() <= 0 || assetSize.GetInt64() > MaximumManifestBytes))
            throw new InvalidDataException("The release manifest is too large or empty.");
        var manifestUrl = assets[0].GetProperty("browser_download_url").GetString() ?? "";
        if (!IsReleaseAssetUrl(manifestUrl, repository)) throw new InvalidDataException("Invalid manifest download URL.");
        var manifestBytes = await GetBytesAsync(new Uri(manifestUrl), MaximumManifestBytes, false, cancellationToken);
        return ParseManifest(repository, releaseVersion, releaseUrl, manifestBytes, installedVersions);
    }

    private async Task<UpdateCheck> CheckLatestAssetAsync(string repository, IReadOnlyDictionary<string, string>? installedVersions,
        CancellationToken cancellationToken)
    {
        // GitHub's documented public latest-asset link does not consume the REST API quota.
        // Resolve a same-repository, stable release tag before following a CDN or reading bytes.
        var latest = new Uri($"https://github.com/{repository}/releases/latest/download/mods-manifest.json");
        Uri? canonical = null;
        string? version = null;
        string? releasePage = null;
        void ValidateRoute(Uri uri)
        {
            if (uri == latest && canonical is null) return;
            if (uri.Host == "github.com")
            {
                if (!IsReleaseAssetUrl(uri.AbsoluteUri, repository))
                    throw new InvalidDataException("Latest manifest redirected outside the selected repository's release assets.");
                var prefix = $"/{repository}/releases/download/";
                var parts = uri.AbsolutePath[prefix.Length..].Split('/');
                if (parts.Length != 2 || parts[1] != "mods-manifest.json")
                    throw new InvalidDataException("Latest manifest redirected to an unexpected release asset.");
                var tag = Uri.UnescapeDataString(parts[0]);
                var candidateVersion = tag.StartsWith('v') ? tag[1..] : tag;
                if (!SemanticVersion.TryParse(candidateVersion, out var parsed) || parsed!.IsPrerelease)
                    throw new InvalidDataException("Latest manifest did not resolve to a stable release version.");
                if (canonical is not null && uri != canonical)
                    throw new InvalidDataException("Latest manifest changed release identity while redirecting.");
                canonical = uri;
                version = candidateVersion;
                releasePage = $"https://github.com/{repository}/releases/tag/{Uri.EscapeDataString(tag)}";
                return;
            }
            RequireCanonical(); // A direct latest-to-CDN redirect provides no verified release tag.
        }
        void RequireCanonical()
        {
            if (canonical is null) throw new InvalidDataException("Latest manifest did not resolve to a canonical GitHub release asset.");
        }
        var bytes = await GetBytesAsync(latest, MaximumManifestBytes, false, cancellationToken, ValidateRoute, RequireCanonical);
        return ParseManifest(repository, version!, releasePage!, bytes, installedVersions);
    }

    public static UpdateCheck ParseManifest(string repository, string releaseVersion, string releaseUrl, byte[] bytes,
        IReadOnlyDictionary<string, string>? installedVersions = null)
    {
        ValidateRepository(repository);
        if (bytes.Length > MaximumManifestBytes) throw new InvalidDataException("Manifest exceeds the size limit.");
        if (!IsReleasePageUrl(releaseUrl, repository)) throw new InvalidDataException("Invalid release page URL.");
        var manifest = JsonSerializer.Deserialize<Manifest>(bytes, JsonOptions) ?? throw new InvalidDataException("Empty manifest.");
        var latest = SemanticVersion.Parse(manifest.Version);
        if (latest.IsPrerelease || !latest.Equals(SemanticVersion.Parse(releaseVersion))) throw new InvalidDataException("Manifest and stable release versions differ.");
        var current = SemanticVersion.Parse(Catalog.LauncherVersion);
        var minimum = StableVersion(manifest.MinLauncherVersion);
        var launcherVersion = manifest.Launcher?.Version ?? manifest.Version;
        var launcherReleaseVersion = manifest.Launcher?.ReleaseVersion ?? launcherVersion;
        var launcher = StableVersion(launcherVersion);
        _ = StableVersion(launcherReleaseVersion);
        if (launcher.CompareTo(minimum) < 0)
            throw new InvalidDataException("The advertised launcher cannot satisfy the manifest's minimum launcher version.");
        var launcherDownload = manifest.Launcher?.DownloadUrl ?? "";
        if (manifest.Launcher is not null && !IsLauncherDownloadUrl(launcherDownload, repository, launcherVersion, launcherReleaseVersion))
            throw new InvalidDataException("Invalid launcher package URL or version.");
        if (manifest.Mods == null || manifest.Mods.Count > Catalog.Bundled.Count
            || manifest.Mods.Select(mod => mod?.Id).Distinct(StringComparer.Ordinal).Count() != manifest.Mods.Count)
            throw new InvalidDataException("Manifest contains invalid or duplicate mods.");
        var updates = new List<ModUpdate>();
        var blocked = new List<ModUpdate>();
        foreach (var mod in manifest.Mods)
        {
            ValidateMod(mod, repository);
            var effectiveMinimum = string.IsNullOrEmpty(mod.MinLauncherVersion) ? manifest.MinLauncherVersion : mod.MinLauncherVersion;
            var modMinimum = StableVersion(effectiveMinimum);
            // Older launchers know only the global minimum. It must remain a conservative
            // ceiling so they cannot install a DLL requiring a newer launcher by mistake.
            if (modMinimum.CompareTo(minimum) > 0)
                throw new InvalidDataException("A mod minimum exceeds the legacy global launcher minimum.");
            var localVersion = installedVersions != null && installedVersions.TryGetValue(mod.Id, out var known)
                ? SemanticVersion.Parse(known) : SemanticVersion.Parse(Catalog.ById(mod.Id)!.Version);
            if (SemanticVersion.Parse(mod.Version).CompareTo(localVersion) <= 0) continue;
            var normalized = mod with { MinLauncherVersion = effectiveMinimum };
            (modMinimum.CompareTo(current) > 0 ? blocked : updates).Add(normalized);
        }
        return new UpdateCheck
        {
            Repository = repository, Version = manifest.Version, ReleaseUrl = releaseUrl,
            LauncherVersion = launcherVersion, LauncherReleaseVersion = launcherReleaseVersion, LauncherDownloadUrl = launcherDownload,
            HasLauncherUpdate = launcher.CompareTo(current) > 0,
            RequiresLauncherUpdate = blocked.Count > 0,
            Mods = updates.AsReadOnly(), BlockedMods = blocked.AsReadOnly()
        };
    }

    /// <summary>Downloads all selected updates in memory and verifies hashes before returning any installable files.</summary>
    public async Task<IReadOnlyDictionary<string, byte[]>> DownloadAsync(UpdateCheck check, CancellationToken cancellationToken = default)
    {
        ValidateRepository(check.Repository);
        if (check.Mods == null || check.Mods.Count > Catalog.Bundled.Count) throw new InvalidDataException("Too many or invalid mod updates.");
        // Validate the complete selection before requesting bytes. Mixed feeds may contain
        // blocked updates elsewhere, but compatible mods remain independently installable.
        var selectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in check.Mods)
        {
            ValidateMod(mod, check.Repository);
            if (string.IsNullOrEmpty(mod.MinLauncherVersion)
                || StableVersion(mod.MinLauncherVersion).CompareTo(SemanticVersion.Parse(Catalog.LauncherVersion)) > 0)
                throw new InvalidOperationException("Install the required launcher version before updating this mod.");
            if (!selectedFiles.Add(mod.File)) throw new InvalidDataException("Duplicate mod update.");
        }
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in check.Mods)
        {
            var bytes = await GetBytesAsync(new Uri(mod.DownloadUrl), MaximumModBytes, false, cancellationToken);
            var expected = Convert.FromHexString(mod.Sha256);
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), expected))
                throw new InvalidDataException($"SHA-256 mismatch for {mod.File}; no update was installed.");
            result.Add(mod.File, bytes);
        }
        return result;
    }

    private static void ValidateRepository(string repository)
    {
        if (!IsValidRepository(repository)) throw new ArgumentException("Specify a GitHub source as owner/repository.", nameof(repository));
    }

    private static void ValidateMod(ModUpdate? mod, string repository)
    {
        var bundled = mod == null ? null : Catalog.ById(mod.Id);
        if (mod == null || bundled == null || mod.File != bundled.File || !Catalog.IsSafeModFileName(mod.File)
            || !SemanticVersion.TryParse(mod.Version, out var version) || version!.IsPrerelease || mod.MinLauncherVersion is null
            || mod.MinLauncherVersion.Length > 0 && (!SemanticVersion.TryParse(mod.MinLauncherVersion, out var minimum) || minimum!.IsPrerelease)
            || mod.Sha256 == null || mod.Sha256.Length != 64
            || !mod.Sha256.All(Uri.IsHexDigit) || !IsReleaseAssetUrl(mod.DownloadUrl, repository))
            throw new InvalidDataException("Manifest contains an unknown mod, unsafe file, invalid version/hash or untrusted download URL.");
    }

    private static SemanticVersion StableVersion(string value)
    {
        var version = SemanticVersion.Parse(value);
        if (version.IsPrerelease) throw new InvalidDataException("A stable version is required.");
        return version;
    }

    public static bool IsLauncherDownloadUrl(string? url, string repository, string version, string? releaseVersion = null)
    {
        releaseVersion ??= version;
        if (!SemanticVersion.TryParse(version, out var parsed) || parsed!.IsPrerelease
            || !SemanticVersion.TryParse(releaseVersion, out var release) || release!.IsPrerelease
            || !IsReleaseAssetUrl(url, repository)) return false;
        var uri = new Uri(url!);
        var parts = uri.AbsolutePath[("/" + repository + "/releases/download/").Length..].Split('/');
        if (parts.Length != 2) return false;
        var tag = Uri.UnescapeDataString(parts[0]);
        if (tag.StartsWith('v')) tag = tag[1..];
        return tag == releaseVersion && Uri.UnescapeDataString(parts[1]) == $"SS14ModLauncher-{version}-win-x64.zip";
    }

    public static bool IsReleaseAssetUrl(string? url, string repository) => IsRepositoryUrl(url, repository, "releases/download/");
    public static bool IsReleasePageUrl(string? url, string repository) => IsRepositoryUrl(url, repository, "releases/tag/");

    private static bool IsRepositoryUrl(string? url, string repository, string suffix)
    {
        if (!IsValidRepository(repository) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || !IsHttps(uri)
            || uri.Host != "github.com" || uri.Query.Length != 0 || uri.Fragment.Length != 0) return false;
        var prefix = "/" + repository + "/" + suffix;
        // Escapes can hide path traversal or separators. Real release URLs use escaped tag names,
        // but ambiguous dots, slashes and backslashes are never accepted in trusted paths.
        var path = uri.AbsolutePath;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && path.Length > prefix.Length
            && !path.Contains("%2f", StringComparison.OrdinalIgnoreCase) && !path.Contains("%5c", StringComparison.OrdinalIgnoreCase)
            && !path.Contains("%2e", StringComparison.OrdinalIgnoreCase) && !url!.Contains('\\');
    }

    private static bool IsHttps(Uri uri) => uri.Scheme == Uri.UriSchemeHttps && uri.Port == 443 && string.IsNullOrEmpty(uri.UserInfo);

    private async Task<byte[]> GetBytesAsync(Uri uri, int maximumBytes, bool api, CancellationToken cancellationToken,
        Action<Uri>? validateRoute = null, Action? validatePayload = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        for (var redirect = 0; redirect < 6; redirect++)
        {
            if (!IsHttps(uri) || (api ? uri.Host != "api.github.com" : uri.Host is not ("github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com")))
                throw new InvalidDataException("Download redirected to an untrusted host.");
            validateRoute?.Invoke(uri);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (api && redirect == 0 && (response.StatusCode == HttpStatusCode.TooManyRequests
                || response.StatusCode == HttpStatusCode.Forbidden && response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining)
                    && remaining.Count() == 1 && remaining.Single().Trim() == "0"))
                throw new GitHubRateLimitException();
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.SeeOther
                or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                var location = response.Headers.Location ?? throw new InvalidDataException("Redirect has no target.");
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                continue;
            }
            response.EnsureSuccessStatusCode();
            validatePayload?.Invoke();
            if (response.Content.Headers.ContentLength > maximumBytes) throw new InvalidDataException("Download exceeds the size limit.");
            await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            while (true)
            {
                var read = await input.ReadAsync(buffer, timeout.Token);
                if (read == 0) break;
                if (output.Length + read > maximumBytes) throw new InvalidDataException("Download exceeds the size limit.");
                await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
            }
            if (output.Length == 0) throw new InvalidDataException("The downloaded file is empty.");
            return output.ToArray();
        }
        throw new InvalidDataException("Too many download redirects.");
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>SemVer 2 ordering, including prerelease identifiers; build metadata has no ordering effect.</summary>
public sealed class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    private static readonly Regex Pattern = new(@"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\z", RegexOptions.CultureInvariant);
    private readonly string[] _numbers;
    private readonly string[] _pre;
    private SemanticVersion(string[] numbers, string[] pre) { _numbers = numbers; _pre = pre; }
    public bool IsPrerelease => _pre.Length != 0;
    public static SemanticVersion Parse(string value) => TryParse(value, out var result) ? result! : throw new InvalidDataException("Invalid semantic version.");
    public static bool TryParse(string? value, out SemanticVersion? result)
    {
        result = null;
        if (value == null || value.Length > 128) return false;
        var match = Pattern.Match(value);
        if (!match.Success) return false;
        var pre = match.Groups[4].Success ? match.Groups[4].Value.Split('.') : [];
        if (pre.Any(part => part.Length > 1 && part[0] == '0' && part.All(char.IsAsciiDigit))) return false;
        result = new SemanticVersion([match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value], pre);
        return true;
    }
    private static int CompareNumber(string a, string b) => a.Length == b.Length ? string.CompareOrdinal(a, b) : a.Length.CompareTo(b.Length);
    public int CompareTo(SemanticVersion? other)
    {
        if (other == null) return 1;
        for (var index = 0; index < 3; index++) { var comparison = CompareNumber(_numbers[index], other._numbers[index]); if (comparison != 0) return comparison; }
        if (_pre.Length == 0 || other._pre.Length == 0) return (_pre.Length == 0 ? 1 : 0).CompareTo(other._pre.Length == 0 ? 1 : 0);
        for (var index = 0; index < Math.Min(_pre.Length, other._pre.Length); index++)
        {
            var a = _pre[index]; var b = other._pre[index];
            var numberA = a.All(char.IsAsciiDigit); var numberB = b.All(char.IsAsciiDigit);
            var comparison = numberA && numberB ? CompareNumber(a, b) : numberA != numberB ? (numberA ? -1 : 1) : string.CompareOrdinal(a, b);
            if (comparison != 0) return comparison;
        }
        return _pre.Length.CompareTo(other._pre.Length);
    }
    public bool Equals(SemanticVersion? other) => other != null && CompareTo(other) == 0;
    public override bool Equals(object? other) => other is SemanticVersion version && Equals(version);
    public override int GetHashCode() => string.Join('.', _numbers).GetHashCode(StringComparison.Ordinal) ^ string.Join('.', _pre).GetHashCode(StringComparison.Ordinal);
}
