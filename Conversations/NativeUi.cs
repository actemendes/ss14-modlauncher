using System.Linq.Expressions;
using System.Reflection;

namespace SS14LocalMods.Conversations;

internal static class NativeUi
{
    public static Assembly Engine = null!;
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    public static object New(string name) => Activator.CreateInstance(Engine.GetType("Robust.Client.UserInterface.Controls." + name, true)!)!;
    public static object? Get(object target, string name)
    {
        for (var t = target.GetType(); t != null; t = t.BaseType)
        {
            if (t.GetProperty(name, Flags) is { } p) return p.GetValue(target);
            if (t.GetField(name, Flags) is { } f) return f.GetValue(target);
        }
        throw new MissingMemberException(target.GetType().FullName, name);
    }
    public static void Set(object target, string name, object? value)
    {
        for (var t = target.GetType(); t != null; t = t.BaseType)
        {
            if (t.GetProperty(name, Flags) is not { } p) continue;
            p.SetValue(target, p.PropertyType.IsEnum ? Enum.Parse(p.PropertyType, (string)value!) : value);
            return;
        }
        throw new MissingMemberException(target.GetType().FullName, name);
    }
    public static object? Call(object target, string name, params object?[] args)
    {
        for (var t = target.GetType(); t != null; t = t.BaseType)
        {
            var m = t.GetMethods(Flags).FirstOrDefault(m => !m.IsGenericMethod && m.Name == name && m.GetParameters().Length == args.Length &&
                m.GetParameters().Select((p, i) => args[i] == null || p.ParameterType.IsInstanceOfType(args[i])).All(x => x));
            if (m != null) return m.Invoke(target, args);
        }
        throw new MissingMethodException(target.GetType().FullName, name);
    }
    public static void Add(object parent, object child) => Call(parent, "AddChild", child);
    public static void Hook(object target, string name, Action action)
    {
        var ev = target.GetType().GetEvent(name)!;
        var handler = ev.EventHandlerType!;
        var parameters = handler.GetMethod("Invoke")!.GetParameters().Select(p => Expression.Parameter(p.ParameterType, p.Name)).ToArray();
        ev.AddEventHandler(target, Expression.Lambda(handler,
            Expression.Call(Expression.Constant(action), typeof(Action).GetMethod("Invoke")!), parameters).Compile());
    }
    public static object Box(string orientation)
    {
        var box = New("BoxContainer"); Set(box, "Orientation", orientation);
        Set(box, "HorizontalExpand", true); Set(box, "SeparationOverride", 4); return box;
    }
    public static object Button(string text, Action action, bool toggle = false)
    {
        var button = New("Button"); Set(button, "Text", text); Set(button, "ToggleMode", toggle);
        Call(button, "AddStyleClass", "ChatFilterOptionButton");
        Hook(button, toggle ? "OnToggled" : "OnPressed", action); return button;
    }
    public static object Label(string text)
    {
        var label = New("Label"); Set(label, "Text", text); Set(label, "ClipText", true);
        Set(label, "HorizontalExpand", true); return label;
    }
    public static string T(string ru, string en) => Environment.GetEnvironmentVariable("SS14_MOD_LANGUAGE") == "en" ? en : ru;
}
