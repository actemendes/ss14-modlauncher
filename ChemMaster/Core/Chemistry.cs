using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SS14LocalMods.ChemMaster;

public sealed record Reagent(string Id, string Name, float SpecificHeat);
public sealed record Ingredient(string Id, int Amount, bool Catalyst = false);
public sealed record Reaction(string Id, Ingredient[] Inputs, Ingredient[] Outputs, int Priority = 0,
    float MinimumTemperature = 0, float MaximumTemperature = float.PositiveInfinity,
    bool Quantized = false, bool ConserveEnergy = true, bool HasEffects = false, bool RequiresMixer = false,
    string[]? MixerCategories = null, string[]? EffectTypes = null)
{
    // These server effects do not alter the beaker's composition or thermal energy.
    public bool HasUnsupportedEffects => HasEffects && (EffectTypes == null || EffectTypes.Any(type => type is not
        "Content.Shared.EntityEffects.Effects.Atmos.CreateGas" and not
        "Content.Shared.EntityEffects.Effects.Transform.PopupMessage"));
}
public sealed record Target(string Id, int Amount);
public enum BeakerPhase { Cold, Hot }
public sealed record BeakerTemperatures(float Hot, float Cold = 293.15f)
{
    public bool Valid => float.IsFinite(Hot) && float.IsFinite(Cold) && Cold > 0 && Hot > Cold;
}
public enum TargetMode { Make, Ensure }

// All amounts are fixed-point hundredths. Catalogs belong to one connection; no disk/wiki fallback.
public sealed class ChemistryCatalog
{
    public IReadOnlyDictionary<string, Reagent> Reagents { get; }
    public IReadOnlyList<Reaction> Reactions { get; }
    public IReadOnlyList<int> Doses { get; }
    public string Fingerprint { get; }
    public ChemistryCatalog(IEnumerable<Reagent> reagents, IEnumerable<Reaction> reactions, IEnumerable<int> doses)
    {
        Reagents = reagents.ToDictionary(r => r.Id, StringComparer.Ordinal);
        Reactions = reactions.OrderByDescending(r => r.Priority).ThenBy(r => r.Outputs.Length)
            .ThenBy(r => r.Id, StringComparer.Ordinal).ToArray();
        Doses = doses.Where(d => d > 0 && d <= 100000).Distinct().OrderDescending().ToArray();
        if (Reagents.Count == 0 || Reactions.Count == 0 || Doses.Count == 0)
            throw new ChemistryException(Text.T("Пустой каталог химии", "Empty chemistry catalog."));
        if (Reagents.Values.Any(r => !float.IsFinite(r.SpecificHeat) || r.SpecificHeat <= 0))
            throw new ChemistryException(Text.T("Неподдерживаемая теплоёмкость", "Unsupported heat capacity."));
        foreach (var r in Reactions)
            if (r.Inputs.Length == 0 || r.Inputs.Concat(r.Outputs).Any(i => i.Amount <= 0 || !Reagents.ContainsKey(i.Id)) ||
                r.Inputs.Select(i => i.Id).Distinct(StringComparer.Ordinal).Count() != r.Inputs.Length ||
                float.IsNaN(r.MinimumTemperature) || float.IsNaN(r.MaximumTemperature))
                throw new ChemistryException(Text.T("Неподдерживаемая реакция: ", "Unsupported reaction: ") + r.Id);
        Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            reagents = Reagents.Values.OrderBy(r => r.Id, StringComparer.Ordinal),
            reactions = Reactions.Select(r => new { r.Id, r.Inputs, r.Outputs, r.Priority,
                min = r.MinimumTemperature.ToString("R", CultureInfo.InvariantCulture),
                max = r.MaximumTemperature.ToString("R", CultureInfo.InvariantCulture),
                r.Quantized, r.ConserveEnergy, r.HasEffects, r.RequiresMixer, r.MixerCategories, r.EffectTypes }), doses = Doses
        }))));
    }
    public string Name(string id) => Reagents.GetValueOrDefault(id)?.Name ?? id;
}

public sealed class ChemistryException(string message) : Exception(message);
public sealed record MachineSnapshot(Dictionary<string, int> Buffer, Dictionary<string, int> Beaker,
    int Capacity, int Mode, string Identity, float? Temperature = null)
{
    public MachineSnapshot Copy() => this with { Buffer = new(Buffer, StringComparer.Ordinal), Beaker = new(Beaker, StringComparer.Ordinal) };
    public bool SameChemistry(MachineSnapshot other) => Capacity == other.Capacity && Identity == other.Identity &&
        Same(Buffer, other.Buffer) && Same(Beaker, other.Beaker) &&
        (Temperature == null || other.Temperature == null || Math.Abs(Temperature.Value - other.Temperature.Value) <= 1);
    public static bool Same(IReadOnlyDictionary<string, int> a, IReadOnlyDictionary<string, int> b) =>
        a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out var value) && value == p.Value);
}
// A temperature marker sends no game message: the user installs an empty beaker and confirms.
public sealed record TransferAction(string ReagentId, int? Dose, bool FromBuffer, MachineSnapshot Before, MachineSnapshot After,
    float? RequiredBeakerTemperature = null, BeakerPhase? RequiredBeakerPhase = null);
public sealed record ProductionPlan(string CatalogFingerprint, MachineSnapshot Initial,
    IReadOnlyList<Target> AbsoluteGoals, IReadOnlyList<TransferAction> Actions, MachineSnapshot Final);

// The reaction order, integer stoichiometric batches and ingredient-order search follow
// ChemMaster Assistant (d379b6c). Runtime inputs replace its pinned SS220 JSON and calibrated UI.
public sealed class ProductionPlanner(ChemistryCatalog catalog)
{
    private const int MaxActions = 10000;
    public ProductionPlan Build(MachineSnapshot initial, IReadOnlyList<Target> targets, TargetMode mode, bool confirmCold = false,
        CancellationToken cancellation = default, BeakerTemperatures? beakers = null)
    {
        if (initial.Capacity <= 0 || initial.Beaker.Count != 0) throw new ChemistryException(Text.T("Нужна пустая входная мензурка", "Empty input beaker required."));
        if (initial.Temperature is null && !confirmCold) throw new ChemistryException(Text.T("Подтвердите холодную мензурку", "Confirm a cold beaker."));
        if (beakers is { Valid: false }) throw new ChemistryException(Text.T("Неверная температура мензурок", "Invalid beaker temperatures."));
        if (targets.Count == 0 || targets.Count > 64 || targets.Any(t => t.Amount <= 0 || t.Amount > 10000000 || !catalog.Reagents.ContainsKey(t.Id)))
            throw new ChemistryException(Text.T("Неверные цели", "Invalid targets."));
        var goals = targets.GroupBy(t => t.Id, StringComparer.Ordinal).Select(g => new Target(g.Key,
            checked(g.Sum(t => t.Amount) + (mode == TargetMode.Make ? initial.Buffer.GetValueOrDefault(g.Key) : 0)))).ToArray();
        var machine = new Simulation(catalog, initial, confirmCold, beakers);
        var actions = new List<TransferAction>();
        var attempts = new SearchBudget(cancellation);
        // A later target may consume an earlier one. Reconcile absolute goals instead of
        // interpreting every replan as another make request.
        for (var pass = 0; pass < 16; pass++)
        {
            foreach (var goal in goals) Ensure(machine, goal.Id, goal.Amount, [], actions, attempts);
            if (goals.All(g => machine.Buffer.GetValueOrDefault(g.Id) >= g.Amount))
                return new(catalog.Fingerprint, initial.Copy(), goals, actions, machine.Snapshot());
        }
        throw new ChemistryException(Text.T("Цели конкурируют за продукты", "Conflicting production goals."));
    }

    private void Ensure(Simulation machine, string id, int amount, HashSet<string> stack,
        List<TransferAction> actions, SearchBudget budget)
    {
        budget.Check();
        if (machine.Buffer.GetValueOrDefault(id) >= amount) return;
        if (stack.Count >= 32 || !stack.Add(id)) throw new ChemistryException(Text.T("Цикл рецепта: ", "Recipe cycle: ") + id);
        try
        {
            var recipes = catalog.Reactions.Where(r => r.Outputs.Any(o => o.Id == id) &&
                r.Outputs.Where(o => o.Id == id).Sum(o => o.Amount) > r.Inputs.Where(i => i.Id == id && !i.Catalyst).Sum(i => i.Amount))
                .OrderBy(r => r.HasUnsupportedEffects || r.RequiresMixer).ThenBy(r => r.Inputs.Length).ToArray();
            string failure = Text.T("Не хватает исходного реагента: ", "Missing base reagent: ") + catalog.Name(id);
            string? supportedFailure = null;
            foreach (var recipe in recipes)
            {
                var trial = machine.Clone();
                var candidate = new List<TransferAction>();
                try
                {
                    if (recipe.RequiresMixer)
                        throw new ChemistryException(failure + Text.T(". Реакция ", ". Reaction ") + recipe.Id + Text.T(" требует аппарата: ", " requires apparatus: ") + string.Join(", ", recipe.MixerCategories ?? ["mixer"]));
                    if (recipe.HasUnsupportedEffects)
                        throw new ChemistryException(Text.T("Неподдерживаемые побочные эффекты реакции: ", "Unsupported reaction effects: ") + recipe.Id);
                    if (trial.Beakers == null && (trial.Temperature < recipe.MinimumTemperature || trial.Temperature > recipe.MaximumTemperature))
                        throw new ChemistryException(Text.T("Нужна мензурка в диапазоне ", "Beaker temperature required: ") + recipe.MinimumTemperature.ToString("0.##", CultureInfo.InvariantCulture) + "–" + recipe.MaximumTemperature.ToString("0.##", CultureInfo.InvariantCulture) + " K: " + recipe.Id);
                    Produce(trial, recipe, id, amount, stack, candidate, budget);
                    machine.Adopt(trial); actions.AddRange(candidate);
                    if (actions.Count > MaxActions) throw new ChemistryException(Text.T("Слишком большой план", "Action limit."));
                    return;
                }
                catch (ChemistryException e)
                {
                    if (!recipe.RequiresMixer && !recipe.HasUnsupportedEffects) supportedFailure ??= e.Message;
                    else failure = e.Message;
                }
            }
            throw new ChemistryException(supportedFailure ?? failure);
        }
        finally { stack.Remove(id); }
    }

    private void Produce(Simulation machine, Reaction recipe, string targetId, int goal, HashSet<string> stack,
        List<TransferAction> actions, SearchBudget budget)
    {
        var quantum = Enumerable.Range(1, 100).FirstOrDefault(n => recipe.Inputs.Where(i => !i.Catalyst).All(i => (long)i.Amount * n % 100 == 0));
        if (quantum == 0) throw new ChemistryException(Text.T("Дозы рецепта недостижимы: ", "Unreachable recipe doses: ") + recipe.Id);
        var netYield = recipe.Outputs.Where(o => o.Id == targetId).Sum(o => o.Amount) - recipe.Inputs.Where(i => i.Id == targetId && !i.Catalyst).Sum(i => i.Amount);
        var catalysts = recipe.Inputs.Where(i => i.Catalyst).Sum(i => i.Amount);
        var perRepeat = Math.Max(recipe.Inputs.Where(i => !i.Catalyst).Sum(i => i.Amount), recipe.Outputs.Sum(o => o.Amount));
        var maxRepeats = perRepeat == 0 ? 0 : (machine.Capacity - catalysts) / perRepeat / quantum * quantum;
        if (maxRepeats < quantum) throw new ChemistryException(Text.T("Рецепт не помещается: ", "Beaker too small: ") + recipe.Id);
        while (machine.Buffer.GetValueOrDefault(targetId) < goal)
        {
            budget.Check();
            var remaining = goal - machine.Buffer.GetValueOrDefault(targetId);
            var repeats = checked((int)Math.Ceiling((decimal)remaining / netYield / quantum) * quantum);
            var maximum = Math.Min(repeats, maxRepeats);
            foreach (var i in recipe.Inputs)
            {
                var required = checked(i.Amount * (i.Catalyst ? 1 : maximum));
                if (i.Id == targetId && machine.Buffer.GetValueOrDefault(i.Id) < required)
                    throw new ChemistryException(Text.T("Нужен исходный продукт: ", "Seed reagent required: ") + i.Id);
                Ensure(machine, i.Id, required, stack, actions, budget);
            }
            var sizes = new HashSet<int> { maximum, quantum };
            foreach (var i in recipe.Inputs.Where(i => !i.Catalyst))
                foreach (var dose in catalog.Doses)
                    if ((long)dose * 100 % i.Amount == 0 && dose * 100 / i.Amount is var n && n >= quantum && n <= maximum && n % quantum == 0)
                        sizes.Add(n);
            Simulation? accepted = null;
            List<TransferAction>? acceptedActions = null;
            foreach (var n in sizes.OrderDescending())
            {
                var temperatures = machine.Beakers is { } beakers
                    ? new[] { machine.Temperature, beakers.Cold, beakers.Hot }.Where(t => t >= recipe.MinimumTemperature && t <= recipe.MaximumTemperature).Distinct().ToArray()
                    : new[] { machine.Temperature };
                if (temperatures.Length == 0)
                    throw new ChemistryException(Text.T("Подготовленные мензурки не подходят для реакции: ", "Prepared beakers cannot perform reaction: ") + recipe.Id);
                foreach (var temperature in temperatures)
                {
                foreach (var order in Orders(recipe.Inputs).Take(120))
                {
                    budget.Check();
                    var trial = machine.Clone();
                    var candidate = new List<TransferAction>();
                    try
                    {
                        if (Math.Abs(trial.Temperature - temperature) > 1)
                            candidate.Add(trial.ChangeBeaker(temperature));
                        var expected = recipe.Outputs.ToDictionary(i => i.Id, i => checked(i.Amount * n), StringComparer.Ordinal);
                        foreach (var catalyst in recipe.Inputs.Where(i => i.Catalyst))
                            expected[catalyst.Id] = checked(expected.GetValueOrDefault(catalyst.Id) + catalyst.Amount);
                        foreach (var input in order) TransferExact(trial, input.Id, checked(input.Amount * (input.Catalyst ? 1 : n)), candidate);
                        if (!MachineSnapshot.Same(trial.Beaker, expected)) throw new ChemistryException(Text.T("Конкурирующая реакция", "Competing reaction."));
                        foreach (var product in trial.Beaker.Keys.ToArray()) candidate.Add(trial.Transfer(product, null, false));
                        accepted = trial; acceptedActions = candidate; break;
                    }
                    catch (ChemistryException) { }
                }
                if (accepted != null) break;
                }
                if (accepted != null) break;
            }
            if (accepted == null) throw new ChemistryException(Text.T("Нет проверенного порядка смешивания: ", "No safe mixing sequence: ") + recipe.Id);
            machine.Adopt(accepted); actions.AddRange(acceptedActions!);
            if (actions.Count > MaxActions) throw new ChemistryException(Text.T("Слишком большой план", "Action limit."));
        }
    }

    private void TransferExact(Simulation machine, string id, int amount, List<TransferAction> actions)
    {
        if (machine.Buffer.GetValueOrDefault(id) < amount) throw new ChemistryException(Text.T("Не хватает: ", "Missing: ") + id);
        while (amount > 0)
        {
            var dose = catalog.Doses.FirstOrDefault(d => d * 100 <= amount);
            if (dose == 0) throw new ChemistryException(Text.T("Дробная доза недоступна", "Fractional dose unavailable."));
            actions.Add(machine.Transfer(id, dose, true)); amount -= dose * 100;
        }
    }
    private static IEnumerable<Ingredient[]> Orders(Ingredient[] inputs)
    {
        if (inputs.Length <= 1) { yield return inputs; yield break; }
        for (var i = 0; i < inputs.Length; i++)
            foreach (var tail in Orders(inputs.Where((_, j) => j != i).ToArray())) yield return [inputs[i], ..tail];
    }
    private sealed class SearchBudget(CancellationToken cancellation)
    {
        private int _attempts;
        public void Check() { cancellation.ThrowIfCancellationRequested(); if (++_attempts > 100000) throw new OperationCanceledException(Text.T("Лимит поиска", "Search limit.")); }
    }
}

internal sealed class Simulation
{
    private readonly ChemistryCatalog _catalog;
    private readonly string _identity;
    public Dictionary<string, int> Buffer { get; private set; }
    public Dictionary<string, int> Beaker { get; private set; }
    public int Capacity { get; }
    public float Temperature { get; private set; }
    public BeakerTemperatures? Beakers { get; }
    public Simulation(ChemistryCatalog catalog, MachineSnapshot state, bool confirmCold, BeakerTemperatures? beakers = null)
    {
        _catalog = catalog; _identity = state.Identity; Capacity = state.Capacity; Beakers = beakers;
        Buffer = new(state.Buffer, StringComparer.Ordinal); Beaker = new(state.Beaker, StringComparer.Ordinal);
        Temperature = state.Temperature ?? (confirmCold ? 293.15f : throw new ChemistryException(Text.T("Температура неизвестна.", "Unknown temperature.")));
        if (!float.IsFinite(Temperature) || Buffer.Concat(Beaker).Any(p => p.Value <= 0 || !catalog.Reagents.ContainsKey(p.Key)))
            throw new ChemistryException(Text.T("Неподдерживаемый состав", "Unsupported inventory."));
    }
    public MachineSnapshot Snapshot() => new(new(Buffer, StringComparer.Ordinal), new(Beaker, StringComparer.Ordinal), Capacity, 0, _identity, Temperature);
    public Simulation Clone() => new(_catalog, Snapshot(), false, Beakers);
    public TransferAction ChangeBeaker(float temperature)
    {
        if (Beaker.Count != 0) throw new ChemistryException(Text.T("Смена мензурки требует пустой ёмкости", "Beaker must be empty before replacement."));
        var before = Snapshot(); Temperature = temperature;
        return new("", null, false, before, Snapshot(), temperature, Beakers?.Hot == temperature ? BeakerPhase.Hot : BeakerPhase.Cold);
    }
    public void Adopt(Simulation source) { Buffer = new(source.Buffer, StringComparer.Ordinal); Beaker = new(source.Beaker, StringComparer.Ordinal); Temperature = source.Temperature; }
    public TransferAction Transfer(string id, int? dose, bool fromBuffer)
    {
        var before = Snapshot();
        var source = fromBuffer ? Buffer : Beaker;
        var destination = fromBuffer ? Beaker : Buffer;
        var wanted = dose.HasValue ? checked(dose.Value * 100) : source.GetValueOrDefault(id);
        var moved = Math.Min(wanted, source.GetValueOrDefault(id));
        if (fromBuffer) moved = Math.Min(moved, Capacity - Beaker.Values.Sum());
        if (moved <= 0 || dose.HasValue && moved != wanted) throw new ChemistryException(Text.T("Неполный перенос", "Partial transfer."));
        Remove(source, id, moved); Add(destination, id, moved);
        if (fromBuffer) React();
        return new(id, dose, fromBuffer, before, Snapshot());
    }
    private float HeatCapacity() => Beaker.Sum(p => p.Value / 100f * _catalog.Reagents[p.Key].SpecificHeat);
    private void React()
    {
        for (var iteration = 0; iteration < 20; iteration++)
        {
            Reaction? reaction = null;
            long repeats = 0;
            foreach (var candidate in _catalog.Reactions)
            {
                if (candidate.RequiresMixer || Temperature < candidate.MinimumTemperature || Temperature > candidate.MaximumTemperature) continue;
                long units = int.MaxValue;
                var possible = true;
                foreach (var input in candidate.Inputs)
                {
                    var have = Beaker.GetValueOrDefault(input.Id);
                    if (have <= 0 || input.Catalyst && candidate.Quantized && have < input.Amount) { possible = false; break; }
                    if (!input.Catalyst) units = Math.Min(units, 100L * have / input.Amount);
                }
                if (candidate.Quantized) units = units / 100 * 100;
                if (!possible || units <= 0) continue;
                if (units == int.MaxValue) throw new ChemistryException(Text.T("Реакция без расходуемых веществ", "Reaction without consumables."));
                reaction = candidate; repeats = units; break;
            }
            if (reaction == null)
            {
                if (Beaker.Values.Sum() > Capacity) throw new ChemistryException(Text.T("Переполнение", "Overflow."));
                return;
            }
            if (reaction.HasUnsupportedEffects) throw new ChemistryException(Text.T("Неподдерживаемый эффект: ", "Unsupported effect: ") + reaction.Id);
            var energy = reaction.ConserveEnergy ? HeatCapacity() * Temperature : 0;
            foreach (var i in reaction.Inputs.Where(i => !i.Catalyst)) Remove(Beaker, i.Id, checked((int)(i.Amount * repeats / 100)));
            foreach (var o in reaction.Outputs) Add(Beaker, o.Id, checked((int)(o.Amount * repeats / 100)));
            if (reaction.ConserveEnergy && HeatCapacity() is var heat && heat > 0) Temperature = energy / heat;
        }
        throw new ChemistryException(Text.T("Лимит реакций", "Reaction limit."));
    }
    private static void Add(Dictionary<string, int> values, string id, int amount) { if (amount > 0) values[id] = checked(values.GetValueOrDefault(id) + amount); }
    private static void Remove(Dictionary<string, int> values, string id, int amount)
    {
        var left = values.GetValueOrDefault(id) - amount;
        if (left < 0) throw new ChemistryException(Text.T("Недостаточный запас", "Insufficient stock."));
        if (left == 0) values.Remove(id); else values[id] = left;
    }
}
