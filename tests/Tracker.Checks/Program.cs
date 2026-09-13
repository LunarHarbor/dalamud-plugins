using System.Text.Json;
using DeepDungeonTracker;

if (args.Length == 2 && args[0] == "--replay")
{
    using var capture = JsonDocument.Parse(File.ReadAllText(args[1]));
    var root = capture.RootElement;
    var run = root.TryGetProperty("Run", out var nested) ? nested.Deserialize<ScoreRun>() : root.Deserialize<ScoreRun>();
    var result = ScoreEngine.Calculate(run!);
    Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    if (root.TryGetProperty("Results", out var results) && results.GetArrayLength() > 0 &&
        results[results.GetArrayLength() - 1].TryGetProperty("ObservedScore", out var observed) &&
        observed.ValueKind == JsonValueKind.Number)
        Console.WriteLine($"Game result: {observed.GetInt32()}; difference: {result.Total - observed.GetInt32()}");
    return;
}

var checks = 0;
void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"{label}: expected {expected}, got {actual}");
    checks++;
}
void Throws<T>(Action action, string label) where T : Exception
{
    try { action(); } catch (T) { checks++; return; }
    throw new Exception(label + ": expected " + typeof(T).Name);
}
ScoreFloor FloorAt(int floor) => new(floor, false, false, 0, 0, 0, 0, 0, 0, 0, 0, 0, false);
ScoreRun Run(DeepDungeon dungeon, params ScoreFloor[] floors) =>
    new(dungeon, ScoreEngine.MaximumLevel(dungeon), 0, 0, 0, [new(1, false, false, floors)]);

// 51,980 + 44,820 + 440 + 288 * 101 = 126,328.
var floors = Enumerable.Range(31, 10).Select(n => FloorAt(n) with { Cleared = true }).ToArray();
floors[0] = floors[0] with { Kills = 3, Map = true, Coffers = 4, Enchantments = 2, Traps = 2, Deaths = 1 };
floors[9] = floors[9] with { Kills = 1, BossDefeated = true };
var duo = new ScoreRun(DeepDungeon.PilgrimsTraverse, 100, 99, 99, 0, [new(2, true, true, floors)]);
var score = ScoreEngine.Calculate(duo);
Equal(126328, score.Total, "worked duo example");
Equal(288, score.BonusUnits, "aggregate including penalties");
Equal(10100, score.Party, "duo set contribution");
foreach (var (size, total) in new[] { (1, 136428), (2, 126328), (3, 121278), (4, 116228) })
    Equal(total, ScoreEngine.Calculate(duo with { Sets = [duo.Sets[0] with { PartySize = size }] }).Total, $"party size {size}");
var inProgress = duo with { Sets = [duo.Sets[0] with { Completed = false }] };
Equal(0, ScoreEngine.Calculate(inProgress).Party, "no unearned party award");
Equal(0, ScoreEngine.Calculate(inProgress).Time, "no unearned time award");
Equal(0, ScoreEngine.Calculate(duo with { Sets = [duo.Sets[0] with { TimeBonus = false }] }).Time, "no speed bonus for slow set");
Equal(50000, ScoreEngine.Calculate(Run(DeepDungeon.PilgrimsTraverse, FloorAt(71))).Total, "shortcut starts retain character score");
Equal(50201, ScoreEngine.Calculate(Run(DeepDungeon.PilgrimsTraverse, FloorAt(31) with { Kills = 1 })).Total, "deep ordinary kill");
Equal(50605, ScoreEngine.Calculate(Run(DeepDungeon.PilgrimsTraverse, FloorAt(31) with { Kills = 1, Mimics = 1 })).Total, "mimic replaces deep kill bonus");
Equal(50000, ScoreEngine.Calculate(Run(DeepDungeon.PilgrimsTraverse, FloorAt(1) with { Deaths = 1, Coffers = 1 })).Total, "aggregate penalty clamp");
Equal(49990, ScoreEngine.Calculate(Run(DeepDungeon.PilgrimsTraverse, FloorAt(1) with { Deaths = 1 }) with { KOs = 1 }).Total, "failed-run base penalty");
foreach (var (dungeon, levelBase) in new[] { (DeepDungeon.PalaceOfTheDead,30000), (DeepDungeon.HeavenOnHigh,35000), (DeepDungeon.EurekaOrthos,45000), (DeepDungeon.PilgrimsTraverse,50000) })
    Equal(levelBase + 100, ScoreEngine.Calculate(Run(dungeon, FloorAt(1) with { Kills = 1 })).Total, $"first-floor candidate {dungeon}");
Equal(0, ScoreEngine.Calculate(Run(DeepDungeon.PilgrimsTraverse, FloorAt(1) with { Map = true })).Maps, "map award deferred until transition");
Equal(2525, ScoreEngine.Calculate(Run(DeepDungeon.PilgrimsTraverse, FloorAt(1) with { Map = true, Cleared = true })).Maps, "cleared map award");
Equal(0, ScoreEngine.Calculate(Run(DeepDungeon.PilgrimsTraverse, FloorAt(99) with { Map = true, Cleared = true })).Maps, "boss map excluded");
var ending = Run(DeepDungeon.PilgrimsTraverse, FloorAt(99) with { Kills = 2, BossDefeated = true, Cleared = true }, FloorAt(100) with { Cleared = true });
ending = ending with { Sets = [ending.Sets[0] with { Completed = true }] };
Equal(50500, ScoreEngine.Calculate(ending).Bosses, "one encounter weight at 99");
Equal(303000, ScoreEngine.Calculate(ending).Completion, "actual clear award");
Equal(0, ScoreEngine.Calculate(Run(DeepDungeon.PilgrimsTraverse, FloorAt(71)), 100).Completion, "projection cannot invent completion");
Throws<ArgumentException>(() => ScoreEngine.Calculate(duo with { Sets = [duo.Sets[0] with { PartySize = 0 }] }), "reject invalid party");
Throws<ArgumentException>(() => ScoreEngine.Calculate(Run(DeepDungeon.PilgrimsTraverse, FloorAt(31), FloorAt(33))), "reject missing floor");
Throws<ArgumentException>(() => ScoreEngine.Calculate(Run(DeepDungeon.PilgrimsTraverse, FloorAt(31) with { Mimics = 1 })), "reject impossible special kills");
Throws<ArgumentException>(() => ScoreEngine.Calculate(duo with { Sets = null! }), "reject malformed JSON sets");
Throws<OverflowException>(() => ScoreEngine.Calculate(Run(DeepDungeon.PilgrimsTraverse, FloorAt(31) with { Kills = int.MaxValue })), "reject overflowing counts");

var ledger = new DeathLedger();
Equal(true, ledger.Record(42), "first death");
ledger.Observe(42, false); // notification can arrive before HP/dead flag update
Equal(false, ledger.Record(42), "duplicate before dead flag");
ledger.Observe(42, true);
Equal(false, ledger.Record(42), "duplicate while dead");
ledger.Observe(42, false);
Equal(true, ledger.Record(42), "new death after resurrection");
ledger.Clear();
Equal(true, ledger.Record(42), "new floor can reuse entity");

var set = new FloorSet();
set.AddFloor(10);
set.MarkBossTime(TimeSpan.FromSeconds(1799));
set.Complete(TimeSpan.FromMinutes(31));
set.Complete(TimeSpan.FromMinutes(32));
set.Fail();
Equal(true, set.TimeBonus, "time measured at boss kill and idempotent completion");
Equal(false, set.Failed, "completed duty cannot become failed on exit");
var late = new FloorSet();
late.AddFloor(20);
late.MarkBossTime(TimeSpan.FromMinutes(30));
late.Complete(TimeSpan.FromMinutes(30));
Equal(false, late.TimeBonus, "exactly thirty minutes is not fast");
var unknownTime = new FloorSet();
unknownTime.AddFloor(20);
unknownTime.SetTimerKnown(false);
unknownTime.Complete(TimeSpan.FromMinutes(1));
Equal(false, unknownTime.TimeBonus, "unknown timer cannot earn speed award");
var failed = new FloorSet();
failed.AddFloor(31);
failed.Fail();
failed.Complete(TimeSpan.Zero);
Equal(false, failed.Completed, "failed duty cannot become completed");

var save = new SaveSlot(DeepDungeon.PilgrimsTraverse, 60044, 24, 100);
save.MarkCurrentSchema();
save.AddFloorSet(31);
save.CurrentFloorSet()!.SetPartySize(2);
save.CurrentFloor()!.EnemyKilled();
save.CurrentFloor()!.PlayerKilled();
save.CurrentFloor()!.LocalPlayerKilled();
save.CurrentFloor()!.CandleLit();
save.CurrentFloor()!.PomanderUsed(Pomander.Haste);
save.CurrentFloor()!.CofferOpened(Coffer.PomanderOfDevotion);
save.CurrentFloorSet()!.RecordResult(12345, 9);
save.Note("synthetic incomplete capture");
var roundtrip = JsonSerializer.Deserialize<SaveSlot>(JsonSerializer.Serialize(save))!;
Equal(2, roundtrip.CurrentFloorSet()!.PartySize, "party survives JSON");
Equal(1, roundtrip.Snapshot().Sets[0].Floors[0].LocalDeaths, "local deaths survive export");
Equal(1, roundtrip.Snapshot().Sets[0].Floors[0].Candles, "candles survive export");
Equal(12345, roundtrip.CurrentFloorSet()!.ObservedScore, "observed result survives JSON");
Equal(1, roundtrip.Kills(), "game result never rewrites captured kills");
Equal(Pomander.Haste, roundtrip.CurrentFloor()!.Pomanders.Single(), "new item survives JSON");
var adapter = new Score(roundtrip, true);
adapter.TotalScoreCalculation(true, ScoreCalculationType.CurrentFloor);
Equal(ScoreEngine.Calculate(roundtrip.Snapshot()).Total, adapter.TotalScore, "widget and replay share same arithmetic");
roundtrip.CurrentFloorSet()!.Fail();
roundtrip.ResetFloorSet();
Equal(31, roundtrip.CurrentFloorNumber(), "retry retains starting floor");
Equal(0, roundtrip.Kills(), "retry rolls back failed set events");
Equal(false, roundtrip.CurrentFloorSet()!.Failed, "retry resets failure state");
Equal(true, PilgrimData.IsBoss(14037, 99) && PilgrimData.IsBoss(14038, 99), "both 99 boss IDs");
Equal(false, PilgrimData.IsBoss(14037, 90), "boss cannot match wrong floor");
Equal(true, PilgrimData.IsMimic(14264) && PilgrimData.IsMimic(14265) && PilgrimData.IsMimic(14266), "all Pilgrim mimic variants");
Equal(Pomander.Haste, PilgrimData.MapPomander(36), "haste mapping");
Equal(Pomander.Devotion, PilgrimData.MapPomander(38), "devotion mapping");
Equal(Pomander.Intuition, PilgrimData.MapPomander(34), "shared EO item mapping");
Equal(null, PilgrimData.MapPomander(999), "unknown item never becomes invalid sprite");

var directory = Path.Combine(Path.GetTempPath(), "ddt-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    await LocalStream.Save(directory, "run.json", save);
    save.CurrentFloor()!.EnemyKilled();
    await LocalStream.Save(directory, "run.json", save);
    Equal(2, LocalStream.Load<SaveSlot>(directory, "run.json")!.Kills(), "atomic replacement saves newest data");
    Equal(1, LocalStream.Load<SaveSlot>(directory, "run.json.bak")!.Kills(), "previous run retained");
    save.CurrentFloorSet()!.SetTimerKnown(false);
    await LocalStream.Save(directory, "unknown-time.json", save);
    Equal(false, LocalStream.Load<SaveSlot>(directory, "unknown-time.json")!.CurrentFloorSet()!.TimerKnown, "unknown timer survives default-omitting serializer");
    var before = File.ReadAllText(Path.Combine(directory, "run.json"));
    Throws<InvalidOperationException>(() => LocalStream.Save(directory, "run.json", new Unserializable()).GetAwaiter().GetResult(), "failed serialization");
    Equal(before, File.ReadAllText(Path.Combine(directory, "run.json")), "failed write preserves existing file");
    File.WriteAllText(Path.Combine(directory, "run.json"), "{broken");
    Equal(1, LocalStream.Load<SaveSlot>(directory, "run.json")!.Kills(), "corrupt save recovers backup");
    Equal("{broken", File.ReadAllText(Path.Combine(directory, "run.json")), "corrupt evidence is preserved");
}
finally
{
    var resolved = Path.GetFullPath(directory);
    if (!resolved.StartsWith(Path.Combine(Path.GetTempPath(), "ddt-checks-"), StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Unexpected temporary check directory.");
    Directory.Delete(resolved, true);
}
checks += ScoreReviewChecks.Run();
checks += CaptureReviewChecks.Run();
checks += TimerReviewChecks.Run();
checks += UiSaveReviewChecks.Run();
checks += CaptureSetOutcomeChecks.Run();
checks += SetContinuationChecks.Run();
checks += CapturePendingCompletionChecks.Run();
Console.WriteLine($"PASS: {checks} score, capture, lifecycle, mapping, and persistence checks.");

sealed class Unserializable { public string Value => throw new InvalidOperationException("synthetic serialization failure"); }
