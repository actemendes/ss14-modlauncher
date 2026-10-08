using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace SS14LocalMods.Installer;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] is "--install" or "--uninstall")
        {
            try { if (args[0] == "--install") Installation.Install(args[1]); else Installation.Uninstall(args[1]); return 0; }
            catch (Exception e) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "installer-error.txt"), e.ToString()); return 1; }
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new InstallerWindow());
        return 0;
    }
}

internal sealed class InstallerWindow : Form
{
    private readonly TextBox _path = new() { Dock = DockStyle.Fill };
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoSize = false, Text = "Hello World: отдельное окно по F1/0. Crew Console: адаптивный прототип.\nПеред установкой закройте игру. Launcher можно оставить открытым." };
    public InstallerWindow()
    {
        Text = "SS14 — локальные моды";
        ClientSize = new Size(620, 260);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 2, RowCount = 4 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        var label = new Label { Text = "Папка с SS14.Launcher.exe (обычно bin_x64):", Dock = DockStyle.Fill };
        table.Controls.Add(label, 0, 0); table.SetColumnSpan(label, 2);
        var defaults = new[] { @"E:\SteamLibrary\steamapps\common\Space Station 14 Playtest\bin_x64", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Steam\steamapps\common\Space Station 14 Playtest\bin_x64") };
        _path.Text = defaults.FirstOrDefault(p => File.Exists(Path.Combine(p, "SS14.Launcher.exe"))) ?? "";
        table.Controls.Add(_path, 0, 1);
        var browse = new Button { Text = "Выбрать…", Dock = DockStyle.Fill };
        browse.Click += (_, _) => { using var dialog = new FolderBrowserDialog(); if (dialog.ShowDialog() == DialogResult.OK) _path.Text = dialog.SelectedPath; };
        table.Controls.Add(browse, 1, 1);
        table.Controls.Add(_status, 0, 2); table.SetColumnSpan(_status, 2);
        var install = new Button { Text = "Установить / обновить моды", Dock = DockStyle.Fill };
        install.Click += (_, _) => Run(() => Installation.Install(_path.Text), "Установлено. Hello World: F1/0. Crew Console: откройте игровую консоль мониторинга экипажа.");
        var remove = new Button { Text = "Удалить моды", Dock = DockStyle.Fill };
        remove.Click += (_, _) => Run(() => Installation.Uninstall(_path.Text), "Исходный загрузчик восстановлен. Моды отключены.");
        table.Controls.Add(install, 0, 3); table.Controls.Add(remove, 1, 3);
        Controls.Add(table);
    }
    private void Run(Action action, string success)
    {
        try { action(); _status.Text = success; }
        catch (Exception e) { _status.Text = e.Message; }
    }
}

internal static class Installation
{
    private sealed record State(string OriginalHash, string PatchedHash);
    private static string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
    private static string LoaderPath(string root)
    {
        root = Path.GetFullPath(root);
        var path = Path.Combine(root, "loader", "SS14.Loader.dll");
        if (!File.Exists(Path.Combine(root, "SS14.Launcher.exe")) || !File.Exists(path)) throw new InvalidOperationException("Выберите папку с SS14.Launcher.exe и loader/SS14.Loader.dll.");
        var game = System.Diagnostics.Process.GetProcessesByName("SS14.Loader");
        try
        {
            var executable = Path.Combine(root, "loader", "SS14.Loader.exe");
            foreach (var process in game)
            {
                string? runningPath;
                try { runningPath = process.MainModule?.FileName; }
                catch { throw new InvalidOperationException("Не удалось проверить работающий клиент. Закройте SS14 и повторите."); }
                if (string.Equals(runningPath, executable, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Закройте SS14 перед установкой или удалением мода.");
            }
        }
        finally { foreach (var process in game) process.Dispose(); }
        return path;
    }

    public static void Install(string root)
    {
        var loader = LoaderPath(root);
        var directory = Path.Combine(Path.GetDirectoryName(loader)!, "SS14LocalMods");
        var statePath = Path.Combine(directory, "installation.json");
        if (File.Exists(statePath))
        {
            var state = JsonSerializer.Deserialize<State>(File.ReadAllText(statePath))!;
            if (Hash(loader) == state.PatchedHash) { ExtractPayload(directory); return; }
            throw new InvalidOperationException("Загрузчик изменён после установки (возможно, обновление Steam). Нужна повторная проверка совместимости.");
        }
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(Path.Combine(directory, "Mods"));
        var backup = Path.Combine(directory, "SS14.Loader.original.dll");
        if (File.Exists(backup)) throw new InvalidOperationException("Найдена резервная копия незавершённой установки. Не перезаписываю её.");
        var originalHash = Hash(loader);
        var temporary = loader + ".localmods.tmp";
        using (var assembly = AssemblyDefinition.ReadAssembly(loader, new ReaderParameters { InMemory = true }))
        {
            var run = assembly.MainModule.GetType("SS14.Loader.Program")?.Methods.SingleOrDefault(m => m.Name == "Run" && m.Parameters.Count == 0);
            if (run?.HasBody != true) throw new InvalidOperationException("Эта версия SS14.Loader не поддерживается.");
            var module = assembly.MainModule;
            var il = run.Body.GetILProcessor();
            var first = run.Body.Instructions[0];
            // Resolve bootstrap relative to the loader; keep normal signature verification and authentication intact.
            var instructions = new[] {
                il.Create(OpCodes.Call, module.ImportReference(typeof(AppContext).GetProperty(nameof(AppContext.BaseDirectory))!.GetMethod!)),
                il.Create(OpCodes.Ldstr, "SS14LocalMods/SS14LocalMods.Bootstrap.dll"),
                il.Create(OpCodes.Call, module.ImportReference(typeof(Path).GetMethod(nameof(Path.Combine), [typeof(string), typeof(string)])!)),
                il.Create(OpCodes.Call, module.ImportReference(typeof(Assembly).GetMethod(nameof(Assembly.LoadFrom), [typeof(string)])!)),
                il.Create(OpCodes.Ldstr, "SS14LocalMods.Bootstrap"),
                il.Create(OpCodes.Callvirt, module.ImportReference(typeof(Assembly).GetMethod(nameof(Assembly.GetType), [typeof(string)])!)),
                il.Create(OpCodes.Ldstr, "Initialize"),
                il.Create(OpCodes.Callvirt, module.ImportReference(typeof(Type).GetMethod(nameof(Type.GetMethod), [typeof(string)])!)),
                il.Create(OpCodes.Ldnull), il.Create(OpCodes.Ldnull),
                il.Create(OpCodes.Callvirt, module.ImportReference(typeof(MethodBase).GetMethod(nameof(MethodBase.Invoke), [typeof(object), typeof(object[])])!)),
                il.Create(OpCodes.Pop)
            };
            foreach (var instruction in instructions) il.InsertBefore(first, instruction);
            assembly.Write(temporary);
        }
        ExtractPayload(directory);
        File.Copy(loader, backup);
        var newState = new State(originalHash, Hash(temporary));
        File.WriteAllText(statePath, JsonSerializer.Serialize(newState));
        try { File.Move(temporary, loader, overwrite: true); }
        catch { File.Delete(statePath); throw; }
    }

    private static void ExtractPayload(string directory)
    {
        foreach (var filename in new[] { "SS14LocalMods.Bootstrap.dll", "0Harmony.dll", "HelloWorld.Mod.dll", "CrewConsole.Mod.dll" })
        {
            var resource = typeof(Installation).Assembly.GetManifestResourceNames().Single(n => n.EndsWith("." + filename, StringComparison.Ordinal));
            using var input = typeof(Installation).Assembly.GetManifestResourceStream(resource)!;
            var target = Path.Combine(directory, filename.EndsWith(".Mod.dll", StringComparison.Ordinal) ? "Mods/" + filename : filename);
            using var output = File.Create(target); input.CopyTo(output);
        }
    }

    public static void Uninstall(string root)
    {
        var loader = LoaderPath(root);
        var directory = Path.Combine(Path.GetDirectoryName(loader)!, "SS14LocalMods");
        var statePath = Path.Combine(directory, "installation.json");
        if (!File.Exists(statePath)) throw new InvalidOperationException("Установленные локальные моды не найдены.");
        var state = JsonSerializer.Deserialize<State>(File.ReadAllText(statePath))!;
        var backup = Path.Combine(directory, "SS14.Loader.original.dll");
        if (Hash(backup) != state.OriginalHash) throw new InvalidOperationException("Резервная копия повреждена. Восстановление остановлено.");
        if (Hash(loader) != state.PatchedHash) throw new InvalidOperationException("Загрузчик изменён. Автоматически перезаписывать его небезопасно.");
        File.Copy(backup, loader, overwrite: true);
        // Preserve other plugins. Remove only the installation record and verified backup.
        File.Delete(statePath); File.Delete(backup);
    }
}
