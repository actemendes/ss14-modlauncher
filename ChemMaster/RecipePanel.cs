using System.Diagnostics;
using System.Globalization;
using static SS14LocalMods.ChemMaster.Native;

namespace SS14LocalMods.ChemMaster;

internal sealed class RecipePanel : IDisposable
{
    private enum Act { None, Insert, Start, Resume, Replan }
    private enum Tone { Normal, Weak, Ok, Warn, Danger }
    // One row of the bottom bar. Controls are touched only when this value changes.
    private sealed record View(string Status, string Tip, Tone Tone, float Progress, string? Primary = null, Act Act = Act.None,
        bool Pause = false, bool Stop = false, bool Eject = false);
    private sealed record TargetRow(string Id, object Row, object Have, object Amount, object Remove);
    private sealed record PlanRow(object Label, int First, int Last, string Text);

    private const int MaxResults = 6, MaxPlanRows = 60;
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static RecipePanel? _running;
    private readonly GameChemistryAdapter _adapter;
    private readonly object _bui, _window, _root, _search, _results, _amount, _add, _make, _ensure, _phaseRow, _cold, _hot, _temperatureStatus, _beakers, _hotTemperature,
        _targetsBox, _targetScroll, _planBox, _progress, _status, _primary, _pause, _eject, _stop, _saved, _name, _save, _delete;
    private readonly Dictionary<Tone, object?> _colors;
    private readonly Dictionary<Tone, object> _bars;
    private readonly List<Action> _unhook = [];
    private readonly List<TargetRow> _targetRows = [];
    private readonly List<PlanRow> _planRows = [];
    private readonly RecipeStore _store = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SS14LocalMods", "ChemMaster", "recipes.json"));
    private readonly TimingStore _timingStore = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SS14LocalMods", "ChemMaster", "settings.json"));
    private readonly BeakerStore _beakerStore = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SS14LocalMods", "ChemMaster", "beakers.json"));
    private ExecutionTiming _timing = new();
    private readonly List<Target> _targets = [];
    private string[] _choices = [];
    private SavedRecipe[] _recipes = [];
    private ChemistryCatalog _catalog;
    private ProductionPlan? _plan;
    private ExecutionSession? _session;
    private CancellationTokenSource? _planningCancellation;
    private Task<ProductionPlan>? _planning;
    private int _editVersion, _planningVersion;
    private bool _disposed;
    private bool _catalogAvailable = true;
    private bool _resumeAfterPlanning;
    private BeakerTemperatures? _plannedBeakers;
    private TimeSpan _nextRefresh, _planAt, _startedAt, _noticeUntil;
    private TimeSpan? _duration;
    // Edits request a preview; it starts once the machine state is readable and typing has settled.
    private bool _dirty, _renderTargets, _renderResults, _locked;
    private MachineSnapshot? _snapshot, _planInput;
    private Target[]? _recover;
    private string? _error, _readError, _notice;
    private float _savedHot;
    private View? _view;
    private Act _act;
    private (int Step, bool Active) _planProgress = (-1, false);
    public bool Sending { get; private set; }
    public object Bui => _bui;

    public RecipePanel(object bui, object window)
    {
        _bui = bui; _window = window; _adapter = new(); _catalog = _adapter.ReadCatalog();
        _colors = new()
        {
            [Tone.Normal] = null, [Tone.Weak] = Color(0.55f, 0.55f, 0.58f), [Tone.Ok] = Color(0.42f, 0.78f, 0.45f),
            [Tone.Warn] = Color(0.93f, 0.72f, 0.28f), [Tone.Danger] = Color(0.92f, 0.38f, 0.34f)
        };
        _bars = _colors.ToDictionary(p => p.Key, p => Flat(p.Value ?? Color(0.3f, 0.55f, 0.85f)));
        _root = Box("Vertical");
        Set(_root, "VerticalExpand", true);

        var savedRow = Box("Horizontal"); _saved = Control("OptionButton"); Set(_saved, "HorizontalExpand", true); Add(savedRow, _saved);
        _name = Control("LineEdit"); Set(_name, "PlaceHolder", T("Название", "Name")); Set(_name, "SetWidth", 170f); Add(savedRow, _name);
        _save = Button(T("Сохр.", "Save"), () => Guard(SaveRecipe)); Tip(_save, T("Сохранить цели как набор", "Save targets as a recipe")); Add(savedRow, _save);
        _delete = Button("×", () => Guard(DeleteRecipe)); Tip(_delete, T("Удалить выбранный набор", "Delete the selected recipe")); Add(savedRow, _delete);
        Add(_root, savedRow);

        var picker = Box("Horizontal");
        _search = Control("LineEdit"); Set(_search, "PlaceHolder", T("Вещество", "Reagent")); Set(_search, "HorizontalExpand", true); Add(picker, _search);
        _amount = Control("LineEdit"); Set(_amount, "Text", "30"); Set(_amount, "SetWidth", 64f); Tip(_amount, T("Объём, u", "Amount, u")); Add(picker, _amount);
        _add = Button("+", () => Guard(AddFirst)); Tip(_add, T("Добавить первое найденное (Enter)", "Add the first match (Enter)")); Add(picker, _add);
        var modes = Activator.CreateInstance(Native.Type("Robust.Client", "Robust.Client.UserInterface.Controls.ButtonGroup"), [false])!;
        _make = Toggle(T("Ещё", "More"), modes, "OpenRight"); Tip(_make, T("Приготовить ещё: объём прибавляется к запасу", "Make additional: amounts are added to stock")); Set(_make, "Pressed", true); Add(picker, _make);
        _ensure = Toggle(T("До", "Up to"), modes, "OpenLeft"); Tip(_ensure, T("Довести запас до объёма", "Ensure stock reaches the amount")); Add(picker, _ensure);
        Add(_root, picker);
        _results = Box("Vertical"); Set(_results, "Visible", false); Add(_root, _results);

        _targetsBox = Box("Vertical"); _targetScroll = Control("ScrollContainer"); Set(_targetScroll, "Visible", false); Add(_targetScroll, _targetsBox); Add(_root, _targetScroll);

        var thermalRow = Box("Horizontal");
        _beakers = Control("CheckBox"); Set(_beakers, "Text", T("Гор. мензурка", "Hot beaker")); Set(_beakers, "Pressed", true);
        Tip(_beakers, T("Разрешить смену холодной и горячей мензурки. Нагревает её игрок.", "Allow cold/hot beaker changes. You heat the beaker yourself.")); Add(thermalRow, _beakers);
        _hotTemperature = Control("LineEdit");
        _savedHot = 500f;
        try { _savedHot = _beakerStore.Load().Hot; } catch (Exception e) { Bootstrap.Log("ChemMaster: " + e.GetBaseException().Message); }
        Set(_hotTemperature, "Text", _savedHot.ToString("0.##", CultureInfo.InvariantCulture)); Set(_hotTemperature, "SetWidth", 70f);
        Tip(_hotTemperature, T("Температура подготовленной горячей мензурки, K", "Prepared hot beaker temperature, K")); Add(thermalRow, _hotTemperature); Add(thermalRow, Label("K"));
        _temperatureStatus = Label(""); Set(_temperatureStatus, "HorizontalExpand", true); Set(_temperatureStatus, "Align", "Right"); Set(_temperatureStatus, "ClipText", true); Set(_temperatureStatus, "MouseFilter", "Stop"); Add(thermalRow, _temperatureStatus);
        Add(_root, thermalRow);
        // Needed only when the client cannot read the beaker temperature.
        _phaseRow = Box("Horizontal"); Set(_phaseRow, "Visible", false); Add(_phaseRow, Label(T("Во входе:", "Inserted:")));
        var phases = Activator.CreateInstance(Native.Type("Robust.Client", "Robust.Client.UserInterface.Controls.ButtonGroup"), [false])!;
        _cold = Toggle(T("Холодная", "Cold"), phases, "OpenRight"); Set(_cold, "Pressed", true); Add(_phaseRow, _cold);
        _hot = Toggle(T("Горячая", "Hot"), phases, "OpenLeft"); Add(_phaseRow, _hot);
        Add(_root, _phaseRow);

        var planScroll = Control("ScrollContainer"); Set(planScroll, "VerticalExpand", true); Set(planScroll, "MinHeight", 90f);
        _planBox = Box("Vertical"); Set(_planBox, "SeparationOverride", 1); Add(planScroll, _planBox); Add(_root, planScroll);

        _progress = Control("ProgressBar"); Set(_progress, "MinValue", 0f); Set(_progress, "MaxValue", 1f); Set(_progress, "SetHeight", 6f); Set(_progress, "HorizontalExpand", true); Add(_root, _progress);
        var controls = Box("Horizontal");
        _status = Label(""); Set(_status, "ClipText", true); Set(_status, "HorizontalExpand", true); Set(_status, "MouseFilter", "Stop"); Add(controls, _status);
        _eject = Button("", () => Guard(ChangeInputBeaker)); Add(controls, _eject);
        _primary = Button("", () => Guard(Primary)); Call(_primary, "AddStyleClass", "ButtonColorGreen"); Add(controls, _primary);
        _pause = Button(T("Пауза", "Pause"), () => _session?.Pause()); Add(controls, _pause);
        _stop = Button(T("Стоп", "Stop"), Stop); Call(_stop, "AddStyleClass", "ButtonColorRed"); Add(controls, _stop);
        Add(_root, controls);

        _unhook.Add(Hook(_search, "OnTextChanged", () => _renderResults = true));
        _unhook.Add(Hook(_search, "OnTextEntered", () => Guard(AddFirst)));
        _unhook.Add(Hook(_amount, "OnTextEntered", () => Guard(AddFirst)));
        _unhook.Add(Hook(_make, "OnToggled", ModeChanged));
        _unhook.Add(Hook(_ensure, "OnToggled", ModeChanged));
        _unhook.Add(HookArgument(_saved, "OnItemSelected", arg => Guard(() => { Call(_saved, "SelectId", Get(arg!, "Id")); LoadRecipe(); })));
        _unhook.Add(Hook(_cold, "OnToggled", BeakerSelectionChanged));
        _unhook.Add(Hook(_hot, "OnToggled", BeakerSelectionChanged));
        _unhook.Add(Hook(_beakers, "OnToggled", Edited));
        _unhook.Add(Hook(_hotTemperature, "OnTextChanged", () => { if (!Active) Edited(); }));
        try { ReloadSaved(); } catch (Exception e) { Notice(e.GetBaseException().Message); }
        var tabs = Get(_window, "Tabs")!;
        var index = Items(Get(tabs, "Children")).Count();
        Add(tabs, _root); Call(tabs, "SetTabTitle", index, T("АВТО", "AUTO"));
        AddSettings(tabs, index + 1);
        _unhook.Add(Hook(_window, "OnClose", Dispose));
        Refresh(true); Render();
    }
    private static object Toggle(string text, object group, string style)
    {
        var button = Control("Button"); Set(button, "Text", text); Set(button, "ToggleMode", true); Set(button, "Group", group); Call(button, "AddStyleClass", style); return button;
    }
    private static void Tip(object control, string text) => Set(control, "ToolTip", text);
    private static string U(int cents) => (cents / 100m).ToString("0.##", CultureInfo.InvariantCulture);
    private static string K(float temperature) => temperature.ToString("0.##", CultureInfo.InvariantCulture) + " K";
    private void AddSettings(object tabs, int index)
    {
        string? loadError = null;
        try { _timing = _timingStore.Load(); } catch (Exception e) { loadError = e.GetBaseException().Message; }
        var settings = Box("Vertical"); Set(settings, "VerticalExpand", true);
        var savedStatus = Label(loadError ?? ""); Set(savedStatus, "ClipText", true);
        var summary = Label("");
        var refresh = new List<Action>();
        AddSlider(T("Скорость", "Speed"), T("Команд в секунду для одного вещества", "Commands per second for the same reagent"), 1, 20, 0,
            () => _timing.ClicksPerSecond, v => _timing with { ClicksPerSecond = v },
            v => v.ToString("0", CultureInfo.InvariantCulture) + T(" /с", " /s"));
        AddSlider(T("Пауза при смене вещества", "Reagent-switch pause"), T("Добавляется к интервалу и не сокращается хаотичностью", "Added to the interval; chaos never shortens it"), 0, 3, 1,
            () => _timing.SwitchPauseSeconds, v => _timing with { SwitchPauseSeconds = v },
            v => v.ToString("0.0", CultureInfo.InvariantCulture) + T(" с", " s"));
        AddSlider(T("Хаотичность", "Chaos"), T("Вероятность, что интервал изменится на величину до 60%", "Chance that an interval varies by up to 60%"), 0, 100, 0,
            () => _timing.ChaosPercent, v => _timing with { ChaosPercent = v },
            v => v.ToString("0", CultureInfo.InvariantCulture) + "%");
        Add(settings, summary);
        var footer = Box("Horizontal");
        Add(footer, Button(T("По умолчанию", "Defaults"), () => Guard(() =>
        {
            _timing = new(); _session?.UpdateTiming(_timing);
            foreach (var update in refresh) update(); Summarize(); Save();
        })));
        Add(footer, savedStatus); Add(settings, footer);
        Summarize();
        Add(tabs, settings); Call(tabs, "SetTabTitle", index, T("Настройки АВТО", "AUTO settings"));

        void Summarize()
        {
            var interval = 1000 / _timing.ClicksPerSecond;
            string Ms(double value) => value.ToString("0", CultureInfo.InvariantCulture);
            Set(summary, "Text", T("Интервал ", "Interval ") + Ms(interval) + T(" мс", " ms") +
                (_timing.ChaosPercent > 0 ? " · " + Ms(interval * 0.4) + "–" + Ms(interval * 1.6) : "") +
                " · +" + Ms(_timing.SwitchPauseSeconds * 1000) + T(" мс смена", " ms switch"));
        }
        void Save()
        {
            try { _timingStore.Save(_timing); Set(savedStatus, "Text", T("Сохранено", "Saved")); }
            catch (Exception e) { Set(savedStatus, "Text", e.GetBaseException().Message); }
        }
        void AddSlider(string title, string tip, float min, float max, int decimals, Func<double> read,
            Func<double, ExecutionTiming> change, Func<double, string> format)
        {
            var row = Box("Horizontal");
            var name = Label(title); Set(name, "SetWidth", 230f); Set(name, "MouseFilter", "Stop"); Tip(name, tip); Add(row, name);
            var slider = Control("Slider"); Set(slider, "MinValue", min); Set(slider, "MaxValue", max);
            Set(slider, "Rounded", true); Set(slider, "RoundingDecimals", decimals);
            Set(slider, "Value", (float)read()); Set(slider, "HorizontalExpand", true); Set(slider, "SetHeight", 28f);
            var value = Label(format(read())); Set(value, "SetWidth", 60f); Add(row, slider); Add(row, value); Add(settings, row);
            _unhook.Add(Hook(slider, "OnValueChanged", () => Guard(() =>
            {
                _timing = change((float)Get(slider, "Value")!); _session?.UpdateTiming(_timing);
                Set(value, "Text", format(read())); Summarize();
            })));
            _unhook.Add(Hook(slider, "OnReleased", Save));
            refresh.Add(() => { Call(slider, "SetValueWithoutEvent", (float)read()); Set(value, "Text", format(read())); });
        }
    }
    private TargetMode Mode => Get(_make, "Pressed") is true ? TargetMode.Make : TargetMode.Ensure;
    private bool Active => _session?.Active == true;
    private void RequireEditable() { if (Active) throw new ChemistryException(T("Сначала «Стоп».", "Stop first.")); }
    private void Notice(string message) { _notice = message; _noticeUntil = Clock.Elapsed + TimeSpan.FromSeconds(5); }
    // The plan no longer matches the machine; the goals themselves are unchanged.
    private void Invalidate() { _editVersion++; _plan = null; _planningCancellation?.Cancel(); }
    private void Edited()
    {
        if (Active) Stop();
        Invalidate(); _session = null; _recover = null; _error = null; _notice = null;
        _dirty = true; _planAt = Clock.Elapsed + TimeSpan.FromMilliseconds(300); ClearPlan();
    }
    private void ModeChanged() { Edited(); _renderTargets = true; }
    private void RenderResults()
    {
        var search = ((string)Get(_search, "Text")!).Trim();
        foreach (var old in Items(Get(_results, "Children")).ToArray()) Call(old, "Dispose");
        _choices = search.Length == 0 ? [] : _catalog.Reagents.Values
            .Where(r => r.Id.Contains(search, StringComparison.OrdinalIgnoreCase) || r.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => !r.Name.StartsWith(search, StringComparison.CurrentCultureIgnoreCase))
            .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).Take(MaxResults).Select(r => r.Id).ToArray();
        foreach (var id in _choices)
        {
            var button = Button(_catalog.Name(id), () => Guard(() => AddTarget(id)));
            Set(button, "TextAlign", "Left"); Set(button, "ClipText", true); Tip(button, id); Add(_results, button);
        }
        Set(_results, "Visible", _choices.Length > 0);
    }
    private static int Amount(string text)
    {
        if (!decimal.TryParse(text.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) ||
            number <= 0 || number > 100000 || number * 100 != decimal.Truncate(number * 100))
            throw new ChemistryException(T("Объём: 0.01–100000 u.", "Amount: 0.01–100000 u."));
        return checked((int)(number * 100));
    }
    private void AddFirst()
    {
        if (_renderResults) { _renderResults = false; RenderResults(); }
        if (_choices.Length == 0) throw new ChemistryException(T("Вещество не найдено.", "No reagent found."));
        AddTarget(_choices[0]);
    }
    private void AddTarget(string id)
    {
        RequireEditable();
        var amount = Amount((string)Get(_amount, "Text")!);
        var index = _targets.FindIndex(t => t.Id == id);
        if (index < 0) { if (_targets.Count >= 64) throw new ChemistryException(T("Не более 64 целей.", "Target limit: 64.")); _targets.Add(new(id, amount)); }
        else _targets[index] = new(id, amount);
        Set(_search, "Text", ""); _renderResults = true;
        Edited(); _renderTargets = true;
    }
    private void RenderTargets()
    {
        foreach (var row in _targetRows) Call(row.Row, "Dispose"); _targetRows.Clear();
        foreach (var target in _targets.ToArray())
        {
            var id = target.Id;
            var row = Box("Horizontal");
            var label = Label(_catalog.Name(id)); Set(label, "HorizontalExpand", true); Set(label, "ClipText", true); Set(label, "MouseFilter", "Stop"); Tip(label, id); Add(row, label);
            var have = Label(""); Set(have, "FontColorOverride", _colors[Tone.Weak]); Set(have, "MouseFilter", "Stop"); Tip(have, T("Сейчас в буфере, u", "In the buffer now, u")); Add(row, have);
            Add(row, Label(Mode == TargetMode.Make ? "+" : "→"));
            var amount = Control("LineEdit"); Set(amount, "Text", U(target.Amount)); Set(amount, "SetWidth", 64f); Set(amount, "Editable", !Active); Add(row, amount);
            void Commit() => Guard(() =>
            {
                var index = _targets.FindIndex(t => t.Id == id); if (index < 0) return;
                try
                {
                    var value = Amount((string)Get(amount, "Text")!);
                    if (value == _targets[index].Amount) return;
                    RequireEditable(); _targets[index] = new(id, value); Edited();
                }
                catch (ChemistryException) { Set(amount, "Text", U(_targets[index].Amount)); throw; }
            });
            Hook(amount, "OnTextEntered", Commit); Hook(amount, "OnFocusExit", Commit);
            var remove = Button("×", () => Guard(() => { RequireEditable(); _targets.RemoveAll(t => t.Id == id); Edited(); _renderTargets = true; }));
            Set(remove, "Disabled", Active); Add(row, remove);
            Add(_targetsBox, row); _targetRows.Add(new(id, row, have, amount, remove));
        }
        Set(_targetScroll, "Visible", _targets.Count > 0);
        Set(_targetScroll, "SetHeight", Math.Min(_targets.Count, 4) * 32f);
        RenderStock();
    }
    private void RenderStock()
    {
        foreach (var row in _targetRows)
            Set(row.Have, "Text", _snapshot == null ? "" : U(_snapshot.Buffer.GetValueOrDefault(row.Id)));
    }
    private void ReloadSaved()
    {
        _recipes = _store.Load(); Call(_saved, "Clear");
        Call(_saved, "AddItem", T("Наборы", "Recipes") + " (" + _recipes.Length + ")", (int?)0);
        for (var i = 0; i < _recipes.Length; i++) Call(_saved, "AddItem", _recipes[i].Name, (int?)(i + 1));
        Call(_saved, "SelectId", 0);
    }
    private void Select(string name)
    {
        var index = Array.FindIndex(_recipes, r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) Call(_saved, "SelectId", index + 1);
    }
    private void SaveRecipe()
    {
        var name = ((string)Get(_name, "Text")!).Trim();
        _store.Save(new(name, Mode, _targets.ToArray())); ReloadSaved(); Select(name);
        Notice(T("Набор сохранён.", "Recipe saved."));
    }
    private void DeleteRecipe()
    {
        var selected = Convert.ToInt32(Get(_saved, "SelectedId"));
        if (selected <= 0 || selected > _recipes.Length) throw new ChemistryException(T("Выберите набор.", "Select a recipe."));
        _store.Delete(_recipes[selected - 1].Name); ReloadSaved();
    }
    private void LoadRecipe()
    {
        var selected = Convert.ToInt32(Get(_saved, "SelectedId"));
        if (selected <= 0 || selected > _recipes.Length) return;
        if (Active) { Call(_saved, "SelectId", 0); RequireEditable(); }
        var recipe = _recipes[selected - 1];
        _targets.Clear(); _targets.AddRange(recipe.Targets); Set(recipe.Mode == TargetMode.Make ? _make : _ensure, "Pressed", true);
        Set(_name, "Text", recipe.Name); Edited(); _renderTargets = true;
    }
    private void StartPlanning()
    {
        _dirty = false; _error = null; _planInput = null; _resumeAfterPlanning = false; ClearPlan();
        try
        {
            if (!_catalogAvailable) throw new ChemistryException(T("Правила сервера недоступны. Откройте химмастер заново.", "Server rules unavailable. Reopen the ChemMaster."));
            var snapshot = _planInput = _adapter.Read(_bui);
            var targets = _recover ?? _targets.ToArray(); var mode = _recover != null ? TargetMode.Ensure : Mode;
            var hot = Temperature((string)Get(_hotTemperature, "Text")!);
            _plannedBeakers = Get(_beakers, "Pressed") is true ? new(hot) : null;
            snapshot = ConfirmUnknownTemperature(snapshot, _plannedBeakers ?? new(hot));
            if (_plannedBeakers != null && hot != _savedHot) { _beakerStore.Save(_plannedBeakers); _savedHot = hot; }
            Plan(snapshot, targets, mode);
        }
        catch (Exception e) { _error = e.GetBaseException().Message; }
    }
    private void Plan(MachineSnapshot snapshot, Target[] targets, TargetMode mode)
    {
        var catalog = _catalog; var beakers = _plannedBeakers; _planInput = snapshot;
        _planningCancellation?.Dispose(); _planningCancellation = new(); var token = _planningCancellation.Token;
        _planningVersion = _editVersion;
        _planning = Task.Run(() => new ProductionPlanner(catalog).Build(snapshot, targets, mode, false, token, beakers), token);
    }
    private void Primary()
    {
        switch (_act)
        {
            case Act.Insert: ChangeInputBeaker(); break;
            case Act.Start: Start(); break;
            case Act.Resume: Resume(); break;
            // After a stop or failure the remaining work is whatever the absolute goals still lack.
            case Act.Replan:
                if (_session?.State == ExecutionState.Completed) _recover = null;
                _session = null; _error = null; _dirty = true; _planAt = Clock.Elapsed; ClearPlan(); break;
        }
    }
    private void Start()
    {
        if (Active) return;
        if (_running is { Active: true } other && other != this) throw new ChemistryException(T("Другой химмастер уже выполняет рецепт.", "Another ChemMaster is executing."));
        if (_plan == null) return;
        Begin(_plan, _adapter.Read(_bui));
    }
    private void Begin(ProductionPlan plan, MachineSnapshot current)
    {
        _session = new(plan, timing: _timing); _recover = plan.AbsoluteGoals.ToArray(); _startedAt = Clock.Elapsed; _duration = null; _session.Start(current);
        if (_session.Active) _running = this;
    }
    private static float Temperature(string text)
    {
        if (!float.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !new BeakerTemperatures(value).Valid)
            throw new ChemistryException(T("Горячая мензурка: выше 293.15 K.", "Hot beaker: above 293.15 K."));
        return value;
    }
    private MachineSnapshot ConfirmUnknownTemperature(MachineSnapshot snapshot, BeakerTemperatures temperatures) => snapshot.Temperature != null ? snapshot :
        snapshot with { Temperature = Get(_hot, "Pressed") is true ? temperatures.Hot : temperatures.Cold };
    private void BeakerSelectionChanged()
    {
        // Selecting a phase is a confirmation choice, never an edit to an active production goal.
        if (!Active) Edited();
    }
    private void ChangeInputBeaker()
    {
        if (Active && _session!.State != ExecutionState.AwaitingBeaker)
            throw new ChemistryException(T("Мензурку меняют на остановке.", "Change the beaker at a replacement stop."));
        var state = Get(_bui, "State")!; var input = Get(state, "InputContainerInfo");
        if (input != null && Cents(Get(input, "CurrentVolume")!) != 0)
            throw new ChemistryException(T("Мензурка не пуста.", "Input beaker must be empty."));
        var type = Native.Type("Content.Shared", "Content.Shared.Containers.ItemSlots.ItemSlotButtonPressedEvent");
        Call(_bui, "SendMessage", Activator.CreateInstance(type, ["beakerSlot", true, true])!);
    }
    private bool HasBeaker => Get(_bui, "State") is { } state && Get(state, "InputContainerInfo") != null;
    private void Resume()
    {
        if (_planning != null || _session == null) return;
        if (_session.State != ExecutionState.AwaitingBeaker) { _session.Resume(_adapter.Read(_bui)); return; }
        var snapshot = _adapter.Read(_bui); _session.Observe(snapshot);
        if (!_session.Active) return;
        // Pressing Resume at a barrier is the explicit confirmation that the required beaker is installed.
        var hot = _session.RequiredBeakerPhase == BeakerPhase.Hot;
        Set(hot ? _hot : _cold, "Pressed", true);
        if (snapshot.Temperature is { } actual)
        {
            // Replan from telemetry, because a prepared beaker can cool while the cold phase runs.
            if (hot && actual <= _plannedBeakers!.Cold + 1 || !hot && actual >= _plannedBeakers!.Hot)
                throw new ChemistryException((hot ? T("Нужна горячая, сейчас ", "Hot required, now ") : T("Нужна холодная, сейчас ", "Cold required, now ")) + K(actual));
            _plannedBeakers = hot ? _plannedBeakers! with { Hot = actual } : _plannedBeakers! with { Cold = actual };
        }
        else snapshot = snapshot with { Temperature = hot ? Temperature((string)Get(_hotTemperature, "Text")!) : _plannedBeakers!.Cold };
        if (snapshot.Temperature is { } confirmed && hot) _plannedBeakers = _plannedBeakers! with { Hot = confirmed };
        Plan(snapshot, _recover!, TargetMode.Ensure); _resumeAfterPlanning = true;
    }
    // Idle state follows the machine: a changed buffer or beaker makes the preview stale.
    private void Sync(MachineSnapshot current)
    {
        _snapshot = current; _readError = null;
        if (Active) return;
        var stale = _plan != null && (!_plan.Initial.SameChemistry(current) || _plan.Initial.Mode != current.Mode);
        if (stale) Invalidate();
        // Request once: this runs every refresh, and re-arming the delay would postpone the preview forever.
        if (!_dirty && _session == null && (stale || _plan == null && _error != null && _planning == null && _planInput?.SameChemistry(current) != true))
        { _dirty = true; _planAt = Clock.Elapsed + TimeSpan.FromMilliseconds(300); }
    }
    private void Refresh(bool force = false)
    {
        if (!force && Clock.Elapsed < _nextRefresh) return;
        _nextRefresh = Clock.Elapsed + TimeSpan.FromMilliseconds(250);
        try { Sync(_adapter.Read(_bui)); }
        catch (Exception e) { _snapshot = null; _readError = e.GetBaseException().Message; }
        RenderStock();
        var temperature = _snapshot?.Temperature;
        Set(_temperatureStatus, "Text", _snapshot == null ? "" : temperature is { } value ? K(value) : "? K");
        Tip(_temperatureStatus, temperature != null ? T("Температура входной мензурки", "Input beaker temperature")
            : T("Сервер не сообщает температуру; укажите, какая мензурка вставлена.", "Temperature unavailable; choose which beaker is inserted."));
        Set(_phaseRow, "Visible", _snapshot is { Temperature: null } && !Active);
    }
    public void Observe()
    {
        if (_disposed) return;
        try
        {
            var current = _adapter.Read(_bui);
            _session?.Observe(current);
            Sync(current);
        }
        catch (Exception e)
        {
            _snapshot = null; _readError = e.GetBaseException().Message;
            if (Active && _session!.State != ExecutionState.AwaitingBeaker) _session.Fail(_readError);
        }
        Render();
    }
    public void Tick()
    {
        if (_disposed) return;
        if (_renderResults) { _renderResults = false; Guard(RenderResults); }
        if (_renderTargets) { _renderTargets = false; Guard(RenderTargets); }
        if (_planning is { IsCompleted: true } task)
        {
            _planning = null;
            var resume = _resumeAfterPlanning; _resumeAfterPlanning = false;
            if (_planningVersion == _editVersion)
                try
                {
                    var plan = task.GetAwaiter().GetResult();
                    var current = _adapter.Read(_bui);
                    if (!plan.Initial.SameChemistry(current) || plan.Initial.Mode != current.Mode)
                    {
                        if (resume) throw new ChemistryException(T("Состав изменился. Повторите.", "State changed. Try again."));
                        _dirty = true; _planAt = Clock.Elapsed;
                    }
                    else
                    {
                        _plan = plan; ShowPlan(plan);
                        if (resume) { var started = _startedAt; Begin(plan, current); _startedAt = started; }
                    }
                }
                catch (Exception e)
                {
                    var message = e.GetBaseException().Message; Bootstrap.Log("ChemMaster: " + message);
                    if (resume) Notice(message); else _error = message;
                }
            else if (task.IsFaulted) _ = task.Exception;
        }
        Refresh();
        if (_dirty && !Active && _session == null && _planning == null && _snapshot != null && Clock.Elapsed >= _planAt)
        {
            if (_targets.Count == 0) { _dirty = false; _error = null; }
            else StartPlanning();
        }
        if (Active && _session!.State != ExecutionState.AwaitingBeaker) Guard(() =>
        {
            var command = _session!.Tick(Clock.Elapsed, _adapter.Read(_bui));
            if (command != null)
            {
                Sending = true;
                try { _adapter.Send(_bui, command); }
                finally { Sending = false; }
            }
        });
        Render();
    }
    private void ClearPlan()
    {
        foreach (var old in Items(Get(_planBox, "Children")).ToArray()) Call(old, "Dispose");
        _planRows.Clear(); _planProgress = (-1, false);
    }
    private string Describe(TransferAction action) => action.RequiredBeakerTemperature is { } temperature
        ? (action.RequiredBeakerPhase == BeakerPhase.Hot ? T("Горячая ", "Hot ") : T("Холодная ", "Cold ")) + K(temperature)
        : _catalog.Name(action.ReagentId) + " " + (action.Dose?.ToString() ?? T("всё", "all")) + (action.FromBuffer ? T(" → мензурка", " → beaker") : T(" → буфер", " → buffer"));
    private void ShowPlan(ProductionPlan plan)
    {
        ClearPlan();
        foreach (var goal in plan.AbsoluteGoals)
        {
            var final = plan.Final.Buffer.GetValueOrDefault(goal.Id); var row = Box("Horizontal");
            var name = Label(_catalog.Name(goal.Id)); Set(name, "HorizontalExpand", true); Set(name, "ClipText", true); Add(row, name);
            var amount = Label(U(final) + (final > goal.Amount ? " (+" + U(final - goal.Amount) + ")" : "")); Set(amount, "MouseFilter", "Stop");
            if (final > goal.Amount) { Set(amount, "FontColorOverride", _colors[Tone.Warn]); Tip(amount, T("Избыток целой партии", "Whole-batch surplus")); }
            else Tip(amount, T("Запас после приготовления, u", "Stock after production, u"));
            Add(row, amount); Add(_planBox, row);
        }
        // A batch runs from the first transfer into the beaker until the beaker is empty again.
        var batch = 0; var first = -1; var products = new List<string>();
        for (var i = 0; i < plan.Actions.Count && _planRows.Count < MaxPlanRows; i++)
        {
            var action = plan.Actions[i];
            if (action.RequiredBeakerTemperature != null) { AddRow(i, i, Describe(action)); continue; }
            if (first < 0) first = i;
            if (action.FromBuffer) continue;
            products.Add(_catalog.Name(action.ReagentId));
            if (action.After.Beaker.Count != 0) continue;
            AddRow(first, i, ++batch + " · " + string.Join(", ", products.Distinct()) + " · " + (i - first + 1));
            first = -1; products.Clear();
        }
        if (_planRows.Count == MaxPlanRows && _planRows[^1].Last < plan.Actions.Count - 1) Add(_planBox, Label("…"));

        void AddRow(int from, int to, string text)
        {
            var label = Label(text); Set(label, "ClipText", true); Add(_planBox, label); _planRows.Add(new(label, from, to, text));
        }
    }
    private void RenderPlan()
    {
        var progress = (_session?.Step ?? 0, Active);
        if (progress == _planProgress || _planRows.Count == 0) return;
        _planProgress = progress;
        var step = _session == null ? -1 : _session.Step; var plan = _plan;
        foreach (var row in _planRows)
        {
            var current = Active && step >= row.First && step <= row.Last;
            Set(row.Label, "FontColorOverride", step > row.Last ? _colors[Tone.Weak] : null);
            Set(row.Label, "Text", current && plan != null && row.Last > row.First
                ? "▶ " + Describe(plan.Actions[step]) + " · " + (step - row.First) + "/" + (row.Last - row.First + 1)
                : (current ? "▶ " : "") + row.Text);
        }
    }
    private View Compute()
    {
        if (_session is { } session)
        {
            var count = session.Step + " / " + session.Total;
            var progress = session.Total == 0 ? 1 : (float)session.Step / session.Total;
            switch (session.State)
            {
                case ExecutionState.AwaitingBeaker:
                    if (_planning != null) return new(T("Расчёт…", "Planning…"), "", Tone.Warn, progress, Stop: true);
                    var need = (session.RequiredBeakerPhase == BeakerPhase.Hot ? T("Гор. ", "Hot ") : T("Хол. ", "Cold ")) + K(session.RequiredBeakerTemperature ?? 0);
                    return new(need + (_snapshot?.Temperature is { } now ? T(" · сейчас ", " · now ") + K(now) : !HasBeaker ? T(" · нет мензурки", " · no beaker") : ""),
                        session.Message, Tone.Warn, progress, T("Продолжить", "Resume"), HasBeaker ? Act.Resume : Act.None, Stop: true, Eject: true);
                case ExecutionState.Paused:
                    return new(T("Пауза · ", "Paused · ") + count, "", Tone.Weak, progress, T("Продолжить", "Resume"), Act.Resume, Stop: true);
                case ExecutionState.Completed:
                    return new(T("Готово · ", "Done · ") + Seconds(_duration ??= Clock.Elapsed - _startedAt), "", Tone.Ok, 1, T("Повтор", "Again"), Act.Replan);
                case ExecutionState.Failed or ExecutionState.Stopped:
                    return new((session.State == ExecutionState.Failed ? T("Сбой ", "Failed ") : T("Стоп ", "Stopped ")) + count +
                        (session.State == ExecutionState.Failed ? " · " + session.Message : ""), session.Message, Tone.Danger, progress, T("Пересчитать", "Replan"), Act.Replan);
                default:
                    var left = _plan == null ? "" : " · " + Seconds(_timing.Estimate(_plan.Actions.Skip(session.Step)));
                    return new(count + left, _plan != null && session.Step < session.Total ? Describe(_plan.Actions[session.Step]) : "", Tone.Normal, progress, Pause: true, Stop: true);
            }
        }
        if (_planning != null) return new(T("Расчёт…", "Planning…"), "", Tone.Weak, 0, T("Начать", "Start"));
        if (_snapshot == null)
            return HasBeaker ? new(_readError ?? "…", _readError ?? "", Tone.Danger, 0, T("Начать", "Start"))
                : Get(_bui, "State") == null ? new("…", "", Tone.Weak, 0, T("Начать", "Start"))
                : new(T("Нет мензурки", "No beaker"), T("Вставьте пустую входную мензурку из активной руки.", "Insert an empty input beaker from your active hand."), Tone.Warn, 0, T("Вставить", "Insert"), Act.Insert);
        if (_targets.Count == 0) return new(T("Нет целей", "No targets"), T("Найдите вещество и нажмите Enter.", "Search for a reagent and press Enter."), Tone.Weak, 0, T("Начать", "Start"));
        if (_error != null) return new(_error, _error, Tone.Danger, 0, T("Обновить", "Retry"), Act.Replan);
        if (_plan == null) return new("…", "", Tone.Weak, 0, T("Начать", "Start"));
        var changes = _plan.Actions.Count(a => a.RequiredBeakerTemperature != null);
        if (_plan.Actions.Count == 0) return new(T("Уже есть", "Already in stock"), "", Tone.Ok, 0, T("Начать", "Start"));
        return new(T("Шагов: ", "Steps: ") + _plan.Actions.Count + " · " + Seconds(_timing.Estimate(_plan.Actions)) + (changes > 0 ? T(" · смен: ", " · swaps: ") + changes : ""),
            T("Проверьте выход выше.", "Check the output above."), Tone.Normal, 0, T("Начать", "Start"), Act.Start);

        static string Seconds(TimeSpan time) => Math.Ceiling(time.TotalSeconds).ToString("0", CultureInfo.InvariantCulture) + T(" с", " s");
    }
    private void Render()
    {
        if (_session is { Active: false } && _running == this) _running = null;
        if (_locked != Active)
        {
            _locked = Active;
            foreach (var edit in new[] { _search, _amount, _hotTemperature }) Set(edit, "Editable", !_locked);
            foreach (var button in new[] { _add, _make, _ensure, _beakers, _saved, _cold, _hot }) Set(button, "Disabled", _locked);
            foreach (var row in _targetRows) { Set(row.Amount, "Editable", !_locked); Set(row.Remove, "Disabled", _locked); }
        }
        RenderPlan();
        var view = Compute();
        if (_notice != null && Clock.Elapsed < _noticeUntil) view = view with { Status = _notice, Tip = _notice, Tone = view.Tone == Tone.Ok ? Tone.Ok : Tone.Danger };
        else _notice = null;
        if (view == _view) return;
        var bar = view.Tone is Tone.Weak ? Tone.Normal : view.Tone;
        if (_view?.Status != view.Status) Set(_status, "Text", view.Status);
        if (_view?.Tip != view.Tip) Tip(_status, view.Tip);
        Set(_status, "FontColorOverride", _colors[view.Tone]);
        Set(_progress, "Value", view.Progress); Set(_progress, "ForegroundStyleBoxOverride", _bars[bar]);
        Set(_primary, "Visible", view.Primary != null); Set(_primary, "Text", view.Primary ?? ""); Set(_primary, "Disabled", view.Act == Act.None);
        Set(_pause, "Visible", view.Pause); Set(_stop, "Visible", view.Stop);
        Set(_eject, "Visible", view.Eject); Set(_eject, "Text", HasBeaker ? T("Извлечь", "Eject") : T("Вставить", "Insert"));
        _act = view.Act; _view = view;
    }
    public void CatalogChanged()
    {
        Stop(); Edited(); Set(_cold, "Pressed", true); _catalogAvailable = false;
        Guard(() => { _catalog = _adapter.ReadCatalog(); _catalogAvailable = true; _renderResults = true; _renderTargets = true; });
    }
    public void ManualCommand()
    {
        if (Sending) return;
        if (_session?.State == ExecutionState.AwaitingBeaker) return;
        if (Active) _session!.Fail(T("Ручная команда", "Manual command"));
        // Keep a finished or failed run on screen; the next state update decides whether to preview again.
        Invalidate();
        if (_session == null) { _dirty = true; _planAt = Clock.Elapsed + TimeSpan.FromMilliseconds(300); }
    }
    private void Stop()
    {
        _session?.Stop(); Invalidate();
        if (_running == this) _running = null;
    }
    private void Guard(Action action)
    {
        if (_disposed) return;
        try { action(); }
        catch (Exception e)
        {
            var message = e.GetBaseException().Message;
            if (Active && _session!.State != ExecutionState.AwaitingBeaker) _session.Fail(message);
            else Notice(message);
            Bootstrap.Log("ChemMaster: " + message);
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _session?.Stop();
        if (_running == this) _running = null;
        _plan = null; _editVersion++;
        _planningCancellation?.Cancel(); _planningCancellation?.Dispose();
        if (_planning is { } task) _ = task.ContinueWith(t => _ = t.Exception,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        foreach (var action in _unhook) { try { action(); } catch { } } _unhook.Clear();
    }
}
