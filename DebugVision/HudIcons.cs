using System.Reflection;
using HarmonyLib;
using static SS14LocalMods.Conversations.NativeUi;

namespace SS14LocalMods;

public static partial class Mod
{
    private static Type _healthBarsType = null!, _healthOverlayType = null!;
    private static FieldInfo _jobActive = null!, _jobHud = null!, _barOverlay = null!, _barManager = null!,
        _barActive = null!, _barContainers = null!, _barStatusIcon = null!;
    private static object? _healthBarsSystem;
    private static object _medicalStatusIcon = null!;

    private static IEnumerable<(MethodInfo Target, string Handler, bool Prefix)> HudHooks(Assembly content)
    {
        _healthBarsType = content.GetType("Content.Client.Overlays.ShowHealthBarsSystem", true)!;
        _healthOverlayType = content.GetType("Content.Client.Overlays.EntityHealthBarOverlay", true)!;
        var shared = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Robust.Shared");
        _ = Required(shared.GetType("Robust.Shared.GameObjects.IEntitySystemManager", true)!, "GetEntitySystem");
        var managerType = Engine.GetType("Robust.Client.Graphics.IOverlayManager", true)!;
        var overlayType = Engine.GetType("Robust.Client.Graphics.Overlay", true)!;
        foreach (var (name, argument) in new[] { ("AddOverlay", overlayType), ("RemoveOverlay", overlayType), ("HasOverlay", typeof(Type)), ("GetOverlay", typeof(Type)) })
            _ = managerType.GetMethod(name, [argument]) ?? throw new MissingMethodException(managerType.FullName, name);
        var job = content.GetType("Content.Client.Overlays.ShowJobIconsSystem", true)!;
        var status = content.GetType("Content.Client.Access.Systems.JobStatusSystem", true)!;
        _jobActive = ActiveField(job);
        _barActive = ActiveField(_healthBarsType);
        _barOverlay = AccessTools.Field(_healthBarsType, "_overlay") ?? throw new MissingFieldException(_healthBarsType.FullName, "_overlay");
        _barManager = AccessTools.Field(_healthBarsType, "_overlayMan") ?? throw new MissingFieldException(_healthBarsType.FullName, "_overlayMan");
        _barContainers = AccessTools.Field(_healthOverlayType, "DamageContainers") ?? throw new MissingFieldException(_healthOverlayType.FullName, "DamageContainers");
        _barStatusIcon = AccessTools.Field(_healthOverlayType, "StatusIcon") ?? throw new MissingFieldException(_healthOverlayType.FullName, "StatusIcon");
        if (_barContainers.FieldType != typeof(HashSet<string>) || _barOverlay.FieldType != _healthOverlayType)
            throw new InvalidOperationException("Unsupported native health bar overlay.");
        var iconType = Nullable.GetUnderlyingType(_barStatusIcon.FieldType) ?? throw new InvalidOperationException("Unsupported health bar visibility prototype.");
        _medicalStatusIcon = Activator.CreateInstance(iconType, ["HealthIconFine"])!;
        _jobHud = AccessTools.Field(status, "_showJobIcons") ?? throw new MissingFieldException(status.FullName, "_showJobIcons");
        if (_jobHud.FieldType != job) throw new InvalidOperationException("Unsupported job HUD dependency.");
        var draw = _healthOverlayType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Single(m => m.Name == "Draw" && m.GetParameters().Length == 1
                && m.GetParameters()[0].ParameterType.FullName == "Robust.Client.Graphics.OverlayDrawArgs&");
        yield return (draw, nameof(BeforeHealthBarDraw), true);
        yield return (Required(_healthBarsType, "DeactivateInternal"), nameof(KeepHealthBars), false);
        yield return (Required(status, "OnGetStatusIconsEvent"), nameof(BeforeHudIcons), true);
    }

    private static FieldInfo ActiveField(Type type)
    {
        var field = AccessTools.Field(type, "<IsActive>k__BackingField") ?? throw new MissingFieldException(type.FullName, "IsActive backing field");
        if (field.FieldType != typeof(bool)) throw new InvalidOperationException("Unsupported equipment HUD activation.");
        return field;
    }

    private static void SyncHealthBars()
    {
        if (HealthHud && _inGame && _healthBarsSystem == null)
            _healthBarsSystem = Call(Resolve("Robust.Shared.GameObjects.IEntitySystemManager"), "GetEntitySystem", _healthBarsType)!;
        if (_healthBarsSystem == null) return;
        var overlay = _barOverlay.GetValue(_healthBarsSystem)!;
        var manager = _barManager.GetValue(_healthBarsSystem)!;
        if (HealthHud && _inGame)
            Call(manager, "AddOverlay", overlay); // Native manager prevents duplicate overlays.
        else if (!(bool)_barActive.GetValue(_healthBarsSystem)! && (bool)Call(manager, "HasOverlay", _healthOverlayType)!
            && ReferenceEquals(Call(manager, "GetOverlay", _healthOverlayType), overlay))
            Call(manager, "RemoveOverlay", overlay);
    }

    public static void KeepHealthBars(object __instance)
    {
        if (_inGame && HealthHud && ReferenceEquals(__instance, _healthBarsSystem)) SyncHealthBars();
    }

    public sealed record BarState(object Overlay, HashSet<string> Containers, object? StatusIcon);

    // Reuse the actual native renderer and damage/threshold calculation. Override
    // only draw configuration; restore it even when native drawing throws.
    public static void BeforeHealthBarDraw(object __instance, out BarState? __state)
    {
        __state = null;
        if (!_inGame || !HealthHud || _healthBarsSystem == null || !ReferenceEquals(__instance, _barOverlay.GetValue(_healthBarsSystem))) return;
        var containers = (HashSet<string>)_barContainers.GetValue(__instance)!;
        var icon = _barStatusIcon.GetValue(__instance);
        __state = new(__instance, containers, icon);
        _barContainers.SetValue(__instance, new HashSet<string>(containers) { "Biological" });
        // Native medical glasses use this prototype for visibility/stealth rules.
        if (icon == null) _barStatusIcon.SetValue(__instance, _medicalStatusIcon);
    }

    public static void RestoreHealthBarDraw(BarState? __state)
    {
        if (__state == null) return;
        _barContainers.SetValue(__state.Overlay, __state.Containers);
        _barStatusIcon.SetValue(__state.Overlay, __state.StatusIcon);
    }

    public sealed record HudState(object System, bool WasActive);
    public static void BeforeHudIcons(object __instance, out HudState? __state)
    {
        __state = null;
        if (!_inGame || !JobHud) return;
        var system = _jobHud.GetValue(__instance)!;
        __state = new(system, (bool)_jobActive.GetValue(system)!);
        _jobActive.SetValue(system, true);
    }
    public static void RestoreHudIcons(HudState? __state)
    {
        if (__state != null) _jobActive.SetValue(__state.System, __state.WasActive);
    }
}
