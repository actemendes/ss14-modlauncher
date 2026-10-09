using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

internal static class BootstrapSelectionTests
{
    public static void Run()
    {
        var folder = Path.Combine(Path.GetTempPath(), "SS14BootstrapTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "Mods"));
        try { ExerciseSelection(folder); }
        finally { Directory.Delete(folder, recursive: true); }
    }

    private static void ExerciseSelection(string folder)
    {
        var oldLanguage = Environment.GetEnvironmentVariable("SS14_MOD_LANGUAGE");
        var oldDisabled = Environment.GetEnvironmentVariable("SS14_MODS_DISABLED");
        try
        {
            var type = typeof(SS14LocalMods.Bootstrap);
            var read = type.GetMethod("ReadSelection", BindingFlags.NonPublic | BindingFlags.Static)!;
            object? Read() => read.Invoke(null, [folder]);
            void Reject(string contents)
            {
                File.WriteAllText(Path.Combine(folder, "selection.json"), contents);
                try { Read(); } catch (TargetInvocationException) { return; }
                throw new Exception("Invalid bootstrap selection was accepted.");
            }
            if (((System.Collections.IList)Read()!).Count != 0) throw new Exception("Missing selection must disable mods.");
            Reject("{broken");
            Reject("{}");
            Reject("{\"Version\":2,\"Language\":\"en\",\"EnabledMods\":[]}");
            Reject("{\"Version\":1,\"Language\":\"fr\",\"EnabledMods\":[]}");
            var bytes = new byte[] { 1, 2, 3, 4 };
            File.WriteAllBytes(Path.Combine(folder, "Mods", "Fixture.Mod.dll"), bytes);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            string Selection(string file, string digest) => JsonSerializer.Serialize(new { Version = 1, Language = "en", EnabledMods = new[] { new { File = file, Sha256 = digest } } });
            Reject(Selection("../Fixture.Mod.dll", hash));
            Reject(Selection("Fixture.Mod.dll", new string('0', 64)));
            Reject(Selection("Missing.Mod.dll", hash));
            Reject(JsonSerializer.Serialize(new { Version = 1, Language = "en", EnabledMods = new[] { new { File = "Fixture.Mod.dll", Sha256 = hash }, new { File = "Fixture.Mod.dll", Sha256 = hash } } }));
            File.WriteAllText(Path.Combine(folder, "selection.json"), Selection("Fixture.Mod.dll", hash));
            if (((System.Collections.IList)Read()!).Count != 1 || Environment.GetEnvironmentVariable("SS14_MOD_LANGUAGE") != "en")
                throw new Exception("Verified selection or language propagation failed.");
            Environment.SetEnvironmentVariable("SS14_MODS_DISABLED", "1");
            type.GetMethod("Initialize")!.Invoke(null, null);
            if ((int)type.GetField("_started", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)! != 0)
                throw new Exception("Clean launch started bootstrap initialization.");
            Console.WriteLine("Bootstrap selection, path confinement, SHA-256 and clean launch checks passed.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SS14_MOD_LANGUAGE", oldLanguage);
            Environment.SetEnvironmentVariable("SS14_MODS_DISABLED", oldDisabled);
        }
    }
}
