using Mono.Cecil;

namespace SS14ModLauncher.Core;

public static class BundledPayload
{
    /// <summary>Upgrade older installed mods while retaining independent updates and unknown versions.</summary>
    public static Dictionary<string, byte[]> Merge(IReadOnlyDictionary<string, byte[]> bundled,
        IReadOnlyDictionary<string, byte[]> verifiedInstalled)
    {
        var result = new Dictionary<string, byte[]>(bundled, StringComparer.OrdinalIgnoreCase);
        foreach (var (file, installed) in verifiedInstalled)
        {
            if (!result.TryGetValue(file, out var included) || !file.EndsWith(".Mod.dll", StringComparison.OrdinalIgnoreCase)) continue;
            result[file] = IsNewer(included, installed) ? included : installed;
        }
        return result;
    }

    private static bool IsNewer(byte[] bundled, byte[] installed)
    {
        try
        {
            using var bundledStream = new MemoryStream(bundled);
            using var installedStream = new MemoryStream(installed);
            using var bundledAssembly = AssemblyDefinition.ReadAssembly(bundledStream);
            using var installedAssembly = AssemblyDefinition.ReadAssembly(installedStream);
            return bundledAssembly.Name.Name.Equals(installedAssembly.Name.Name, StringComparison.OrdinalIgnoreCase)
                && bundledAssembly.Name.Version > installedAssembly.Name.Version;
        }
        catch (Exception exception) when (exception is BadImageFormatException or IOException or ArgumentException)
        {
            return false;
        }
    }
}
