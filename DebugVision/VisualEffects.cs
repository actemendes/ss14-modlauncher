using System.Reflection;
using static SS14LocalMods.Conversations.NativeUi;

namespace SS14LocalMods;

public static partial class Mod
{
    private static FieldInfo _blindLightSetup = null!, _blindGraceFrame = null!;

    private static IEnumerable<(MethodInfo Target, string Handler, bool Prefix)> EffectHooks(Assembly content)
    {
        foreach (var (name, required) in new (string, bool)[]
        {
            ("Content.Client.Flash.FlashOverlay", true),
            ("Content.Client.Eye.Blinding.BlindOverlay", true),
            ("Content.Client.Eye.Blinding.BlurryVisionOverlay", true),
            ("Content.Client.Drunk.DrunkOverlay", false),
            ("Content.Client.Drowsiness.DrowsinessOverlay", false),
            ("Content.Client.Drugs.RainbowOverlay", false),
            ("Content.Client.UserInterface.Systems.DamageOverlays.Overlays.DamageOverlay", false)
        })
        {
            var type = content.GetType(name);
            var draw = type?.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .SingleOrDefault(m => m.Name == "Draw" && m.GetParameters().Length == 1
                    && m.GetParameters()[0].ParameterType.FullName == "Robust.Client.Graphics.OverlayDrawArgs&");
            if (draw == null)
            {
                if (required) throw new MissingMethodException(name, "Draw");
                Bootstrap.Log("Debug Vision: optional effect overlay unavailable: " + name);
                continue;
            }
            var blind = name == "Content.Client.Eye.Blinding.BlindOverlay";
            if (blind)
            {
                var component = type!.GetField("_blindableComponent", BindingFlags.Instance | BindingFlags.NonPublic)?.FieldType
                    ?? throw new MissingFieldException(name, "_blindableComponent");
                _blindLightSetup = component.GetField("LightSetup") ?? throw new MissingFieldException(component.FullName, "LightSetup");
                _blindGraceFrame = component.GetField("GraceFrame") ?? throw new MissingFieldException(component.FullName, "GraceFrame");
                _ = type.GetField("_lightManager", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new MissingFieldException(name, "_lightManager");
                _ = Setter(Engine.GetType("Robust.Client.Graphics.LightManager", true)!, "Enabled");
            }
            yield return (draw, blind ? nameof(DrawBlindEffect) : nameof(DrawVisualEffect), true);
        }
    }

    // Keep overlay registration, BeforeDraw and FrameUpdate intact so an active
    // effect resumes at its current native state when protection is switched off.
    // No OverlayDrawArgs is boxed: the engine passes it as a byref-like struct.
    public static bool DrawVisualEffect() => !_inGame || !NoEffects;

    public static bool DrawBlindEffect(object ____blindableComponent, object ____lightManager)
    {
        if (DrawVisualEffect()) return true;
        // BlindOverlay may already have disabled lighting before protection was
        // enabled. Undo only its render bookkeeping, leaving IsBlind/EyeDamage intact.
        if ((bool)_blindLightSetup.GetValue(____blindableComponent)!)
        {
            Set(____lightManager, "Enabled", true);
            _blindLightSetup.SetValue(____blindableComponent, false);
        }
        _blindGraceFrame.SetValue(____blindableComponent, false);
        return false;
    }
}
