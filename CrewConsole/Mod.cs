using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using HarmonyLib;
using SS14LocalMods.CrewConsole;

namespace SS14LocalMods;

// Stage 1: adaptive native shell around the existing subscription and map.
// No extra UI messages. No polling, process memory attachment or HTML runtime.
public static class Mod
{
    private sealed class View
    {
        public required object Body;
        public required object Map;
        public required object List;
        public required object Summary;
        public bool Reflowing;
        public bool? Portrait;
    }
    private static readonly ConditionalWeakTable<object, View> Views = new();
    private static Assembly _engine = null!;
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    public static void Install(Assembly content)
    {
        var type = content.GetType("Content.Client.Medical.CrewMonitoring.CrewMonitoringWindow");
        if (type == null) { Bootstrap.Log("Crew Console: unsupported content (no monitoring window)."); return; }
        _engine = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Robust.Client");
        var harmony = new Harmony("local.crew-console.shell");
        harmony.Patch(type.GetConstructor(Type.EmptyTypes)!, postfix: new HarmonyMethod(typeof(Mod), nameof(Created)));
        harmony.Patch(type.GetMethod("ShowSensors")!, postfix: new HarmonyMethod(typeof(Mod), nameof(SensorsUpdated)));
        Bootstrap.Log("Crew Console adaptive shell installed.");
    }

    public static void Created(object __instance)
    {
        try
        {
            var contents = Get(__instance, "ContentsContainer")!;
            var map = Get(__instance, "NavMap")!;
            var search = Get(__instance, "SearchLineEdit")!;
            var scroller = Get(__instance, "SensorScroller")!;
            var station = Get(__instance, "StationName")!;
            foreach (var item in new[] { map, search, scroller, station }) Call(item, "Orphan");
            Call(contents, "RemoveAllChildren");
            var root = Box("Vertical");
            var summary = New("Label");
            Set(summary, "Text", "Датчики: ожидание данных");
            Add(root, station); Add(root, summary);
            var toolbar = Box("Horizontal");
            Set(toolbar, "VerticalExpand", false);
            Add(toolbar, ResizeButton("Альбом", __instance, new Vector2(1000, 650)));
            Add(toolbar, ResizeButton("Портрет", __instance, new Vector2(560, 800)));
            Add(root, toolbar);
            var body = Box("Horizontal");
            var list = Box("Vertical");
            Add(list, search); Add(list, scroller);
            Set(scroller, "SetWidth", float.NaN);
            Set(scroller, "HorizontalExpand", true);
            Add(body, map); Add(body, list); Add(root, body); Add(contents, root);
            Set(__instance, "Title", "Crew Monitor — адаптивная консоль");
            Set(__instance, "Resizable", true);
            Set(__instance, "MinSize", new Vector2(360, 360));
            Set(__instance, "SetSize", new Vector2(1000, 650));
            var view = new View { Body = body, Map = map, List = list, Summary = summary };
            Views.Add(__instance, view);
            __instance.GetType().GetEvent("OnResized")!.AddEventHandler(__instance, (Action)(() => Reflow(__instance, view)));
            Reflow(__instance, view);
            Bootstrap.Log("Crew Console created; native sensor subscription preserved.");
        }
        catch (Exception e) { Bootstrap.Log("Crew Console shell failed: " + e.GetBaseException()); }
    }

    public static void SensorsUpdated(object __instance, object __0)
    {
        try
        {
            if (!Views.TryGetValue(__instance, out var view)) return;
            var unique = new Dictionary<object, object>();
            foreach (var sensor in ((IEnumerable)__0).Cast<object>())
            {
                var owner = Get(sensor, "OwnerUid")!;
                if (unique.TryGetValue(owner, out var previous))
                {
                    if (Get(previous, "Coordinates") != null && Get(sensor, "Coordinates") == null) continue;
                    if (Get(previous, "DamagePercentage") != null && Get(sensor, "DamagePercentage") == null) continue;
                }
                unique[owner] = sensor;
            }
            var sensors = unique.Values.ToList();
            var dead = sensors.Count(s => Get(s, "IsAlive") is false);
            var injured = sensors.Count(s => Get(s, "IsAlive") is true && Convert.ToDouble(Get(s, "DamagePercentage") ?? 0) >= .25);
            Set(view.Summary, "Text", $"Датчики: {sensors.Count}  •  Ранены: {injured}  •  Погибли: {dead}");
        }
        catch (Exception e) { Bootstrap.Log("Crew Console state read failed: " + e.GetBaseException()); }
    }

    private static void Reflow(object window, View view)
    {
        if (view.Reflowing) return;
        view.Reflowing = true;
        try
        {
            var size = (Vector2)Get(window, "Size")!;
            var layout = LayoutPolicy.Calculate(size.X, size.Y);
            Set(view.Body, "Orientation", layout.Portrait ? "Vertical" : "Horizontal");
            Set(view.Map, "SetHeight", layout.Portrait ? layout.MapHeight : float.NaN);
            Set(view.Map, "VerticalExpand", !layout.Portrait);
            Set(view.List, "SetWidth", layout.Portrait ? float.NaN : layout.ListWidth);
            Set(view.List, "VerticalExpand", true);
            if (view.Portrait != layout.Portrait)
            {
                view.Portrait = layout.Portrait;
                Bootstrap.Log("Crew Console layout: " + (layout.Portrait ? "portrait" : "landscape"));
            }
        }
        finally { view.Reflowing = false; }
    }
    private static object Box(string orientation)
    {
        var box = New("BoxContainer"); Set(box, "Orientation", orientation);
        Set(box, "HorizontalExpand", true); Set(box, "VerticalExpand", true); return box;
    }
    private static object ResizeButton(string text, object window, Vector2 size)
    {
        var button = New("Button");
        Set(button, "Text", text);
        var pressed = button.GetType().GetEvent("OnPressed")!;
        var handler = pressed.EventHandlerType!;
        var parameters = handler.GetMethod("Invoke")!.GetParameters()
            .Select(p => Expression.Parameter(p.ParameterType, p.Name)).ToArray();
        Action resize = () => Set(window, "SetSize", size);
        var callback = Expression.Lambda(handler,
            Expression.Call(Expression.Constant(resize), typeof(Action).GetMethod("Invoke")!), parameters).Compile();
        pressed.AddEventHandler(button, callback);
        return button;
    }
    private static object New(string name) => Activator.CreateInstance(_engine.GetType("Robust.Client.UserInterface.Controls." + name, true)!)!;
    private static object? Get(object target, string name)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var property = type.GetProperty(name, Flags); if (property != null) return property.GetValue(target);
            var field = type.GetField(name, Flags); if (field != null) return field.GetValue(target);
        }
        throw new MissingMemberException(target.GetType().FullName, name);
    }
    private static void Set(object target, string name, object? value)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var property = type.GetProperty(name, Flags);
            if (property == null) continue;
            property.SetValue(target, property.PropertyType.IsEnum ? Enum.Parse(property.PropertyType, (string)value!) : value); return;
        }
        throw new MissingMemberException(target.GetType().FullName, name);
    }
    private static void Call(object target, string name) => target.GetType().GetMethod(name, Type.EmptyTypes)!.Invoke(target, null);
    private static void Add(object parent, object child) => parent.GetType().GetMethod("AddChild")!.Invoke(parent, [child]);
}
