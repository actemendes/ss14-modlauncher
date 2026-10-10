using System.Reflection;
using System.Runtime.Loader;
using HarmonyLib;

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
var entry = plugin.GetType("SS14LocalMods.Mod", true)!;
entry.GetMethod("Install")!.Invoke(null, [content]);
foreach (var flag in new[] { "_disabled", "_attackerDisabled", "_faunaDisabled" })
    if ((bool)entry.GetField(flag, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!)
        throw new Exception("Game API capability failed: " + flag);
var patched = Harmony.GetAllPatchedMethods().Where(m => Harmony.GetPatchInfo(m)?.Owners.Contains("local.death-rattle") == true).ToArray();
if (patched.Length != 5) throw new Exception("Expected all five game hooks.");
foreach (var method in patched) Console.WriteLine("Patched actual game method: " + method.DeclaringType!.FullName + "." + method.Name);
Console.WriteLine("Distress Call: release DLL installed all five hooks against actual game assemblies.");
