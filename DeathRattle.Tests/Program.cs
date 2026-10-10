using SS14LocalMods.DeathRattle;
using System.Numerics;

if (args.Length != 0) { IntegrationCases.Run(args[0]); return; }
var checks = 0;
void Check(bool condition, string title) { if (!condition) throw new Exception(title); checks++; }
TimeSpan At(double seconds) => TimeSpan.FromSeconds(seconds);
string? See(DistressPolicy p, double t, double? damage, short? severity = 4, bool crit = false, object? body = null, string? language = "ru") =>
    p.Observe(body ?? 1, severity, crit, damage, At(t), language);
DistressPolicy Injured(double t = 0)
{
    var p = new DistressPolicy(); See(p, t, 80, 3); return p;
}
string? Call(DistressPolicy p, double t = 1, double damage = 90, string? language = "ru")
{
    Check(See(p, t, damage, language: language) == null, "Threshold waits for state/event settlement");
    return See(p, t + .5, damage, language: language);
}
var policy = new DistressPolicy();
Check(policy.Observe(null, 4, false, 90, At(0)) == null, "Detached character never speaks");
Check(See(policy, 0, 90) == null && See(policy, 60, 90) == null, "Attach to existing awful health stays quiet");
policy = Injured();
Check(Call(policy) == "Помогите, я умираю!", "Fresh serious injury sends exact Russian call");
Check(See(policy, 2, 95) == null && See(policy, 80, 120) == null, "One attempt during continued awful health");
See(policy, 81, 70, 2); See(policy, 86, 70, 2);
Check(Call(policy, 87, 80) != null, "Stable safe recovery rearms an actual new injury");
See(policy, 88, 70, 2); See(policy, 93, 70, 2);
Check(Call(policy, 94, 90) == null, "Cooldown still protects separate episodes");
Check(See(policy, 120, 90) == null, "Cooldown expiry never shouts for stale injury");
Check(Call(policy, 121, 100) != null, "Fresh injury after cooldown can speak");
policy.Clear();
See(policy, 122, 80, 3, body: 2);
See(policy, 123, 90, body: 2);
Check(See(policy, 123.5, 90, body: 2) == null, "Switching bodies retains global cooldown");

policy = Injured();
See(policy, 1, 90); See(policy, 1.2, 85);
Check(See(policy, 2, 85) == null && See(policy, 40, 85) == null, "Healing cancels a queued shout permanently");
Check(Call(policy, 41, 89) == null, "Small relapse after treatment stays quiet");
Check(Call(policy, 42, 95) != null, "Ten net damage after best recovered value permits real relapse");
policy = Injured();
See(policy, 1, 110, null, true); See(policy, 2, 85, 4);
Check(See(policy, 40, 85) == null, "Getting up from crit is recovery even after thirty seconds");
Check(Call(policy, 41, 89) == null, "Small post-crit loss does not send");
Check(Call(policy, 42, 95) != null, "Severe renewed attack after crit can send");
policy = Injured(); See(policy, 1, 100, null, true); See(policy, 2, 100);
Check(See(policy, 50, 100) == null, "Crit-to-awful without damage update stays quiet");
policy = Injured();
See(policy, 1, 90); See(policy, 1.2, 90, null, true);
Check(See(policy, 2, 90) == null, "Going unconscious cancels pending call");
policy = Injured();
See(policy, 1, 84.9); Check(See(policy, 2, 84.9) == null, "Below five damage is ignored");
See(policy, 2.1, 85); Check(See(policy, 2.6, 85) != null, "Several small hits can accumulate into a serious injury");
policy = Injured(); See(policy, 1, 83); See(policy, 5, 86);
Check(See(policy, 5.5, 86) == null, "Widely spaced small losses do not accumulate forever");
policy = Injured();
for (var i = 1; i < 40; i++)
{
    See(policy, i, i % 2 == 0 ? 80 : 81, i % 2 == 0 ? (short)3 : (short)4);
    Check(See(policy, i + .5, i % 2 == 0 ? 80 : 81, i % 2 == 0 ? (short)3 : (short)4) == null, "Border jitter never sends");
}
policy = Injured(); See(policy, 1, 90); See(policy, 1.1, 90, 3);
Check(See(policy, 1.6, 90, 3) == null, "Leaving awful health cancels pending call");
Check(See(policy, 1.7, 90) == null && See(policy, 2.2, 90) == null, "Improving alert with late damage replication protects a return to awful");
Check(Call(policy, 2.5, 94) == null, "Small relapse cannot override improving-alert protection");
Check(Call(policy, 3.5, 100) != null, "Serious relapse after improving alert is eligible");
policy = Injured(); Call(policy); See(policy, 35, 100, null, true); See(policy, 36, 100);
Check(Call(policy, 37, 110) != null, "Crit recovery without healing can rearm only for a serious new injury");
foreach (var invalid in new double?[] { null, double.NaN, double.PositiveInfinity, -1 })
{
    policy = Injured(); Check(See(policy, 1, invalid) == null, "Unavailable/invalid damage fails silent");
}
policy = Injured();
Check(Call(policy, language: "en") == "Help, I'm dying!", "English basic call");
foreach (var language in new string?[] { "ru", null, "" })
{
    policy = Injured(); Check(Call(policy, language: language) == "Помогите, я умираю!", "Russian/default basic call");
}
foreach (var (attacker, expectedRu, expectedEn) in new[]
{
    (new Attacker("Анна Смирнова", true), "Помогите, меня убивает Анна Смирнова!", "Help, Анна Смирнова is killing me!"),
    (new Attacker("паук", false), "Помогите, меня убивает паук!", "Help, паук is killing me!"),
    (new Attacker("spider", false, false), "Помогите, похоже, меня атакует spider!", "Help, I think spider is attacking me!"),
    (new Attacker("", false), "Помогите, я умираю!", "Help, I'm dying!")
})
{
    foreach (var language in new[] { "ru", "en" })
    {
        policy = Injured(); policy.RecordDamage(1, attacker, At(1));
        Check(Call(policy, language: language) == (language == "en" ? expectedEn : expectedRu), "Language and source certainty preserved");
    }
}
policy = Injured(); policy.RecordDamage(1, new("Old attacker", true), At(-10));
Check(Call(policy) == "Помогите, я умираю!", "Stale attacker expires");
policy = Injured(); policy.RecordDamage(1, new("Old attacker", true), At(0)); policy.RecordDamage(1, null, At(1));
Check(Call(policy) == "Помогите, я умираю!", "Unknown damage clears old blame");
policy = Injured(); policy.RecordDamage(1, new("Future", true), At(10));
Check(Call(policy) == "Помогите, я умираю!", "Future source rejected");
policy = Injured(); policy.RecordDamage(1, new("Имя\r\n" + new string('а', 200), true), At(1));
var clean = Call(policy)!;
Check(!clean.Contains('\n') && !clean.Contains('\r') && clean.Length < 150, "Chat names cannot inject lines or exceed length");
policy = Injured(); policy.RecordDamage(1, new("Other body", true), At(1));
See(policy, 2, 80, 3, body: 2); See(policy, 3, 90, body: 2);
Check(See(policy, 3.5, 90, body: 2) == "Помогите, я умираю!", "Body change discards attribution");

FaunaEvidence Evidence() => new();
var fauna = Evidence(); var spider = new Attacker("космический паук", false);
var offset = new Vector2(1, 0);
fauna.Swing(1, 10, spider, offset, offset, At(0));
Check(fauna.Match(1, At(.1)) == null, "A swing without damage cannot accuse fauna");
fauna.Damage(1, At(.1));
Check(fauna.Match(1, At(.5)) is { Confirmed: false, Name: "космический паук" }, "Directed swing plus damage produces uncertain attribution");
Check(fauna.Match(1, At(1)) == null, "Fauna evidence expires quickly");
fauna = Evidence(); fauna.Damage(1, At(0)); fauna.Swing(1, 10, spider, offset, offset, At(.2));
Check(fauna.Match(1, At(.5)) != null, "Damage-before-lunge ordering also works");
fauna.Swing(1, 11, spider, offset, offset, At(.3));
Check(fauna.Match(1, At(.5)) == null, "Multiple possible fauna sources are ambiguous");
Check(fauna.Match(2, At(.5)) == null, "Fauna evidence is body scoped");
foreach (var (source, attacker, swing, target) in new[]
{
    (1, spider, offset, offset),
    (10, new Attacker("Person", true), offset, offset),
    (10, spider, offset, new Vector2(-1, 0)),
    (10, spider, Vector2.Zero, offset),
    (10, spider, new Vector2(float.NaN, 0), offset),
    (10, spider, new Vector2(4, 0), new Vector2(4, 0))
})
{
    fauna = Evidence(); fauna.Damage(1, At(0)); fauna.Swing(1, source, attacker, swing, target, At(.1));
    Check(fauna.Match(1, At(.2)) == null, "Self, person, wrong direction and invalid geometry cannot name fauna");
}
fauna = Evidence(); fauna.Damage(1, At(0)); fauna.Swing(1, 10, spider, offset, offset, At(.1)); fauna.Clear();
Check(fauna.Match(1, At(.2)) == null, "Healing/reset drops fauna evidence");
Console.WriteLine($"Distress Call: {checks} trend, recovery, episode and attribution policy checks passed.");
foreach (var scenario in new[] { "entity", "healing", "recovery", "relapse", "self", "other", "replay", "unknown", "optional-failure", "send-failure", "poll", "unsupported", "en-basic", "en-entity", "en-person", "en-poll", "fauna", "fauna-before", "fauna-away", "fauna-ambiguous", "fauna-grid", "fauna-held", "fauna-person", "fauna-healing", "fauna-replay", "crit", "missing-damage", "en-fauna", "server-fauna", "server-healing", "server-origin" })
{
    var start = new System.Diagnostics.ProcessStartInfo("dotnet")
    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    start.ArgumentList.Add(System.Reflection.Assembly.GetExecutingAssembly().Location);
    start.ArgumentList.Add(scenario);
    using var process = System.Diagnostics.Process.Start(start)!;
    var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
    process.WaitForExit(); Console.Write(output.GetAwaiter().GetResult()); Console.Write(error.GetAwaiter().GetResult());
    if (process.ExitCode != 0) throw new Exception("Integration scenario failed: " + scenario);
}
