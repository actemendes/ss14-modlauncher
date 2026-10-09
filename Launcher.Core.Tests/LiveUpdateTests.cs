using System.Security.Cryptography;
using Mono.Cecil;
using SS14ModLauncher.Core;

/// <summary>Opt-in public release smoke check. Downloads are verified in memory and never installed.</summary>
internal static class LiveUpdateTests
{
    public static async Task RunAsync(string repository, string? releaseAssets)
    {
        using var updater = new UpdateService();
        // A first release is already bundled. Supply an older installed version explicitly so
        // the production updater exercises both asset downloads instead of returning no updates.
        var oldVersions = Catalog.Bundled.ToDictionary(mod => mod.Id, _ => "0.0.0", StringComparer.Ordinal);
        var check = await updater.CheckAsync(repository, oldVersions);
        Assert(check.LauncherVersion == Catalog.LauncherVersion, "advertised launcher matches this release candidate independently of the feed tag");
        Assert(!check.RequiresLauncherUpdate, "published manifest accepts the current launcher");
        Assert(check.BlockedMods.Count == 0, "candidate supports all advertised mod updates");
        var downloaded = await updater.DownloadAsync(check);
        Assert(downloaded.Count == check.Mods.Count, "all advertised newer mod assets downloaded and verified");
        foreach (var update in check.Mods)
        {
            var bytes = downloaded[update.File];
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            Assert(hash.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase), "release SHA-256 for " + update.File);
            using var stream = new MemoryStream(bytes);
            using var assembly = AssemblyDefinition.ReadAssembly(stream);
            Assert(assembly.Name.Name == Path.GetFileNameWithoutExtension(update.File), "managed mod assembly identity: " + update.File);
            if (releaseAssets != null)
                Assert(bytes.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(releaseAssets, update.File))), "public asset equals local release bytes: " + update.File);
            Console.WriteLine($"Public release asset: {update.File}; bytes={bytes.Length}; SHA-256={hash}");
        }
        var current = check.Mods.ToDictionary(mod => mod.Id, mod => mod.Version, StringComparer.Ordinal);
        var repeated = await updater.CheckAsync(repository, current);
        Assert(repeated.Mods.Count == 0, "current installed versions suppress repeated updates");
        Assert(!repeated.HasLauncherUpdate, "current launcher version is up to date");
        Console.WriteLine($"PASS live public GitHub manifest, redirects, verified mod downloads and current-version check: {check.ReleaseUrl}");
    }

    private static void Assert(bool condition, string description)
    {
        if (!condition) throw new InvalidDataException("Live release check failed: " + description);
    }
}
