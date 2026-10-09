using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using SS14LocalMods.ChemMaster;

namespace SS14LocalMods;

public static class Mod
{
    private static readonly ConditionalWeakTable<object, RecipePanel> Buis = new(), Windows = new();
    public static void Install(Assembly content)
    {
        var bui = content.GetType("Content.Client.Chemistry.UI.ChemMasterBoundUserInterface", true)!;
        var control = Native.Type("Robust.Client", "Robust.Client.UserInterface.Control");
        var bound = Native.Type("Robust.Shared", "Robust.Shared.GameObjects.BoundUserInterface");
        var shared = Native.Type("Content.Shared", "Content.Shared.Chemistry.ChemMasterBoundUserInterfaceState");
        foreach (var name in new[] { "InputContainerInfo", "BufferReagents", "Mode" })
            if (shared.GetField(name) == null) throw new MissingFieldException(shared.FullName, name);
        var harmony = new Harmony("local.chem-master");
        try
        {
            Patch(bui.GetMethod("Open", Native.Flags)!, nameof(Opened));
            Patch(bui.GetMethod("UpdateState", Native.Flags)!, nameof(Updated));
            Patch(control.GetMethod("DoFrameUpdateRecursive", Native.Flags)!, nameof(Frame));
            Patch(bound.GetMethod("Close", Type.EmptyTypes)!, nameof(Closing), true);
            Patch(bound.GetMethod("Dispose", Type.EmptyTypes)!, nameof(Closing), true);
            Patch(bound.GetMethod("SendMessage")!, nameof(Sending), true);
            Patch(bound.GetMethod("OnProtoReload")!, nameof(PrototypesChanged));
            void Patch(MethodInfo method, string callback, bool prefix = false)
            {
                if (method == null) throw new MissingMethodException(callback);
                var patch = new HarmonyMethod(typeof(Mod), callback);
                harmony.Patch(method, prefix: prefix ? patch : null, postfix: prefix ? null : patch);
            }
        }
        catch { harmony.UnpatchAll(harmony.Id); throw; }
        Bootstrap.Log("ChemMaster: native recipes tab, server prototypes and confirmed BUI commands installed.");
    }
    public static void Opened(object __instance)
    {
        if (Buis.TryGetValue(__instance, out _)) return;
        try
        {
            var window = Native.Get(__instance, "_window") ?? throw new MissingMemberException("ChemMaster window.");
            var panel = new RecipePanel(__instance, window);
            Buis.Add(__instance, panel); Windows.Add(window, panel);
        }
        catch (Exception e) { Bootstrap.Log("ChemMaster UI unavailable: " + e.GetBaseException()); }
    }
    private static void Run(RecipePanel panel, Action action)
    {
        try { action(); }
        catch (Exception e)
        {
            panel.Dispose();
            Bootstrap.Log("ChemMaster disabled for this window: " + e.GetBaseException());
        }
    }
    public static void Updated(object __instance) { if (Buis.TryGetValue(__instance, out var panel)) Run(panel, panel.Observe); }
    public static void Frame(object __instance) { if (Windows.TryGetValue(__instance, out var panel)) Run(panel, panel.Tick); }
    public static void Sending(object __instance) { if (Buis.TryGetValue(__instance, out var panel)) Run(panel, panel.ManualCommand); }
    public static void PrototypesChanged(object __instance) { if (Buis.TryGetValue(__instance, out var panel)) Run(panel, panel.CatalogChanged); }
    public static void Closing(object __instance)
    {
        if (!Buis.TryGetValue(__instance, out var panel)) return;
        panel.Dispose(); Buis.Remove(__instance);
    }
}
