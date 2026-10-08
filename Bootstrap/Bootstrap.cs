using System.Reflection;
using System.Runtime.Loader;

namespace SS14LocalMods;

public static class Bootstrap
{
    private static readonly string Root = Path.GetDirectoryName(typeof(Bootstrap).Assembly.Location)!;
    private static readonly List<Assembly> Plugins = [];
    private static readonly HashSet<string> Loaded = [];
    private static bool _started;

    public static void Initialize()
    {
        if (_started) return;
        _started = true;
        // Dependencies live beside the bootstrap, outside the server content cache.
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            var path = Path.Combine(Root, name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        foreach (var path in Directory.EnumerateFiles(Path.Combine(Root, "Mods"), "*.dll"))
        {
            try { Plugins.Add(AssemblyLoadContext.Default.LoadFromAssemblyPath(path)); }
            catch (Exception e) { Log("Cannot load " + Path.GetFileName(path) + ": " + e.Message); }
        }
        AppDomain.CurrentDomain.AssemblyLoad += (_, e) => ContentLoaded(e.LoadedAssembly);
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) ContentLoaded(assembly);
        Log("Started. Local plugins: " + Plugins.Count);
    }

    private static void ContentLoaded(Assembly content)
    {
        if (content.GetName().Name != "Content.Client") return;
        foreach (var plugin in Plugins)
        {
            var key = plugin.FullName! + "|" + content.GetHashCode();
            if (!Loaded.Add(key)) continue;
            try
            {
                var entry = plugin.GetType("SS14LocalMods.Mod")?.GetMethod("Install", BindingFlags.Public | BindingFlags.Static)
                    ?? throw new InvalidOperationException("Expected SS14LocalMods.Mod.Install(Assembly).");
                entry.Invoke(null, [content]);
                Log("Installed " + plugin.GetName().Name + " into " + content.GetName().Name);
            }
            catch (Exception e) { Log("Plugin failed: " + e.GetBaseException()); }
        }
    }

    public static void Log(string message)
    {
        Console.WriteLine("[SS14 Local Mods] " + message);
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SS14LocalMods");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "mods.log"), DateTimeOffset.Now.ToString("O") + " " + message + Environment.NewLine);
        }
        catch { /* Logging must never break the game. */ }
    }
}
