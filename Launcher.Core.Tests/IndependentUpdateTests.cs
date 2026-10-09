using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SS14ModLauncher.Core;

internal static class IndependentUpdateTests
{
    private const string Repository = "owner/project";
    private const string Feed = "2.0.0";
    private const string Page = "https://github.com/owner/project/releases/tag/v2.0.0";
    private static readonly byte[] ModBytes = Encoding.UTF8.GetBytes("independently versioned compatible mod");

    public static async Task RunAsync()
    {
        var current = Catalog.LauncherVersion;
        var crew = Mod("crew-console", "CrewConsole.Mod.dll", current);
        var hello = Mod("hello-world", "HelloWorld.Mod.dll", "2.0.0");

        var modOnly = Parse(Manifest(current, current, [crew]));
        Assert(modOnly.Version == Feed && modOnly.LauncherVersion == current, "feed and launcher versions are independent");
        Assert(!modOnly.HasLauncherUpdate && !modOnly.RequiresLauncherUpdate && modOnly.Mods.Count == 1 && modOnly.BlockedMods.Count == 0,
            "mod-only feed does not announce a new launcher");
        Assert(modOnly.LauncherDownloadUrl == LauncherUrl(current), "mod-only feed can link previous launcher package");
        Assert(modOnly.LauncherReleaseVersion == current, "omitted launcher release tag inherits its version");

        var separateRelease = UpdateService.ParseManifest(Repository, "1.5.0", ReleasePage("1.5.0"),
            Manifest("0.1.0", "0.2.0", [], launcherReleaseVersion: "1.5.0", feedVersion: "1.5.0"));
        Assert(separateRelease.LauncherVersion == "0.2.0" && separateRelease.LauncherReleaseVersion == "1.5.0"
            && separateRelease.LauncherDownloadUrl == LauncherUrl("0.2.0", "1.5.0") && separateRelease.HasLauncherUpdate,
            "full feed tag is independent of the launcher package version");
        var nextFeed = UpdateService.ParseManifest(Repository, "1.5.1", ReleasePage("1.5.1"),
            Manifest(current, current, [crew], launcherReleaseVersion: "1.5.0", feedVersion: "1.5.1"));
        Assert(nextFeed.Version == "1.5.1" && nextFeed.LauncherReleaseVersion == "1.5.0"
            && nextFeed.LauncherDownloadUrl == LauncherUrl(current, "1.5.0") && !nextFeed.HasLauncherUpdate && nextFeed.Mods.Count == 1,
            "subsequent mod-only feed retains the actual prior launcher release without a false launcher update");
        var independentOrder = UpdateService.ParseManifest(Repository, "1.5.0", ReleasePage("1.5.0"),
            Manifest("0.1.0", "3.0.0", [], launcherReleaseVersion: "1.5.0", feedVersion: "1.5.0"));
        Assert(independentOrder.LauncherVersion == "3.0.0", "feed and launcher versions need no semantic ordering relationship");

        var launcherOnly = Parse(Manifest("0.1.0", "2.0.0", []));
        Assert(launcherOnly.HasLauncherUpdate && launcherOnly.LauncherVersion == "2.0.0"
            && launcherOnly.Mods.Count == 0 && launcherOnly.BlockedMods.Count == 0, "launcher-only release does not invent mod updates");
        using (var handler = new Handler(_ => throw new Exception("Launcher-only check must not download a mod.")))
        using (var updater = new UpdateService(handler))
        {
            Assert((await updater.DownloadAsync(launcherOnly)).Count == 0 && handler.Requests == 0, "launcher-only download is empty");
        }

        var mixed = Parse(Manifest("2.0.0", "2.0.0", [crew, hello]));
        Assert(mixed.HasLauncherUpdate && mixed.RequiresLauncherUpdate && mixed.Mods.Single().Id == crew.Id
            && mixed.BlockedMods.Single().Id == hello.Id, "mixed feed separates compatible and blocked mods");
        Assert(mixed.Mods[0].MinLauncherVersion == current && mixed.BlockedMods[0].MinLauncherVersion == "2.0.0", "effective mod requirements preserved");
        using (var handler = new Handler(request =>
        {
            Assert(request.RequestUri!.AbsoluteUri == crew.DownloadUrl, "only compatible mod is requested");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(ModBytes) };
        }))
        using (var updater = new UpdateService(handler))
        {
            var downloaded = await updater.DownloadAsync(mixed);
            Assert(downloaded.Count == 1 && downloaded[crew.File].AsSpan().SequenceEqual(ModBytes) && handler.Requests == 1,
                "compatible update installs despite another mod requiring newer launcher");
            await RejectAsync(() => updater.DownloadAsync(mixed with { Mods = mixed.BlockedMods, RequiresLauncherUpdate = false }),
                "callers cannot bypass a blocked mod requirement");
            await RejectAsync(() => updater.DownloadAsync(mixed with { Mods = [mixed.Mods[0], mixed.BlockedMods[0]] }),
                "entire selection is preflighted before any download");
            Assert(handler.Requests == 1, "blocked or mixed injection did not request any bytes");
        }

        var skipped = Parse(Manifest("2.0.0", "2.0.0", [crew, hello]), new Dictionary<string, string>
        {
            [crew.Id] = "3.0.0", [hello.Id] = "2.0.0"
        });
        Assert(skipped.Mods.Count == 0 && skipped.BlockedMods.Count == 0 && !skipped.RequiresLauncherUpdate,
            "current or newer installed mods are not offered or falsely blocked");

        var inherited = Parse(Manifest(current, current, [crew with { MinLauncherVersion = "" }]));
        Assert(inherited.Mods.Single().MinLauncherVersion == current, "omitted per-mod minimum inherits global");
        var legacy = Parse(JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = Feed, minLauncherVersion = current,
            mods = new[] { crew with { MinLauncherVersion = "" } }
        }));
        Assert(legacy.HasLauncherUpdate && legacy.LauncherVersion == Feed && legacy.LauncherDownloadUrl == ""
            && legacy.LauncherReleaseVersion == Feed && legacy.Mods.Single().MinLauncherVersion == current, "legacy manifest keeps launcher inference and mod compatibility");
        var legacyBlocked = Parse(JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = Feed, minLauncherVersion = "2.0.0",
            mods = new[] { crew with { MinLauncherVersion = "" } }
        }));
        Assert(legacyBlocked.RequiresLauncherUpdate && legacyBlocked.Mods.Count == 0 && legacyBlocked.BlockedMods.Count == 1,
            "legacy global minimum remains enforced");

        foreach (var url in new[]
        {
            LauncherUrl(current).Replace("https:", "http:"),
            LauncherUrl(current).Replace("owner/project", "other/project"),
            LauncherUrl(current).Replace("github.com", "github.com.evil.test"),
            LauncherUrl(current) + "?download=1",
            LauncherUrl(current) + "#part",
            LauncherUrl(current).Replace("https://", "https://name:password@"),
            LauncherUrl(current).Replace("-win-x64.zip", ".exe"),
            LauncherUrl(current).Replace("-win-x64.zip", "-win-x64.zip/extra"),
            LauncherUrl(current).Replace("/download/v" + current + "/", "/download/v9.0.0/"),
            LauncherUrl(current).Replace("/download/v" + current + "/", "/latest/download/"),
            LauncherUrl(current).Replace("SS14ModLauncher-", "OtherLauncher-")
        })
        {
            Assert(!UpdateService.IsLauncherDownloadUrl(url, Repository, current), "reject unrelated launcher URL");
            Reject(() => Parse(Manifest(current, current, [crew], url)), "manifest rejects unrelated launcher URL");
        }
        foreach (var version in new[] { "", "1.0", "01.0.0", "1.0.0-beta" })
        {
            Reject(() => Parse(Manifest(current, version, [crew])), "invalid launcher version");
            Reject(() => Parse(Manifest(current, current, [crew], launcherReleaseVersion: version)), "invalid launcher release version");
            Assert(!UpdateService.IsLauncherDownloadUrl(LauncherUrl(current), Repository, current, version), "invalid release tag is rejected independently");
            Reject(() => Parse(Manifest(version, current, [crew])), "invalid global minimum");
            if (version.Length > 0) Reject(() => Parse(Manifest(current, current, [crew with { MinLauncherVersion = version }])), "invalid per-mod minimum");
        }
        Reject(() => Parse(Manifest(current, current, [crew with { Version = "2.0.0-beta" }])), "prerelease mod in stable feed");
        Reject(() => Parse(Manifest(current, "2.0.0", [hello])), "per-mod minimum cannot exceed legacy safety ceiling");
        Reject(() => Parse(Manifest("2.0.0", current, [crew])), "advertised launcher must satisfy manifest minimum");
        Reject(() => Parse(JsonSerializer.SerializeToUtf8Bytes(new { version = Feed, minLauncherVersion = current, launcher = new { }, mods = new[] { crew } })),
            "incomplete explicit launcher metadata is not treated as legacy");
        Reject(() => Parse(Manifest(current, current, [crew, crew])), "duplicate mod IDs");
        Assert(UpdateService.IsLauncherDownloadUrl(LauncherUrl(current, "1.5.0"), Repository, current, "1.5.0"), "explicit independent release route is valid");
        Reject(() => Parse(Manifest(current, current, [crew], LauncherUrl(current, "1.5.0"))), "different package tag requires explicit release metadata");
        Reject(() => Parse(Manifest(current, current, [crew], LauncherUrl(current, "1.5.1"), "1.5.0")), "URL tag must equal explicit launcher release version");
        Reject(() => Parse(Manifest(current, current, [crew], LauncherUrl("9.0.0", "1.5.0"), "1.5.0")), "explicit release tag cannot bypass launcher filename version");
        var publishedElsewhere = Parse(Manifest(current, current, [crew], launcherReleaseVersion: "1.5.0"));
        await ApiFlow(publishedElsewhere, Manifest(current, current, [crew], launcherReleaseVersion: "1.5.0"));
        await FallbackFlow(publishedElsewhere, Manifest(current, current, [crew], launcherReleaseVersion: "1.5.0"));
        Console.WriteLine("Independent launcher/mod versions, mixed compatibility, legacy safety and package URL validation passed.");
    }

    private static async Task ApiFlow(UpdateCheck expected, byte[] manifest)
    {
        var manifestUrl = $"https://github.com/{Repository}/releases/download/v{Feed}/mods-manifest.json";
        var metadata = JsonSerializer.SerializeToUtf8Bytes(new
        {
            tag_name = "v" + Feed, draft = false, prerelease = false, html_url = Page,
            assets = new[] { new { name = "mods-manifest.json", size = manifest.Length, browser_download_url = manifestUrl } }
        });
        using var handler = new Handler(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(request.RequestUri!.Host == "api.github.com" ? metadata : manifest)
        });
        using var updater = new UpdateService(handler);
        var actual = await updater.CheckAsync(Repository);
        Assert(actual.LauncherVersion == expected.LauncherVersion && actual.LauncherReleaseVersion == expected.LauncherReleaseVersion
            && actual.LauncherDownloadUrl == expected.LauncherDownloadUrl && !actual.HasLauncherUpdate && actual.Mods.Count == 1 && handler.Requests == 2,
            "public API pipeline retains independent metadata");
    }

    private static async Task FallbackFlow(UpdateCheck expected, byte[] manifest)
    {
        var latest = $"https://github.com/{Repository}/releases/latest/download/mods-manifest.json";
        var canonical = $"https://github.com/{Repository}/releases/download/v{Feed}/mods-manifest.json";
        using var handler = new Handler(request =>
        {
            var url = request.RequestUri!.AbsoluteUri;
            if (request.RequestUri.Host == "api.github.com") return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            if (url == latest) return new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri(canonical) } };
            if (url == canonical) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(manifest) };
            throw new Exception("Unexpected fallback request.");
        });
        using var updater = new UpdateService(handler);
        var actual = await updater.CheckAsync(Repository);
        Assert(actual.LauncherVersion == expected.LauncherVersion && actual.LauncherReleaseVersion == expected.LauncherReleaseVersion
            && actual.LauncherDownloadUrl == expected.LauncherDownloadUrl && !actual.HasLauncherUpdate && actual.Mods.Count == 1 && handler.Requests == 3,
            "rate-limit fallback retains independent metadata and feed tag identity");
    }

    private static ModUpdate Mod(string id, string file, string minimum) => new()
    {
        Id = id, File = file, Version = "2.0.0", MinLauncherVersion = minimum,
        Sha256 = Convert.ToHexString(SHA256.HashData(ModBytes)),
        DownloadUrl = $"https://github.com/{Repository}/releases/download/v{Feed}/{file}"
    };
    private static string ReleasePage(string version) => $"https://github.com/{Repository}/releases/tag/v{version}";
    private static string LauncherUrl(string version, string? releaseVersion = null) => $"https://github.com/{Repository}/releases/download/v{releaseVersion ?? version}/SS14ModLauncher-{version}-win-x64.zip";
    private static byte[] Manifest(string minimum, string launcherVersion, ModUpdate[] mods, string? launcherUrl = null,
        string? launcherReleaseVersion = null, string feedVersion = Feed) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        version = feedVersion, minLauncherVersion = minimum, launcher = new { version = launcherVersion, releaseVersion = launcherReleaseVersion,
            downloadUrl = launcherUrl ?? LauncherUrl(launcherVersion, launcherReleaseVersion) }, mods
    });
    private static UpdateCheck Parse(byte[] bytes, IReadOnlyDictionary<string, string>? installed = null) => UpdateService.ParseManifest(Repository, Feed, Page, bytes, installed);
    private static void Assert(bool value, string name) { if (!value) throw new Exception("FAILED: " + name); }
    private static void Reject(Action action, string name)
    {
        try { action(); } catch (Exception error) when (error is InvalidDataException or JsonException) { return; }
        throw new Exception("FAILED: expected metadata rejection: " + name);
    }
    private static async Task RejectAsync(Func<Task> action, string name)
    {
        try { await action(); } catch (Exception error) when (error is InvalidDataException or InvalidOperationException) { return; }
        throw new Exception("FAILED: expected download rejection: " + name);
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(response(request));
        }
    }
}
