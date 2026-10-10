using System.Reflection;
using System.Runtime.Loader;

if (args.Length != 2) throw new ArgumentException("Usage: game-directory mod-dll");
var game = Path.GetFullPath(args[0]); var mod = Path.GetFullPath(args[1]);
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    foreach (var directory in new[] { Path.GetDirectoryName(mod)!, Path.Combine(Directory.GetCurrentDirectory(), "payload"), game })
    {
        var path = Path.Combine(directory, name.Name + ".dll");
        if (File.Exists(path)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
    }
    return null;
};
foreach (var name in new[] { "Robust.Shared", "Robust.Client", "Content.Shared", "Content.Client" })
    AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(game, name + ".dll"));
var plugin = AssemblyLoadContext.Default.LoadFromAssemblyPath(mod);
var content = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Content.Client");
plugin.GetType("SS14LocalMods.Mod", true)!.GetMethod("Install")!.Invoke(null, [content]);
Console.WriteLine("Debug Vision: all twenty-five Harmony hooks installed against actual game assemblies.");
