using Content.Client.Alerts;
using Content.Client.Chat.Managers;
using Content.Client.UserInterface.Systems.Alerts;
using Content.Shared.Alert;
using Content.Shared.Chat;
using Content.Shared.Damage.Systems;
using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
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
        SS14LocalMods.Mod.Install(typeof(AlertsUIController).Assembly);
        var ui = new AlertsUIController(player);
        var damage = new DamageableSystem(); var attacker = new EntityUid(10);
        Identity.Names[attacker] = english ? "spider" : "паук";
        switch (action)
        {
            case "basic": break;
            case "person":
                Identity.Names[attacker] = "Alex Smith";
                entities.Components[(attacker, typeof(HumanoidProfileComponent))] = new HumanoidProfileComponent();
                damage.Change(new(1), 5, attacker);
                break;
            case "entity": damage.Change(new(1), 5, attacker); break;
            case "healing": damage.Change(new(1), -5, attacker); break;
            case "self": damage.Change(new(1), 5, new(1)); break;
            case "other": damage.Change(new(2), 5, attacker); break;
            case "replay": timing.IsFirstTimePredicted = false; damage.Change(new(1), 5, attacker); break;
            case "unknown": damage.Change(new(1), 5, attacker); damage.Change(new(1), 5, null); break;
            case "optional-failure":
                damage.Change(new(1), 5, new(99)); // Identity lookup fails; the original damage call must survive.
                damage.Change(new(1), 5, attacker);
                break;
            case "send-failure": chat.Throw = true; break;
            case "poll":
                var alerts = new ClientAlertsSystem(player)
                {
                    ActiveAlerts = new Dictionary<AlertKey, AlertState> { [new("Health")] = new() { Type = "HumanHealth", Severity = 4 } }
                };
                alerts.Update(0.25f);
                alerts.Update(0.25f);
                break;
            default: throw new Exception("Unknown scenario: " + scenario);
        }
        ui.Sync("HumanHealth", 4);
        ui.Sync("HumanHealth", 4);
        if (ui.NativeSyncs != 2) throw new Exception("Native alerts must remain active.");
        if (scenario == "send-failure")
        {
            chat.Throw = false;
            ui.Sync("HumanHealth", 4);
            if (chat.Sent.Count != 0) throw new Exception("Failed chat must disable automatic attempts.");
        }
        else
        {
            var text = english
                ? action is "entity" or "person" ? ";Help, " + Identity.Names[attacker] + " is killing me!" : ";Help, I'm dying!"
                : action == "entity" ? ";Помогите, меня убивает паук!" : ";Помогите, я умираю!";
            if (chat.Sent.Count != 1 || chat.Sent[0] != (text, ChatSelectChannel.Radio))
                throw new Exception("Unexpected distress call in " + scenario);
        }
        Console.WriteLine("Death Rattle integration: " + scenario + " passed.");
    }
}
