using System.Text.Json;
using SS14LocalMods.ChemMaster;

var checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; }
void Reject(Action action, string name) { try { action(); } catch (Exception e) when (e is ChemistryException or ArgumentException or InvalidDataException or OperationCanceledException) { checks++; return; } throw new Exception(name); }
var substances = new[] { "A", "B", "C", "D", "Product", "Intermediate", "Catalyst", "Wrong" }.Select(id => new Reagent(id, id, 1)).ToArray();
ChemistryCatalog Catalog(Reaction[] reactions, int[]? doses = null) => new(substances, reactions, doses ?? [1, 5, 10, 20, 25, 50, 100]);
Reaction Mix(string id = "Mix", int a = 100) => new(id, [new("A", a), new("B", 100)], [new("Product", a + 100)]);
MachineSnapshot Snapshot(int capacity = 10000, int mode = 0, float? temperature = 293.15f) =>
    new(new() { ["A"] = 100000, ["B"] = 100000, ["C"] = 100000, ["Catalyst"] = 10000 }, [], capacity, mode, "owner/beaker/player", temperature);
var catalog = Catalog([Mix()]);
var planner = new ProductionPlanner(catalog);
var initial = Snapshot();
var plan = planner.Build(initial, [new("Product", 500)], TargetMode.Make);
Check(plan.Final.Buffer["Product"] == 600 && plan.Final.Beaker.Count == 0, "Whole batches expose overproduction");
Check(initial.Buffer["A"] == 100000 && initial.Beaker.Count == 0, "Planning never changes live snapshot");
Check(plan.Actions.All(a => a.Dose == null || catalog.Doses.Contains(a.Dose.Value)), "Only available command doses");
initial.Buffer["Product"] = 700;
Check(planner.Build(initial, [new("Product", 500)], TargetMode.Ensure).Actions.Count == 0, "Ensure uses existing stock");
Check(planner.Build(initial, [new("Product", 500)], TargetMode.Make).AbsoluteGoals.Single().Amount == 1200, "Make pins absolute goal");
var server2 = Catalog([Mix(a: 200)]);
Check(server2.Fingerprint != catalog.Fingerprint, "Recipe coefficients change catalog fingerprint");
Check(new ProductionPlanner(server2).Build(Snapshot(), [new("Product", 600)], TargetMode.Make).Final.Buffer["A"] == 99600, "Second server changes required inputs");
var small = planner.Build(Snapshot(600), [new("Product", 3000)], TargetMode.Make);
Check(small.Final.Buffer["Product"] == 3000 && small.Actions.All(a => a.After.Beaker.Values.Sum() <= 600), "Capacity splits production into batches");
Reject(() => planner.Build(Snapshot(100), [new("Product", 300)], TargetMode.Make), "Too-small beaker rejected");
Reject(() => planner.Build(initial with { Beaker = new() { ["A"] = 1 } }, [new("Product", 200)], TargetMode.Make), "Nonempty beaker rejected");
Reject(() => planner.Build(Snapshot(temperature: null), [new("Product", 200)], TargetMode.Make), "Unknown temperature needs confirmation");
Check(planner.Build(Snapshot(temperature: null), [new("Product", 200)], TargetMode.Make, true).Final.Buffer["Product"] == 200, "Explicit cold fallback");
Reject(() => planner.Build(Snapshot() with { Buffer = new() { ["A"] = 1000 } }, [new("Product", 200)], TargetMode.Make), "Missing base reagent rejected");
var nested = Catalog([
    new("Intermediate", [new("A", 100), new("B", 100)], [new("Intermediate", 200)]),
    new("Final", [new("Intermediate", 200), new("C", 100)], [new("Product", 300)])]);
Check(new ProductionPlanner(nested).Build(Snapshot(), [new("Product", 600)], TargetMode.Make).Final.Buffer["Product"] == 600, "Recursive intermediates");
var catalyst = Catalog([new("CatalystRecipe", [new("A", 100), new("Catalyst", 100, true)], [new("Product", 100)], Quantized: true)]);
var catalyzed = new ProductionPlanner(catalyst).Build(Snapshot(), [new("Product", 300)], TargetMode.Make);
Check(catalyzed.Final.Buffer["Catalyst"] == 10000, "Catalyst returned without consumption");
var competing = Catalog([Mix(), new("Premature", [new("A", 100), new("B", 100)], [new("Wrong", 200)], Priority: 10)]);
Reject(() => new ProductionPlanner(competing).Build(Snapshot(), [new("Product", 200)], TargetMode.Make), "Competing high-priority reaction blocks unsafe plan");
Check(new ProductionPlanner(Catalog([Mix() with { HasEffects = true }])).Build(Snapshot(), [new("Product", 200)], TargetMode.Make).Final.Buffer["Product"] == 200, "Reaction effects never block a plan");
Reject(() => new ProductionPlanner(Catalog([Mix() with { RequiresMixer = true }])).Build(Snapshot(), [new("Product", 200)], TargetMode.Make), "External apparatus rejected");
Reject(() => new ProductionPlanner(Catalog([Mix() with { MinimumTemperature = 400 }])).Build(Snapshot(), [new("Product", 200)], TargetMode.Make), "External heat rejected");
var thermalCatalog = Catalog([
    new("ColdIntermediate", [new("A", 100), new("B", 100)], [new("Intermediate", 200)]),
    new("HotInterception", [new("A", 100), new("B", 100)], [new("Wrong", 200)], Priority: 10, MinimumTemperature: 310),
    new("HeatedFinal", [new("Intermediate", 200), new("C", 100)], [new("Product", 300)], MinimumTemperature: 370)]);
var thermal = new ProductionPlanner(thermalCatalog).Build(Snapshot() with { Temperature = 988 }, [new("Product", 600)], TargetMode.Make, beakers: new(988));
Check(thermal.Final.Buffer["Product"] == 600, "Cold intermediate and hot final produce requested stock");
var changes = thermal.Actions.Where(a => a.RequiredBeakerTemperature != null).ToArray();
Check(changes.Length == 2 && changes[0].RequiredBeakerTemperature == 293.15f && changes[1].RequiredBeakerTemperature == 988, "Cold then hot phases avoid competing reaction");
Check(changes[0].RequiredBeakerPhase == BeakerPhase.Cold && changes[1].RequiredBeakerPhase == BeakerPhase.Hot, "Barriers carry explicit phases independently of numeric temperatures");
var warmCold = new ProductionPlanner(thermalCatalog).Build(Snapshot() with { Temperature = 988 }, [new("Product", 600)], TargetMode.Make, beakers: new(988, 295.5f));
Check(warmCold.Actions.First().RequiredBeakerTemperature == 295.5f && warmCold.Actions.First().RequiredBeakerPhase == BeakerPhase.Cold, "A slightly warmed cold beaker remains the cold phase");
var warmColdSession = new ExecutionSession(warmCold); warmColdSession.Start(warmCold.Initial); warmColdSession.Tick(TimeSpan.Zero, warmCold.Initial);
Check(warmColdSession.RequiredBeakerPhase == BeakerPhase.Cold && warmColdSession.State == ExecutionState.AwaitingBeaker, "Execution exposes cold phase above the old 294.15 K cutoff");
var superheated = new ProductionPlanner(thermalCatalog).Build(Snapshot() with { Temperature = 58706.812f }, [new("Product", 600)], TargetMode.Make, beakers: new(58706.812f));
Check(superheated.Actions.First().RequiredBeakerPhase == BeakerPhase.Cold && superheated.Final.Buffer["Product"] == 600, "Actual superheated beaker is valid but still requires a cold phase");
Check(changes.All(a => a.Before.Beaker.Count == 0 && a.After.Beaker.Count == 0 && MachineSnapshot.Same(a.Before.Buffer, a.After.Buffer)), "Beaker changes are empty boundaries and preserve stock");
Reject(() => new ProductionPlanner(thermalCatalog).Build(Snapshot(), [new("Product", 600)], TargetMode.Make, beakers: new(350)), "Prepared hot beaker below required threshold rejected");
Reject(() => planner.Build(Snapshot(), [new("Product", 200)], TargetMode.Make, beakers: new(float.NaN)), "Invalid hot temperature rejected");
var knownEffects = Mix() with { HasEffects = true, EffectTypes = ["Content.Shared.EntityEffects.Effects.Atmos.CreateGas", "Content.Shared.EntityEffects.Effects.Transform.PopupMessage"] };
Check(new ProductionPlanner(Catalog([knownEffects])).Build(Snapshot(), [new("Product", 200)], TargetMode.Make).Final.Buffer["Product"] == 200, "Gas and popup effects preserve calculable solution chemistry");
Check(new ProductionPlanner(Catalog([knownEffects with { EffectTypes = ["Unknown.ChangeSolution"] }])).Build(Snapshot(), [new("Product", 200)], TargetMode.Make).Final.Buffer["Product"] == 200, "Unknown effects are planned like any other reaction");
var plainFirst = Catalog([Mix("WithEffect") with { HasEffects = true, EffectTypes = ["Unknown.Emp"] }, new("Plain", [new("C", 100), new("D", 100)], [new("Product", 200)])]);
Check(new ProductionPlanner(plainFirst).Build(Snapshot() with { Buffer = new() { ["A"] = 1000, ["B"] = 1000, ["C"] = 1000, ["D"] = 1000 } }, [new("Product", 200)], TargetMode.Make).Final.Buffer["C"] == 900, "An effect-free recipe is preferred when both are possible");
Check(Catalog([knownEffects]).Fingerprint != Catalog([knownEffects with { EffectTypes = ["Unknown.ChangeSolution"] }]).Fingerprint, "Effect behavior changes catalog fingerprint");
var diagnosticCatalog = Catalog([Mix(), new("FiberBreakdown", [new("C", 100)], [new("A", 100)], RequiresMixer: true, MixerCategories: ["Centrifuge"])]);
try { new ProductionPlanner(diagnosticCatalog).Build(Snapshot() with { Buffer = new() { ["A"] = 100, ["B"] = 1000 } }, [new("Product", 600)], TargetMode.Make); throw new Exception("Missing stock unexpectedly available"); }
catch (ChemistryException error) { Check(error.Message.EndsWith(": A 2") && !error.Message.Contains("Centrifuge") && !error.Message.Contains("нагрев"), "Missing source stock names the reagent and shortfall, without apparatus or false heating claims"); }
var electrolysis = Catalog([Mix(), .. new[] { "WaterBreakdown", "EthanolBreakdown", "FatBreakdown" }.Select(name => new Reaction(name, [new("C", 100)], [new("A", 100)], RequiresMixer: true, MixerCategories: ["Electrolysis"]))]);
try { new ProductionPlanner(electrolysis).Build(Snapshot() with { Buffer = new() { ["B"] = 1000 } }, [new("Product", 200)], TargetMode.Make); throw new Exception("Apparatus-only sources rejected"); }
catch (ChemistryException error) { Check(error.Message.EndsWith(": A 1") && !error.Message.Contains("Electrolysis") && !error.Message.Contains("Breakdown"), "Apparatus-only sources never lengthen the missing-reagent message"); }
try { new ProductionPlanner(nested).Build(Snapshot() with { Buffer = new() { ["A"] = 100 } }, [new("Product", 600)], TargetMode.Make); throw new Exception("Several shortages rejected"); }
catch (ChemistryException error) { Check(error.Message.EndsWith(": A 1, B 2, C 2"), "Every missing reagent is listed once with its shortfall"); }
try { new ProductionPlanner(nested).Build(Snapshot() with { Buffer = new() { ["D"] = 300 } }, [new("D", 500)], TargetMode.Make); throw new Exception("Unmakeable target rejected"); }
catch (ChemistryException error) { Check(error.Message.EndsWith(": D 5"), "A target that can only be loaded reports the requested amount"); }
diagnosticCatalog = Catalog([Mix(), new("UnsupportedAlternative", [new("C", 100)], [new("Product", 200)], RequiresMixer: true)]);
try { new ProductionPlanner(diagnosticCatalog).Build(Snapshot() with { Buffer = new() { ["A"] = 1000 } }, [new("Product", 200)], TargetMode.Make); throw new Exception("Missing stock unexpectedly available"); }
catch (ChemistryException error) { Check(error.Message.Contains("B") && !error.Message.Contains("UnsupportedAlternative"), "Unsupported alternative cannot mask missing ingredients in the standard recipe"); }

var phaseSession = new ExecutionSession(thermal);
var phaseCurrent = thermal.Initial;
phaseSession.Start(phaseCurrent);
Check(phaseSession.Tick(TimeSpan.Zero, phaseCurrent) == null && phaseSession.State == ExecutionState.AwaitingBeaker, "Temperature marker sends no server command");
Check(phaseSession.Tick(TimeSpan.FromHours(1), phaseCurrent) == null && phaseSession.Active, "Beaker wait has no command timeout or automatic continuation");
phaseSession.Pause(); Check(phaseSession.State == ExecutionState.AwaitingBeaker, "Pause preserves explicit beaker barrier");
phaseCurrent = phaseCurrent with { Identity = "owner/beaker/new-cold", Temperature = 293.15f };
phaseSession.Observe(phaseCurrent);
Check(phaseSession.State == ExecutionState.AwaitingBeaker && phaseSession.Step == 0, "Replacement alone never confirms the beaker");
phaseSession.Resume(phaseCurrent);
Check(phaseSession.Step == 1 && phaseSession.State == ExecutionState.Running, "Explicit confirmation adopts replacement identity");
var guardSteps = 0;
while (phaseSession.Active && ++guardSteps < 1000)
{
    var command = phaseSession.Tick(TimeSpan.FromSeconds(2), phaseCurrent);
    if (phaseSession.State == ExecutionState.AwaitingBeaker)
    {
        phaseCurrent = phaseCurrent with { Identity = "owner/beaker/new-hot", Temperature = phaseSession.RequiredBeakerTemperature };
        phaseSession.Resume(phaseCurrent);
    }
    else if (command?.Transfer is { } action) { phaseCurrent = action.After with { Identity = phaseCurrent.Identity }; phaseSession.Observe(phaseCurrent); }
}
Check(phaseSession.State == ExecutionState.Completed && phaseCurrent.Buffer["Product"] == 600, "Confirmed cold and hot phases complete across rebased beaker identities");
foreach (var mutation in new MachineSnapshot[] {
    thermal.Initial with { Identity = "another-machine/player/beaker" },
    thermal.Initial with { Capacity = 5000 },
    thermal.Initial with { Beaker = new() { ["A"] = 100 } },
    thermal.Initial with { Buffer = new() { ["A"] = 1 } },
    thermal.Initial with { Mode = 1 } })
{
    phaseSession = new(thermal); phaseSession.Start(thermal.Initial); phaseSession.Tick(TimeSpan.Zero, thermal.Initial); phaseSession.Resume(mutation);
    Check(phaseSession.State == ExecutionState.Failed, "Unsafe change during beaker wait rejected");
}
phaseSession = new(thermal); phaseSession.Start(thermal.Initial); phaseSession.Tick(TimeSpan.Zero, thermal.Initial);
Reject(() => phaseSession.Resume(thermal.Initial), "Wrong-temperature beaker cannot pass confirmation");
Check(phaseSession.State == ExecutionState.AwaitingBeaker, "Wrong temperature keeps replacement barrier");
phaseSession.Stop(); Check(!phaseSession.Active, "Stop cancels replacement wait");
var no75 = new ProductionPlanner(Catalog([Mix()], [1, 5, 25, 50, 100])).Build(Snapshot(), [new("Product", 15000)], TargetMode.Make);
Check(no75.Actions.All(a => a.Dose != 75), "No hardcoded SS220 dose 75");
var cycle = Catalog([new("Cycle1", [new("Intermediate", 100)], [new("Product", 100)]), new("Cycle2", [new("Product", 100)], [new("Intermediate", 100)])]);
Reject(() => new ProductionPlanner(cycle).Build(Snapshot(), [new("Product", 200)], TargetMode.Make), "Recipe cycles bounded");
using (var cancel = new CancellationTokenSource()) { cancel.Cancel(); Reject(() => planner.Build(Snapshot(), [new("Product", 200)], TargetMode.Make, cancellation: cancel.Token), "Search cancellable"); }

plan = planner.Build(Snapshot(), [new("Product", 600)], TargetMode.Make);
var session = new ExecutionSession(plan);
var current = plan.Initial.Copy(); session.Start(current);
var first = session.Tick(TimeSpan.Zero, current)!;
Check(first.Transfer == plan.Actions[0] && session.Tick(TimeSpan.FromSeconds(1), current) == null, "Exactly one in-flight command");
session.Observe(first.Transfer!.After); session.Observe(first.Transfer.After);
Check(session.Step == 1, "Duplicate state never commits action twice");
current = first.Transfer.After;
session.Pause(); Check(session.Tick(TimeSpan.FromSeconds(2), current) == null, "Pause prevents new commands");
session.Resume(current);
while (session.Active)
{
    var command = session.Tick(TimeSpan.FromSeconds(2), current);
    if (command?.Transfer is { } action) { current = action.After; session.Observe(current); }
}
Check(session.State == ExecutionState.Completed && session.Step == plan.Actions.Count, "Confirmed sequence completes");
session = new(plan); session.Start(plan.Initial); session.Tick(TimeSpan.Zero, plan.Initial);
Check(session.Tick(TimeSpan.FromSeconds(9), plan.Initial) == null && session.State == ExecutionState.Failed, "Timeout never retries");
session = new(plan); session.Start(plan.Initial); session.Tick(TimeSpan.Zero, plan.Initial); session.Stop();
session.Observe(plan.Actions[0].After);
Check(session.Tick(TimeSpan.FromSeconds(1), plan.Actions[0].After) == null && session.State == ExecutionState.Stopped, "Close/stop ignores later confirmations");
session = new(plan); session.Start(plan.Initial with { Identity = "another-beaker" });
Check(session.State == ExecutionState.Failed, "Stale preview identity rejected");
session = new(plan); session.Start(plan.Initial); session.Observe(plan.Initial with { Buffer = new() { ["A"] = 3 } });
Check(session.State == ExecutionState.Failed, "Manual inventory mutation rejected");
session = new(plan); session.Start(plan.Initial); session.Tick(TimeSpan.Zero, plan.Initial); session.Observe(plan.Initial with { Identity = "another-player" });
Check(session.State == ExecutionState.Failed, "Character/beaker change cancels in-flight session");
var discard = planner.Build(Snapshot(mode: 1), [new("Product", 200)], TargetMode.Make);
session = new(discard); session.Start(discard.Initial);
Check(session.Tick(TimeSpan.Zero, discard.Initial)?.SetTransferMode == true, "Discard first requests transfer mode");
Check(session.Tick(TimeSpan.FromSeconds(1), discard.Initial) == null, "Wait for transfer-mode confirmation");
session.Observe(discard.Initial with { Mode = 0 });
Check(session.Tick(TimeSpan.FromSeconds(2), discard.Initial with { Mode = 0 })?.Transfer != null, "Transfer starts after mode confirmation");

var fixedTiming = new ExecutionTiming(2, 0.6, 0);
Check(fixedTiming.Delay(false, () => throw new Exception("No randomness expected")) == TimeSpan.FromSeconds(0.5), "Fixed speed avoids random sampling");
Check(fixedTiming.Delay(true, () => 0) == TimeSpan.FromSeconds(1.1), "Reagent switch adds an independent pause");
var chaos = fixedTiming with { ChaosPercent = 100 };
var samples = new Queue<double>([0.1, 0]);
Check(chaos.Delay(false, samples.Dequeue) == TimeSpan.FromSeconds(0.2), "Random delay can be shorter");
samples = new([0.1, 1]);
Check(chaos.Delay(false, samples.Dequeue) == TimeSpan.FromSeconds(0.8), "Random delay can be longer");
samples = new([0.1, 0]);
Check(chaos.Delay(true, samples.Dequeue) == TimeSpan.FromSeconds(0.8), "Chaos never removes the switch pause");
var calls = 0;
Check((fixedTiming with { ChaosPercent = 40 }).Delay(false, () => { calls++; return 0.5; }) == TimeSpan.FromSeconds(0.5) && calls == 1, "Probability miss keeps normal interval");
Reject(() => (fixedTiming with { ClicksPerSecond = double.NaN }).Delay(false, () => 0), "Invalid timing rejected");
// A 200 u batch is A 100 + B 100 into the beaker, then the product back: 3 transfers, 2 waits, 2 reagent switches.
Check(fixedTiming.Estimate(planner.Build(Snapshot(), [new("Product", 200)], TargetMode.Make).Actions) == TimeSpan.FromSeconds(2.2), "Estimate sums intervals and reagent-switch pauses after the first transfer");
Check(fixedTiming.Estimate([]) == TimeSpan.Zero, "Empty plan takes no time");
Check(fixedTiming.Estimate(thermal.Actions) < fixedTiming.Estimate(thermal.Actions.Where(a => a.RequiredBeakerTemperature == null)), "Beaker barriers restart the interval instead of adding a wait");
Reject(() => (fixedTiming with { ClicksPerSecond = 0 }).Estimate([]), "Invalid timing cannot estimate");
var timedPlan = new ProductionPlanner(Catalog([Mix()], [1])).Build(Snapshot(), [new("Product", 600)], TargetMode.Make);
session = new(timedPlan, timing: fixedTiming);
session.Start(timedPlan.Initial);
var timedFirst = session.Tick(TimeSpan.Zero, timedPlan.Initial)!.Transfer!;
session.Observe(timedFirst.After);
Check(session.Tick(TimeSpan.Zero, timedFirst.After) == null && session.State == ExecutionState.Waiting, "Confirmed action enters timed wait");
Check(session.Tick(TimeSpan.FromSeconds(0.49), timedFirst.After) == null, "No early command during interval");
var timedSecond = session.Tick(TimeSpan.FromSeconds(0.5), timedFirst.After)!.Transfer!;
Check(timedSecond.ReagentId == timedFirst.ReagentId, "Repeated reagent uses fast interval");
session.Observe(timedSecond.After);
session.Tick(TimeSpan.FromSeconds(0.5), timedSecond.After);
var timedThird = session.Tick(TimeSpan.FromSeconds(1), timedSecond.After)!.Transfer!;
session.Observe(timedThird.After);
session.Tick(TimeSpan.FromSeconds(1), timedThird.After);
Check(session.Tick(TimeSpan.FromSeconds(1.5), timedThird.After) == null, "Different reagent waits beyond base interval");
Check(session.Tick(TimeSpan.FromSeconds(2.1), timedThird.After)?.Transfer?.ReagentId == "B", "Switch proceeds after extra pause");
calls = 0;
session = new(timedPlan, timing: chaos, random: () => { calls++; return 0.5; }); session.Start(timedPlan.Initial);
session.Tick(TimeSpan.Zero, timedPlan.Initial); session.Observe(timedFirst.After);
session.Tick(TimeSpan.Zero, timedFirst.After);
for (var frame = 1; frame <= 20; frame++) session.Tick(TimeSpan.FromMilliseconds(frame), timedFirst.After);
Check(calls == 2, "Random delay sampled once despite repeated frame ticks");
session.UpdateTiming(new(20, 0, 0));
session.Tick(TimeSpan.FromSeconds(1), timedFirst.After);
Check(session.Tick(TimeSpan.FromSeconds(1.051), timedFirst.After)?.Transfer != null, "Live speed adjustment applies to next command");
session.Observe(timedSecond.After); session.Tick(TimeSpan.FromSeconds(2), timedSecond.After); session.Pause();
Check(session.Tick(TimeSpan.FromSeconds(3), timedSecond.After) == null, "Pause also blocks elapsed timed delay");
session.Resume(timedSecond.After);
Check(session.Tick(TimeSpan.FromSeconds(3), timedSecond.After)?.Transfer != null, "Resume can use elapsed delay");
session = new(timedPlan, timing: fixedTiming); session.Start(timedPlan.Initial); session.Tick(TimeSpan.Zero, timedPlan.Initial);
session.Observe(timedFirst.After); session.Tick(TimeSpan.Zero, timedFirst.After);
session.Observe(timedFirst.After with { Identity = "removed-beaker" });
Check(session.State == ExecutionState.Failed, "Timed wait still validates beaker identity");
session = new(timedPlan, timing: fixedTiming); session.Start(timedPlan.Initial); session.Tick(TimeSpan.Zero, timedPlan.Initial);
Check(session.Tick(TimeSpan.FromSeconds(9), timedPlan.Initial) == null && session.State == ExecutionState.Failed, "Timing does not weaken confirmation timeout");
var priorLanguage = Environment.GetEnvironmentVariable("SS14_MOD_LANGUAGE");
try
{
    Environment.SetEnvironmentVariable("SS14_MOD_LANGUAGE", "en");
    Check(Text.State(ExecutionState.Waiting) == "Waiting between commands" && Text.T("РУ", "EN") == "EN", "English statuses selected");
    try { planner.Build(Snapshot(temperature: null), [new("Product", 200)], TargetMode.Make); }
    catch (ChemistryException e) { Check(e.Message == "Confirm a cold beaker.", "Planner error uses English only"); }
    Environment.SetEnvironmentVariable("SS14_MOD_LANGUAGE", "ru");
    Check(Text.State(ExecutionState.Waiting) == "Пауза между командами", "Russian statuses selected");
}
finally { Environment.SetEnvironmentVariable("SS14_MOD_LANGUAGE", priorLanguage); }

var path = Path.Combine(Path.GetTempPath(), "ChemMaster-tests-" + Guid.NewGuid().ToString("N"), "recipes.json");
var store = new RecipeStore(path);
try
{
    Check(store.Load().Length == 0, "No recipe file means empty collection");
    store.Save(new("Меднабор", TargetMode.Make, [new("Product", 500)]));
    Check(store.Load().Single().Targets.Single().Amount == 500, "Unicode recipe persists targets");
    store.Save(new("Меднабор", TargetMode.Ensure, [new("Product", 600)]));
    Check(store.Load().Length == 1 && store.Load()[0].Mode == TargetMode.Ensure, "Save replaces matching recipe");
    Reject(() => store.Save(new("", TargetMode.Make, [])), "Invalid saved recipe rejected");
    store.Save(new("Топливо", TargetMode.Make, [new("Product", 100)]));
    store.Delete("меднабор");
    Check(store.Load().Single().Name == "Топливо", "Delete removes only the named recipe, ignoring case");
    store.Delete("missing");
    Check(store.Load().Length == 1, "Deleting an unknown recipe keeps the collection");
    File.WriteAllText(path, "{\"Version\":2,\"Recipes\":[]}");
    Reject(() => store.Load(), "Unknown storage version rejected");
    var timingPath = Path.ChangeExtension(path, ".settings.json");
    var timingStore = new TimingStore(timingPath);
    Check(timingStore.Load() == new ExecutionTiming(), "Timing defaults when no file exists");
    timingStore.Save(fixedTiming);
    Check(new TimingStore(timingPath).Load() == fixedTiming, "Timing persists across reopened windows");
    Reject(() => timingStore.Save(fixedTiming with { ChaosPercent = 101 }), "Invalid settings cannot overwrite file");
    Check(timingStore.Load() == fixedTiming, "Rejected settings preserve saved values");
    File.WriteAllText(timingPath, "{\"Version\":2,\"Timing\":{}}");
    Reject(() => timingStore.Load(), "Unknown timing storage version rejected");
    File.Delete(timingPath);
    var beakerPath = Path.ChangeExtension(path, ".beakers.json"); var beakerStore = new BeakerStore(beakerPath);
    Check(beakerStore.Load().Hot == 500, "Beaker default temperature");
    beakerStore.Save(new(988)); Check(new BeakerStore(beakerPath).Load().Hot == 988, "Hot temperature persists across reopened windows");
    Reject(() => beakerStore.Save(new(0)), "Invalid temperature cannot overwrite persisted profile");
    Check(beakerStore.Load().Hot == 988, "Rejected temperature preserves profile");
    beakerStore.Save(new(58706.812f)); Check(beakerStore.Load().Hot == 58706.812f, "Actual superheated temperature persists without an arbitrary upper cutoff");
    File.Delete(beakerPath);
}
finally { File.Delete(path); Directory.Delete(Path.GetDirectoryName(path)!); }

if (args.Length > 0)
{
    using var rules = JsonDocument.Parse(File.ReadAllText(args[0]));
    var root = rules.RootElement;
    var reagents = root.GetProperty("reagents").EnumerateObject().Select(p => new Reagent(p.Name, p.Name, p.Value.GetProperty("specificHeat").GetSingle())).ToArray();
    var reactions = root.GetProperty("reactions").EnumerateArray().Select(r => new Reaction(r.GetProperty("id").GetString()!,
        r.GetProperty("inputs").EnumerateArray().Select(i => new Ingredient(i.GetProperty("prototype").GetString()!, (int)(i.GetProperty("amount").GetDecimal() * 100), i.GetProperty("catalyst").GetBoolean())).ToArray(),
        r.GetProperty("outputs").EnumerateArray().Select(i => new Ingredient(i.GetProperty("prototype").GetString()!, (int)(i.GetProperty("amount").GetDecimal() * 100))).ToArray(),
        r.GetProperty("priority").GetInt32(), r.GetProperty("minTemperature").GetSingle(),
        r.GetProperty("maxTemperature").ValueKind == JsonValueKind.Null ? float.PositiveInfinity : r.GetProperty("maxTemperature").GetSingle(),
        r.GetProperty("quantized").GetBoolean(), r.GetProperty("conserveEnergy").GetBoolean(), r.GetProperty("hasEffects").GetBoolean(),
        r.GetProperty("mixerCategories").GetArrayLength() > 0,
        r.GetProperty("mixerCategories").EnumerateArray().Select(c => c.GetString()!).ToArray(),
        r.TryGetProperty("effectTypes", out var effects) ? effects.EnumerateArray().Select(e => e.GetString()!).ToArray() : null)).ToArray();
    var gameCatalog = new ChemistryCatalog(reagents, reactions, [1, 5, 10, 15, 20, 25, 30, 50, 75, 100]);
    var stock = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach (var id in new[] { "Carbon", "Oxygen", "Sugar", "Hydrogen", "Nitrogen", "Potassium", "Silicon", "Iron", "Sulfur", "Phosphorus", "Chlorine", "Sodium", "Copper", "Ethanol", "Water", "Radium", "Fluorine", "Aluminium", "Lithium", "Mercury", "Plasma" })
        if (gameCatalog.Reagents.ContainsKey(id)) stock[id] = 100000;
    foreach (var id in new[] { "Inaprovaline", "Bicaridine", "Dylovene", "Kelotane", "Dexalin", "Tricordrazine" })
    {
        if (!gameCatalog.Reagents.ContainsKey(id)) continue;
        var result = new ProductionPlanner(gameCatalog).Build(new(new(stock), [], 10000, 0, "SS220 fixture", 293.15f), [new(id, 3000)], TargetMode.Make);
        Check(result.Final.Buffer.GetValueOrDefault(id) >= 3000 && result.Final.Beaker.Count == 0, "SS220 compound " + id);
        Console.WriteLine(id + ": " + result.Actions.Count + " confirmed actions");
    }
    if (root.GetProperty("reactions").EnumerateArray().Any(r => r.GetProperty("id").GetString() == "Aglomorphine") && reactions.Any(r => r.EffectTypes != null))
    {
        foreach (var id in new[] { "WeldingFuel", "Iodine", "TableSalt" }) stock[id] = 100000;
        var aglo = new ProductionPlanner(gameCatalog).Build(new(new(stock), [], 10000, 0, "machine/player/cold", 293.15f), [new("Aglomorphine", 10000)], TargetMode.Make, beakers: new(988));
        Check(aglo.Final.Buffer.GetValueOrDefault("Aglomorphine") >= 10000 && aglo.Final.Beaker.Count == 0, "SS220 Aglomorphine 100u from raw stock");
        Check(aglo.Actions.Any(a => a.RequiredBeakerTemperature > 370), "SS220 Aglomorphine includes heated phase");
        Console.WriteLine("Aglomorphine 100u: " + aglo.Actions.Count + " actions, " + aglo.Actions.Count(a => a.RequiredBeakerTemperature != null) + " beaker changes");
        Console.WriteLine("Raw stock consumed: " + string.Join(", ", stock.Where(p => aglo.Final.Buffer.GetValueOrDefault(p.Key) < p.Value).Select(p => p.Key + "=" + ((p.Value - aglo.Final.Buffer.GetValueOrDefault(p.Key)) / 100m).ToString(System.Globalization.CultureInfo.InvariantCulture) + "u")));
        var livePlan = aglo; var liveState = aglo.Initial; var liveSession = new ExecutionSession(livePlan); liveSession.Start(liveState);
        var beakerCount = 0; var productionSteps = 0; var liveTemperatures = new BeakerTemperatures(988);
        while (liveSession.Active && ++productionSteps < 1000)
        {
            var command = liveSession.Tick(TimeSpan.FromSeconds(productionSteps), liveState);
            if (command?.Transfer is { } transfer) { liveState = transfer.After with { Identity = liveState.Identity }; liveSession.Observe(liveState); }
            else if (liveSession.State == ExecutionState.AwaitingBeaker)
            {
                if (++beakerCount > 16) throw new Exception("Beaker replanning did not make progress");
                var hotPhase = liveSession.RequiredBeakerPhase == BeakerPhase.Hot;
                var measured = hotPhase ? 987.5f : 295.5f;
                liveState = liveState with { Temperature = measured, Identity = "machine/player/replacement-" + beakerCount };
                liveSession.Observe(liveState); Check(liveSession.Active, "Live phase replacement retains inventory guard");
                liveTemperatures = hotPhase ? liveTemperatures with { Hot = measured } : liveTemperatures with { Cold = measured };
                livePlan = new ProductionPlanner(gameCatalog).Build(liveState, aglo.AbsoluteGoals, TargetMode.Ensure, beakers: liveTemperatures);
                liveSession = new(livePlan); liveSession.Start(liveState);
            }
        }
        Check(liveSession.State == ExecutionState.Completed && liveState.Buffer.GetValueOrDefault("Aglomorphine") >= 10000, "Aglomorphine completes with measured-temperature replanning and replacement identities");
        Check(liveState.Buffer.GetValueOrDefault("Aglomorphine") <= 10400, "Replanning retains original absolute goal instead of making another 100u");
        var recoveryPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "aglomorphine-recovery.json");
        var recoveryStock = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(recoveryPath))!;
        var recovery = new ProductionPlanner(gameCatalog).Build(new(new(recoveryStock), [], 10000, 0, "machine/player/used-hot", 58706.812f), [new("Aglomorphine", 10000)], TargetMode.Ensure, beakers: new(988));
        Check(recovery.Actions.Count == 29 && recovery.Actions[0].RequiredBeakerPhase == BeakerPhase.Cold, "Recorded live stock reproduces the 29-step cold-beaker barrier");
        var resumed = new ProductionPlanner(gameCatalog).Build(recovery.Initial with { Identity = "machine/player/replacement-cold", Temperature = 295.5f }, recovery.AbsoluteGoals, TargetMode.Ensure, beakers: new(988, 295.5f));
        Check(resumed.Actions.Count == 28 && resumed.Final.Buffer.GetValueOrDefault("Aglomorphine") == 10000, "Recorded remaining Aglomorphine stock completes after a warmed cold replacement");
        Check(recoveryStock.GetValueOrDefault("Opium") == 2500 && MachineSnapshot.Same(recoveryStock, recovery.Initial.Buffer), "Recovery preserves live intermediate stock and input snapshot");
    }
}
Console.WriteLine("ChemMaster: " + checks + " checks passed.");
