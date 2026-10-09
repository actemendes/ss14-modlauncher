using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using SS14LocalMods.Conversations;
using static SS14LocalMods.Conversations.NativeUi;

namespace SS14LocalMods;

public static class Mod
{
    private sealed record Entry(Speaker Speaker, TimeSpan? Time);
    private sealed class View
    {
        public required object Chat, Controller, Root, Panel, Search, TextSearch, Department, People, OnlySelected, Status;
        public ConversationFilter Filter = new();
        public bool ShowTime = true, Updating, Disabled;
        public string? PendingTime;
        public SortedDictionary<string, string> Speakers = new(StringComparer.OrdinalIgnoreCase);
        public List<string?> Departments = [null];
    }
    private static readonly ConditionalWeakTable<object, View> Views = new();
    private static readonly ConditionalWeakTable<object, Entry> Entries = new();
    private static bool _installed;

    public static void Install(Assembly content)
    {
        if (_installed) return;
        var chat = content.GetType("Content.Client.UserInterface.Systems.Chat.Widgets.ChatBox");
        if (chat == null) { Bootstrap.Log("Conversations: unsupported client (no ChatBox)."); return; }
        Engine = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Robust.Client");
        // Resolve every required hook before installing any patch.
        var ctor = chat.GetConstructor(Type.EmptyTypes) ?? throw new MissingMethodException("ChatBox constructor");
        var message = AccessTools.Method(chat, "OnMessageAdded") ?? throw new MissingMethodException("ChatBox.OnMessageAdded");
        var line = AccessTools.Method(chat, "AddLine") ?? throw new MissingMethodException("ChatBox.AddLine");
        var harmony = new Harmony("local.conversations.chat");
        try
        {
            harmony.Patch(ctor, postfix: new HarmonyMethod(typeof(Mod), nameof(Created)));
            harmony.Patch(message, prefix: new HarmonyMethod(typeof(Mod), nameof(BeforeMessage)),
                finalizer: new HarmonyMethod(typeof(Mod), nameof(AfterMessage)));
            harmony.Patch(line, prefix: new HarmonyMethod(typeof(Mod), nameof(BeforeLine)));
            _installed = true;
            Bootstrap.Log("Conversations: native chat timestamps and speaker/radio filters installed.");
        }
        catch { harmony.UnpatchAll(harmony.Id); throw; }
    }

    public static void Created(object __instance)
    {
        object? root = null;
        try
        {
            var output = Get(__instance, "Contents")!;
            var parent = Get(output, "Parent")!;
            var controller = Get(__instance, "_controller")!;
            root = Box("Vertical");
            var toolbar = Box("Horizontal");
            var panel = Box("Vertical"); Set(panel, "Visible", false);
            var search = New("LineEdit"); Set(search, "HorizontalExpand", true);
            Set(search, "PlaceHolder", T("Поиск по имени…", "Search speaker name…"));
            var textSearch = New("LineEdit"); Set(textSearch, "HorizontalExpand", true);
            Set(textSearch, "PlaceHolder", T("Поиск по сообщениям: слово или фраза…", "Search messages: word or phrase…"));
            Set(textSearch, "ToolTip", T("Например, зомби — покажет сообщения с «зомби!». Регистр не важен; поиск только по тексту сообщения.",
                "For example, zombie matches zombie! Case-insensitive search in message text only."));
            var department = New("OptionButton"); Set(department, "HorizontalExpand", true);
            Call(department, "AddItem", T("Все отделы / каналы рации", "All departments / radio channels"), (int?)0);
            Call(department, "SelectId", 0);
            Set(department, "ToolTip", T("Отбор по каналу рации, а не по должности человека. Доступны только полученные каналы.",
                "Filters the radio channel, not the person's job. Only received channels are available."));
            var people = Box("Vertical");
            var status = Label("");
            var view = new View { Chat = __instance, Controller = controller, Root = root, Panel = panel,
                Search = search, TextSearch = textSearch, Department = department, People = people, OnlySelected = null!, Status = status };
            var expand = Button(T("Переговоры", "Conversations"), () =>
            {
                Set(panel, "Visible", !(bool)Get(panel, "Visible")!);
                if ((bool)Get(panel, "Visible")!) RenderPeople(view);
            }, true);
            Set(expand, "ToolTip", T("Поиск по имени, выбор собеседников и отдела", "Search names, choose speakers and a department"));
            object time = null!;
            time = Button(T("Время", "Time"), () => { view.ShowTime = (bool)Get(time, "Pressed")!; Rebuild(view); }, true);
            Set(time, "Pressed", true);
            Set(time, "ToolTip", T("Время получения по игровым часам (чч:мм:сс). Исходное время произнесения сервер не передаёт.",
                "Receive time on the game clock (hh:mm:ss). The server does not send the utterance time."));
            Add(toolbar, expand); Add(toolbar, status); Add(toolbar, time); Add(root, toolbar);
            var queryRow = Box("Horizontal"); Add(queryRow, search); Add(queryRow, department); Add(panel, queryRow);
            Add(panel, textSearch);
            var selection = Box("Horizontal");
            view.OnlySelected = Button("", () => { view.Filter.SelectedOnly = (bool)Get(view.OnlySelected, "Pressed")!; Rebuild(view); }, true);
            Set(view.OnlySelected, "HorizontalExpand", true); Add(selection, view.OnlySelected);
            Add(selection, Button(T("Сбросить", "Reset"), () =>
            {
                view.Filter.Reset(); view.Updating = true;
                try { Set(search, "Text", ""); Set(textSearch, "Text", ""); Call(department, "SelectId", 0); Set(view.OnlySelected, "Pressed", false); }
                finally { view.Updating = false; }
                RenderPeople(view); Rebuild(view);
            }));
            Add(panel, selection);
            var scroll = New("ScrollContainer"); Set(scroll, "HorizontalExpand", true);
            Set(scroll, "SetHeight", 60f); Set(scroll, "HScrollEnabled", false); Add(scroll, people); Add(panel, scroll);
            Add(root, panel);
            Hook(search, "OnTextChanged", () =>
            {
                if (view.Updating) return;
                view.Filter.Query = (string)Get(search, "Text")!; RenderPeople(view); Rebuild(view);
            });
            HookDepartment(view);
            Hook(textSearch, "OnTextChanged", () =>
            {
                if (view.Updating) return;
                view.Filter.MessageQuery = (string)Get(textSearch, "Text")!; Rebuild(view);
            });
            Seed(view);
            Add(parent, root); Call(root, "SetPositionInParent", 0);
            Views.Add(__instance, view);
            UpdateStatus(view); Rebuild(view);
        }
        catch (Exception e)
        {
            if (root != null) { try { Call(root, "Orphan"); Call(root, "Dispose"); } catch { } }
            Views.Remove(__instance);
            Bootstrap.Log("Conversations UI skipped; native chat retained: " + e.GetBaseException().Message);
        }
    }

    private static void HookDepartment(View view)
    {
        var ev = view.Department.GetType().GetEvent("OnItemSelected")!;
        var arg = System.Linq.Expressions.Expression.Parameter(ev.EventHandlerType!.GetMethod("Invoke")!.GetParameters()[0].ParameterType);
        var action = new Action<object>(args =>
        {
            if (view.Updating) return;
            var id = (int)Get(args, "Id")!;
            Call(view.Department, "SelectId", id); view.Filter.RadioChannel = view.Departments[id]; Rebuild(view);
        });
        ev.AddEventHandler(view.Department, System.Linq.Expressions.Expression.Lambda(ev.EventHandlerType,
            System.Linq.Expressions.Expression.Invoke(System.Linq.Expressions.Expression.Constant(action),
                System.Linq.Expressions.Expression.Convert(arg, typeof(object))), arg).Compile());
    }

    private static Entry Describe(View view, object msg, object? tick = null)
    {
        if (Entries.TryGetValue(msg, out var cached)) return cached;
        var speaker = ConversationFilter.Parse(Get(msg, "Channel")!.ToString()!, (string)Get(msg, "WrappedMessage")!, (string)Get(msg, "Message")!);
        TimeSpan? time = null;
        if (tick != null)
        {
            var timing = Get(view.Controller, "_timing")!;
            var timeBase = Get(timing, "TimeBase")!;
            time = ConversationFilter.GameTime((uint)Get(tick, "Value")!, (uint)Get(Get(timeBase, "Item2")!, "Value")!,
                (TimeSpan)Get(timeBase, "Item1")!, (TimeSpan)Get(timing, "TickPeriod")!);
        }
        var entry = new Entry(speaker, time); Entries.Add(msg, entry); return entry;
    }

    private static void Seed(View view)
    {
        foreach (var tuple in (IEnumerable)Get(view.Controller, "History")!)
            Discover(view, Describe(view, Get(tuple, "Item2")!, Get(tuple, "Item1")));
    }

    private static bool Discover(View view, Entry entry)
    {
        var changed = false;
        if (entry.Speaker is { Key: { } key, Name: { } name } && !view.Speakers.ContainsKey(key))
        { view.Speakers.Add(key, name); changed = true; }
        if (entry.Speaker.RadioChannel is { } channel && !view.Departments.Contains(channel, StringComparer.OrdinalIgnoreCase))
        {
            view.Departments.Add(channel); Call(view.Department, "AddItem", channel, (int?)(view.Departments.Count - 1)); changed = true;
        }
        return changed;
    }

    public static bool BeforeMessage(object __instance, object __0, out string? __state)
    {
        __state = null;
        if (!Views.TryGetValue(__instance, out var view) || view.Disabled) return true;
        __state = view.PendingTime;
        try
        {
            if (!Entries.TryGetValue(__0, out var entry))
            {
                var history = (IList)Get(view.Controller, "History")!;
                var last = history.Count > 0 ? history[history.Count - 1] : null;
                if (last != null && ReferenceEquals(Get(last, "Item2"), __0)) entry = Describe(view, __0, Get(last, "Item1"));
                else { Seed(view); entry = Describe(view, __0); }
            }
            if (Discover(view, entry) && !view.Updating && (bool)Get(view.Panel, "Visible")!) RenderPeople(view);
            if (!view.Filter.Matches(entry.Speaker, (string)Get(__0, "Message")!)) return false;
            view.PendingTime = view.ShowTime ? ConversationFilter.FormatTime(entry.Time) : null;
            return true;
        }
        catch (Exception e) { Disable(view, e); return true; }
    }

    public static Exception? AfterMessage(object __instance, string? __state, Exception? __exception)
    {
        if (Views.TryGetValue(__instance, out var view)) view.PendingTime = __state;
        return __exception;
    }

    public static void BeforeLine(object __instance, ref string __0)
    {
        if (Views.TryGetValue(__instance, out var view) && !view.Disabled && view.PendingTime is { } time)
            __0 = "[color=#8d9ba9]" + time + "[/color] " + __0;
    }

    private static void RenderPeople(View view)
    {
        if (view.Disabled) return;
        Call(view.People, "DisposeAllChildren");
        var people = view.Speakers.Where(p => view.Filter.Query.Trim().Length == 0 || p.Value.Contains(view.Filter.Query.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        // Keep the popup small even on busy servers; name search reaches the rest.
        foreach (var (key, name) in people.Take(100))
        {
            object button = null!;
            button = Button(name, () =>
            {
                if ((bool)Get(button, "Pressed")!) view.Filter.Selected.Add(key); else view.Filter.Selected.Remove(key);
                // Picking the first speaker immediately enables the selected-speakers filter.
                view.Filter.SelectedOnly = view.Filter.Selected.Count > 0;
                Set(view.OnlySelected, "Pressed", view.Filter.SelectedOnly); Rebuild(view);
            }, true);
            Set(button, "Pressed", view.Filter.Selected.Contains(key));
            Set(button, "HorizontalExpand", true); Set(button, "ClipText", true); Set(button, "ToolTip", name);
            Add(view.People, button);
        }
        if (people.Length == 0) Add(view.People, Label(T("Нет собеседников с таким именем", "No speakers match this name")));
        else if (people.Length > 100) Add(view.People, Label(T("Уточните имя: показаны первые 100", "Refine the name: first 100 shown")));
        UpdateStatus(view);
    }

    private static void UpdateStatus(View view)
    {
        var text = view.Filter.Active ? T("Фильтр включён", "Filter active") : T("Все сообщения", "All messages");
        Set(view.Status, "Text", text); Set(view.Status, "ToolTip", text);
        Set(view.OnlySelected, "Text", T("Только выбранные", "Selected only") + $" ({view.Filter.Selected.Count})");
    }

    private static void Rebuild(View view)
    {
        if (view.Updating || view.Disabled) return;
        view.Updating = true;
        try { UpdateStatus(view); Call(view.Chat, "Repopulate"); }
        catch (Exception e) { Disable(view, e); }
        finally { view.Updating = false; }
    }

    private static void Disable(View view, Exception e)
    {
        view.Disabled = true; view.PendingTime = null;
        Set(view.Root, "Visible", false);
        Bootstrap.Log("Conversations disabled for this chat; native chat retained: " + e.GetBaseException().Message);
    }
}
