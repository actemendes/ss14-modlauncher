namespace SS14LocalMods.ChemMaster;

public enum ExecutionState { Ready, Running, Waiting, AwaitingState, AwaitingBeaker, Paused, Completed, Stopped, Failed }
public sealed record MachineCommand(bool SetTransferMode, TransferAction? Transfer = null);

// Tick is called on the game thread. Observe never sends a command; only one command
// may be in flight, including a mode switch. Timeouts never retry ambiguous input.
public sealed class ExecutionSession(ProductionPlan plan, TimeSpan? timeout = null, ExecutionTiming? timing = null, Func<double>? random = null)
{
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(8);
    private ExecutionTiming? _timing = timing;
    private readonly Func<double> _random = random ?? Random.Shared.NextDouble;
    private string? _lastReagent;
    private bool _hasSent;
    private int _scheduledStep = -1;
    private TimeSpan _notBefore;
    private MachineSnapshot _confirmed = plan.Initial.Copy();
    private TransferAction? _pending;
    private bool _modePending, _paused;
    private TimeSpan _sentAt;
    public ExecutionState State { get; private set; } = ExecutionState.Ready;
    public string Message { get; private set; } = "";
    public int Step { get; private set; }
    public int Total => plan.Actions.Count;
    public bool Active => State is ExecutionState.Running or ExecutionState.Waiting or ExecutionState.AwaitingState or ExecutionState.AwaitingBeaker or ExecutionState.Paused;
    public float? RequiredBeakerTemperature => State == ExecutionState.AwaitingBeaker ? plan.Actions[Step].RequiredBeakerTemperature : null;
    public BeakerPhase? RequiredBeakerPhase => State == ExecutionState.AwaitingBeaker ? plan.Actions[Step].RequiredBeakerPhase : null;
    private static string Scope(string identity) => identity.LastIndexOf('/') is var slash && slash >= 0 ? identity[..slash] : identity;
    private MachineSnapshot CurrentIdentity(MachineSnapshot snapshot) => snapshot with { Identity = _confirmed.Identity };
    public void UpdateTiming(ExecutionTiming timing)
    {
        if (!timing.Valid) throw new ArgumentException(Text.T("Неверные настройки скорости.", "Invalid timing settings."));
        _timing = timing; _scheduledStep = -1;
    }
    public void Start(MachineSnapshot current)
    {
        if (State != ExecutionState.Ready) throw new InvalidOperationException("Session already started.");
        if (!_confirmed.SameChemistry(current) || _confirmed.Mode != current.Mode) { Fail(Text.T("Состояние изменилось после предпросмотра", "Preview is stale.")); return; }
        _confirmed = current.Copy(); State = ExecutionState.Running;
    }
    public void Observe(MachineSnapshot current)
    {
        if (!Active) return;
        if (State == ExecutionState.AwaitingBeaker)
        {
            if (Scope(current.Identity) != Scope(_confirmed.Identity) || current.Capacity != _confirmed.Capacity ||
                !MachineSnapshot.Same(current.Buffer, _confirmed.Buffer) || current.Beaker.Count != 0 || current.Mode != _confirmed.Mode)
                Fail(Text.T("Во время смены мензурки изменились аппарат, персонаж, состав, режим или вместимость", "Machine, player, inventory, mode or capacity changed during beaker replacement."));
            return;
        }
        if (current.Identity != _confirmed.Identity || current.Capacity != _confirmed.Capacity) { Fail(Text.T("Аппарат или мензурка изменились", "Machine or beaker changed.")); return; }
        if (_modePending)
        {
            if (!_confirmed.SameChemistry(current)) { Fail(Text.T("Состав изменился", "Inventory changed.")); return; }
            if (current.Mode == 0) { _modePending = false; _confirmed = current.Copy(); State = _paused ? ExecutionState.Paused : ExecutionState.Running; }
            return;
        }
        if (_pending != null)
        {
            if (current.Mode != 0) { Fail(Text.T("Режим уничтожения", "Discard mode.")); return; }
            if (CurrentIdentity(_pending.After).SameChemistry(current))
            {
                _confirmed = current.Copy(); _pending = null; Step++;
                State = _paused ? ExecutionState.Paused : ExecutionState.Running;
            }
            // A transfer and its reaction can arrive in separate updates. Hold until
            // the expected stable state or timeout, without sending or accepting a replan.
            return;
        }
        if (!_confirmed.SameChemistry(current) || current.Mode != _confirmed.Mode)
            Fail(Text.T("Ручное вмешательство: нужен новый план", "Inventory changed: build a new plan."));
    }
    public MachineCommand? Tick(TimeSpan now, MachineSnapshot current)
    {
        if (!Active) return null;
        if (State == ExecutionState.AwaitingBeaker) { Observe(current); return null; }
        Observe(current);
        if (!Active) return null;
        if (_pending != null || _modePending)
        {
            if (now - _sentAt >= _timeout) Fail(Text.T("Результат команды не подтверждён; повтор запрещён", "Unconfirmed command; no retry."));
            return null;
        }
        if (_paused) { State = ExecutionState.Paused; return null; }
        if (Step == Total) { State = ExecutionState.Completed; Message = Text.T("Готово", "Completed"); return null; }
        var action = plan.Actions[Step];
        if (action.RequiredBeakerTemperature is { } temperature)
        {
            if (!CurrentIdentity(action.Before).SameChemistry(current)) { Fail(Text.T("Состав отличается от плана", "Inventory differs from plan.")); return null; }
            State = ExecutionState.AwaitingBeaker;
            Message = (action.RequiredBeakerPhase == BeakerPhase.Hot ? Text.T("Нужна горячая пустая мензурка: ", "Hot empty beaker required: ") : Text.T("Нужна холодная пустая мензурка: ", "Cold empty beaker required: ")) + temperature.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
                Text.T(" K той же вместимости и нажмите «Продолжить».", " K with the same capacity, then Resume.");
            return null;
        }
        if (_hasSent && _timing != null)
        {
            if (_scheduledStep != Step)
            {
                _notBefore = now + _timing.Delay(_lastReagent != null && _lastReagent != action.ReagentId, _random);
                _scheduledStep = Step;
            }
            if (now < _notBefore) { State = ExecutionState.Waiting; return null; }
        }
        if (current.Mode != 0)
        {
            _hasSent = true; _scheduledStep = -1;
            _modePending = true; _sentAt = now; State = ExecutionState.AwaitingState; return new(true);
        }
        if (!CurrentIdentity(action.Before).SameChemistry(current)) { Fail(Text.T("Состав отличается от плана", "Inventory differs from plan.")); return null; }
        _lastReagent = action.ReagentId; _hasSent = true;
        _pending = action; _sentAt = now; State = ExecutionState.AwaitingState;
        return new(false, action);
    }
    public void Pause() { if (Active && State != ExecutionState.AwaitingBeaker) { _paused = true; State = ExecutionState.Paused; } }
    public void Resume(MachineSnapshot current)
    {
        if (!Active) return;
        if (State == ExecutionState.AwaitingBeaker)
        {
            Observe(current); if (!Active) return;
            var temperature = RequiredBeakerTemperature!.Value;
            if (current.Temperature is { } measured && Math.Abs(measured - temperature) > 1)
                throw new ChemistryException(Text.T("Температура мензурки отличается от плана: ", "Beaker temperature differs from plan: ") + measured.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " K");
            // If telemetry is unavailable, Resume explicitly confirms the displayed temperature.
            _confirmed = current.Copy(); Step++; _hasSent = false; _lastReagent = null; _scheduledStep = -1;
            State = ExecutionState.Running; Message = ""; return;
        }
        Observe(current);
        if (!Active) return;
        _paused = false; State = _pending != null || _modePending ? ExecutionState.AwaitingState : ExecutionState.Running;
    }
    public void Stop() { if (State != ExecutionState.Completed) { State = ExecutionState.Stopped; Message = Text.T("Остановлено", "Stopped"); } }
    public void Fail(string message) { State = ExecutionState.Failed; Message = message; }
}
