using System.Collections;
using System.Linq.Expressions;
using System.Reflection;

namespace SS14LocalMods.ChemMaster;

internal static class Native
{
    internal const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
    public static Type Type(string assembly, string name) => AppDomain.CurrentDomain.GetAssemblies()
        .First(a => a.GetName().Name == assembly).GetType(name, true)!;
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
            if (t.GetProperty(name, Flags) is { } p) { p.SetValue(target, p.PropertyType.IsEnum && value is string s ? Enum.Parse(p.PropertyType, s) : value); return; }
            if (t.GetField(name, Flags) is { } f) { f.SetValue(target, value); return; }
        }
        throw new MissingMemberException(target.GetType().FullName, name);
    }
    public static object? Call(object target, string name, params object?[] args)
    {
        for (var t = target.GetType(); t != null; t = t.BaseType)
        {
            var method = t.GetMethods(Flags).FirstOrDefault(m => m.Name == name && !m.ContainsGenericParameters &&
                m.GetParameters().Length == args.Length && m.GetParameters().Select((p, i) => args[i] == null || p.ParameterType.IsInstanceOfType(args[i])).All(x => x));
            if (method != null) return method.Invoke(target, args);
        }
        throw new MissingMethodException(target.GetType().FullName, name);
    }
    public static object Resolve(Type type) => Type("Robust.Shared", "Robust.Shared.IoC.IoCManager")
        .GetMethod("ResolveType", [typeof(Type)])!.Invoke(null, [type])!;
    public static object Control(string name) => Activator.CreateInstance(Type("Robust.Client", "Robust.Client.UserInterface.Controls." + name))!;
    public static void Add(object parent, object child) => Call(parent, "AddChild", child);
    public static object Box(string orientation)
    {
        var box = Control("BoxContainer"); Set(box, "Orientation", orientation); Set(box, "HorizontalExpand", true); Set(box, "SeparationOverride", 5); return box;
    }
    public static object Label(string text) { var label = Control("Label"); Set(label, "Text", text); return label; }
    public static object Button(string text, Action action)
    {
        var button = Control("Button"); Set(button, "Text", text); Hook(button, "OnPressed", action); return button;
    }
    public static Action Hook(object target, string name, Action action)
        => HookArgument(target, name, _ => action());
    public static Action HookArgument(object target, string name, Action<object?> action)
    {
        var ev = target.GetType().GetEvent(name);
        var field = target.GetType().GetField(name);
        var handler = ev?.EventHandlerType ?? field?.FieldType ?? throw new MissingMemberException(name);
        var parameters = handler.GetMethod("Invoke")!.GetParameters().Select(p => Expression.Parameter(p.ParameterType)).ToArray();
        var argument = parameters.Length == 0 ? (Expression)Expression.Constant(null, typeof(object)) : Expression.Convert(parameters[0], typeof(object));
        var callback = Expression.Lambda(handler, Expression.Call(Expression.Constant(action), typeof(Action<object?>).GetMethod("Invoke")!, argument), parameters).Compile();
        if (ev != null) ev.AddEventHandler(target, callback);
        else field!.SetValue(target, Delegate.Combine((Delegate?)field.GetValue(target), callback));
        return () => { if (ev != null) ev.RemoveEventHandler(target, callback); else field!.SetValue(target, Delegate.Remove((Delegate?)field.GetValue(target), callback)); };
    }
    public static object Color(float r, float g, float b) =>
        Activator.CreateInstance(Type("Robust.Shared.Maths", "Robust.Shared.Maths.Color"), r, g, b, 1f)!;
    public static object Flat(object color)
    {
        var box = Activator.CreateInstance(Type("Robust.Client", "Robust.Client.Graphics.StyleBoxFlat"))!; Set(box, "BackgroundColor", color); return box;
    }
    public static IEnumerable<object> Items(object? value) => value is IEnumerable list ? list.Cast<object>() : [];
    public static int Cents(object value) => (int)Get(value, "Value")!;
    public static string T(string ru, string en) => Text.T(ru, en);
}

internal sealed class GameChemistryAdapter
{
    private readonly object _prototypeManager;
    private readonly MethodInfo _enumerate;
    private readonly Type _reactionType, _reagentType, _amountType, _messageType, _modeType, _modeMessage;
    public GameChemistryAdapter()
    {
        var contract = Native.Type("Robust.Shared", "Robust.Shared.Prototypes.IPrototypeManager");
        _prototypeManager = Native.Resolve(contract);
        _enumerate = contract.GetMethod("EnumeratePrototypes", [typeof(Type)])!;
        _reactionType = Native.Type("Content.Shared", "Content.Shared.Chemistry.Reaction.ReactionPrototype");
        _reagentType = Native.Type("Content.Shared", "Content.Shared.Chemistry.Reagent.ReagentPrototype");
        _amountType = Native.Type("Content.Shared", "Content.Shared.Chemistry.ChemMasterReagentAmount");
        _messageType = Native.Type("Content.Shared", "Content.Shared.Chemistry.ChemMasterReagentAmountButtonMessage");
        _modeType = Native.Type("Content.Shared", "Content.Shared.Chemistry.ChemMasterMode");
        _modeMessage = Native.Type("Content.Shared", "Content.Shared.Chemistry.ChemMasterSetModeMessage");
    }
    public ChemistryCatalog ReadCatalog()
    {
        var reagents = Native.Items(_enumerate.Invoke(_prototypeManager, [_reagentType])).Select(r => new Reagent(
            (string)Native.Get(r, "ID")!, (string)Native.Get(r, "LocalizedName")!, (float)Native.Get(r, "SpecificHeat")!)).ToArray();
        var reactions = Native.Items(_enumerate.Invoke(_prototypeManager, [_reactionType])).Select(r => new Reaction(
            (string)Native.Get(r, "ID")!, Native.Items(Native.Get(r, "Reactants")).Select(p => new Ingredient(
                (string)Native.Get(p, "Key")!, Native.Cents(Native.Get(Native.Get(p, "Value")!, "Amount")!),
                (bool)Native.Get(Native.Get(p, "Value")!, "Catalyst")!)).ToArray(),
            Native.Items(Native.Get(r, "Products")).Select(p => new Ingredient((string)Native.Get(p, "Key")!, Native.Cents(Native.Get(p, "Value")!))).ToArray(),
            (int)Native.Get(r, "Priority")!, (float)Native.Get(r, "MinimumTemperature")!, (float)Native.Get(r, "MaximumTemperature")!,
            (bool)Native.Get(r, "Quantized")!, (bool)Native.Get(r, "ConserveEnergy")!,
            Native.Items(Native.Get(r, "Effects")).Any(), Native.Get(r, "MixingCategories") != null,
            Native.Items(Native.Get(r, "MixingCategories")).Select(c => c.ToString()!).ToArray(),
            Native.Items(Native.Get(r, "Effects")).Select(e => e.GetType().FullName!).ToArray())).ToArray();
        var doses = Enum.GetNames(_amountType).Where(n => n.StartsWith('U') && int.TryParse(n.AsSpan(1), out _))
            .Select(n => int.Parse(n.AsSpan(1))).ToArray();
        return new(reagents, reactions, doses);
    }
    public MachineSnapshot Read(object bui)
    {
        if (Native.Get(bui, "IsOpened") is not true) throw new ChemistryException(Text.T("Химмастер закрыт", "ChemMaster is closed."));
        var state = Native.Get(bui, "State") ?? throw new ChemistryException(Text.T("Ожидание состояния", "Waiting for state."));
        var input = Native.Get(state, "InputContainerInfo") ?? throw new ChemistryException(Text.T("Нет входной мензурки", "No input beaker."));
        var owner = Native.Get(bui, "Owner")!;
        var player = Native.Get(Native.Get(bui, "PlayerManager")!, "LocalEntity") ?? throw new ChemistryException(Text.T("Нет персонажа", "No player."));
        var manager = Native.Get(bui, "EntMan")!;
        var slots = Native.Call(Native.Get(manager, "EntitySysManager")!, "GetEntitySystem",
            Native.Type("Content.Shared", "Content.Shared.Containers.ItemSlots.ItemSlotsSystem"))!;
        var beaker = Native.Call(slots, "GetItemOrNull", owner, "beakerSlot", null) ?? throw new ChemistryException(Text.T("Мензурка не подтверждена клиентом", "Beaker identity unavailable."));
        float? temperature = null;
        try
        {
            var system = Native.Call(Native.Get(manager, "EntitySysManager")!, "GetEntitySystem",
                Native.Type("Content.Client", "Content.Client.Chemistry.Containers.EntitySystems.SolutionContainerSystem"))!;
            var method = system.GetType().GetMethods().Single(m => m.Name == "TryGetFitsInDispenser" && m.GetParameters().Length == 3);
            var entity = Activator.CreateInstance(method.GetParameters()[0].ParameterType)!;
            Native.Set(entity, "Owner", beaker);
            object?[] args = [entity, null, null];
            if (method.Invoke(system, args) is true && args[2] is { } solution)
            {
                var measured = (float)Native.Get(solution, "Temperature")!;
                if (float.IsFinite(measured) && measured > 0) temperature = measured;
            }
        }
        catch (Exception) { /* optional capability: the UI requires an explicit cold-beaker confirmation */ }
        return new(ReadRows(Native.Get(state, "BufferReagents")), ReadRows(Native.Get(input, "Reagents")),
            Native.Cents(Native.Get(input, "MaxVolume")!), Convert.ToInt32(Native.Get(state, "Mode")),
            owner + "/" + Native.Get(bui, "UiKey") + "/" + player + "/" + beaker, temperature);
    }
    private static Dictionary<string, int> ReadRows(object? rows)
    {
        if (rows == null) throw new ChemistryException(Text.T("Состав недоступен", "Inventory unavailable."));
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in Native.Items(rows))
        {
            var id = Native.Get(row, "Reagent")!;
            if (Native.Items(Native.Get(id, "Data")).Any()) throw new ChemistryException(Text.T("Реагенты с дополнительными данными пока не поддерживаются", "Reagent data unsupported."));
            var prototype = (string)Native.Get(id, "Prototype")!;
            var amount = Native.Cents(Native.Get(row, "Quantity")!);
            if (amount <= 0 || !result.TryAdd(prototype, amount)) throw new ChemistryException(Text.T("Неоднозначный состав", "Ambiguous inventory."));
        }
        return result;
    }
    public void Send(object bui, MachineCommand command)
    {
        object message;
        if (command.SetTransferMode) message = Activator.CreateInstance(_modeMessage, [Enum.Parse(_modeType, "Transfer")])!;
        else
        {
            var transfer = command.Transfer!;
            var state = Native.Get(bui, "State")!;
            var rows = transfer.FromBuffer ? Native.Get(state, "BufferReagents") : Native.Get(Native.Get(state, "InputContainerInfo")!, "Reagents");
            var candidates = Native.Items(rows).Select(r => Native.Get(r, "Reagent")!)
                .Where(r => (string)Native.Get(r, "Prototype")! == transfer.ReagentId).ToArray();
            if (candidates.Length != 1) throw new ChemistryException(Text.T("Реагент отсутствует или неоднозначен", "Reagent unavailable or ambiguous."));
            var amount = Enum.Parse(_amountType, transfer.Dose is { } dose ? "U" + dose : "All");
            message = Activator.CreateInstance(_messageType, [candidates[0], amount, transfer.FromBuffer])!;
        }
        Native.Call(bui, "SendMessage", message);
    }
}
