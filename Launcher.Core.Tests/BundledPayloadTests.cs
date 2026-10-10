using Mono.Cecil;
using SS14ModLauncher.Core;

internal static class BundledPayloadTests
{
    public static void Run()
    {
        var old = Dll("DebugVision.Mod", "0.1.0.0");
        var bundled = Dll("DebugVision.Mod", "0.1.1.0");
        var newer = Dll("DebugVision.Mod", "0.2.0.0");
        var same = Dll("DebugVision.Mod", "0.1.1.0");
        byte[] Merge(byte[] installed) => BundledPayload.Merge(
            new Dictionary<string, byte[]> { ["DebugVision.Mod.dll"] = bundled },
            new Dictionary<string, byte[]> { ["DebugVision.Mod.dll"] = installed })["DebugVision.Mod.dll"];
        Assert(ReferenceEquals(Merge(old), bundled), "bundled upgrade replaces installed 0.1.0 with 0.1.1");
        Assert(ReferenceEquals(Merge(newer), newer), "newer independent update survives reinstall");
        Assert(ReferenceEquals(Merge(same), same), "same version retains exact installed bytes");
        var unknown = new byte[] { 1, 2, 3 };
        Assert(ReferenceEquals(Merge(unknown), unknown), "unknown version retains installed bytes");
        var foreign = Dll("Other.Mod", "0.1.0.0");
        Assert(ReferenceEquals(Merge(foreign), foreign), "different assembly identity is not upgraded");
        var runtime = BundledPayload.Merge(new Dictionary<string, byte[]> { ["0Harmony.dll"] = bundled },
            new Dictionary<string, byte[]> { ["0Harmony.dll"] = old, ["Untracked.Mod.dll"] = old });
        Assert(ReferenceEquals(runtime["0Harmony.dll"], bundled) && runtime.Count == 1,
            "installed runtime and unrelated mods do not replace bundled payload");
        Console.WriteLine("PASS bundled payload upgrades and independent update preservation (6 checks)");
    }

    private static byte[] Dll(string name, string version)
    {
        using var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(name, Version.Parse(version)), name, ModuleKind.Dll);
        using var stream = new MemoryStream();
        assembly.Write(stream);
        return stream.ToArray();
    }

    private static void Assert(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }
}
