using System.Numerics;
using Content.Client.Alerts;
using Content.Client.Chat.Managers;
using Content.Client.UserInterface.Systems.Alerts;
using Content.Client.Weapons.Melee;
using Content.Shared.Alert;
using Content.Shared.Chat;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs.Components;
using Content.Shared.Weapons.Melee.Events;
using Robust.Client.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Timing;

static class IntegrationCases
{
    public static void Run(string scenario)
    {
        var english = scenario.StartsWith("en-", StringComparison.Ordinal);
        Environment.SetEnvironmentVariable("SS14_MOD_LANGUAGE", english ? "en" : "ru");
        var action = english ? scenario[3..] : scenario;
        if (scenario == "unsupported")
        {
            try { SS14LocalMods.Mod.Install(typeof(string).Assembly); }
            catch (TypeLoadException) { Console.WriteLine("Unsupported content rejected before patching."); return; }
            throw new Exception("Unsupported content was accepted.");
        }
        var player = new PlayerManager { LocalEntity = new(1) };
        var chat = new ChatManager(); var entities = new EntityManager(); var timing = new GameTiming();
        IoCManager.Services[typeof(IPlayerManager)] = player;
        IoCManager.Services[typeof(IChatManager)] = chat;
        IoCManager.Services[typeof(IEntityManager)] = entities;
        IoCManager.Services[typeof(IGameTiming)] = timing;
        var body = new EntityUid(1); var attacker = new EntityUid(10);
        var hurt = new DamageableComponent { Damage = new(80) };
        var mob = new MobStateComponent();
        entities.Components[(body, typeof(DamageableComponent))] = hurt;
        entities.Components[(body, typeof(MobStateComponent))] = mob;
        entities.Components[(attacker, typeof(MobStateComponent))] = new MobStateComponent();
        entities.Components[(body, typeof(TransformComponent))] = new TransformComponent { LocalPosition = new(1, 0) };
        entities.Components[(attacker, typeof(TransformComponent))] = new TransformComponent();
        Identity.Names[attacker] = english ? "space spider" : "космический паук";
        Identity.Names[new(11)] = english ? "bear" : "медведь";
        SS14LocalMods.Mod.Install(typeof(AlertsUIController).Assembly);
        var ui = new AlertsUIController(player);
        var damage = new DamageableSystem(); var melee = new MeleeWeaponSystem();
        void Swing(int source = 10, Vector2? offset = null, int? weapon = null) => melee.Lunge(new MeleeLungeEvent
        { Entity = new(source), Weapon = new(weapon ?? source), LocalPos = offset ?? new(1, 0) });
        ui.Sync("HumanHealth", 3);
        var silent = action is "healing" or "recovery" or "other" or "crit" or "missing-damage" or "fauna-healing" or "server-healing";
        var expected = english ? ";Help, I'm dying!" : ";Помогите, я умираю!";
        switch (action)
        {
            case "basic": damage.Change(body, 10, null); break;
            case "person":
                Identity.Names[attacker] = "Alex Smith";
                entities.Components[(attacker, typeof(HumanoidProfileComponent))] = new HumanoidProfileComponent();
                damage.Change(body, 10, attacker);
                expected = ";Help, Alex Smith is killing me!";
                break;
            case "entity":
            case "server-origin":
                if (action == "server-origin") { timing.IsFirstTimePredicted = false; timing.ApplyingState = true; }
                damage.DuringDamage = () => ui.Sync("HumanHealth", 4);
                damage.Change(body, 10, attacker);
                timing.IsFirstTimePredicted = true; timing.ApplyingState = false;
                expected = english ? ";Help, space spider is killing me!" : ";Помогите, меня убивает космический паук!";
                break;
            case "healing": damage.Change(body, 10, null); ui.Sync("HumanHealth", 4); damage.Change(body, -5, attacker); break;
            case "server-healing":
                damage.Change(body, 10, null); ui.Sync("HumanHealth", 4);
                Thread.Sleep(550); // The old queue is now eligible, but this state transaction must not send.
                timing.IsFirstTimePredicted = false; timing.ApplyingState = true;
                ui.Sync("HumanHealth", 4);
                if (chat.Sent.Count != 0) throw new Exception("Call escaped during state application.");
                damage.Change(body, -5, null);
                timing.IsFirstTimePredicted = true; timing.ApplyingState = false;
                break;
            case "recovery":
            case "relapse":
                mob.CurrentState = "Critical"; damage.Change(body, 30, null); ui.Sync("HumanCrit", null);
                damage.Change(body, -25, attacker); mob.CurrentState = "Alive"; ui.Sync("HumanHealth", 4);
                damage.Change(body, action == "relapse" ? 10 : 2, null);
                break;
            case "self": damage.Change(body, 10, body); break;
            case "other": damage.Change(new(2), 10, attacker); break;
            case "replay":
                damage.Change(body, 10, null); timing.IsFirstTimePredicted = false; damage.Change(body, 5, attacker);
                // Prediction is subsequently restored to the authoritative value.
                hurt.Damage = new(90); timing.IsFirstTimePredicted = true; break;
            case "unknown": damage.Change(body, 5, attacker); damage.Change(body, 5, null); break;
            case "optional-failure": damage.Change(body, 10, new(99)); break;
            case "send-failure": chat.Throw = true; damage.Change(body, 10, null); break;
            case "poll": damage.Change(body, 10, null); break;
            case "crit": mob.CurrentState = "Critical"; damage.Change(body, 10, attacker); break;
            case "missing-damage": entities.Components.Remove((body, typeof(DamageableComponent))); break;
            case "fauna":
            case "server-fauna":
            case "fauna-before":
            case "fauna-away":
            case "fauna-ambiguous":
            case "fauna-grid":
            case "fauna-held":
            case "fauna-person":
            case "fauna-healing":
            case "fauna-replay":
                if (action == "fauna-grid") ((TransformComponent)entities.Components[(attacker, typeof(TransformComponent))]).ParentUid = new(999);
                if (action == "fauna-person") entities.Components[(attacker, typeof(HumanoidProfileComponent))] = new HumanoidProfileComponent();
                if (action == "server-fauna") { timing.IsFirstTimePredicted = false; timing.ApplyingState = true; }
                if (action != "fauna-before") damage.Change(body, 10, null);
                timing.IsFirstTimePredicted = true; timing.ApplyingState = false;
                if (action == "fauna-replay") timing.IsFirstTimePredicted = false;
                Swing(offset: action == "fauna-away" ? new(-1, 0) : null, weapon: action == "fauna-held" ? 20 : null);
                timing.IsFirstTimePredicted = true;
                if (action == "fauna-before") damage.Change(body, 10, null);
                if (action == "fauna-ambiguous")
                {
                    entities.Components[(new(11), typeof(MobStateComponent))] = new MobStateComponent();
                    entities.Components[(new(11), typeof(TransformComponent))] = new TransformComponent();
                    Swing(11);
                }
                if (action == "fauna-healing") damage.Change(body, -5, null);
                if (action is "fauna" or "fauna-before" or "server-fauna")
                    expected = english ? ";Help, I think space spider is attacking me!" : ";Помогите, похоже, меня атакует космический паук!";
                if (melee.NativeLunges == 0) throw new Exception("Native lunge must run.");
                break;
            default: throw new Exception("Unknown scenario: " + scenario);
        }
        var alerts = new ClientAlertsSystem(player)
        { ActiveAlerts = new Dictionary<AlertKey, AlertState> { [new("Health")] = new() { Type = "HumanHealth", Severity = 4 } } };
        if (action == "poll") alerts.Update(.25f);
        else ui.Sync("HumanHealth", 4);
        if (chat.Sent.Count != 0) throw new Exception("Call sent before settling or while recovering in " + scenario);
        // Deterministic policy tests use virtual time; this verifies actual Harmony/poll dispatch with the runtime clock.
        Thread.Sleep(550);
        if (action == "poll") alerts.Update(.25f);
        else ui.Sync("HumanHealth", 4);
        if (action == "send-failure")
        {
            chat.Throw = false; ui.Sync("HumanHealth", 4);
            if (chat.Sent.Count != 0) throw new Exception("Failed send must disable automatic attempts.");
        }
        else if (silent)
        {
            if (chat.Sent.Count != 0) throw new Exception("Recovery/non-local damage unexpectedly sent in " + scenario);
        }
        else if (chat.Sent.Count != 1 || chat.Sent[0] != (expected, ChatSelectChannel.Radio))
            throw new Exception("Unexpected call in " + scenario + ": " + string.Join(", ", chat.Sent));
        ui.Clear(); ui.Sync("HumanHealth", 4);
        if (chat.Sent.Count > 1) throw new Exception("Transient UI clear bypassed protection.");
        Console.WriteLine("Distress Call Harmony integration: " + scenario + " passed.");
    }
}
