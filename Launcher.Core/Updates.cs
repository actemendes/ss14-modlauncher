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
    public string Sha256 { get; init; } = "";
    public string DownloadUrl { get; init; } = "";
}

public sealed record UpdateCheck
{
    public string Repository { get; init; } = "";
    public string Version { get; init; } = "";
    public string ReleaseUrl { get; init; } = "";
    public bool HasLauncherUpdate { get; init; }
    public bool RequiresLauncherUpdate { get; init; }
    public IReadOnlyList<ModUpdate> Mods { get; init; } = Array.Empty<ModUpdate>();
}

/// <summary>Explicit, bounded GitHub release checks. No network activity occurs before CheckAsync.</summary>
public sealed class UpdateService : IDisposable
{
    public const int MaximumModBytes = 32 * 1024 * 1024;
    public const int MaximumManifestBytes = 1024 * 1024;
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Regex RepositoryPattern = new(@"\A[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})/[A-Za-z0-9_.-]{1,100}\z", RegexOptions.CultureInvariant);
    private sealed record Manifest
    {
        public string Version { get; init; } = "";
        public string MinLauncherVersion { get; init; } = "";
        public List<ModUpdate> Mods { get; init; } = [];
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
        var releaseBytes = await GetBytesAsync(new Uri($"https://api.github.com/repos/{repository}/releases/latest"), MaximumManifestBytes, true, cancellationToken);
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
        var minimum = SemanticVersion.Parse(manifest.MinLauncherVersion);
        if (manifest.Mods == null || manifest.Mods.Count > Catalog.Bundled.Count
            || manifest.Mods.Select(mod => mod?.Id).Distinct(StringComparer.Ordinal).Count() != manifest.Mods.Count)
            throw new InvalidDataException("Manifest contains invalid or duplicate mods.");
        var updates = new List<ModUpdate>();
        foreach (var mod in manifest.Mods)
        {
            ValidateMod(mod, repository);
            var localVersion = installedVersions != null && installedVersions.TryGetValue(mod.Id, out var known)
                ? SemanticVersion.Parse(known) : SemanticVersion.Parse(Catalog.ById(mod.Id)!.Version);
            if (SemanticVersion.Parse(mod.Version).CompareTo(localVersion) > 0) updates.Add(mod);
        }
        return new UpdateCheck
        {
            Repository = repository, Version = manifest.Version, ReleaseUrl = releaseUrl,
            HasLauncherUpdate = latest.CompareTo(current) > 0,
            RequiresLauncherUpdate = minimum.CompareTo(current) > 0,
            Mods = updates.AsReadOnly()
        };
    }

    /// <summary>Downloads all selected updates in memory and verifies hashes before returning any installable files.</summary>
    public async Task<IReadOnlyDictionary<string, byte[]>> DownloadAsync(UpdateCheck check, CancellationToken cancellationToken = default)
    {
        ValidateRepository(check.Repository);
        if (check.RequiresLauncherUpdate) throw new InvalidOperationException("Install the newer launcher release before updating mods.");
        if (check.Mods.Count > Catalog.Bundled.Count) throw new InvalidDataException("Too many mod updates.");
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in check.Mods)
        {
            ValidateMod(mod, check.Repository);
            if (result.ContainsKey(mod.File)) throw new InvalidDataException("Duplicate mod update.");
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
            || !SemanticVersion.TryParse(mod.Version, out _) || mod.Sha256 == null || mod.Sha256.Length != 64
            || !mod.Sha256.All(Uri.IsHexDigit) || !IsReleaseAssetUrl(mod.DownloadUrl, repository))
            throw new InvalidDataException("Manifest contains an unknown mod, unsafe file, invalid version/hash or untrusted download URL.");
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

    private async Task<byte[]> GetBytesAsync(Uri uri, int maximumBytes, bool api, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        for (var redirect = 0; redirect < 6; redirect++)
        {
            if (!IsHttps(uri) || (api ? uri.Host != "api.github.com" : uri.Host is not ("github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com")))
                throw new InvalidDataException("Download redirected to an untrusted host.");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.SeeOther
                or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                var location = response.Headers.Location ?? throw new InvalidDataException("Redirect has no target.");
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                continue;
            }
            response.EnsureSuccessStatusCode();
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
