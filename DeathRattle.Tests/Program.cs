using Content.Client.Chat.Managers;
using Content.Client.UserInterface.Systems.Alerts;
using Content.Shared.Chat;
using Content.Shared.Damage.Systems;
using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
using Robust.Client.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Timing;
using SS14LocalMods.DeathRattle;

if (args.Length != 0) { IntegrationCases.Run(args[0]); return; }
Environment.SetEnvironmentVariable("SS14_MOD_LANGUAGE", "ru");
var checks = 0;
void Check(bool condition, string title) { if (!condition) throw new Exception(title); checks++; }
TimeSpan At(int seconds) => TimeSpan.FromSeconds(seconds);
var policy = new DistressPolicy();
Check(policy.Observe(null, true, At(0)) == null, "No attached character never speaks");
Check(policy.Observe(1, false, At(0)) == null, "Healthy character never speaks");
Check(policy.Observe(1, true, At(1)) == "Помогите, я умираю!", "Awful transition sends exact requested text");
Check(policy.Observe(1, true, At(80)) == null, "Continued awful health doesn't spam");
policy.Observe(1, false, At(81));
Check(policy.Observe(1, true, At(82)) != null, "Recovery rearms");
policy.Observe(1, false, At(83));
Check(policy.Observe(1, true, At(84)) == null, "Cooldown across episodes");
Check(policy.Observe(1, true, At(112)) != null, "Pending episode can announce after cooldown");
policy.Clear();
Check(policy.Observe(2, true, At(113)) == null, "Detach and body switch retain cooldown");
Check(policy.Observe(2, true, At(142)) != null, "New body can announce later");

policy = new();
policy.RecordDamage(1, new("Анна Смирнова", true), At(10));
Check(policy.Observe(1, true, At(11)) == "Помогите, меня убивает Анна Смирнова!", "Humanoid reports visible name");
policy = new(); policy.RecordDamage(1, new("паук", false), At(10));
Check(policy.Observe(1, true, At(11)) == "Помогите, меня убивает паук!", "Creature reports its name in one in-game sentence");
policy = new(); policy.RecordDamage(1, new("", false), At(10));
Check(policy.Observe(1, true, At(11)) == "Помогите, я умираю!", "Unnamed creature uses the basic distress call");
policy = new(); policy.RecordDamage(1, new("", true), At(10));
Check(policy.Observe(1, true, At(11)) == "Помогите, я умираю!", "Unknown person name isn't invented");
policy = new(); policy.RecordDamage(1, new("Old attacker", true), At(0));
Check(policy.Observe(1, true, At(6)) == "Помогите, я умираю!", "Stale attacker expires");
policy = new(); policy.RecordDamage(1, new("Other body's attacker", true), At(0));
Check(policy.Observe(2, true, At(1)) == "Помогите, я умираю!", "Body switch clears attacker");
policy = new(); policy.RecordDamage(1, new("Old attacker", true), At(0)); policy.RecordDamage(1, null, At(1));
Check(policy.Observe(1, true, At(2)) == "Помогите, я умираю!", "Unknown source clears old blame");
policy = new(); policy.RecordDamage(1, new("Future attacker", true), At(10));
Check(policy.Observe(1, true, At(9)) == "Помогите, я умираю!", "Clock reversal drops future attribution");
policy = new(); policy.RecordDamage(1, new("Имя\r\n" + new string('а', 200), true), At(0));
var clean = policy.Observe(1, true, At(1))!;
Check(!clean.Contains('\n') && !clean.Contains('\r') && clean.Length < 150, "Names cannot inject lines or exceed chat size");

policy = new();
Check(policy.Observe(1, true, At(0), "en") == "Help, I'm dying!", "English interface selects English distress call");
policy = new(); policy.RecordDamage(1, new("Alex Smith", true), At(0));
Check(policy.Observe(1, true, At(1), "en") == "Help, Alex Smith is killing me!", "English person attribution preserves visible name");
policy = new(); policy.RecordDamage(1, new("spider", false), At(0));
Check(policy.Observe(1, true, At(1), "en") == "Help, spider is killing me!", "English creature attribution uses one sentence");
policy = new(); policy.RecordDamage(1, new("", false), At(0));
Check(policy.Observe(1, true, At(1), "en") == "Help, I'm dying!", "English unknown name falls back in English");
policy = new(); policy.RecordDamage(1, new("old attacker", true), At(0));
Check(policy.Observe(1, true, At(6), "en") == "Help, I'm dying!", "English stale source falls back in English");
foreach (var language in new string?[] { "ru", null, "" })
{
    policy = new();
    Check(policy.Observe(1, true, At(0), language) == "Помогите, я умираю!", "Russian/default interface keeps Russian distress call");
}
policy = new(); policy.Observe(1, true, At(0), "ru");
Check(policy.Observe(1, true, At(31), "en") == null, "Changing language does not bypass episode spam protection");
policy.Observe(1, false, At(32), "en");
Check(policy.Observe(1, true, At(33), "en") == "Help, I'm dying!", "New episode follows current interface language");

// Real Harmony patch dispatch, boxed Entity<T>/nullable Origin, native radio prefix and failure containment.
var player = new PlayerManager { LocalEntity = new(1) };
var chat = new ChatManager(); var entities = new EntityManager(); var timing = new GameTiming();
IoCManager.Services[typeof(IPlayerManager)] = player;
IoCManager.Services[typeof(IChatManager)] = chat;
IoCManager.Services[typeof(IEntityManager)] = entities;
IoCManager.Services[typeof(IGameTiming)] = timing;
SS14LocalMods.Mod.Install(typeof(AlertsUIController).Assembly);
var ui = new AlertsUIController(player);
ui.Sync("HumanHealth", 3); ui.Sync("HumanCrit", null); ui.Sync("HumanDead", null); ui.Sync("BorgHealth", 4);
Check(chat.Sent.Count == 0 && ui.NativeSyncs == 4, "Only HumanHealth 4 triggers; native alerts remain active");
var damage = new DamageableSystem(); var attacker = new EntityUid(10);
Identity.Names[attacker] = "Неизвестный";
entities.Components[(attacker, typeof(HumanoidProfileComponent))] = new HumanoidProfileComponent();
damage.Change(new(2), 5, attacker);
damage.DuringDamage = () => ui.Sync("HumanHealth", 4);
damage.Change(new(1), 5, attacker);
Check(chat.Sent.Count == 1 && chat.Sent[0] == (";Помогите, меня убивает Неизвестный!", ChatSelectChannel.Radio),
    "Origin hook runs before threshold update and sends native radio prefix");
ui.Sync("HumanHealth", 4); ui.Clear(); ui.Sync("HumanHealth", 4);
Check(chat.Sent.Count == 1, "Duplicate alerts and clear events cannot bypass cooldown");
// A new module instance isn't required to exercise independent optional metadata failures.
var resets = new DistressPolicy();
resets.RecordDamage(1, new("attacker", true), At(0)); resets.ClearAttacker();
Check(resets.Observe(1, true, At(1)) == "Помогите, я умираю!", "Optional failure clears attribution");
Console.WriteLine($"Death Rattle: {checks} policy and Harmony integration checks passed.");
foreach (var scenario in new[] { "entity", "healing", "self", "other", "replay", "unknown", "optional-failure", "send-failure", "poll", "unsupported", "en-basic", "en-entity", "en-person", "en-poll" })
{
    var start = new System.Diagnostics.ProcessStartInfo("dotnet")
    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    start.ArgumentList.Add(System.Reflection.Assembly.GetExecutingAssembly().Location);
    start.ArgumentList.Add(scenario);
    using var process = System.Diagnostics.Process.Start(start)!;
    var output = process.StandardOutput.ReadToEndAsync();
    var error = process.StandardError.ReadToEndAsync();
    process.WaitForExit();
    Console.Write(output.GetAwaiter().GetResult());
    Console.Write(error.GetAwaiter().GetResult());
    if (process.ExitCode != 0) throw new Exception("Integration scenario failed: " + scenario);
}
