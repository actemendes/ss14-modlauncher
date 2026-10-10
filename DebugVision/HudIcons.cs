using System.Reflection;
using HarmonyLib;

namespace SS14LocalMods;

public static partial class Mod
{
    private static Type _healthHudType = null!;
    private static FieldInfo _healthActive = null!, _jobActive = null!, _healthContainers = null!, _jobHud = null!;

    private static IEnumerable<(MethodInfo Target, string Handler, bool Prefix)> HudHooks(Assembly content)
    {
        _healthHudType = content.GetType("Content.Client.Overlays.ShowHealthIconsSystem", true)!;
        var job = content.GetType("Content.Client.Overlays.ShowJobIconsSystem", true)!;
        var status = content.GetType("Content.Client.Access.Systems.JobStatusSystem", true)!;
        _healthActive = ActiveField(_healthHudType);
        _jobActive = ActiveField(job);
        _healthContainers = AccessTools.Field(_healthHudType, "DamageContainers")
            ?? throw new MissingFieldException(_healthHudType.FullName, "DamageContainers");
        if (_healthContainers.FieldType != typeof(HashSet<string>))
            throw new InvalidOperationException("Unsupported health HUD damage containers.");
        _jobHud = AccessTools.Field(status, "_showJobIcons")
            ?? throw new MissingFieldException(status.FullName, "_showJobIcons");
        if (_jobHud.FieldType != job) throw new InvalidOperationException("Unsupported job HUD dependency.");
        yield return (Required(_healthHudType, "OnGetStatusIconsEvent"), nameof(BeforeHudIcons), true);
        yield return (Required(status, "OnGetStatusIconsEvent"), nameof(BeforeHudIcons), true);
    }

    private static FieldInfo ActiveField(Type type)
    {
        var field = AccessTools.Field(type, "<IsActive>k__BackingField")
            ?? throw new MissingFieldException(type.FullName, "IsActive backing field");
        if (field.FieldType != typeof(bool)) throw new InvalidOperationException("Unsupported equipment HUD activation.");
        return field;
    }

    // Scope the override to native icon collection. Equipment refresh, detach,
    // and all other HUDs keep their native state, including while this is enabled.
    public sealed record HudState(object System, FieldInfo Active, bool WasActive, HashSet<string>? Containers);

    public static void BeforeHudIcons(object __instance, out HudState? __state)
    {
        __state = null;
        if (!_inGame) return;
        var health = _healthHudType.IsInstanceOfType(__instance);
        if (health ? !HealthHud : !JobHud) return;
        var system = health ? __instance : _jobHud.GetValue(__instance)!;
        var active = health ? _healthActive : _jobActive;
        var containers = health ? (HashSet<string>)_healthContainers.GetValue(system)! : null;
        __state = new(system, active, (bool)active.GetValue(system)!, containers);
        active.SetValue(system, true);
        // Biological is the game's default medical HUD container. Preserve any
        // additional containers supplied by equipped HUDs without modifying them.
        if (health) _healthContainers.SetValue(system, new HashSet<string>(containers!) { "Biological" });
    }

    public static void RestoreHudIcons(HudState? __state)
    {
        if (__state == null) return;
        __state.Active.SetValue(__state.System, __state.WasActive);
        if (__state.Containers != null) _healthContainers.SetValue(__state.System, __state.Containers);
    }
}
