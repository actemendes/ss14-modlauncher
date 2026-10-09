using System.Collections;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using SS14LocalMods.DeathRattle;

namespace SS14LocalMods;

public static class Mod
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
    private static readonly DistressPolicy Policy = new();
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static Type _chatContract = null!, _channelType = null!, _entityContract = null!, _timingContract = null!;
    private static Type[] _humanoids = [];
    private static MethodInfo _resolve = null!, _send = null!, _tryComponent = null!, _identityName = null!;
    private static object? _chat;
    private static Type _clientAlerts = null!;
    private static TimeSpan _nextPoll;
    private static bool _disabled, _attackerDisabled;

    public static void Install(Assembly content)
    {
        var controller = content.GetType("Content.Client.UserInterface.Systems.Alerts.AlertsUIController", true)!;
        var sync = controller.GetMethod("SystemOnSyncAlerts", Flags) ?? throw new MissingMethodException(controller.FullName, "SystemOnSyncAlerts");
        var clear = controller.GetMethod("SystemOnClearAlerts", Flags) ?? throw new MissingMethodException(controller.FullName, "SystemOnClearAlerts");
        _chatContract = content.GetType("Content.Client.Chat.Managers.IChatManager", true)!;
        var shared = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Content.Shared");
        var robust = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Robust.Shared");
        _channelType = shared.GetType("Content.Shared.Chat.ChatSelectChannel", true)!;
        _send = _chatContract.GetMethod("SendMessage", [typeof(string), _channelType])!;
        if (_send == null || !Enum.IsDefined(_channelType, "Radio")) throw new MissingMethodException("Radio chat API unavailable.");
        _resolve = robust.GetType("Robust.Shared.IoC.IoCManager", true)!.GetMethod("ResolveType", [typeof(Type)])!;
        _clientAlerts = content.GetType("Content.Client.Alerts.ClientAlertsSystem", true)!;
        var update = shared.GetType("Content.Shared.Alert.AlertsSystem", true)!.GetMethod("Update", [typeof(float)])
            ?? throw new MissingMethodException("AlertsSystem.Update(float)");
        var harmony = new Harmony("local.death-rattle");
        try
        {
            harmony.Patch(sync, postfix: new HarmonyMethod(typeof(Mod), nameof(AlertsSynced)));
            harmony.Patch(clear, postfix: new HarmonyMethod(typeof(Mod), nameof(AlertsCleared)));
            harmony.Patch(update, postfix: new HarmonyMethod(typeof(Mod), nameof(PollAlerts)));
        }
        catch { harmony.UnpatchAll(harmony.Id); throw; }
        Bootstrap.Log("Death Rattle: awful health (HumanHealth severity 4), native radio chat installed.");

        // Optional capability: replicated damage normally omits Origin. Never guess from nearby mobs or lunges.
        try
        {
            _entityContract = robust.GetType("Robust.Shared.GameObjects.IEntityManager", true)!;
            var uid = robust.GetType("Robust.Shared.GameObjects.EntityUid", true)!;
            _tryComponent = _entityContract.GetMethods().Single(m => m.Name == "TryGetComponent" && !m.IsGenericMethod
                && m.GetParameters() is var p && p.Length == 3 && p[0].ParameterType == uid && p[1].ParameterType == typeof(Type));
            _timingContract = robust.GetType("Robust.Shared.Timing.IGameTiming", true)!;
            _identityName = shared.GetType("Content.Shared.IdentityManagement.Identity", true)!.GetMethods()
                .Single(m => m.Name == "Name" && m.GetParameters().Length == 3);
            _humanoids = new[] { "Content.Shared.Humanoid.HumanoidProfileComponent", "Content.Shared.Humanoid.HumanoidAppearanceComponent" }
                .Select(shared.GetType).OfType<Type>().ToArray();
            if (_humanoids.Length == 0) throw new TypeLoadException("No supported humanoid component.");
            var damage = shared.GetType("Content.Shared.Damage.Systems.DamageableSystem", true)!;
            var changed = damage.GetMethod("OnEntityDamageChanged", Flags)!;
            if (changed == null || changed.GetParameters().Length != 4) throw new MissingMethodException("Damage Origin API unavailable.");
            harmony.Patch(changed, prefix: new HarmonyMethod(typeof(Mod), nameof(DamageChanging)));
            Bootstrap.Log("Death Rattle: optional confirmed damage Origin tracking installed.");
        }
        catch (Exception e) { _attackerDisabled = true; Bootstrap.Log("Death Rattle attacker details unavailable: " + e.GetBaseException().Message); }
    }

    public static void AlertsSynced(object __instance, object __1)
    {
        if (_disabled) return;
        try
        {
            var player = Get(Get(__instance, "_player")!, "LocalEntity");
            CheckAlerts(player, (IEnumerable)__1);
        }
        catch (Exception e) { Disable(e); }
    }

    public static void PollAlerts(object __instance)
    {
        if (_disabled || !_clientAlerts.IsInstanceOfType(__instance) || Clock.Elapsed < _nextPoll) return;
        _nextPoll = Clock.Elapsed + TimeSpan.FromMilliseconds(250);
        try
        {
            var player = Get(Get(__instance, "_playerManager")!, "LocalEntity");
            CheckAlerts(player, Get(__instance, "ActiveAlerts") as IEnumerable);
        }
        catch (Exception e) { Disable(e); }
    }

    private static void CheckAlerts(object? player, IEnumerable? alerts)
    {
        var awful = false;
        if (alerts != null)
            foreach (var pair in alerts)
            {
                var state = Get(pair, "Value")!;
                if (Get(state, "Type")?.ToString() != "HumanHealth") continue;
                awful = Get(state, "Severity") is short severity && severity == 4;
                break;
            }
        if (Policy.Observe(player, awful, Clock.Elapsed, Environment.GetEnvironmentVariable("SS14_MOD_LANGUAGE")) is not { } message) return;
        _chat ??= Resolve(_chatContract);
        // ChatManager routes Radio through say; the native UI adds this prefix too.
        _send.Invoke(_chat, [";" + message, Enum.Parse(_channelType, "Radio")]);
    }

    private static void Disable(Exception e)
    {
        _disabled = true;
        Policy.Clear();
        Bootstrap.Log("Death Rattle disabled; native game retained: " + e.GetBaseException().Message);
    }

    public static void AlertsCleared() => Policy.Clear();

    public static void DamageChanging(object __0, object? __1, object? __3)
    {
        if (_disabled || _attackerDisabled || __1 == null) return;
        try
        {
            var timing = Resolve(_timingContract);
            if (Get(timing, "IsFirstTimePredicted") is not true) return;
            var total = __1.GetType().GetMethod("GetTotal", Type.EmptyTypes)!.Invoke(__1, null)!;
            var amount = (float)total.GetType().GetMethod("Float", Type.EmptyTypes)!.Invoke(total, null)!;
            if (amount <= 0) return;
            var robustClient = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Robust.Client");
            var player = Get(Resolve(robustClient.GetType("Robust.Client.Player.IPlayerManager", true)!), "LocalEntity");
            if (player == null || !Equals(Get(__0, "Owner"), player)) return;
            Attacker? attacker = null;
            if (__3 != null && !Equals(__3, player))
            {
                var entities = Resolve(_entityContract);
                var name = (string)_identityName.Invoke(null, [__3, entities, player])!;
                var humanoid = _humanoids.Any(t => HasComponent(entities, __3, t));
                attacker = new Attacker(name, humanoid);
            }
            Policy.RecordDamage(player, attacker, Clock.Elapsed);
        }
        catch (Exception e)
        {
            _attackerDisabled = true;
            Policy.ClearAttacker();
            Bootstrap.Log("Death Rattle attacker tracking disabled; distress calls retained: " + e.GetBaseException().Message);
        }
    }

    private static bool HasComponent(object entities, object uid, Type type)
    {
        object?[] args = [uid, type, null];
        return (bool)_tryComponent.Invoke(entities, args)!;
    }
    private static object Resolve(Type type) => _resolve.Invoke(null, [type])!;
    private static object? Get(object target, string name)
    {
        for (var t = target.GetType(); t != null; t = t.BaseType)
        {
            if (t.GetProperty(name, Flags) is { } p) return p.GetValue(target);
            if (t.GetField(name, Flags) is { } f) return f.GetValue(target);
        }
        throw new MissingMemberException(target.GetType().FullName, name);
    }
}
