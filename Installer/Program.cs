using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using SS14ModLauncher.Core;

namespace SS14ModLauncher;

internal static class Program
{
    internal const string Version = "0.1.8";
    internal static string[] ForwardedArguments = [];
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            var executable = Environment.ProcessPath!;
            // A Steam entry point only hands off: the running UI must not lock the file Restore replaces.
            if (Path.GetFileName(executable).Equals("SS14.Launcher.exe", StringComparison.OrdinalIgnoreCase))
            {
                var root = Path.GetDirectoryName(executable)!;
                var stable = Path.Combine(root, "SS14ModLauncher", "SS14ModLauncher.exe");
                if (!File.Exists(stable)) throw new IOException("ModLauncher installation is incomplete. Restore Steam files or run the portable ModLauncher.");
                var start = new ProcessStartInfo(stable) { UseShellExecute = false, WorkingDirectory = root };
                start.ArgumentList.Add("--launcher-root"); start.ArgumentList.Add(root); start.ArgumentList.Add("--steam");
                foreach (var arg in args) start.ArgumentList.Add(arg);
                Process.Start(start);
                return 0;
            }
            if (args.Length == 2 && args[0] is "--install" or "--uninstall" or "--restore" or "--status")
            {
                if (args[0] == "--install") AppRuntime.Install(args[1], Payload.ForInstallation(args[1]), ["CrewConsole.Mod.dll"], "en");
                else if (args[0] == "--status") Console.WriteLine(JsonSerializer.Serialize(Installation.Inspect(args[1])));
                else Installation.Restore(args[1]);
                return 0;
            }
            var steam = Array.IndexOf(args, "--steam");
            if (steam >= 0) ForwardedArguments = args[(steam + 1)..];
            var ownArguments = steam >= 0 ? args[..steam] : args;
            string? Option(string name) { var i = Array.IndexOf(ownArguments, name); return i >= 0 && i + 1 < ownArguments.Length ? ownArguments[i + 1] : null; }
            var settingsPath = Option("--settings");
            var settings = AppSettings.Load(settingsPath);
            if (Option("--launcher-root") is { } launcherRoot) settings.LauncherPath = launcherRoot;
            if (Option("--lang") is "en" or "ru") settings.Language = Option("--lang")!;
            using var form = new LauncherWindow(settings, settingsPath, Option("--view"), enableAutomaticCheck: Option("--capture") == null,
                enableOnboarding: Option("--capture") == null);
            if (Option("--capture") is { } capture)
                form.Shown += async (_, _) => { await Task.Delay(650); using var bitmap = new Bitmap(form.Width, form.Height); form.DrawToBitmap(bitmap, form.ClientRectangle with { Width = form.Width, Height = form.Height }); bitmap.Save(Path.GetFullPath(capture)); form.Close(); };
            Application.Run(form);
            return 0;
        }
        catch (Exception e)
        {
            if (args.Length > 0 && args[0] is "--install" or "--uninstall" or "--restore" or "--status")
                Console.Error.WriteLine(e.Message);
            else MessageBox.Show(e.Message, "SS14 ModLauncher", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}

internal static class Payload
{
    public static Dictionary<string, byte[]> ForInstallation(string root)
    {
        return BundledPayload.Merge(Read(), Installation.ReadVerifiedInstalledMods(root));
    }
    public static Dictionary<string, byte[]> Read()
    {
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var assembly = Assembly.GetExecutingAssembly();
        foreach (var filename in new[] { "SS14LocalMods.Bootstrap.dll", "0Harmony.dll" }.Concat(Catalog.Bundled.Select(mod => mod.File)))
        {
            var resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith("." + filename, StringComparison.Ordinal));
            using var input = assembly.GetManifestResourceStream(resource)!;
            using var bytes = new MemoryStream(); input.CopyTo(bytes); result.Add(filename, bytes.ToArray());
        }
        return result;
    }
}
