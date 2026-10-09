using System.Diagnostics;
using System.Globalization;
using static SS14LocalMods.ChemMaster.Native;

namespace SS14LocalMods.ChemMaster;

internal sealed class RecipePanel : IDisposable
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static RecipePanel? _running;
    private readonly GameChemistryAdapter _adapter;
    private readonly object _bui, _window, _root, _search, _choice, _amount, _mode, _cold, _hot, _temperatureStatus, _beakers, _hotTemperature, _targetsBox, _status, _planText, _saved, _name;
    private readonly List<Action> _unhook = [];
    private readonly List<object> _targetRows = [];
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
    private string _lastStatus = "";
    private string _lastSessionStatus = "";
    private bool _disposed;
    private bool _catalogAvailable = true;
    private bool _resumeAfterPlanning;
    private BeakerTemperatures? _plannedBeakers;
    private TimeSpan _nextTemperatureRefresh;
    public bool Sending { get; private set; }
    public object Bui => _bui;

    public RecipePanel(object bui, object window)
    {
        _bui = bui; _window = window; _adapter = new(); _catalog = _adapter.ReadCatalog();
        _root = Box("Vertical");
        Set(_root, "VerticalExpand", true);
        Add(_root, Label(T("АВТО · химия текущего сервера", "AUTO · current server chemistry")));
        Add(_root, Label(T("Выберите вещества и объём. Результат возвращается в буфер.", "Choose reagents and amounts. Products return to the buffer.")));
        _search = Control("LineEdit"); Set(_search, "PlaceHolder", T("Поиск вещества или prototype…", "Search reagent or prototype…")); Add(_root, _search);
        var picker = Box("Horizontal");
        _choice = Control("OptionButton"); Set(_choice, "HorizontalExpand", true); Add(picker, _choice);
        _amount = Control("LineEdit"); Set(_amount, "Text", "30"); Set(_amount, "SetWidth", 85f); Add(picker, _amount); Add(picker, Label("u"));
        Add(picker, Button(T("Добавить", "Add"), () => Guard(AddTarget))); Add(_root, picker);
        var modeRow = Box("Horizontal"); _mode = Control("OptionButton");
        Call(_mode, "AddItem", T("Приготовить ещё", "Make additional"), (int?)0);
        Call(_mode, "AddItem", T("Довести запас до", "Ensure stock"), (int?)1); Call(_mode, "SelectId", 0);
        Add(modeRow, _mode); Add(_root, modeRow);
        var phaseRow = Box("Horizontal"); Add(phaseRow, Label(T("Подтверждаю мензурку:", "Confirm beaker:")));
        var group = Activator.CreateInstance(Native.Type("Robust.Client", "Robust.Client.UserInterface.Controls.ButtonGroup"), [false])!;
        _cold = Control("Button"); Set(_cold, "Text", T("◀ Холодная", "◀ Cold")); Set(_cold, "ToggleMode", true); Set(_cold, "Group", group); Set(_cold, "Pressed", true); Add(phaseRow, _cold);
        _hot = Control("Button"); Set(_hot, "Text", T("Горячая ▶", "Hot ▶")); Set(_hot, "ToggleMode", true); Set(_hot, "Group", group); Add(phaseRow, _hot);
        Add(phaseRow, Button(T("Извлечь / вставить", "Eject / insert"), () => Guard(ChangeInputBeaker))); Add(_root, phaseRow);
        _temperatureStatus = Label(""); Set(_temperatureStatus, "ClipText", true); Add(_root, _temperatureStatus);
        Add(_root, Label(T("Выбор фазы не меняет температуру. Замените мензурку физически.", "Selecting a phase does not change temperature. Physically replace the beaker.")));
        var thermalRow = Box("Horizontal");
        _beakers = Control("CheckBox"); Set(_beakers, "Text", T("Смена холодной/горячей мензурки", "Cold/hot beaker changes")); Set(_beakers, "Pressed", true); Add(thermalRow, _beakers);
        _hotTemperature = Control("LineEdit");
        var hotTemperature = 500f;
        try { hotTemperature = _beakerStore.Load().Hot; } catch (Exception e) { Bootstrap.Log("ChemMaster: " + e.GetBaseException().Message); }
        Set(_hotTemperature, "Text", hotTemperature.ToString("0.##", CultureInfo.InvariantCulture)); Set(_hotTemperature, "SetWidth", 85f); Add(thermalRow, _hotTemperature); Add(thermalRow, Label(T("K горячая", "K hot"))); Add(_root, thermalRow);
        Add(_root, Label(T("Введите температуру горячей мензурки. Замену подтвердите кнопкой «Продолжить».", "Enter the hot beaker temperature. Confirm replacements with Resume.")));
        _targetsBox = Box("Vertical"); var targetScroll = Control("ScrollContainer"); Set(targetScroll, "SetHeight", 145f); Add(targetScroll, _targetsBox); Add(_root, targetScroll);
        var savedRow = Box("Horizontal"); _saved = Control("OptionButton"); Set(_saved, "HorizontalExpand", true); Add(savedRow, _saved);
        Add(savedRow, Button(T("Загрузить", "Load"), () => Guard(LoadRecipe))); Add(_root, savedRow);
        var saveRow = Box("Horizontal"); _name = Control("LineEdit"); Set(_name, "PlaceHolder", T("Название набора…", "Recipe name…")); Set(_name, "HorizontalExpand", true); Add(saveRow, _name);
        Add(saveRow, Button(T("Сохранить", "Save"), () => Guard(SaveRecipe))); Add(_root, saveRow);
        var controls = Box("Horizontal");
        Add(controls, Button(T("Предпросмотр", "Preview"), () => Guard(Preview)));
        Add(controls, Button(T("Начать", "Start"), () => Guard(Start)));
        Add(controls, Button(T("Пауза", "Pause"), () => { _session?.Pause(); RenderStatus(); }));
        Add(controls, Button(T("Продолжить", "Resume"), () => Guard(Resume)));
        Add(controls, Button(T("Стоп", "Stop"), Stop)); Add(_root, controls);
        _status = Label(T("Добавьте цели и постройте план.", "Add targets and preview the plan.")); Set(_status, "ClipText", true); Set(_status, "HorizontalExpand", true); Add(_root, _status);
        var planScroll = Control("ScrollContainer"); Set(planScroll, "VerticalExpand", true); Set(planScroll, "MinHeight", 90f);
        _planText = Label(""); Add(planScroll, _planText); Add(_root, planScroll);
        _unhook.Add(Hook(_search, "OnTextChanged", Filter));
        _unhook.Add(HookArgument(_mode, "OnItemSelected", arg => { Call(_mode, "SelectId", Get(arg!, "Id")); Edited(); }));
        _unhook.Add(HookArgument(_choice, "OnItemSelected", arg => Call(_choice, "SelectId", Get(arg!, "Id"))));
        _unhook.Add(HookArgument(_saved, "OnItemSelected", arg => Call(_saved, "SelectId", Get(arg!, "Id"))));
        _unhook.Add(Hook(_cold, "OnToggled", BeakerSelectionChanged));
        _unhook.Add(Hook(_hot, "OnToggled", BeakerSelectionChanged));
        _unhook.Add(Hook(_beakers, "OnToggled", Edited));
        _unhook.Add(Hook(_hotTemperature, "OnTextChanged", () => { if (!Active) Edited(); }));
        Filter();
        try { ReloadSaved(); } catch (Exception e) { Status(e.GetBaseException().Message); }
        var tabs = Get(_window, "Tabs")!;
        var index = Items(Get(tabs, "Children")).Count();
        Add(tabs, _root); Call(tabs, "SetTabTitle", index, T("АВТО", "AUTO"));
        AddSettings(tabs, index + 1);
        _unhook.Add(Hook(_window, "OnClose", Dispose));
    }
    private void AddSettings(object tabs, int index)
    {
        string? loadError = null;
        try { _timing = _timingStore.Load(); } catch (Exception e) { loadError = e.GetBaseException().Message; }
        var settings = Box("Vertical"); Set(settings, "VerticalExpand", true);
        var savedStatus = Label(loadError ?? ""); Set(savedStatus, "ClipText", true);
        Add(settings, Label(T("НАСТРОЙКИ АВТО", "AUTO SETTINGS")));
        Add(settings, Label(T("Задержки действуют после подтверждения состава сервером.", "Delays begin after the server confirms the inventory.")));
        var refresh = new List<Action>();
        AddSlider(T("Скорость переносов одного вещества", "Transfer speed for the same reagent"), 1, 20, 0,
            () => _timing.ClicksPerSecond, v => _timing with { ClicksPerSecond = v },
            v => v.ToString("0", CultureInfo.InvariantCulture) + T(" команд/с", " commands/s"));
        AddSlider(T("Дополнительная пауза при смене вещества", "Additional pause when changing reagent"), 0, 3, 1,
            () => _timing.SwitchPauseSeconds, v => _timing with { SwitchPauseSeconds = v },
            v => v.ToString("0.0", CultureInfo.InvariantCulture) + T(" с", " s"));
        AddSlider(T("Хаотичность — вероятность изменения интервала", "Chaos — chance of varying an interval"), 0, 100, 0,
            () => _timing.ChaosPercent, v => _timing with { ChaosPercent = v },
            v => v.ToString("0", CultureInfo.InvariantCulture) + "%");
        Add(settings, Label(T("Выбранный интервал случайно сокращается или растёт до 60%.", "Selected intervals randomly shorten or lengthen by up to 60%.")));
        Add(settings, Label(T("Дополнительная пауза при смене вещества сохраняется.", "The additional reagent-switch pause is always preserved.")));
        Add(settings, Label(T("0% — постоянные интервалы; 100% — разброс после каждой команды.", "0%: fixed intervals. 100%: vary the delay after every command.")));
        Add(settings, Label(T("Настройки применяются во время варки и сохраняются локально.", "Settings apply during production and are saved locally.")));
        Add(settings, savedStatus);
        Add(settings, Button(T("По умолчанию", "Restore defaults"), () => Guard(() =>
        {
            _timing = new(); _session?.UpdateTiming(_timing);
            foreach (var update in refresh) update(); Save();
        })));
        Add(tabs, settings); Call(tabs, "SetTabTitle", index, T("Настройки АВТО", "AUTO settings"));

        void Save()
        {
            try { _timingStore.Save(_timing); Set(savedStatus, "Text", T("Настройки сохранены.", "Settings saved.")); }
            catch (Exception e) { Set(savedStatus, "Text", e.GetBaseException().Message); }
        }
        void AddSlider(string title, float min, float max, int decimals, Func<double> read,
            Func<double, ExecutionTiming> change, Func<double, string> format)
        {
            Add(settings, Label(title));
            var row = Box("Horizontal");
            var slider = Control("Slider"); Set(slider, "MinValue", min); Set(slider, "MaxValue", max);
            Set(slider, "Rounded", true); Set(slider, "RoundingDecimals", decimals);
            Set(slider, "Value", (float)read()); Set(slider, "HorizontalExpand", true); Set(slider, "SetHeight", 28f);
            var value = Label(format(read())); Set(value, "SetWidth", 150f); Add(row, slider); Add(row, value); Add(settings, row);
            _unhook.Add(Hook(slider, "OnValueChanged", () => Guard(() =>
            {
                _timing = change((float)Get(slider, "Value")!); _session?.UpdateTiming(_timing);
                Set(value, "Text", format(read()));
            })));
            _unhook.Add(Hook(slider, "OnReleased", Save));
            refresh.Add(() => { Call(slider, "SetValueWithoutEvent", (float)read()); Set(value, "Text", format(read())); });
        }
    }
    private TargetMode Mode => Convert.ToInt32(Get(_mode, "SelectedId")) == 0 ? TargetMode.Make : TargetMode.Ensure;
    private bool Active => _session?.Active == true;
    private void RequireEditable() { if (Active) throw new ChemistryException(T("Остановите выполнение перед изменением целей.", "Stop execution before editing targets.")); }
    private void Edited()
    {
        if (Active) { Stop(); Status(T("Настройки изменены. Постройте новый план.", "Settings changed. Build a new plan.")); }
        _editVersion++; _plan = null; _planningCancellation?.Cancel(); _session = null; _lastSessionStatus = "";
    }
    private void Filter()
    {
        var search = (string)Get(_search, "Text")!;
        _choices = _catalog.Reagents.Values.Where(r => r.Id.Contains(search, StringComparison.OrdinalIgnoreCase) || r.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).Take(100).Select(r => r.Id).ToArray();
        Call(_choice, "Clear");
        for (var i = 0; i < _choices.Length; i++) Call(_choice, "AddItem", _catalog.Name(_choices[i]) + " [" + _choices[i] + "]", (int?)i);
        if (_choices.Length > 0) Call(_choice, "SelectId", 0);
    }
    private static int Amount(string text)
    {
        if (!decimal.TryParse(text.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) ||
            number <= 0 || number > 100000 || number * 100 != decimal.Truncate(number * 100))
            throw new ChemistryException(T("Объём: 0.01–100000 u.", "Amount: 0.01–100000 u."));
        return checked((int)(number * 100));
    }
    private void AddTarget()
    {
        RequireEditable();
        if (_choices.Length == 0) return;
        var id = _choices[Convert.ToInt32(Get(_choice, "SelectedId"))];
        var amount = Amount((string)Get(_amount, "Text")!);
        var index = _targets.FindIndex(t => t.Id == id);
        if (index < 0) { if (_targets.Count >= 64) throw new ChemistryException(T("Не более 64 целей.", "Target limit: 64.")); _targets.Add(new(id, amount)); }
        else _targets[index] = new(id, amount);
        Edited(); RenderTargets();
    }
    private void RenderTargets()
    {
        foreach (var row in _targetRows) Call(row, "Dispose"); _targetRows.Clear();
        foreach (var target in _targets.ToArray())
        {
            var row = Box("Horizontal");
            var label = Label(_catalog.Name(target.Id) + " [" + target.Id + "]"); Set(label, "HorizontalExpand", true); Add(row, label);
            Add(row, Label((target.Amount / 100m).ToString("0.##", CultureInfo.InvariantCulture) + " u"));
            Add(row, Button("×", () => Guard(() => { RequireEditable(); _targets.RemoveAll(t => t.Id == target.Id); Edited(); RenderTargets(); })));
            Add(_targetsBox, row); _targetRows.Add(row);
        }
    }
    private void ReloadSaved()
    {
        _recipes = _store.Load(); Call(_saved, "Clear");
        for (var i = 0; i < _recipes.Length; i++) Call(_saved, "AddItem", _recipes[i].Name, (int?)i);
        if (_recipes.Length > 0) Call(_saved, "SelectId", 0);
    }
    private void SaveRecipe()
    {
        _store.Save(new(((string)Get(_name, "Text")!).Trim(), Mode, _targets.ToArray())); ReloadSaved();
        Status(T("Набор сохранён.", "Recipe saved."));
    }
    private void LoadRecipe()
    {
        RequireEditable(); if (_recipes.Length == 0) return;
        var recipe = _recipes[Convert.ToInt32(Get(_saved, "SelectedId"))];
        _targets.Clear(); _targets.AddRange(recipe.Targets); Call(_mode, "SelectId", recipe.Mode == TargetMode.Make ? 0 : 1);
        Set(_name, "Text", recipe.Name); Edited(); RenderTargets();
        Status(T("Набор загружен. Предпросмотр проверит правила этого сервера.", "Loaded. Preview checks this server's rules."));
    }
    private void Preview()
    {
        if (!_catalogAvailable) throw new ChemistryException(T("Правила сервера недоступны. Откройте химмастер заново.", "Server rules unavailable. Reopen the ChemMaster."));
        RequireEditable(); Edited(); _session = null;
        var snapshot = _adapter.Read(_bui);
        var targets = _targets.ToArray(); var mode = Mode;
        _plannedBeakers = Get(_beakers, "Pressed") is true ? new(Temperature((string)Get(_hotTemperature, "Text")!)) : null;
        snapshot = ConfirmUnknownTemperature(snapshot, _plannedBeakers ?? new(Temperature((string)Get(_hotTemperature, "Text")!)));
        if (_plannedBeakers != null) _beakerStore.Save(_plannedBeakers);
        _resumeAfterPlanning = false;
        var catalog = _catalog; var beakers = _plannedBeakers;
        _planningCancellation?.Dispose(); _planningCancellation = new(); var token = _planningCancellation.Token;
        _planningVersion = _editVersion;
        _planning = Task.Run(() => new ProductionPlanner(catalog).Build(snapshot, targets, mode, false, token, beakers), token);
        Status(T("Рассчитываю порядок смешивания…", "Planning ingredient order…"));
        Set(_planText, "Text", "");
    }
    private void Start()
    {
        if (Active) return;
        if (_running is { Active: true } other && other != this) throw new ChemistryException(T("Другой химмастер уже выполняет рецепт.", "Another ChemMaster is executing."));
        if (_plan == null) throw new ChemistryException(T("Сначала выполните предпросмотр.", "Preview the recipe first."));
        _session = new(_plan, timing: _timing); _lastSessionStatus = ""; _session.Start(_adapter.Read(_bui));
        if (_session.Active) _running = this;
        RenderStatus();
    }
    private static float Temperature(string text)
    {
        if (!float.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !new BeakerTemperatures(value).Valid)
            throw new ChemistryException(T("Температура горячей мензурки должна быть конечной и выше 293.15 K.", "Hot beaker temperature must be finite and above 293.15 K."));
        return value;
    }
    private MachineSnapshot ConfirmUnknownTemperature(MachineSnapshot snapshot, BeakerTemperatures temperatures) => snapshot.Temperature != null ? snapshot :
        snapshot with { Temperature = Get(_hot, "Pressed") is true ? temperatures.Hot : temperatures.Cold };
    private void BeakerSelectionChanged()
    {
        // Selecting a phase is a confirmation choice, never an edit to an active production goal.
        if (!Active) Edited();
        RefreshTemperature();
    }
    private void ChangeInputBeaker()
    {
        if (Active && _session!.State != ExecutionState.AwaitingBeaker)
            throw new ChemistryException(T("Смена ёмкости разрешена на остановке для смены мензурки.", "Wait for a beaker replacement barrier before changing the container."));
        var state = Get(_bui, "State")!; var input = Get(state, "InputContainerInfo");
        if (input != null && Cents(Get(input, "CurrentVolume")!) != 0)
            throw new ChemistryException(T("Сначала нужна пустая входная мензурка.", "Input beaker must be empty."));
        var type = Native.Type("Content.Shared", "Content.Shared.Containers.ItemSlots.ItemSlotButtonPressedEvent");
        Call(_bui, "SendMessage", Activator.CreateInstance(type, ["beakerSlot", true, true])!);
        RefreshTemperature();
    }
    private void RefreshTemperature()
    {
        if (Clock.Elapsed < _nextTemperatureRefresh) return;
        _nextTemperatureRefresh = Clock.Elapsed + TimeSpan.FromMilliseconds(250);
        string message;
        try
        {
            var snapshot = _adapter.Read(_bui);
            message = snapshot.Temperature is { } temperature ? T("Входная мензурка: ", "Input beaker: ") + temperature.ToString("0.##", CultureInfo.InvariantCulture) + " K" :
                T("Температура сервера недоступна; используется выбранная фаза.", "Server temperature unavailable; using the selected phase.");
        }
        catch { message = T("Входная мензурка отсутствует или её состояние ещё не получено.", "No input beaker or its state has not arrived yet."); }
        if (_session?.RequiredBeakerTemperature is { } required)
            message += (_session.RequiredBeakerPhase == BeakerPhase.Hot ? T(" · требуется ГОРЯЧАЯ", " · HOT required") : T(" · требуется ХОЛОДНАЯ", " · COLD required")) + " (~" + required.ToString("0.##", CultureInfo.InvariantCulture) + " K)";
        Set(_temperatureStatus, "Text", message); Set(_temperatureStatus, "ToolTip", message);
    }
    private void Resume()
    {
        if (_planning != null || _session == null) return;
        if (_session.State != ExecutionState.AwaitingBeaker) { _session.Resume(_adapter.Read(_bui)); return; }
        var snapshot = _adapter.Read(_bui); _session.Observe(snapshot);
        if (!_session.Active) return;
        var hot = _session.RequiredBeakerPhase == BeakerPhase.Hot;
        if ((Get(_hot, "Pressed") is true) != hot)
            throw new ChemistryException(hot ? T("Выберите «Горячая ▶» после установки горячей мензурки.", "Select Hot ▶ after inserting the hot beaker.") : T("Выберите «◀ Холодная» после установки холодной мензурки.", "Select ◀ Cold after inserting the cold beaker."));
        if (snapshot.Temperature is { } actual)
        {
            // Replan from telemetry, because a prepared beaker can cool while the cold phase runs.
            if (hot && actual <= _plannedBeakers!.Cold + 1 || !hot && actual >= _plannedBeakers!.Hot)
                throw new ChemistryException((hot ? T("Нужна горячая мензурка", "Hot beaker required") : T("Нужна холодная мензурка", "Cold beaker required")) +
                    T(". Сервер сообщает ", ". Server reports ") + actual.ToString("0.##", CultureInfo.InvariantCulture) + T(" K. Выбор фазы не меняет температуру; замените ёмкость.", " K. Selecting a phase does not change temperature; replace the container."));
            _plannedBeakers = hot ? _plannedBeakers! with { Hot = actual } : _plannedBeakers! with { Cold = actual };
        }
        else snapshot = snapshot with { Temperature = hot ? Temperature((string)Get(_hotTemperature, "Text")!) : _plannedBeakers!.Cold };
        if (snapshot.Temperature is { } confirmed && hot) _plannedBeakers = _plannedBeakers! with { Hot = confirmed };
        var goals = _plan!.AbsoluteGoals.ToArray(); var catalog = _catalog; var beakers = _plannedBeakers;
        _planningCancellation?.Dispose(); _planningCancellation = new(); var token = _planningCancellation.Token;
        _planningVersion = _editVersion; _resumeAfterPlanning = true;
        _planning = Task.Run(() => new ProductionPlanner(catalog).Build(snapshot, goals, TargetMode.Ensure, false, token, beakers), token);
        Status(T("Проверяю оставшийся рецепт после смены мензурки…", "Checking remaining production after beaker replacement…"));
    }
    public void Observe()
    {
        if (_disposed) return;
        try
        {
            var current = _adapter.Read(_bui);
            _session?.Observe(current);
            if (!Active && _plan != null && (!_plan.Initial.SameChemistry(current) || _plan.Initial.Mode != current.Mode)) _plan = null;
        }
        catch (Exception e) { if (Active && _session!.State != ExecutionState.AwaitingBeaker) _session.Fail(e.GetBaseException().Message); }
        RenderStatus();
        RefreshTemperature();
    }
    public void Tick()
    {
        if (_disposed) return;
        if (_planning is { IsCompleted: true } task)
        {
            _planning = null;
            if (_planningVersion == _editVersion)
                Guard(() =>
                {
                    _plan = task.GetAwaiter().GetResult();
                    var current = _adapter.Read(_bui);
                    if (!_plan.Initial.SameChemistry(current) || _plan.Initial.Mode != current.Mode) { _plan = null; throw new ChemistryException(T("Состояние изменилось. Повторите предпросмотр.", "State changed. Preview again.")); }
                    ShowPlan(_plan); Status(T("План готов. Проверьте выход и нажмите «Начать».", "Plan ready. Check output, then Start."));
                    if (_resumeAfterPlanning)
                    {
                        _resumeAfterPlanning = false; _session = new(_plan, timing: _timing); _lastSessionStatus = ""; _session.Start(current);
                    }
                });
            else if (task.IsFaulted) _ = task.Exception;
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
        RenderStatus();
        RefreshTemperature();
    }
    private void ShowPlan(ProductionPlan plan)
    {
        var lines = new List<string> { T("Целевой запас:", "Target stock:") };
        lines.AddRange(plan.AbsoluteGoals.Select(t => _catalog.Name(t.Id) + ": " + (t.Amount / 100m).ToString("0.##", CultureInfo.InvariantCulture) + " u"));
        lines.Add(T("Фактический запас после приготовления:", "Actual stock after production:"));
        lines.AddRange(plan.AbsoluteGoals.Select(t => _catalog.Name(t.Id) + ": " + (plan.Final.Buffer.GetValueOrDefault(t.Id) / 100m).ToString("0.##", CultureInfo.InvariantCulture) + " u"));
        lines.Add(T("Команд: ", "Commands: ") + plan.Actions.Count);
        lines.AddRange(plan.Actions.Take(80).Select((a, i) => (i + 1) + ". " + (a.RequiredBeakerTemperature is { } temperature
            ? T("Установить пустую мензурку ", "Install an empty beaker at ") + temperature.ToString("0.##", CultureInfo.InvariantCulture) + " K"
            : _catalog.Name(a.ReagentId) + " · " + (a.Dose?.ToString() ?? T("всё", "all")) + (a.FromBuffer ? T(" → мензурка", " → beaker") : T(" → буфер", " → buffer")))));
        if (plan.Actions.Count > 80) lines.Add("…");
        Set(_planText, "Text", string.Join('\n', lines));
    }
    public void CatalogChanged()
    {
        Stop(); Edited(); Set(_cold, "Pressed", true); _catalogAvailable = false;
        Guard(() => { _catalog = _adapter.ReadCatalog(); _catalogAvailable = true; Filter(); RenderTargets(); Status(T("Правила изменились. Нужен новый предпросмотр.", "Rules changed. Preview again.")); });
    }
    public void ManualCommand()
    {
        if (Sending) return;
        if (_session?.State == ExecutionState.AwaitingBeaker) return;
        Edited(); if (Active) _session!.Fail(T("Ручная команда остановила рецепт.", "Manual command stopped the recipe."));
    }
    private void Stop()
    {
        _session?.Stop(); _planningCancellation?.Cancel(); _editVersion++; _plan = null;
        if (_running == this) _running = null;
        Status(T("Остановлено. Для нового запуска нужен предпросмотр.", "Stopped. Preview before restarting."));
    }
    private void RenderStatus()
    {
        if (_session == null) return;
        var status = Text.State(_session.State) + " · " + _session.Step + "/" + _session.Total + " " + _session.Message;
        if (_lastSessionStatus != status) { _lastSessionStatus = status; Status(status); }
        if (!Active && _running == this) _running = null;
    }
    private void Status(string message) { if (_lastStatus == message) return; _lastStatus = message; Set(_status, "Text", message); Set(_status, "ToolTip", message); }
    private void Guard(Action action)
    {
        if (_disposed) return;
        try { action(); }
        catch (Exception e)
        {
            var message = e.GetBaseException().Message;
            if (Active && _session!.State != ExecutionState.AwaitingBeaker) _session.Fail(message);
            Status(message); Bootstrap.Log("ChemMaster: " + message);
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
