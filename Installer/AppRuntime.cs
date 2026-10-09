using System.Diagnostics.CodeAnalysis;
namespace SS14ModLauncher;
internal static class AppRuntime
{
    [UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "An empty Assembly.Location is deliberately used to reject non-bundled development executables for Steam integration.")]
    public static bool IsSingleFile() => string.IsNullOrEmpty(typeof(Program).Assembly.Location);
}
