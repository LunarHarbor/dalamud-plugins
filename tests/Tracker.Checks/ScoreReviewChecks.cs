using System.Text.Json;
using DeepDungeonTracker;

public static class ScoreReviewChecks
{
    public static int Run()
    {
        var count = 0;
        void Equal<T>(T expected, T actual, string label)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception($"{label}: expected {expected}, got {actual}");
            count++;
        }
        void Reject(Action action, string label)
        {
            try { action(); }
            catch (ArgumentException) { count++; return; }
            throw new Exception(label + ": expected invalid capture rejection");
        }
        ScoreFloor FloorAt(int number) => new(number, false, false, 0, 0, 0, 0, 0, 0, 0, 0, 0, false);
        ScoreRun RunAt(ScoreFloor floor) => new(DeepDungeon.PilgrimsTraverse, 100, 0, 0, 0, [new(2, false, false, [floor])]);

        var legacy = new SaveSlot(DeepDungeon.PalaceOfTheDead, 60001, 24, 60);
        legacy.AddFloorSet(10);
        Equal(false, legacy.Snapshot().Sets[0].Completed, "legacy boss-floor arrival does not prove completion");
        Equal(0, ScoreEngine.Calculate(legacy.Snapshot()).Party, "legacy arrival earns no party bonus");
        legacy.AddFloorSet(11);
        Equal(true, legacy.Snapshot().Sets[0].Completed, "later legacy set proves earlier completion");
        Equal(true, legacy.Snapshot().Sets[0].Floors[0].Cleared, "legacy continuation proves earlier floor clear");

        var resumedTimer = new FloorSet();
        resumedTimer.AddFloor(10);
        resumedTimer.SetTimerKnown(false);
        resumedTimer.MarkBossTime(TimeSpan.FromMinutes(1));
        resumedTimer.SetTimerKnown(true);
        resumedTimer.Complete(TimeSpan.FromMinutes(31));
        Equal(false, resumedTimer.TimeBonus, "unknown boss time cannot become trusted when timer recovers");
        var negativeTimer = new FloorSet();
        negativeTimer.AddFloor(10);
        negativeTimer.MarkBossTime(TimeSpan.FromSeconds(-1));
        negativeTimer.Complete(TimeSpan.FromMinutes(31));
        Equal(false, negativeTimer.TimeBonus, "negative boss time cannot earn speed bonus");

        var wrongBoss = RunAt(FloorAt(31) with { BossDefeated = true, Kills = 1 });
        Reject(() => ScoreEngine.Calculate(wrongBoss), "ordinary floor cannot claim boss bonus");
        var midSet = RunAt(FloorAt(31) with { Cleared = true });
        Reject(() => ScoreEngine.Calculate(midSet with { Sets = [midSet.Sets[0] with { Completed = true }] }),
            "mid-set completion cannot claim party bonus");
        var crossedSet = new ScoreRun(DeepDungeon.PilgrimsTraverse, 100, 0, 0, 0,
            [new(2, false, false, [FloorAt(40), FloorAt(41)])]);
        Reject(() => ScoreEngine.Calculate(crossedSet), "set boundary cannot be crossed inside one set");

        var scored = new SaveSlot(DeepDungeon.PilgrimsTraverse, 60044, 24, 100);
        scored.MarkCurrentSchema();
        scored.AddFloorSet(31);
        scored.UpdateCurrentFloorScore(50000);
        scored.UpdateCurrentFloorScore(50000);
        Equal(50000, scored.Score(), "repeated current-floor score update is idempotent");
        scored.CurrentFloor()!.MarkCleared();
        scored.AddFloor();
        scored.UpdateCurrentFloorScore(53000);
        scored.UpdateCurrentFloorScore(53100);
        Equal(53100, scored.Score(), "updated run total keeps previous floor contribution");
        Equal(3100, scored.CurrentFloor()!.Score, "current floor receives only its score difference");

        var partial = new SaveSlot(DeepDungeon.PilgrimsTraverse, 60048, 24, 100);
        partial.MarkCurrentSchema();
        partial.AddFloorSet(78);
        Equal(50000, ScoreEngine.Calculate(partial.Snapshot()).Total, "late capture cannot invent earlier floor points");
        Equal(true, SaveSlot.IsValid(partial), "valid partial capture passes save validation");
        Equal(false, SaveSlot.IsValid(null), "null save rejected");
        SaveSlot Mutate(string json) => JsonSerializer.Deserialize<SaveSlot>(json)!;
        var serialized = JsonSerializer.Serialize(partial);
        Equal(false, SaveSlot.IsValid(Mutate(serialized.Replace("\"CaptureNotes\":[]", "\"CaptureNotes\":null"))), "null notes rejected");
        Equal(false, SaveSlot.IsValid(Mutate(serialized.Replace("\"Floors\":[{", "\"Floors\":[null,{"))), "null floor rejected");
        Equal(false, SaveSlot.IsValid(Mutate(serialized.Replace("\"Kills\":0", "\"Kills\":-1"))), "negative persisted kills rejected");
        Equal(false, SaveSlot.IsValid(Mutate(serialized.Replace("\"Pomanders\":null", "\"Pomanders\":[999]"))), "unknown persisted icon rejected");
        using var malformedMap = JsonDocument.Parse("{\"DeepDungeon\":4,\"CurrentLevel\":100,\"SchemaVersion\":2,\"FloorSets\":[{\"Floors\":[{\"Number\":78,\"MapData\":{\"RoomIds\":[]}}]}]}");
        Equal(false, SaveSlot.IsValid(malformedMap.RootElement.Deserialize<SaveSlot>()), "short map array rejected before rendering");
        return count;
    }
}
