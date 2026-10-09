using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SS14ModLauncher.Core;

internal static class RateLimitFallbackTests
{
    private const string Repository = "owner/project";
    private const string Api = "https://api.github.com/repos/owner/project/releases/latest";
    private const string Latest = "https://github.com/owner/project/releases/latest/download/mods-manifest.json";
    private const string Canonical = "https://github.com/owner/project/releases/download/v0.2.0/mods-manifest.json";
    private const string Cdn = "https://release-assets.githubusercontent.com/fixture/manifest?signature=test";
    private const string ModUrl = "https://github.com/owner/project/releases/download/v0.2.0/CrewConsole.Mod.dll";
    private const string ModCdn = "https://objects.githubusercontent.com/fixture/mod?signature=test";
    private static readonly byte[] ModBytes = Encoding.UTF8.GetBytes("verified rate-limit fallback mod");

    public static async Task RunAsync()
    {
        foreach (var status in new[] { HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests })
        {
            using var handler = new Handler((url, _) => url == Api ? Limited(status) : StandardResponse(url));
            using var updater = new UpdateService(handler);
            var check = await updater.CheckAsync(Repository);
            Assert(check.Version == "0.2.0" && check.ReleaseUrl == "https://github.com/owner/project/releases/tag/v0.2.0", "fallback release identity");
            Assert(check.Mods.Count == 1, "fallback selects newer mod");
            var downloaded = await updater.DownloadAsync(check);
            Assert(downloaded["CrewConsole.Mod.dll"].AsSpan().SequenceEqual(ModBytes), "fallback mod download SHA-256");
            Assert(handler.Requests.Count == 6, "fallback uses one API request and bounded public asset flow");
            var current = await updater.CheckAsync(Repository, new Dictionary<string, string> { ["crew-console"] = "0.2.0" });
            Assert(current.Mods.Count == 0, "fallback retains installed version filtering");
        }

        // Only a primary API response that explicitly signals rate limiting may use the fallback.
        foreach (var status in new[] { HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized, HttpStatusCode.InternalServerError })
        {
            await RejectAsync((_, _) => new HttpResponseMessage(status) { Content = new StringContent("API rate limit exceeded") },
                1, typeof(HttpRequestException), "ordinary error body does not trigger fallback");
        }
        await RejectAsync((_, _) => Limited(HttpStatusCode.Forbidden, "12"), 1, typeof(HttpRequestException), "403 with remaining quota");
        await RejectAsync((_, _) => Limited(HttpStatusCode.Unauthorized), 1, typeof(HttpRequestException), "401 with zero quota");
        await RejectAsync((_, _) => Content(Encoding.UTF8.GetBytes("{broken")), 1, typeof(JsonException), "invalid API JSON");
        await RejectAsync((url, _) => url == Api ? Redirect("https://api.github.com/redirected") : Limited(HttpStatusCode.Forbidden),
            2, typeof(HttpRequestException), "redirected API response is not the primary request");

        // Reject unsafe or unidentifiable public routes before requesting their target.
        foreach (var target in new[]
        {
            Cdn,
            "https://github.com/other/project/releases/download/v0.2.0/mods-manifest.json",
            "https://github.com/owner/project/releases/download/v0.2.0/other.json",
            "https://github.com/owner/project/releases/download/v0.2.0/mods-manifest.json/extra",
            "https://github.com/owner/project/releases/download/v0.2.0-beta/mods-manifest.json",
            "https://github.com/owner/project/releases/download/latest/mods-manifest.json",
            "https://github.com/owner/project/releases/download/v0.2.0/mods-manifest.json?unexpected=1",
            "http://github.com/owner/project/releases/download/v0.2.0/mods-manifest.json",
            "https://user:password@github.com/owner/project/releases/download/v0.2.0/mods-manifest.json",
            "https://evil.test/owner/project/releases/download/v0.2.0/mods-manifest.json"
        })
            await RejectAsync((url, _) => url == Latest ? Redirect(target) : StandardResponse(url),
                2, typeof(InvalidDataException), "fallback rejects unexpected initial route");

        await RejectAsync((url, _) => url == Latest ? Content(Manifest()) : StandardResponse(url),
            2, typeof(InvalidDataException), "direct latest payload lacks canonical release tag");
        await RejectAsync((url, _) => url == Canonical ? Redirect("https://github.com/owner/project/releases/download/v0.3.0/mods-manifest.json") : StandardResponse(url),
            3, typeof(InvalidDataException), "canonical release tag cannot change");
        await RejectAsync((url, _) => url == Canonical ? Redirect(Latest) : StandardResponse(url),
            3, typeof(InvalidDataException), "canonical route cannot return to unversioned latest");
        await RejectAsync((url, _) => url == Canonical ? Redirect("https://evil.test/manifest") : StandardResponse(url),
            3, typeof(InvalidDataException), "canonical manifest cannot redirect to foreign CDN");
        await RejectAsync((url, _) => url == Cdn ? Content(Manifest("0.3.0")) : StandardResponse(url),
            4, typeof(InvalidDataException), "manifest must match canonical tag");
        await RejectAsync((url, _) => url == Cdn ? Content(Manifest("0.2.0-beta")) : StandardResponse(url),
            4, typeof(InvalidDataException), "prerelease manifest is rejected");
        await RejectAsync((url, _) => url == Cdn ? Content([]) : StandardResponse(url),
            4, typeof(InvalidDataException), "empty fallback payload");
        await RejectAsync((url, _) => url == Cdn ? Content(new byte[UpdateService.MaximumManifestBytes + 1]) : StandardResponse(url),
            4, typeof(InvalidDataException), "fallback manifest size limit");
        await RejectAsync((url, _) => url == Latest ? Redirect(Latest) : StandardResponse(url),
            7, typeof(InvalidDataException), "latest redirect loop is bounded");
        await RejectAsync((url, _) => url == Cdn ? Redirect(Cdn) : StandardResponse(url),
            7, typeof(InvalidDataException), "CDN redirect loop is bounded");
        await RejectAsync((url, _) => url == Latest ? Limited(HttpStatusCode.Forbidden) : StandardResponse(url),
            2, typeof(HttpRequestException), "fallback HTTP failure is not retried as another fallback");
        Console.WriteLine("Rate-limit fallback, canonical release identity, error isolation and bounded redirects passed.");
    }

    private static HttpResponseMessage StandardResponse(string url) => url switch
    {
        Api => Limited(HttpStatusCode.Forbidden),
        Latest => Redirect(Canonical),
        Canonical => Redirect(Cdn),
        Cdn => Content(Manifest()),
        ModUrl => Redirect(ModCdn),
        ModCdn => Content(ModBytes),
        _ => throw new InvalidOperationException("Unexpected test request: " + url)
    };

    private static byte[] Manifest(string version = "0.2.0") => JsonSerializer.SerializeToUtf8Bytes(new
    {
        version, minLauncherVersion = "0.1.0",
        mods = new[] { new { id = "crew-console", file = "CrewConsole.Mod.dll", version, sha256 = Convert.ToHexString(SHA256.HashData(ModBytes)), downloadUrl = ModUrl } }
    });
    private static HttpResponseMessage Content(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    private static HttpResponseMessage Redirect(string location) => new(HttpStatusCode.Redirect) { Headers = { Location = new Uri(location) } };
    private static HttpResponseMessage Limited(HttpStatusCode status, string remaining = "0")
    {
        var response = new HttpResponseMessage(status);
        if (status != HttpStatusCode.TooManyRequests) response.Headers.Add("X-RateLimit-Remaining", remaining);
        return response;
    }
    private static async Task RejectAsync(Func<string, int, HttpResponseMessage> response, int expectedRequests, Type errorType, string name)
    {
        using var handler = new Handler(response);
        using var updater = new UpdateService(handler);
        Exception? caught = null;
        try { await updater.CheckAsync(Repository); }
        catch (Exception ex) { caught = ex; }
        Assert(caught != null && errorType.IsInstanceOfType(caught), name + ": expected " + errorType.Name + ", got " + caught?.GetType().Name);
        Assert(handler.Requests.Count == expectedRequests, name + ": unexpected request count " + handler.Requests.Count);
    }
    private static void Assert(bool value, string name) { if (!value) throw new Exception("FAILED: " + name); }
    private sealed class Handler(Func<string, int, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(response(Requests[^1], Requests.Count));
        }
    }
}
