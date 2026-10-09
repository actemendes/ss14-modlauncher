using System.Diagnostics.CodeAnalysis;
using SS14ModLauncher.Core;
namespace SS14ModLauncher;
internal static class AppRuntime
{
    [UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "An empty Assembly.Location is deliberately used to reject non-bundled development executables for Steam integration.")]
    public static bool IsSingleFile() => string.IsNullOrEmpty(typeof(Program).Assembly.Location);

    public static void Install(string root, IReadOnlyDictionary<string, byte[]> payload, IReadOnlyList<string> enabledFiles, string language)
    {
        if (Installation.ShouldEnableSteamByDefault(root) && !IsSingleFile())
            throw new InvalidOperationException(language == "ru"
                ? "Для установки в Steam используйте SS14ModLauncher.exe из архива релиза."
                : "Use SS14ModLauncher.exe from the release archive to install into Steam.");
        Installation.InstallWithDefaults(root, payload, enabledFiles, language, Environment.ProcessPath!);
    }
}
