using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;

namespace SS14LocalMods;

public static class Bootstrap
{
    private static readonly string Root = Path.GetDirectoryName(typeof(Bootstrap).Assembly.Location)!;
    private static Assembly[] _plugins = [];
    private static readonly object Gate = new();
    private static readonly HashSet<(Assembly Plugin, Assembly Content)> Installed = [];
    private static int _started;
    private sealed record Selection(int Version, string Language, SelectedMod[] EnabledMods);
    private sealed record SelectedMod(string File, string Sha256);

    public static void Initialize()
    {
        if (Environment.GetEnvironmentVariable("SS14_MODS_DISABLED") == "1") return;
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        try
        {
            // Validate the entire selection before loading executable plugin code.
            var selected = ReadSelection(Root);
            if (selected.Count == 0) { Log("No verified mods selected. Continuing with the standard client."); return; }
            AssemblyLoadContext.Default.Resolving += ResolveDependency;
            var plugins = new List<Assembly>();
            foreach (var (file, bytes) in selected)
            {
                try
                {
                    // Load the exact bytes whose hash was verified, without a second file read.
                    using var stream = new MemoryStream(bytes, writable: false);
                    plugins.Add(AssemblyLoadContext.Default.LoadFromStream(stream));
                }
                catch (Exception e) { Log("Cannot load " + file + ": " + e.Message); }
            }
            _plugins = plugins.ToArray();
            AppDomain.CurrentDomain.AssemblyLoad += (_, e) => ContentLoaded(e.LoadedAssembly);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) ContentLoaded(assembly);
            Log("Started. Verified local plugins: " + _plugins.Length);
        }
        catch (Exception e) { Log("Mod initialization skipped: " + e.GetBaseException().Message); }
    }

    private static List<(string File, byte[] Bytes)> ReadSelection(string root)
    {
        var path = Path.Combine(root, "selection.json");
        if (!File.Exists(path)) return [];
        if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Selection exceeds 64 KiB.");
        var selection = JsonSerializer.Deserialize<Selection>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (selection == null || selection.Version != 1 || selection.Language is not ("ru" or "en") || selection.EnabledMods == null
            || selection.EnabledMods.Length > 32) throw new InvalidDataException("Invalid mod selection.");
        var selected = new List<(string File, byte[] Bytes)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in selection.EnabledMods)
        {
            if (mod == null || !IsSafeModFileName(mod.File) || !seen.Add(mod.File)
                || mod.Sha256 == null || mod.Sha256.Length != 64 || !mod.Sha256.All(Uri.IsHexDigit))
                throw new InvalidDataException("Unsafe or duplicate mod selection.");
            var modPath = Path.Combine(root, "Mods", mod.File);
            var file = new FileInfo(modPath);
            if (!file.Exists || file.Length is <= 0 or > 33554432 || (file.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Selected mod is missing or invalid: " + mod.File);
            var bytes = File.ReadAllBytes(modPath);
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), Convert.FromHexString(mod.Sha256)))
                throw new InvalidDataException("Selected mod hash differs: " + mod.File);
            selected.Add((mod.File, bytes));
        }
        Environment.SetEnvironmentVariable("SS14_MOD_LANGUAGE", selection.Language);
        return selected;
    }

    private static bool IsSafeModFileName(string? name) => name is { Length: > 8 and <= 120 }
        && name.EndsWith(".Mod.dll", StringComparison.Ordinal)
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_')
        && !name.StartsWith('.') && !name.Contains("..", StringComparison.Ordinal);

    private static Assembly? ResolveDependency(AssemblyLoadContext context, AssemblyName name)
    {
        try
        {
            // Dependency resolution must never introduce an unselected plugin.
            if (string.IsNullOrEmpty(name.Name) || name.Name.EndsWith(".Mod", StringComparison.OrdinalIgnoreCase)
                || !name.Name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_')
                || name.Name.StartsWith('.') || name.Name.Contains("..", StringComparison.Ordinal)) return null;
            var path = Path.Combine(Root, name.Name + ".dll");
            if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return null;
            return context.LoadFromAssemblyPath(path);
        }
        catch (Exception e) { Log("Dependency skipped: " + name.Name + ": " + e.Message); return null; }
    }

    private static void ContentLoaded(Assembly content)
    {
        try
        {
            if (content.GetName().Name != "Content.Client") return;
            foreach (var plugin in _plugins)
            {
                lock (Gate) { if (!Installed.Add((plugin, content))) continue; }
                try
                {
                    var entry = plugin.GetType("SS14LocalMods.Mod")?.GetMethod("Install", BindingFlags.Public | BindingFlags.Static,
                        binder: null, types: [typeof(Assembly)], modifiers: null)
                        ?? throw new InvalidOperationException("Expected SS14LocalMods.Mod.Install(Assembly).");
                    entry.Invoke(null, [content]);
                    Log("Installed " + plugin.GetName().Name + " into " + content.GetName().Name);
                }
                catch (Exception e) { Log("Plugin failed: " + e.GetBaseException()); }
            }
        }
        catch (Exception e) { Log("Plugin dispatch skipped: " + e.Message); }
    }

    public static void Log(string message)
    {
        try { Console.WriteLine("[SS14 ModLauncher] " + message); } catch { }
        try
        {
            lock (Gate)
            {
                var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SS14LocalMods");
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "mods.log");
                if (File.Exists(path) && new FileInfo(path).Length > 2 * 1024 * 1024)
                    File.Move(path, Path.Combine(directory, "mods.previous.log"), overwrite: true);
                File.AppendAllText(path, DateTimeOffset.Now.ToString("O") + " " + message + Environment.NewLine);
            }
        }
        catch { /* Logging must never break the game. */ }
    }
}
