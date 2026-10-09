using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using HarmonyLib;
using SS14LocalMods.CrewConsole;
using static SS14LocalMods.CrewConsole.ModText;

namespace SS14LocalMods;

// Only data from the normal console subscription; all controls and history are local.
public static class Mod
{
    private sealed class View
    {
        public required object Window, Body, Map, Sidebar, Summary, People, Search, Details, Timeline, CursorLabel, MapArea, DetailScroll, TimelineScroll, Slider, AxisStart, AxisEnd, AxisMiddle, GraphTitle;
        public CrewHistory History = new();
        public string? Selected;
        public TimeSpan? Cursor;
        public bool HealthOnly, Reflowing, DrawFailed;
        public string Query = "";
        public Dictionary<string, object> NativeRows = new();
        public bool GraphFailed;
        public object? MapGrid;
        public Dictionary<object, object> LiveBlips = new();
        public List<Observation> Route = new();
    }
    private static readonly ConditionalWeakTable<object, View> Views = new();
    private static readonly ConditionalWeakTable<object, View> Maps = new();
    private static readonly ConditionalWeakTable<object, View> Graphs = new();
    private static Assembly _engine = null!;
    private static Type _color = null!, _thickness = null!;
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    public static void Install(Assembly content)
    {
        var type = content.GetType("Content.Client.Medical.CrewMonitoring.CrewMonitoringWindow");
        if (type == null) { Bootstrap.Log("Crew Console: unsupported content (no monitoring window)."); return; }
        _engine = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Robust.Client");
        _color = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Robust.Shared.Maths.Color")).First(t => t != null)!;
        _thickness = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Robust.Shared.Maths.Thickness")).First(t => t != null)!;
        var harmony = new Harmony("local.crew-console.shell");
        var gridControl = content.GetType("Content.Client.UserInterface.Controls.MapGridControl")!;
        harmony.Patch(gridControl.GetProperty("MidPointVector", Flags)!.GetMethod!, postfix: new HarmonyMethod(typeof(Mod), nameof(MapMidpoint)));
        harmony.Patch(gridControl.GetProperty("MinimapScale", Flags)!.GetMethod!, postfix: new HarmonyMethod(typeof(Mod), nameof(MapScale)));
        harmony.Patch(type.GetConstructor(Type.EmptyTypes)!, postfix: new HarmonyMethod(typeof(Mod), nameof(Created)));
        harmony.Patch(type.GetMethod("ShowSensors")!, postfix: new HarmonyMethod(typeof(Mod), nameof(SensorsUpdated)));
        harmony.Patch(content.GetType("Content.Client.Pinpointer.UI.NavMapControl")!.GetMethod("Draw", Flags)!,
            postfix: new HarmonyMethod(typeof(Mod), nameof(DrawRoute)));
        harmony.Patch(_engine.GetType("Robust.Client.UserInterface.Controls.PanelContainer")!.GetMethod("Draw", Flags)!,
            postfix: new HarmonyMethod(typeof(Mod), nameof(DrawTimeline)));
        Bootstrap.Log("Crew Console: compact native rows and health timeline installed.");
    }

    public static void Created(object __instance)
    {
        try
        {
            var contents = Get(__instance, "ContentsContainer")!;
            var map = Get(__instance, "NavMap")!;
            var station = Get(__instance, "StationName")!;
            // Build before detaching the native layout, so unsupported APIs leave it usable.
            var root = Box("Vertical");
            Set(root, "SeparationOverride", 8);
            var header = Box("Vertical", false);
            var summary = Label(T("Ожидание телеметрии…", "Waiting for telemetry…"), "#8d9ba9");
            Add(header, Label(T("CREW MONITOR  /  МОНИТОРИНГ ЭКИПАЖА", "CREW MONITOR  /  TELEMETRY CONSOLE"), "#64b5d6"));
            Add(header, summary); Add(root, Panel(header));
            var toolbar = Box("Horizontal", false);
            Add(toolbar, Button(T("Альбом", "Landscape"), () => Set(__instance, "SetSize", new Vector2(1180, 980))));
            Add(toolbar, Button(T("Портрет", "Portrait"), () => Set(__instance, "SetSize", new Vector2(600, 940))));
            Add(toolbar, Button(T("Новая история", "Reset history"), () => { if (Views.TryGetValue(__instance, out var v)) { v.History = new(); v.Selected = null; v.Cursor = null; Render(v); } }));
            Add(root, toolbar);
            var body = Box("Horizontal");
            var mapArea = Box("Vertical");
            Add(mapArea, Label(T("КАРТА  ·  маршрут выбранного персонажа", "MAP  ·  selected crew member's trail"), "#64b5d6"));
            var sidebar = Box("Vertical");
            var search = New("LineEdit"); Set(search, "PlaceHolder", T("Поиск по имени или должности…", "Search name or job…"));
            var people = Box("Vertical", false);
            var details = Box("Vertical", false);
            var timeline = New("PanelContainer");
            Set(timeline, "HorizontalExpand", true); Set(timeline, "SetHeight", 96f);
            Set(timeline, "PanelOverride", CardStyle("#0e151e", "#273442"));
            Set(timeline, "RectClipContent", true);
            var cursorLabel = Label(T("Выберите персонажа", "Select a crew member"), "#8d9ba9");
            var view = new View { Window = __instance, Body = body, Map = map, Sidebar = sidebar, Summary = summary,
                People = people, Search = search, Details = details, Timeline = timeline, CursorLabel = cursorLabel, MapArea = mapArea, DetailScroll = null!, TimelineScroll = null!, Slider = null!, AxisStart = null!, AxisEnd = null!, AxisMiddle = null!, GraphTitle = null! };
            var filters = Box("Horizontal", false);
            Add(filters, Button(T("Весь экипаж", "All crew"), () => { view.HealthOnly = false; Render(view); }));
            Add(filters, Button(T("Требуют помощи", "Need help"), () => { view.HealthOnly = true; Render(view); }));
            Add(sidebar, Label(T("ЭКИПАЖ  /  текущие данные", "CREW  /  current readings"), "#64b5d6"));
            Add(sidebar, search); Add(sidebar, filters);
            var peopleScroll = Scroll(people); Set(peopleScroll, "MinHeight", 80f);
            Add(sidebar, peopleScroll);
            var detailScroll = details; Set(detailScroll, "SetHeight", 56f); Set(detailScroll, "VerticalExpand", false);
            view.DetailScroll = detailScroll; Add(sidebar, Panel(detailScroll));
            Add(body, mapArea); Add(body, sidebar); Add(root, body);
            var historyHeader = Box("Vertical", false);
            Add(historyHeader, Label(T("ИСТОРИЯ  ·  выберите момент на шкале", "HISTORY  ·  select a point on the timeline"), "#64b5d6"));
            Add(historyHeader, cursorLabel); Add(root, historyHeader);
            var navigation = Box("Horizontal", false);
            Add(navigation, Button("|<", () => Seek(view, int.MinValue)));
            Add(navigation, Button(T("< Назад", "< Previous"), () => Seek(view, -1)));
            Add(navigation, Button(T("Вперёд >", "Next >"), () => Seek(view, 1)));
            Add(navigation, Button(T("Сейчас", "Live"), () => { view.Cursor = null; Render(view); }));
            Add(navigation, Button(T("На карте", "Center map"), () => Center(view)));
            Add(root, navigation);
            var timelineBox = Box("Vertical", false);
            Set(timelineBox, "SeparationOverride", 3);
            view.GraphTitle = Label(T("Урон, % порога  ·  цвет — состояние", "Damage, % of threshold  ·  color indicates status"), "#8d9ba9");
            Add(timelineBox, view.GraphTitle); Add(timelineBox, timeline);
            // Transparent native slider covers the plot: native click/drag input, custom drawing.
            var slider = New("Slider"); view.Slider = slider;
            Set(slider, "HorizontalExpand", true); Set(slider, "VerticalExpand", true);
            Set(slider, "MinValue", 0f); Set(slider, "MaxValue", 1f);
            foreach (var styleName in new[] { "BackgroundStyleBoxOverride", "ForegroundStyleBoxOverride", "FillStyleBoxOverride", "GrabberStyleBoxOverride" })
                Set(slider, styleName, Activator.CreateInstance(_engine.GetType("Robust.Client.Graphics.StyleBoxFlat", true)!)!);
            Set(slider, "ToolTip", T("Нажмите или перетащите курсор: время, здоровье и маршрут изменятся вместе.", "Click or drag the cursor to explore time, health and trail together."));
            Add(timeline, slider);
            Hook(slider, "OnValueChanged", () => {
                if (Selected(view) is not { Count: > 0 } history) return;
                view.Cursor = TimelineScale.Pick(history, (float)Get(slider, "Value")!)!.Time;
                RenderDetails(view, syncSlider: false); UpdateMap(view); Center(view);
            });
            Hook(slider, "OnReleased", () => RenderDetails(view));
            var axis = Box("Horizontal", false);
            view.AxisStart = Label("—", "#8d9ba9"); view.AxisMiddle = Label("", "#8d9ba9"); view.AxisEnd = Label("—", "#8d9ba9");
            Set(view.AxisMiddle, "Align", "Center"); Set(view.AxisEnd, "Align", "Right");
            Add(axis, view.AxisStart); Add(axis, view.AxisMiddle); Add(axis, view.AxisEnd); Add(timelineBox, axis);
            view.TimelineScroll = timelineBox; Add(root, timelineBox);
            Hook(search, "OnTextChanged", () => { view.Query = (string)Get(search, "Text")!; RenderPeople(view); });
            // Native controls remain alive: ShowSensors still supplies map geometry and blips.
            var nativeLayout = ((IEnumerable)Get(contents, "Children")!).Cast<object>().ToArray();
            foreach (var child in nativeLayout) Set(child, "Visible", false);
            Call(map, "Orphan"); Call(station, "Orphan");
            Call(Get(map, "_trackedEntityPanel")!, "Orphan");
            Set(map, "SetSize", new Vector2(float.NaN, float.NaN));
            foreach (var container in ((IEnumerable)Get(map, "Children")!).Cast<object>())
                foreach (var child in ((IEnumerable)Get(container, "Children")!).Cast<object>())
                    if (child.GetType().Name == "PanelContainer" && Get(child, "SetWidth") is float fixedWidth && fixedWidth == 650f)
                        Set(child, "SetWidth", float.NaN);
            Set(map, "Margin", Thickness(0)); Set(station, "Margin", Thickness(0));
            Add(header, station); Add(mapArea, map); Add(contents, root);
            Set(__instance, "Title", T("Crew Monitor — экипаж, здоровье и маршруты", "Crew Monitor — crew, health and trails"));
            Set(__instance, "Resizable", true);
            Set(__instance, "MinSize", new Vector2(520, 840));
            Set(__instance, "SetSize", new Vector2(1180, 980));
            Views.Add(__instance, view); Maps.Add(map, view); Graphs.Add(timeline, view);
            Hook(__instance, "OnResized", () => Reflow(__instance, view));
            Hook(map, "TrackedEntitySelectedAction", () => SelectFromMap(view));
            Reflow(__instance, view); Render(view);
            Bootstrap.Log("Crew Console created with history UI.");
        }
        catch (Exception e) { Bootstrap.Log("Crew Console shell failed: " + e.GetBaseException()); }
    }

    public static void SensorsUpdated(object __instance, object __0)
    {
        try
        {
            if (!Views.TryGetValue(__instance, out var view)) return;
            var blips = (IDictionary)Get(view.Map, "TrackedEntities")!;
            view.LiveBlips.Clear();
            foreach (DictionaryEntry entry in blips) view.LiveBlips.Add(entry.Key, entry.Value!);
            var grid = Get(view.Map, "MapUid");
            // A map change starts a new observation session; never join paths across stations.
            if (view.MapGrid != null && !Equals(grid, view.MapGrid)) { view.History = new(); view.Cursor = null; view.Selected = null; }
            view.MapGrid = grid;
            var samples = new List<Observation>();
            foreach (var sensor in ((IEnumerable)__0).Cast<object>())
            {
                var sensorId = Get(sensor, "SuitSensorUid")!;
                Vector2? position = null;
                string? localGrid = null;
                if (Get(sensor, "Coordinates") != null && blips.Contains(sensorId))
                {
                    var coords = Get(blips[sensorId]!, "Coordinates")!;
                    var entity = Get(coords, "EntityId");
                    if (Equals(entity, grid)) { position = (Vector2)Get(coords, "Position")!; localGrid = grid?.ToString(); }
                }
                samples.Add(new Observation(Get(sensor, "OwnerUid")!.ToString()!, sensorId.ToString()!,
                    (string)Get(sensor, "Name")!, (string)Get(sensor, "Job")!, (TimeSpan)Get(sensor, "Timestamp")!,
                    (bool)Get(sensor, "IsAlive")!, (int?)Get(sensor, "TotalDamage"), (int?)Get(sensor, "TotalDamageThreshold"), localGrid, position));
            }
            view.History.Accept(samples);
            // Reuse the game's exact status sprites, name and job row. No replica assets.
            foreach (var row in ((IEnumerable)Get(Get(__instance, "SensorsTable")!, "Children")!).Cast<object>())
            {
                if (row.GetType().Name != "CrewMonitoringButton") continue;
                var sensorId = Get(row, "SuitSensorUid")!.ToString()!;
                var content = ((IEnumerable)Get(row, "Children")!).Cast<object>().FirstOrDefault(c => c.GetType().Name == "BoxContainer");
                if (content == null) continue;
                Call(content, "Orphan"); view.NativeRows[sensorId] = content;
            }
            var retainedSensors = view.History.People.Values.Select(h => h[^1].Sensor).ToHashSet();
            foreach (var id in view.NativeRows.Keys.Where(id => !retainedSensors.Contains(id)).ToArray()) view.NativeRows.Remove(id);
            Render(view);
        }
        catch (Exception e) { Bootstrap.Log("Crew Console state read failed: " + e.GetBaseException()); }
    }

    private static void Render(View v)
    {
        var current = v.History.People.Where(x => v.History.Present.Contains(x.Key)).Select(x => x.Value[^1]).ToArray();
        Set(v.Summary, "Text", $"{T("Датчики", "Sensors")}: {current.Length}   ·   {T("Травмы", "Injured")}: {current.Count(x => x.Severity is >= 1 and <= 4)}   ·   {T("Крит", "Critical")}: {current.Count(x => x.Severity == 5)}   ·   {T("Погибли", "Dead")}: {current.Count(x => x.Severity == 6)}");
        RenderPeople(v); RenderDetails(v); UpdateMap(v);
    }

    private static void RenderPeople(View v)
    {
        Call(v.People, "RemoveAllChildren");
        var people = v.History.People.Values.Select(x => x[^1])
            .Where(x => !v.HealthOnly || (v.History.Present.Contains(x.Owner) && x.Severity >= 1))
            .Where(x => x.Name.Contains(v.Query, StringComparison.OrdinalIgnoreCase) || x.Job.Contains(v.Query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => v.History.Present.Contains(x.Owner)).ThenByDescending(x => x.Severity).ThenBy(x => x.Name).ToArray();
        foreach (var sample in people)
        {
            var present = v.History.Present.Contains(sample.Owner);
            object row;
            if (v.NativeRows.TryGetValue(sample.Sensor, out var native))
            {
                row = native; Call(row, "Orphan");
                Set(row, "Modulate", Color(present ? "#ffffff" : "#8d9ba9"));
            }
            else
            {
                row = Box("Horizontal", false);
                Add(row, Label(sample.Name)); Add(row, Label(sample.Job, "#8d9ba9"));
            }
            var button = Button("", () => { v.Selected = sample.Owner; v.Cursor = null; Render(v); Center(v); });
            Set(button, "ToolTip", $"{sample.Name} · {sample.Job}\n{(present ? sample.Health : T("Нет телеметрии · последнее наблюдение", "No telemetry · last observation"))} · {sample.Clock}");
            Set(button, "StyleBoxOverride", CardStyle(v.Selected == sample.Owner ? "#273f52" : "#141d28", v.Selected == sample.Owner ? "#64b5d6" : "#273442"));
            Set(row, "Margin", Thickness(3)); Add(button, row); Add(v.People, button);
        }
        if (people.Length == 0) Add(v.People, Label(v.History.People.Count == 0 ? T("Нет полученных данных датчиков", "No sensor data received") : T("Нет совпадений", "No matches"), "#8d9ba9"));
    }

    private static List<Observation>? Selected(View v) => v.Selected != null && v.History.People.TryGetValue(v.Selected, out var list) ? list : null;
    private static Observation? AtCursor(View v) => Selected(v) is { } history ? CrewHistory.At(history, v.Cursor) : null;

    private static void RenderDetails(View v, bool syncSlider = true)
    {
        Call(v.Details, "RemoveAllChildren");
        var history = Selected(v);
        if (history is { Count: > 0 } && v.Cursor < history[0].Time) v.Cursor = history[0].Time;
        var sample = AtCursor(v);
        if (history == null || sample == null)
        {
            Add(v.Details, Label(T("Выберите персонажа в списке или на карте", "Select a crew member in the list or on the map"), "#8d9ba9"));
            Set(v.CursorLabel, "Text", T("Маршрут, здоровье и время появятся здесь", "Trail, health and time will appear here"));
            Set(v.Slider, "Disabled", true);
            Set(v.AxisStart, "Text", "—"); Set(v.AxisMiddle, "Text", T("Нет наблюдений", "No observations")); Set(v.AxisEnd, "Text", "—");
            v.Route.Clear(); return;
        }
        if (v.Cursor < history[0].Time) v.Cursor = history[0].Time;
        Add(v.Details, Label($"{sample.Name} · {sample.Health}", sample.Color));
        Add(v.Details, Label(sample.Ratio is { } ratio ? $"{T("Урон", "Damage")} {sample.Damage} / {sample.Threshold} ({ratio:P0})  ·  {sample.Location}" : $"{T("Урон неизвестен", "Damage unknown")}  ·  {sample.Location}", "#8d9ba9"));
        var absent = !v.History.Present.Contains(sample.Owner);
        Set(v.CursorLabel, "Text", $"{(v.Cursor == null ? absent ? T("НЕТ ТЕЛЕМЕТРИИ", "NO TELEMETRY") : T("СЕЙЧАС", "LIVE") : T("ПРОШЛОЕ", "HISTORY"))}   {sample.Clock}");
        v.Route = history.Where(x => x.Time <= sample.Time).ToList();
        Set(v.Slider, "Disabled", history.Count < 2);
        if (syncSlider) Invoke(v.Slider, "SetValueWithoutEvent", (float)TimelineScale.Fraction(sample.Time, history[0].Time, history[^1].Time));
        Set(v.AxisStart, "Text", history[0].Clock);
        Set(v.AxisEnd, "Text", history[^1].Clock);
        var middle = history[0].Time + (history[^1].Time - history[0].Time) / 2;
        Set(v.AxisMiddle, "Text", $"{(int)middle.TotalHours:00}:{middle.Minutes:00}:{middle.Seconds:00}");
        Set(v.GraphTitle, "Text", T($"Урон 0–{TimelineScale.Ceiling(history):0}% порога · × смерть · серый: нет данных", $"Damage 0–{TimelineScale.Ceiling(history):0}% of threshold · × death · gray: no data"));
    }

    public static void DrawTimeline(object __instance, object __0)
    {
        if (!Graphs.TryGetValue(__instance, out var v) || v.GraphFailed || Selected(v) is not { Count: > 0 } history) return;
        try
        {
            var size = Get(__instance, "PixelSize")!;
            var width = Convert.ToSingle(Get(size, "X")); var height = Convert.ToSingle(Get(size, "Y"));
            if (width < 2 || height < 16) return;
            var top = 7f; var bottom = height - 12f;
            var max = TimelineScale.Ceiling(history);
            float X(Observation p) => (float)TimelineScale.Fraction(p.Time, history[0].Time, history[^1].Time) * (width - 1);
            float Y(Observation p) => bottom - (float)((p.Ratio ?? 0) * 100 / max) * (bottom - top);
            void Line(Vector2 a, Vector2 b, string color) => Invoke(__0, "DrawLine", a, b, Color(color));
            for (var i = 0; i <= 4; i++)
            {
                var x = i * (width - 1) / 4;
                Line(new Vector2(x, top), new Vector2(x, bottom), "#273442");
            }
            Line(new Vector2(0, bottom), new Vector2(width, bottom), "#3a4b5d");
            var thresholdY = bottom - (float)(100 / max) * (bottom - top);
            Line(new Vector2(0, thresholdY), new Vector2(width, thresholdY), "#665735");
            Observation? previous = null;
            foreach (var p in history)
            {
                var x = X(p); var y = Y(p);
                if (previous != null && p.Segment == previous.Segment && p.Ratio != null && previous.Ratio != null)
                {
                    Line(new Vector2(X(previous), Y(previous)), new Vector2(x, Y(previous)), previous.Color);
                    Line(new Vector2(x, Y(previous)), new Vector2(x, y), p.Color);
                }
                if (p.Ratio == null) Line(new Vector2(x, bottom + 3), new Vector2(x, bottom + 7), "#8d9ba9");
                else if (previous == null || previous.Severity != p.Severity || previous.Segment != p.Segment)
                    Invoke(__0, "DrawCircle", new Vector2(x, y), 2.5f, Color(p.Color), true);
                if (!p.Alive && (previous == null || previous.Alive))
                {
                    var deathY = p.Ratio == null ? bottom - 5 : y;
                    Line(new Vector2(x - 4, deathY - 4), new Vector2(x + 4, deathY + 4), "#ef6974");
                    Line(new Vector2(x - 4, deathY + 4), new Vector2(x + 4, deathY - 4), "#ef6974");
                }
                previous = p;
            }
            if (AtCursor(v) is { } selected)
            {
                var x = X(selected);
                Line(new Vector2(x, 0), new Vector2(x, height), "#eef3f7");
                Invoke(__0, "DrawCircle", new Vector2(x, selected.Ratio == null ? bottom : Y(selected)), 4f, Color(selected.Color), true);
            }
        }
        catch (Exception e) { v.GraphFailed = true; Set(v.GraphTitle, "Text", T("График недоступен · используйте шаги времени", "Graph unavailable · use time step controls")); Bootstrap.Log("Crew Console timeline failed: " + e.GetBaseException()); }
    }

    private static void Seek(View v, int step)
    {
        var history = Selected(v); if (history == null || history.Count == 0) return;
        var index = v.Cursor == null ? history.Count - 1 : history.FindLastIndex(x => x.Time <= v.Cursor);
        index = step == int.MinValue ? 0 : Math.Clamp(index + step, 0, history.Count - 1);
        v.Cursor = history[index].Time; RenderDetails(v); UpdateMap(v); Center(v);
    }

    private static void UpdateMap(View v)
    {
        var blips = (IDictionary)Get(v.Map, "TrackedEntities")!;
        blips.Clear();
        // Past view never overlays present-day crew locations. Geometry is always the current map.
        if (v.Cursor == null) foreach (var (key, value) in v.LiveBlips) blips.Add(key, value);
        var sample = AtCursor(v);
        var keyForSensor = sample == null ? null : v.LiveBlips.Keys.FirstOrDefault(k => k.ToString() == sample.Sensor);
        Set(v.Map, "Focus", v.Cursor == null ? keyForSensor : null);
    }

    private static void SelectFromMap(View v)
    {
        var focus = Get(v.Map, "Focus")?.ToString();
        var person = v.History.People.FirstOrDefault(x => x.Value[^1].Sensor == focus);
        if (person.Key == null) return;
        v.Selected = person.Key; v.Cursor = null; Render(v);
    }

    private static void Center(View v)
    {
        if (AtCursor(v) is not { Position: { } position } || Get(v.Map, "_physics") is not { } physics) return;
        Set(v.Map, "Offset", position - (Vector2)Get(physics, "LocalCenter")!);
    }

    public static void MapMidpoint(object __instance, ref Vector2 __result)
    {
        if (Maps.TryGetValue(__instance, out _)) { var size = Get(__instance, "PixelSize")!; __result = new Vector2(Convert.ToSingle(Get(size, "X")), Convert.ToSingle(Get(size, "Y"))) / 2; }
    }
    public static void MapScale(object __instance, ref float __result)
    {
        if (!Maps.TryGetValue(__instance, out _)) return;
        var size = Get(__instance, "PixelSize")!; var range = (float)Get(__instance, "WorldRange")!;
        __result = LayoutPolicy.MapScale(new Vector2(Convert.ToSingle(Get(size, "X")), Convert.ToSingle(Get(size, "Y"))), range);
    }

    public static void DrawRoute(object __instance, object __0)
    {
        if (!Maps.TryGetValue(__instance, out var v) || v.DrawFailed || v.Route.Count == 0 || Get(v.Map, "MapUid") == null) return;
        try
        {
            var offset = (Vector2)Invoke(v.Map, "GetOffset")!;
            var scale = (float)Get(v.Map, "MinimapScale")!;
            var midpoint = (Vector2)Get(v.Map, "MidPointVector")!;
            Vector2 Screen(Vector2 p) => LayoutPolicy.Project(p, offset, midpoint, scale);
            Observation? previous = null;
            // Draw all retained segments, with health-coloured samples and no interpolation over gaps.
            foreach (var point in v.Route)
            {
                if (point.Position is not { } pos || point.Grid != v.MapGrid?.ToString()) { previous = null; continue; }
                var screen = Screen(pos);
                if (previous != null && CrewHistory.Connect(previous, point) && previous.Position != point.Position)
                    Invoke(__0, "DrawLine", Screen(previous.Position!.Value), screen, Color("#64b5d6"));
                if (previous == null || previous.Severity != point.Severity || previous.Segment != point.Segment)
                    Invoke(__0, "DrawCircle", screen, 3f, Color(point.Color), true);
                previous = point;
            }
            if (AtCursor(v) is { Position: { } last } selected)
            {
                Invoke(__0, "DrawCircle", Screen(last), 7f, Color("#eef3f7"), false);
                Invoke(__0, "DrawCircle", Screen(last), 4f, Color(selected.Color), true);
            }
        }
        catch (Exception e) { v.DrawFailed = true; Bootstrap.Log("Crew Console route drawing disabled: " + e.GetBaseException()); }
    }

    private static void Reflow(object window, View v)
    {
        if (v.Reflowing) return; v.Reflowing = true;
        try
        {
            var size = (Vector2)Get(window, "Size")!;
            var layout = LayoutPolicy.Calculate(size.X, size.Y);
            var portrait = layout.Portrait;
            Set(v.Body, "Orientation", portrait ? "Vertical" : "Horizontal");
            Set(v.DetailScroll, "SetHeight", 56f);
            Set(v.Timeline, "SetHeight", portrait ? 86f : 106f);
            Set(v.MapArea, "SetHeight", layout.MapHeight);
            Set(v.MapArea, "VerticalExpand", !portrait);
            Set(v.Sidebar, "SetWidth", layout.ListWidth);
        }
        finally { v.Reflowing = false; }
    }

    private static object Box(string orientation, bool expand = true)
    {
        var box = New("BoxContainer"); Set(box, "Orientation", orientation); Set(box, "SeparationOverride", 6);
        Set(box, "HorizontalExpand", true); Set(box, "VerticalExpand", expand); return box;
    }
    private static object Label(string text, string color = "#eef3f7")
    {
        var label = New("Label"); Set(label, "Text", text); Set(label, "FontColorOverride", Color(color));
        Set(label, "HorizontalExpand", true); Set(label, "ClipText", true); Set(label, "ToolTip", text); return label;
    }
    private static object Scroll(object child)
    {
        var scroll = New("ScrollContainer"); Set(scroll, "HorizontalExpand", true); Set(scroll, "VerticalExpand", true);
        Set(scroll, "HScrollEnabled", false); Add(scroll, child); return scroll;
    }
    private static object Panel(object child)
    {
        var panel = New("PanelContainer");
        var style = Activator.CreateInstance(_engine.GetType("Robust.Client.Graphics.StyleBoxFlat", true)!)!;
        Set(style, "BackgroundColor", Color("#0e151e")); Set(style, "BorderColor", Color("#273442"));
        Set(style, "BorderThickness", Thickness(1)); Set(panel, "PanelOverride", style);
        Set(child, "Margin", Thickness(8)); Set(panel, "HorizontalExpand", true); Add(panel, child); return panel;
    }
    private static object CardStyle(string background, string border)
    {
        var style = Activator.CreateInstance(_engine.GetType("Robust.Client.Graphics.StyleBoxFlat", true)!)!;
        Set(style, "BackgroundColor", Color(background)); Set(style, "BorderColor", Color(border));
        Set(style, "BorderThickness", Thickness(1)); return style;
    }
    private static object Button(string text, Action action)
    {
        var button = New("Button"); Set(button, "Text", text); Set(button, "HorizontalExpand", true);
        Hook(button, "OnPressed", action); return button;
    }
    private static void Hook(object target, string name, Action action)
    {
        var ev = target.GetType().GetEvent(name);
        var field = target.GetType().GetField(name);
        var handler = ev?.EventHandlerType ?? field?.FieldType ?? throw new MissingMemberException(name);
        var parameters = handler.GetMethod("Invoke")!.GetParameters().Select(p => Expression.Parameter(p.ParameterType, p.Name)).ToArray();
        var callback = Expression.Lambda(handler, Expression.Call(Expression.Constant(action), typeof(Action).GetMethod("Invoke")!), parameters).Compile();
        if (ev != null) ev.AddEventHandler(target, callback);
        else field!.SetValue(target, Delegate.Combine((Delegate?)field.GetValue(target), callback));
    }
    private static readonly Dictionary<string, object> Colors = new();
    private static object Color(string hex)
    {
        if (Colors.TryGetValue(hex, out var color)) return color;
        return Colors[hex] = Activator.CreateInstance(_color, [Convert.ToByte(hex.Substring(1, 2), 16), Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16), (byte)255])!;
    }
    private static object Thickness(float size) => Activator.CreateInstance(_thickness, [size])!;
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
            if (property != null) { property.SetValue(target, property.PropertyType.IsEnum ? Enum.Parse(property.PropertyType, (string)value!) : value); return; }
            var field = type.GetField(name, Flags); if (field != null) { field.SetValue(target, value); return; }
        }
        throw new MissingMemberException(target.GetType().FullName, name);
    }
    private static object? Invoke(object target, string name, params object?[] args)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var method = type.GetMethods(Flags).FirstOrDefault(m => m.Name == name && m.GetParameters().Length == args.Length &&
                m.GetParameters().Select((p, i) => args[i] == null || p.ParameterType.IsInstanceOfType(args[i])).All(x => x));
            if (method != null) return method.Invoke(target, args);
        }
        throw new MissingMethodException(target.GetType().FullName, name);
    }
    private static void Call(object target, string name) => Invoke(target, name);
    private static void Add(object parent, object child) => Invoke(parent, "AddChild", child);
}
