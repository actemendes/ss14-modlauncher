using System.Reflection;
using System.Numerics;
using HarmonyLib;

namespace SS14LocalMods;

// Independent client window. No PDA hooks, cartridge entities or network messages.
public static class Mod
{
    private static Assembly _engine = null!;
    private static object? _window;
    private static readonly HashSet<string> ConsumedKeys = [];
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Install(Assembly content)
    {
        _engine = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Robust.Client");
        var input = _engine.GetType("Robust.Client.Input.InputManager", true)!;
        var harmony = new Harmony("local.window.hello-world");
        harmony.Patch(input.GetMethod("KeyDown")!, prefix: new HarmonyMethod(typeof(Mod), nameof(KeyDown)));
        harmony.Patch(input.GetMethod("KeyUp")!, prefix: new HarmonyMethod(typeof(Mod), nameof(KeyUp)));
        Bootstrap.Log("Hello World independent window hotkeys installed: F1 / 0 / numpad 0.");
    }

    public static bool KeyDown(object __0)
    {
        var key = Get(__0, "Key")?.ToString() ?? "";
        if (key is not ("F1" or "Num0" or "NumpadNum0")) return true;
        if (ConsumedKeys.Contains(key)) return false;
        if ((bool)Get(__0, "Alt")! || (bool)Get(__0, "Control")! || (bool)Get(__0, "Shift")!) return true;
        try
        {
            var focused = Get(UIManager(), "KeyboardFocused");
            // Keep numeric text entry and console/chat shortcuts working.
            for (var type = focused?.GetType(); type != null; type = type.BaseType)
                if (type.Name is "LineEdit" or "TextEdit") return true;
            if (!(bool)Get(__0, "IsRepeat")!) Toggle();
            ConsumedKeys.Add(key);
            return false;
        }
        catch (Exception e)
        {
            Bootstrap.Log("Window toggle failed: " + e.GetBaseException());
            return true;
        }
    }

    public static bool KeyUp(object __0) => !ConsumedKeys.Remove(Get(__0, "Key")?.ToString() ?? "");

    private static void Toggle()
    {
        if (_window == null)
        {
            _window = Activator.CreateInstance(_engine.GetType("Robust.Client.UserInterface.CustomControls.DefaultWindow", true)!)!;
            Set(_window, "Title", "Hello World — Local mod");
            Set(_window, "SetSize", new Vector2(360, 180));
            var label = Activator.CreateInstance(_engine.GetType("Robust.Client.UserInterface.Controls.Label", true)!)!;
            Set(label, "Text", "Hello World?");
            Set(label, "HorizontalAlignment", "Center");
            Set(label, "VerticalAlignment", "Center");
            Set(label, "HorizontalExpand", true);
            Set(label, "VerticalExpand", true);
            var contents = Get(_window, "Contents")!;
            contents.GetType().GetMethod("AddChild")!.Invoke(contents, [label]);
        }
        var isOpen = (bool)Get(_window, "IsOpen")!;
        _window.GetType().GetMethod(isOpen ? "Close" : "OpenCentered", Type.EmptyTypes)!.Invoke(_window, null);
        Bootstrap.Log("Hello World window " + (isOpen ? "closed" : "opened") + " locally.");
    }

    private static object UIManager()
    {
        var shared = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Robust.Shared");
        return shared.GetType("Robust.Shared.IoC.IoCManager", true)!.GetMethod("ResolveType")!
            .Invoke(null, [_engine.GetType("Robust.Client.UserInterface.IUserInterfaceManager", true)!])!;
    }
    private static object? Get(object target, string name)
    {
        var type = target.GetType();
        var property = type.GetProperty(name, Flags);
        if (property != null) return property.GetValue(target);
        return type.GetField(name, Flags)?.GetValue(target) ?? throw new MissingMemberException(type.FullName, name);
    }
    private static void Set(object target, string name, object value)
    {
        var property = target.GetType().GetProperty(name, Flags) ?? throw new MissingMemberException(name);
        property.SetValue(target, property.PropertyType.IsEnum ? Enum.Parse(property.PropertyType, (string)value) : value);
    }
}
