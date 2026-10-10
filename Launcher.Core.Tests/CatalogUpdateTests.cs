using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SS14ModLauncher.Core;

internal static class CatalogUpdateTests
{
    // Fake release fixtures must not depend on the versions currently bundled by the product.
    private static readonly IReadOnlyDictionary<string, string> BaselineVersions = new Dictionary<string, string>
    {
        ["crew-console"] = "0.0.0"
    };

    public static async Task RunAsync()
    {
        BootstrapSelectionTests.Run();
        Assert(Catalog.Bundled.Count == 6 && Catalog.ById("crew-console")?.File == "CrewConsole.Mod.dll"
            && Catalog.ById("conversations")?.File == "Conversations.Mod.dll"
            && Catalog.ById("death-rattle")?.File == "DeathRattle.Mod.dll"
            && Catalog.ById("death-rattle")?.MinLauncherVersion == "0.1.6"
            && Catalog.ById("chem-master")?.File == "ChemMaster.Mod.dll"
            && Catalog.ById("chem-master")?.MinLauncherVersion == "0.1.6"
            && Catalog.ById("debug-vision")?.File == "DebugVision.Mod.dll"
            && Catalog.ById("debug-vision")?.MinLauncherVersion == "0.1.8", "embedded catalog");
        foreach (var file in new[] { "../Evil.Mod.dll", "C:\\Evil.Mod.dll", "a/evil.Mod.dll", "foo.dll", ".hidden.Mod.dll", "x..Mod.dll" })
            Assert(!Catalog.IsSafeModFileName(file), "reject unsafe mod filename " + file);
        var versions = new[] { "0.1.0-alpha", "0.1.0-alpha.2", "0.1.0-alpha.10", "0.1.0-beta", "0.1.0", "0.2.0", "1.0.0" };
        for (var i = 1; i < versions.Length; i++) Assert(SemanticVersion.Parse(versions[i - 1]).CompareTo(SemanticVersion.Parse(versions[i])) < 0, "SemVer ordering");
        Assert(SemanticVersion.Parse("1.2.3+one").Equals(SemanticVersion.Parse("1.2.3+two")), "build metadata ignored");
        foreach (var version in new[] { "1.0", "01.0.0", "1.0.0-01", "1.0.0-beta..a", "1.0.0 trailing" }) Assert(!SemanticVersion.TryParse(version, out _), "invalid SemVer");
        Assert(UpdateService.IsValidRepository("actemendes/ss14-mod-launcher"), "repository");
        foreach (var repository in new[] { "", "https://github.com/a/b", "a/b/c", "a/../b", "a/b?x=y", "a/b\n" }) Assert(!UpdateService.IsValidRepository(repository), "reject repository");
        const string repo = "owner/project";
        const string page = "https://github.com/owner/project/releases/tag/v0.2.0";
        const string download = "https://github.com/owner/project/releases/download/v0.2.0/CrewConsole.Mod.dll";
        Assert(UpdateService.IsReleaseAssetUrl(download, repo), "trusted asset");
        foreach (var url in new[] { "http://github.com/owner/project/releases/download/x/y", "https://github.com.evil.test/owner/project/releases/download/x/y", "https://github.com/other/project/releases/download/x/y", "https://user:pass@github.com/owner/project/releases/download/x/y", "https://github.com:444/owner/project/releases/download/x/y", "https://github.com/owner/project/releases/download/x/%2e%2e%2fy", "https://github.com/owner/project/releases/download/x/y?download=evil" })
            Assert(!UpdateService.IsReleaseAssetUrl(url, repo), "reject untrusted asset");

        var bytes = Encoding.UTF8.GetBytes("verified mod bytes");
        var mod = new ModUpdate { Id = "crew-console", File = "CrewConsole.Mod.dll", Version = "0.2.0", Sha256 = Convert.ToHexString(SHA256.HashData(bytes)), DownloadUrl = download };
        byte[] Manifest(ModUpdate value, string minimum = "0.1.0") => JsonSerializer.SerializeToUtf8Bytes(new { version = "0.2.0", minLauncherVersion = minimum, mods = new[] { value } });
        var check = UpdateService.ParseManifest(repo, "0.2.0", page, Manifest(mod), BaselineVersions);
        Assert(check.Mods.Count == 1 && check.HasLauncherUpdate && !check.RequiresLauncherUpdate, "new release");
        Assert(UpdateService.ParseManifest(repo, "0.2.0", page, Manifest(mod), new Dictionary<string, string> { [mod.Id] = "0.2.0" }).Mods.Count == 0, "installed version suppresses repeat");
        Assert(UpdateService.ParseManifest(repo, "0.2.0", page, Manifest(mod), new Dictionary<string, string> { [mod.Id] = "0.3.0" }).Mods.Count == 0, "no downgrade");
        Assert(UpdateService.ParseManifest(repo, "0.2.0", page, Manifest(mod, "0.2.0"), BaselineVersions).RequiresLauncherUpdate, "minimum launcher");
        Throws(() => UpdateService.ParseManifest(repo, "0.2.0", page, Manifest(mod with { Id = "unknown" }), BaselineVersions), "unknown mod");
        Throws(() => UpdateService.ParseManifest(repo, "0.2.0", page, Manifest(mod with { File = "../Evil.Mod.dll" }), BaselineVersions), "manifest filename");
        Throws(() => UpdateService.ParseManifest(repo, "0.2.0", page, Manifest(mod with { Sha256 = "00" }), BaselineVersions), "hash syntax");
        Throws(() => UpdateService.ParseManifest(repo, "0.3.0", page, Manifest(mod), BaselineVersions), "release version mismatch");

        using (var updater = new UpdateService(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) })))
        {
            var result = await updater.DownloadAsync(check);
            Assert(result[mod.File].SequenceEqual(bytes), "verified download");
            await ThrowsAsync(() => updater.DownloadAsync(check with { Mods = [check.Mods[0] with { Sha256 = new string('0', 64) }] }), "hash mismatch");
            await ThrowsAsync(() => updater.DownloadAsync(check with { Mods = [check.Mods[0] with { MinLauncherVersion = "99.0.0" }] }), "per-mod minimum blocks download");
        }
        using (var updater = new UpdateService(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://evil.test/payload.dll") } })))
            await ThrowsAsync(() => updater.DownloadAsync(check), "untrusted redirect");
        using (var updater = new UpdateService(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new AdvertisedLargeContent() })))
            await ThrowsAsync(() => updater.DownloadAsync(check), "oversized download");
        using (var updater = new UpdateService(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new EndlessStream()) })))
            await ThrowsAsync(() => updater.DownloadAsync(check), "unknown-length stream size limit");
        using (var updater = new UpdateService(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri(download) } })))
            await ThrowsAsync(() => updater.DownloadAsync(check), "redirect loop limit");

        await ReleaseApiPipeline(repo, page, mod, bytes, Manifest(mod));
        await RateLimitFallbackTests.RunAsync();
        await IndependentUpdateTests.RunAsync();

        var folder = Path.Combine(Path.GetTempPath(), "SS14ModLauncherSettingsTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "settings.json");
            var settings = AppSettings.Load(path);
            Assert(settings.SelectedModIds.SequenceEqual(new[] { "crew-console" }), "safe default selection");
            settings.AddProfile("Clean", []); settings.Save();
            Assert(AppSettings.Load(path).ActiveProfile == "Clean" && AppSettings.Load(path).SelectedModIds.Count == 0, "profile persisted");
            settings.RecordInstalledVersions(folder, [mod]); settings.Save();
            Assert(AppSettings.Load(path).VersionsFor(folder)[mod.Id] == "0.2.0", "installed version persisted");
            File.WriteAllText(path, "{broken json");
            var broken = AppSettings.Load(path);
            Assert(broken.IsReadOnly && broken.LoadError != null, "corrupt settings reported");
            Throws(() => broken.Save(), "corrupt settings not overwritten");
            Assert(File.ReadAllText(path) == "{broken json", "corrupt original preserved");
            var backup = broken.Recover();
            Assert(backup != null && File.ReadAllText(backup) == "{broken json" && !AppSettings.Load(path).IsReadOnly, "explicit backup recovery");
        }
        finally { Directory.Delete(folder, recursive: true); }
        Console.WriteLine("Catalog, profiles, semantic versions and bounded update validation passed.");
    }

    private static async Task ReleaseApiPipeline(string repository, string releasePage, ModUpdate mod, byte[] modBytes, byte[] manifestBytes)
    {
        var manifestUrl = $"https://github.com/{repository}/releases/download/v0.2.0/mods-manifest.json";
        var cdnManifest = "https://release-assets.githubusercontent.com/fixture/manifest?signature=fixture";
        var cdnMod = "https://release-assets.githubusercontent.com/fixture/mod?signature=fixture";
        var release = new
        {
            draft = false, prerelease = false, tag_name = "v0.2.0", html_url = releasePage,
            assets = new[] { new { name = "mods-manifest.json", size = manifestBytes.Length, browser_download_url = manifestUrl } }
        };
        HttpResponseMessage Content(byte[] value) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(value) };
        HttpResponseMessage Redirect(string url) => new(HttpStatusCode.Redirect) { Headers = { Location = new Uri(url) } };
        var requests = new List<string>();
        using (var updater = new UpdateService(new StubHandler(request =>
        {
            var url = request.RequestUri!.AbsoluteUri;
            requests.Add(url);
            return url switch
            {
                var value when value == $"https://api.github.com/repos/{repository}/releases/latest" => Content(JsonSerializer.SerializeToUtf8Bytes(release)),
                var value when value == manifestUrl => Redirect(cdnManifest),
                var value when value == cdnManifest => Content(manifestBytes),
                var value when value == mod.DownloadUrl => Redirect(cdnMod),
                var value when value == cdnMod => Content(modBytes),
                _ => throw new Exception("Unexpected HTTP request: " + url)
            };
        })))
        {
            var check = await updater.CheckAsync(repository, BaselineVersions);
            Assert(check.Mods.Count == 1 && check.Version == "0.2.0", "release API parses stable public manifest");
            var downloaded = await updater.DownloadAsync(check);
            Assert(downloaded[mod.File].AsSpan().SequenceEqual(modBytes), "release API through trusted CDN to verified DLL");
            Assert(requests.Count == 5, "release pipeline followed both expected CDN redirects");
        }

        foreach (var invalid in new object[]
        {
            new { draft = true, prerelease = false, tag_name = "v0.2.0", html_url = releasePage, assets = release.assets },
            new { draft = false, prerelease = true, tag_name = "v0.2.0", html_url = releasePage, assets = release.assets },
            new { draft = false, prerelease = false, tag_name = "v0.2.0-beta", html_url = releasePage, assets = release.assets },
            new { draft = false, prerelease = false, tag_name = "v0.2.0", html_url = "https://github.com/other/project/releases/tag/v0.2.0", assets = release.assets },
            new { draft = false, prerelease = false, tag_name = "v0.2.0", html_url = releasePage, assets = Array.Empty<object>() },
            new { draft = false, prerelease = false, tag_name = "v0.2.0", html_url = releasePage, assets = new[] { release.assets[0], release.assets[0] } },
            new { draft = false, prerelease = false, tag_name = "v0.2.0", html_url = releasePage, assets = new[] { new { name = "mods-manifest.json", size = 1, browser_download_url = "https://evil.test/mods-manifest.json" } } }
        })
        {
            var count = 0;
            using var updater = new UpdateService(new StubHandler(_ => { count++; return Content(JsonSerializer.SerializeToUtf8Bytes(invalid)); }));
            await ThrowsAsync(() => updater.CheckAsync(repository, BaselineVersions), "invalid release API metadata");
            Assert(count == 1, "invalid release rejected before manifest request");
        }
        Console.WriteLine("Public release API pipeline, trusted redirects and invalid metadata rejection passed offline.");
    }

    private static void Assert(bool condition, string name) { if (!condition) throw new Exception("FAILED: " + name); }
    private static void Throws(Action action, string name) { try { action(); } catch { return; } throw new Exception("FAILED: expected error: " + name); }
    private static async Task ThrowsAsync(Func<Task> action, string name) { try { await action(); } catch { return; } throw new Exception("FAILED: expected error: " + name); }
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }
    private sealed class AdvertisedLargeContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => Task.CompletedTask;
        protected override bool TryComputeLength(out long length) { length = UpdateService.MaximumModBytes + 1; return true; }
    }
    private sealed class EndlessStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) { Array.Fill(buffer, (byte)1, offset, count); return count; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); buffer.Span.Fill(1); return ValueTask.FromResult(buffer.Length); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
