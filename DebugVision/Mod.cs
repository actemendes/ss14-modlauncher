using System.Numerics;
using System.Reflection;
using HarmonyLib;
using static SS14LocalMods.Conversations.NativeUi;

namespace SS14LocalMods;

public static partial class Mod
{
    private static bool _installed, _inGame, _writingZoom;
    private static object? _eyeManager, _lightManager, _playerManager, _timing, _window, _zoomLabel, _zoomEye;
    private static float _zoomStep = 1.5f;
    private static float _displayZoom = 1;
    private static Vector2 _nativeZoom;
    private static readonly HashSet<string> ConsumedKeys = [];
    private static readonly Dictionary<string, object> Toggles = [];
    public static bool Omnivision { get; private set; }
    public static bool Fullbright { get; private set; }
    public static bool NoShadows { get; private set; }
    public static bool NoEffects { get; private set; }
    public static bool HealthHud { get; private set; }
    public static bool JobHud { get; private set; }
    public static bool ExpandedZoom => _inGame;
    public static float Zoom { get; private set; } = 1;
    // A finite, positive range keeps projection matrices valid. These are rendering
    // safeguards, independent of the server's normal camera limits.
    public const float MinimumZoom = 0.05f, MaximumZoom = 32f;

    public static void Install(Assembly content)
    {
        if (_installed) return;
        Engine = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Robust.Client");
        var shared = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Robust.Shared");
        var eye = shared.GetType("Robust.Shared.Graphics.Eye", true)!;
        var manager = Engine.GetType("Robust.Client.Graphics.EyeManager", true)!;
        var light = Engine.GetType("Robust.Client.Graphics.LightManager", true)!;
        var input = Engine.GetType("Robust.Client.Input.InputManager", true)!;
        var game = content.GetType("Content.Client.Gameplay.GameplayState", true)!;
        var eyeLerping = content.GetType("Content.Client.Eye.EyeLerpingSystem", true)!;
        var frameUpdate = AccessTools.Method(eyeLerping, "FrameUpdate", [typeof(float)])
            ?? throw new MissingMethodException(eyeLerping.FullName, "FrameUpdate");
        _ = Getter(shared.GetType("Robust.Shared.Timing.IGameTiming", true)!, "IsFirstTimePredicted");
        var contentEye = content.GetType("Content.Client.Movement.Systems.ContentEyeSystem", true)!.BaseType!;
        var nativeZoom = new[] { "ZoomIn", "ZoomOut", "ResetZoom" }.Select(name =>
            contentEye.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Single(m => m.Name == name && m.GetParameters().Length == 1
                    && m.GetParameters()[0].ParameterType.FullName == "Robust.Shared.Player.ICommonSession")).ToArray();
        _zoomStep = (float)(contentEye.GetField("ZoomMod")?.GetRawConstantValue()
            ?? throw new MissingFieldException(contentEye.FullName, "ZoomMod"));
        if (!float.IsFinite(_zoomStep) || _zoomStep <= 1) throw new InvalidOperationException("Unsupported native zoom step.");
        // Resolve the entire contract before installing any hooks; roll back on failure.
        var hooks = new (MethodInfo Target, string Handler, bool Prefix)[]
        {
            (Getter(eye, "DrawFov"), nameof(ReadFov), false),
            (Setter(eye, "Zoom"), nameof(WriteZoom), true),
            (Setter(eye, "Scale"), nameof(WriteScale), true),
            (Setter(manager, "CurrentEye"), nameof(BeforeEyeChange), true),
            (Setter(manager, "CurrentEye"), nameof(AfterEyeChange), false),
            (Getter(light, "DrawLighting"), nameof(ReadLighting), false),
            (Getter(light, "DrawShadows"), nameof(ReadShadows), false),
            (Required(input, "KeyDown"), nameof(KeyDown), true),
            (Required(input, "KeyUp"), nameof(KeyUp), true),
            (Required(game, "Startup"), nameof(GameStarted), false),
            (Required(game, "Shutdown"), nameof(GameStopped), true),
            (frameUpdate, nameof(AnimateZoom), false)
        };
        hooks = [.. hooks, .. nativeZoom.SelectMany(method => new[]
        {
            (method, nameof(BeforeNativeZoom), true), (method, nameof(AfterNativeZoom), false)
        })];
        hooks = [.. hooks, .. EffectHooks(content)];
        hooks = [.. hooks, .. HudHooks(content)];
        var harmony = new Harmony("local.debug-vision.camera");
        try
        {
            foreach (var (target, handler, prefix) in hooks)
            {
                var patch = new HarmonyMethod(typeof(Mod), handler);
                if (handler is nameof(KeyDown) or nameof(KeyUp))
                {
                    // F1 belongs to Debug Vision during gameplay, even when the
                    // independently shipped Hello World mod is also selected.
                    patch.priority = Priority.First;
                    patch.before = ["local.window.hello-world"];
                }
                harmony.Patch(target, prefix: prefix ? patch : null, postfix: prefix ? null : patch,
                    finalizer: handler == nameof(BeforeHudIcons) ? new HarmonyMethod(typeof(Mod), nameof(RestoreHudIcons)) : null);
            }
            _installed = true;
            Bootstrap.Log("Debug Vision: F1 panel, FOV, lighting, shadows, visual effect protection, health/job HUD and local zoom installed.");
        }
        catch { harmony.UnpatchAll(harmony.Id); throw; }
    }

    private static MethodInfo Getter(Type type, string name) => AccessTools.PropertyGetter(type, name)
        ?? throw new MissingMethodException(type.FullName, "get_" + name);
    private static MethodInfo Setter(Type type, string name) => AccessTools.PropertySetter(type, name)
        ?? throw new MissingMethodException(type.FullName, "set_" + name);
    private static MethodInfo Required(Type type, string name) => AccessTools.Method(type, name)
        ?? throw new MissingMethodException(type.FullName, name);
    private static object Resolve(string name)
    {
        var shared = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Robust.Shared");
        return shared.GetType("Robust.Shared.IoC.IoCManager", true)!.GetMethod("ResolveType", [typeof(Type)])!
            .Invoke(null, [Engine.GetType(name) ?? shared.GetType(name, true)!])!;
    }
    private static object CurrentEye => Get(_eyeManager!, "CurrentEye")!;
    public static void GameStarted()
    {
        try
        {
            Reset();
            _eyeManager = Resolve("Robust.Client.Graphics.IEyeManager");
            _lightManager = Resolve("Robust.Client.Graphics.ILightManager");
            _playerManager = Resolve("Robust.Client.Player.IPlayerManager");
            _timing = Resolve("Robust.Shared.Timing.IGameTiming");
            _inGame = true;
            Zoom = _displayZoom = 1; ApplyZoom();
        }
        catch (Exception e) { Bootstrap.Log("Debug Vision unavailable: " + e.GetBaseException().Message); }
    }
    public static void GameStopped()
    {
        _inGame = false;
        try { Reset(); }
        catch (Exception e) { Bootstrap.Log("Debug Vision reset failed: " + e.GetBaseException().Message); }
        finally
        {
            _eyeManager = _lightManager = _playerManager = _timing = null;
            // Retain consumed releases until KeyUp so F1 cannot leak a half-keypress.
            if (_window != null)
            {
                try { Call(_window, "Dispose"); } catch { }
                _window = _zoomLabel = null; Toggles.Clear();
            }
        }
    }
    public static void ReadFov(object __instance, ref bool __result)
    {
        if (_inGame && Omnivision && ReferenceEquals(__instance, CurrentEye)) __result = false;
    }
    public static void ReadLighting(object __instance, ref bool __result)
    {
        if (_inGame && Fullbright && ReferenceEquals(__instance, _lightManager)) __result = false;
    }
    public static void ReadShadows(object __instance, ref bool __result)
    {
        if (_inGame && NoShadows && ReferenceEquals(__instance, _lightManager)) __result = false;
    }
    public static void WriteZoom(object __instance, ref Vector2 __0)
    {
        if (!_writingZoom && ExpandedZoom && ReferenceEquals(__instance, _zoomEye))
        {
            _nativeZoom = __0;
            __0 = new Vector2(_displayZoom);
        }
    }
    public static void WriteScale(object __instance, ref Vector2 __0)
    {
        if (!_writingZoom && ExpandedZoom && ReferenceEquals(__instance, _zoomEye))
        {
            _nativeZoom = Vector2.One / __0;
            __0 = new Vector2(1 / _displayZoom);
        }
    }
    public static void BeforeNativeZoom(object? __0, out float? __state)
    {
        // Hook the game's commands, not physical keys: custom keyboard/mouse
        // bindings still dispatch normally and the original handler still runs.
        // The engine replays pending input on each prediction pass. The local
        // render target is not rolled back, so only the first dispatch can step it.
        __state = _inGame && __0 != null && ReferenceEquals(__0, Get(_playerManager!, "LocalSession"))
            && (bool)Get(_timing!, "IsFirstTimePredicted")! ? Zoom : null;
    }
    public static void AfterNativeZoom(MethodBase __originalMethod, float? __state)
    {
        if (__state is not { } previous) return;
        SetZoom(__originalMethod.Name switch { "ZoomIn" => previous / _zoomStep, "ZoomOut" => previous * _zoomStep, _ => 1f });
    }
    public static void BeforeEyeChange() => RestoreZoom();
    public static void AfterEyeChange()
    {
        if (_inGame && ExpandedZoom) ApplyZoom();
    }
    public static void AnimateZoom(float __0)
    {
        if (!_inGame || !float.IsFinite(__0) || __0 <= 0) return;
        // Match the native eye lerper's response rate (8/s), with a frame-rate
        // independent approach to each discrete target rather than an instant jump.
        _displayZoom += (Zoom - _displayZoom) * (1 - MathF.Exp(-8 * __0));
        if (MathF.Abs(Zoom - _displayZoom) < 0.0001f) _displayZoom = Zoom;
        ApplyZoom();
    }
    private static void SetEyeZoom(object eye, Vector2 value)
    {
        _writingZoom = true;
        try { Set(eye, "Zoom", value); }
        finally { _writingZoom = false; }
    }
    private static void ApplyZoom()
    {
        var eye = CurrentEye;
        if (!ReferenceEquals(_zoomEye, eye))
        {
            RestoreZoom(); _nativeZoom = (Vector2)Get(eye, "Zoom")!; _zoomEye = eye;
        }
        SetEyeZoom(eye, new Vector2(_displayZoom));
    }
    private static void RestoreZoom()
    {
        if (_zoomEye == null) return;
        var eye = _zoomEye; _zoomEye = null;
        SetEyeZoom(eye, _nativeZoom);
    }
    public static void SetOption(string option, bool enabled)
    {
        if (!_inGame) return;
        switch (option)
        {
            case "omni": Omnivision = enabled; break;
            case "light": Fullbright = enabled; break;
            case "shadows": NoShadows = enabled; break;
            case "effects": NoEffects = enabled; break;
            case "health": HealthHud = enabled; break;
            case "job": JobHud = enabled; break;
            default: throw new ArgumentException("Unknown debug vision option.", nameof(option));
        }
        Refresh();
    }
    public static void SetZoom(float value)
    {
        if (!_inGame || !ExpandedZoom || !float.IsFinite(value) || value <= 0) return;
        Zoom = Math.Clamp(value, MinimumZoom, MaximumZoom); ApplyZoom(); Refresh();
    }
    public static void Reset()
    {
        Omnivision = Fullbright = NoShadows = NoEffects = HealthHud = JobHud = false;
        RestoreZoom(); Zoom = _displayZoom = 1;
        if (_inGame) ApplyZoom();
        Refresh();
    }

    public static bool KeyDown(object __0)
    {
        var key = Get(__0, "Key")?.ToString() ?? "";
        if (ConsumedKeys.Contains(key)) return false;
        if (!_inGame) return true;
        try
        {
            if ((bool)Get(__0, "Alt")! || (bool)Get(__0, "Shift")!) return true;
            var control = (bool)Get(__0, "Control")!;
            var action = (key, control) switch
            {
                ("F1", false) => "panel", ("N", true) => "omni", ("L", true) => "light",
                ("H", true) => "shadows", ("B", true) => "effects", ("R", true) => "reset", _ => null
            };
            if (action == null) return true;
            var ui = Resolve("Robust.Client.UserInterface.IUserInterfaceManager");
            for (var type = Get(ui, "KeyboardFocused")?.GetType(); type != null; type = type.BaseType)
                if (type.Name is "LineEdit" or "TextEdit") return true;
            if (!(bool)Get(__0, "IsRepeat")!)
            {
                if (action == "panel") ToggleWindow();
                else if (action == "reset") Reset();
                else SetOption(action, action switch { "omni" => !Omnivision, "light" => !Fullbright, "effects" => !NoEffects, _ => !NoShadows });
            }
            ConsumedKeys.Add(key); return false;
        }
        catch (Exception e)
        {
            Bootstrap.Log("Debug Vision action failed: " + e.GetBaseException().Message); return true;
        }
    }
    public static bool KeyUp(object __0) => !ConsumedKeys.Remove(Get(__0, "Key")?.ToString() ?? "");
    private static void ToggleWindow()
    {
        if (_window == null || (bool)Get(_window, "Disposed")!) CreateWindow();
        Refresh();
        Call(_window!, (bool)Get(_window!, "IsOpen")! ? "Close" : "OpenCentered");
    }
    private static void CreateWindow()
    {
        object? window = null;
        try
        {
            window = Activator.CreateInstance(Engine.GetType("Robust.Client.UserInterface.CustomControls.DefaultWindow", true)!)!;
            Set(window, "Title", T("Дебаг-видение · F1", "Debug Vision · F1"));
            Set(window, "SetSize", new Vector2(460, 430));
            var panel = Box("Vertical"); Set(panel, "SeparationOverride", 8);
            Add(Get(window, "Contents")!, panel);
            AddToggle(panel, "omni", T("Омнивизион · Ctrl+N", "Omnivision · Ctrl+N"));
            AddToggle(panel, "light", T("Полная яркость · Ctrl+L", "Fullbright · Ctrl+L"));
            AddToggle(panel, "shadows", T("Отключить тени · Ctrl+H", "Disable shadows · Ctrl+H"));
            AddToggle(panel, "effects", T("Защита от визуальных эффектов · Ctrl+B", "Visual effect protection · Ctrl+B"));
            AddToggle(panel, "health", T("HUD: состояние здоровья", "HUD: health status"));
            AddToggle(panel, "job", T("HUD: иконка профессии", "HUD: job icon"));
            Add(panel, Label(T("Расширенный зум всегда включён", "Extended zoom is always enabled")));
            var row = Box("Horizontal");
            Add(row, Button(T("Приблизить +", "Zoom in +"), () => SetZoom(Zoom / _zoomStep)));
            _zoomLabel = Label(""); Add(row, _zoomLabel);
            Add(row, Button(T("Отдалить −", "Zoom out −"), () => SetZoom(Zoom * _zoomStep)));
            Add(panel, row);
            var presets = Box("Horizontal");
            foreach (var value in new[] { 0.25f, 0.5f, 1f, 2f, 4f, 8f })
                Add(presets, Button(value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), () => SetZoom(value)));
            Add(panel, presets);
            Add(panel, Label(T("Зум: штатные клавиши и кнопки + / −", "Zoom: native bindings and + / − buttons")));
            Add(panel, Label(T("Видны только данные, переданные сервером.", "Only data received from the server is visible.")));
            Add(panel, Button(T("Сбросить всё · Ctrl+R", "Reset all · Ctrl+R"), Reset));
            _window = window;
        }
        catch
        {
            if (window != null) { try { Call(window, "Dispose"); } catch { } }
            _window = _zoomLabel = null; Toggles.Clear(); throw;
        }
    }
    private static void AddToggle(object panel, string name, string title)
    {
        var button = Button(title, () => SetOption(name, (bool)Get(Toggles[name], "Pressed")!), true);
        Set(button, "HorizontalExpand", true); Toggles[name] = button; Add(panel, button);
    }
    private static void Refresh()
    {
        foreach (var (name, button) in Toggles)
            Set(button, "Pressed", name switch { "omni" => Omnivision, "light" => Fullbright, "shadows" => NoShadows, "effects" => NoEffects, "health" => HealthHud, "job" => JobHud, _ => ExpandedZoom });
        if (_zoomLabel != null)
            Set(_zoomLabel, "Text", ExpandedZoom ? $"{Zoom:0.##}" : T("Штатный", "Native"));
    }
}
