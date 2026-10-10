using System.Collections;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using HarmonyLib;
using SS14LocalMods.DeathRattle;

namespace SS14LocalMods;

public static class Mod
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
    private static readonly DistressPolicy Policy = new();
    private static readonly FaunaEvidence Fauna = new();
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static Type _chatContract = null!, _channelType = null!, _entityContract = null!, _timingContract = null!, _playerContract = null!;
    private static Type _damageComponent = null!, _mobStateComponent = null!, _transformComponent = null!;
    private static Type[] _humanoids = [];
    private static MethodInfo _resolve = null!, _send = null!, _tryComponent = null!, _identityName = null!, _getEntity = null!;
    private static object? _chat, _body;
    private static Type _clientAlerts = null!;
    private static TimeSpan _nextPoll;
    private static bool _disabled, _attackerDisabled, _faunaDisabled, _unknownDamage;
    private static TimeSpan _damageAt;

    public static void Install(Assembly content)
    {
        var controller = content.GetType("Content.Client.UserInterface.Systems.Alerts.AlertsUIController", true)!;
        var sync = controller.GetMethod("SystemOnSyncAlerts", Flags) ?? throw new MissingMethodException(controller.FullName, "SystemOnSyncAlerts");
        var clear = controller.GetMethod("SystemOnClearAlerts", Flags) ?? throw new MissingMethodException(controller.FullName, "SystemOnClearAlerts");
        _chatContract = content.GetType("Content.Client.Chat.Managers.IChatManager", true)!;
        var shared = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Content.Shared");
        var robust = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Robust.Shared");
        var robustClient = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Robust.Client");
        _playerContract = robustClient.GetType("Robust.Client.Player.IPlayerManager", true)!;
        _channelType = shared.GetType("Content.Shared.Chat.ChatSelectChannel", true)!;
        _send = _chatContract.GetMethod("SendMessage", [typeof(string), _channelType])!;
        if (_send == null || !Enum.IsDefined(_channelType, "Radio")) throw new MissingMethodException("Radio chat API unavailable.");
        _resolve = robust.GetType("Robust.Shared.IoC.IoCManager", true)!.GetMethod("ResolveType", [typeof(Type)])!;
        _entityContract = robust.GetType("Robust.Shared.GameObjects.IEntityManager", true)!;
        var uid = robust.GetType("Robust.Shared.GameObjects.EntityUid", true)!;
        _tryComponent = _entityContract.GetMethods().Single(m => m.Name == "TryGetComponent" && !m.IsGenericMethod
            && m.GetParameters() is var p && p.Length == 3 && p[0].ParameterType == uid && p[1].ParameterType == typeof(Type));
        _timingContract = robust.GetType("Robust.Shared.Timing.IGameTiming", true)!;
        _damageComponent = shared.GetType("Content.Shared.Damage.Components.DamageableComponent", true)!;
        _mobStateComponent = shared.GetType("Content.Shared.Mobs.Components.MobStateComponent", true)!;
        _clientAlerts = content.GetType("Content.Client.Alerts.ClientAlertsSystem", true)!;
        var update = shared.GetType("Content.Shared.Alert.AlertsSystem", true)!.GetMethod("Update", [typeof(float)])
            ?? throw new MissingMethodException("AlertsSystem.Update(float)");
        var changed = shared.GetType("Content.Shared.Damage.Systems.DamageableSystem", true)!.GetMethod("OnEntityDamageChanged", Flags);
        if (changed == null || changed.GetParameters().Length != 4) throw new MissingMethodException("Damage change API unavailable.");
        var harmony = new Harmony("local.death-rattle");
        try
        {
            harmony.Patch(sync, postfix: new HarmonyMethod(typeof(Mod), nameof(AlertsSynced)));
            harmony.Patch(clear, postfix: new HarmonyMethod(typeof(Mod), nameof(AlertsCleared)));
            harmony.Patch(update, postfix: new HarmonyMethod(typeof(Mod), nameof(PollAlerts)));
            harmony.Patch(changed, prefix: new HarmonyMethod(typeof(Mod), nameof(DamageChanging)));
        }
        catch { harmony.UnpatchAll(harmony.Id); throw; }
        Bootstrap.Log("Distress Call: damage trend, recovery protection and native radio installed.");
        try
        {
            _identityName = shared.GetType("Content.Shared.IdentityManagement.Identity", true)!.GetMethods()
                .Single(m => m.Name == "Name" && m.GetParameters().Length == 3);
            _humanoids = new[] { "Content.Shared.Humanoid.HumanoidProfileComponent", "Content.Shared.Humanoid.HumanoidAppearanceComponent" }
                .Select(shared.GetType).OfType<Type>().ToArray();
            if (_humanoids.Length == 0) throw new TypeLoadException("No supported humanoid component.");
        }
        catch (Exception e) { _attackerDisabled = true; Bootstrap.Log("Distress Call attacker names unavailable: " + e.GetBaseException().Message); }
        try
        {
            if (_attackerDisabled) throw new InvalidOperationException("Attacker names unavailable.");
            _transformComponent = robust.GetType("Robust.Shared.GameObjects.TransformComponent", true)!;
            var net = robust.GetType("Robust.Shared.GameObjects.NetEntity", true)!;
            _getEntity = _entityContract.GetMethod("GetEntity", [net]) ?? throw new MissingMethodException("GetEntity(NetEntity)");
            var lunge = content.GetType("Content.Client.Weapons.Melee.MeleeWeaponSystem", true)!.GetMethod("OnMeleeLunge", Flags)
                ?? throw new MissingMethodException("OnMeleeLunge");
            harmony.Patch(lunge, prefix: new HarmonyMethod(typeof(Mod), nameof(MeleeLunging)));
            Bootstrap.Log("Distress Call: optional, uncertain fauna swing/damage correlation installed.");
        }
        catch (Exception e) { _faunaDisabled = true; Bootstrap.Log("Distress Call fauna correlation unavailable: " + e.GetBaseException().Message); }
    }

    private static void SetBody(object? player)
    {
        if (Equals(_body, player)) return;
        _body = player;
        Policy.SetPlayer(player);
        Fauna.SetPlayer(player);
        _unknownDamage = false;
    }

    public static void AlertsSynced(object __instance, object __1)
    {
        if (_disabled) return;
        try { CheckAlerts(Get(Get(__instance, "_player")!, "LocalEntity"), (IEnumerable)__1); }
        catch (Exception e) { Disable(e); }
    }

    public static void PollAlerts(object __instance)
    {
        if (_disabled || !_clientAlerts.IsInstanceOfType(__instance) || Clock.Elapsed < _nextPoll) return;
        _nextPoll = Clock.Elapsed + TimeSpan.FromMilliseconds(250);
        try { CheckAlerts(Get(Get(__instance, "_playerManager")!, "LocalEntity"), Get(__instance, "ActiveAlerts") as IEnumerable); }
        catch (Exception e) { Disable(e); }
    }

    private static void CheckAlerts(object? player, IEnumerable? alerts)
    {
        var timing = Resolve(_timingContract);
        // Alerts can be raised in the middle of state application or a replay. Poll after it settles.
        if (Get(timing, "IsFirstTimePredicted") is not true || Get(timing, "ApplyingState") is true) return;
        SetBody(player);
        short? severity = null;
        var incapacitated = false;
        if (alerts != null)
            foreach (var pair in alerts)
            {
                var state = Get(pair, "Value")!;
                var type = Get(state, "Type")?.ToString();
                if (type == "HumanHealth") severity = Get(state, "Severity") as short?;
                if (type is "HumanCrit" or "HumanDead") incapacitated = true;
            }
        double? total = null;
        if (player != null)
        {
            var entities = Resolve(_entityContract);
            if (Component(entities, player, _damageComponent) is { } damage)
                total = DamageTotal(Get(damage, "Damage")!);
            if (Component(entities, player, _mobStateComponent) is { } mob)
                incapacitated |= Get(mob, "CurrentState")?.ToString() != "Alive";
            if (_unknownDamage)
                Policy.RecordDamage(player, _faunaDisabled ? null : Fauna.Match(player, Clock.Elapsed), _damageAt);
        }
        if (Policy.Observe(player, severity, incapacitated, total, Clock.Elapsed,
            Environment.GetEnvironmentVariable("SS14_MOD_LANGUAGE")) is not { } message) return;
        _chat ??= Resolve(_chatContract);
        _send.Invoke(_chat, [";" + message, Enum.Parse(_channelType, "Radio")]);
    }

    private static void Disable(Exception e)
    {
        _disabled = true;
        Policy.Clear(); Fauna.Clear();
        Bootstrap.Log("Distress Call disabled; native game retained: " + e.GetBaseException().Message);
    }

    // UI alerts can be cleared transiently on the same body; this is not recovery.
    public static void AlertsCleared(object __instance)
    {
        if (_disabled) return;
        try { SetBody(Get(Get(__instance, "_player")!, "LocalEntity")); }
        catch (Exception e) { Disable(e); }
    }

    public static void DamageChanging(object __0, object? __1, object? __3)
    {
        if (_disabled) return;
        try
        {
            var timing = Resolve(_timingContract);
            // Robust sets IsFirstTimePredicted=false during authoritative state application too.
            if (Get(timing, "IsFirstTimePredicted") is not true && Get(timing, "ApplyingState") is not true) return;
            var player = Get(Resolve(_playerContract), "LocalEntity");
            if (player == null || !Equals(Get(__0, "Owner"), player)) return;
            SetBody(player);
            // Damage has already been assigned; cached TotalDamage is updated inside the native method.
            Policy.ObserveDamage(player, DamageTotal(Get(Get(__0, "Comp")!, "Damage")!), Clock.Elapsed);
            if (__1 == null)
            {
                Fauna.Clear(); _unknownDamage = false;
                Policy.ClearAttacker();
                return;
            }
            var amount = DamageTotal(__1);
            if (amount < 0)
            {
                Fauna.Clear(); _unknownDamage = false;
                Policy.ClearAttacker();
                return;
            }
            if (amount <= 0 || !double.IsFinite(amount)) return;
            _damageAt = Clock.Elapsed;
            _unknownDamage = __3 == null;
            if (_unknownDamage) Fauna.Damage(player, _damageAt);
            else Fauna.Clear();
            Attacker? attacker = null;
            if (!_attackerDisabled && __3 != null && !Equals(__3, player))
            {
                try { attacker = VisibleAttacker(Resolve(_entityContract), __3, player); }
                catch (Exception e)
                {
                    _attackerDisabled = true;
                    Bootstrap.Log("Distress Call attacker names disabled; recovery protection retained: " + e.GetBaseException().Message);
                }
            }
            Policy.RecordDamage(player, attacker, _damageAt);
        }
        catch (Exception e) { Disable(e); }
    }

    public static void MeleeLunging(object __0)
    {
        if (_disabled || _faunaDisabled || _attackerDisabled) return;
        try
        {
            if (Get(Resolve(_timingContract), "IsFirstTimePredicted") is not true) return;
            var player = Get(Resolve(_playerContract), "LocalEntity");
            if (player == null) return;
            SetBody(player);
            var entities = Resolve(_entityContract);
            var source = _getEntity.Invoke(entities, [Get(__0, "Entity")])!;
            var weapon = _getEntity.Invoke(entities, [Get(__0, "Weapon")])!;
            // Only natural attacks by living non-humanoids. Held weapons and humanoids require explicit Origin.
            if (!Equals(source, weapon) || Equals(source, player)
                || _humanoids.Any(t => Component(entities, source, t) != null)
                || Component(entities, source, _mobStateComponent) is not { } mob
                || Get(mob, "CurrentState")?.ToString() != "Alive") return;
            var from = Component(entities, source, _transformComponent);
            var to = Component(entities, player, _transformComponent);
            if (from == null || to == null || !Equals(Get(from, "ParentUid"), Get(to, "ParentUid"))) return;
            // LocalPos is rotated into the attacker's parent frame by the native server code.
            var offset = (Vector2)Get(__0, "LocalPos")!;
            var target = (Vector2)Get(to, "LocalPosition")! - (Vector2)Get(from, "LocalPosition")!;
            Fauna.Swing(player, source, VisibleAttacker(entities, source, player), offset, target, Clock.Elapsed);
        }
        catch (Exception e)
        {
            _faunaDisabled = true; Fauna.Clear();
            Bootstrap.Log("Distress Call fauna correlation disabled; base calls retained: " + e.GetBaseException().Message);
        }
    }

    private static Attacker VisibleAttacker(object entities, object uid, object player) => new(
        (string)_identityName.Invoke(null, [uid, entities, player])!, _humanoids.Any(t => Component(entities, uid, t) != null));
    private static double DamageTotal(object specifier)
    {
        var total = specifier.GetType().GetMethod("GetTotal", Type.EmptyTypes)!.Invoke(specifier, null)!;
        return Convert.ToDouble(total.GetType().GetMethod("Float", Type.EmptyTypes)!.Invoke(total, null));
    }
    private static object? Component(object entities, object uid, Type type)
    {
        object?[] args = [uid, type, null];
        return (bool)_tryComponent.Invoke(entities, args)! ? args[2] : null;
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
