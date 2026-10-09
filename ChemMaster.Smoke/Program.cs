using System.Reflection;
using System.Runtime.Loader;

if (args.Length < 2) throw new ArgumentException("Usage: game-directory mod-dll [--client]");
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
if (args.Contains("--client"))
{
    // A separate developer client and userdata directory. No installed launcher/profile changes.
    foreach (var name in new[] { "Robust.Shared", "Robust.Client" }) AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(game, name + ".dll"));
    var qaPlugin = AssemblyLoadContext.Default.LoadFromAssemblyPath(mod);
    AppDomain.CurrentDomain.AssemblyLoad += (_, e) =>
    {
        if (e.LoadedAssembly.GetName().Name == "Content.Client")
            qaPlugin.GetType("SS14LocalMods.Mod", true)!.GetMethod("Install")!.Invoke(null, [e.LoadedAssembly]);
    };
    var robust = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Robust.Shared");
    robust.GetType("Robust.Shared.ProgramShared", true)!.GetField("PathOffset")!.SetValue(null, game.Replace('\\', '/') + "/");
    var client = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Robust.Client");
    var options = Activator.CreateInstance(client.GetType("Robust.Client.GameControllerOptions", true)!)!;
    options.GetType().GetProperty("UserDataDirectoryName")!.SetValue(options, "SS14-ChemMaster-QA");
    options.GetType().GetProperty("DefaultWindowTitle")!.SetValue(options, "ChemMaster QA");
    var clientArgs = new[] { "--connect", "--connect-address", "127.0.0.1:1212", "--username", "ChemMasterQA", "--cvar", "display.width=1280", "--cvar", "display.height=900" };
    var gameThread = new Thread(() => client.GetType("Robust.Client.ContentStart", true)!.GetMethod("StartLibrary")!.Invoke(null, [clientArgs, options]));
    if (OperatingSystem.IsWindows()) gameThread.SetApartmentState(ApartmentState.STA);
    gameThread.Start(); gameThread.Join();
    return;
}
foreach (var name in new[] { "Robust.Shared", "Content.Shared", "Robust.Client", "Content.Client" })
    AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(game, name + ".dll"));
var plugin = AssemblyLoadContext.Default.LoadFromAssemblyPath(mod);
var content = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Content.Client");
plugin.GetType("SS14LocalMods.Mod", true)!.GetMethod("Install")!.Invoke(null, [content]);
Console.WriteLine("ChemMaster: all seven Harmony hooks installed against actual game assemblies.");
