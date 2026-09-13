using System.Text.Json;
using DeepDungeonTracker;

internal static class CaptureSetOutcomeChecks
{
    public static int Run()
    {
        var checks = 0;
        void Equal<T>(T expected, T actual, string label)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception($"{label}: expected {expected}, got {actual}");
            checks++;
        }
        SaveSlot NewRun(int first, int last, bool boss, DeepDungeon dungeon = DeepDungeon.PilgrimsTraverse)
        {
            var save = new SaveSlot(dungeon, dungeon == DeepDungeon.EurekaOrthos ? 60040 : 60041, 24,
                ScoreEngine.MaximumLevel(dungeon));
            save.MarkCurrentSchema();
            save.AddFloorSet(first);
            for (var floor = first; floor < last; floor++)
            {
                save.CurrentFloor()!.MarkCleared();
                save.AddFloor();
            }
            save.CurrentFloorSet()!.FirstFloor()!.EnemyKilled();
            save.CurrentFloorSet()!.FirstFloor()!.EnemyKilled();
            save.CurrentFloorSet()!.FirstFloor()!.EnemyKilled();
            if (boss)
            {
                save.CurrentFloor()!.EnemyKilled();
                save.CurrentFloor()!.MarkBossDefeated();
                save.CurrentFloorSet()!.MarkBossTime(TimeSpan.FromMinutes(10));
            }
            return save;
        }
        FloorSetTime Timer()
        {
            var timer = new FloorSetTime();
            timer.Start();
            timer.Synchronize(TimeSpan.FromMinutes(11));
            return timer;
        }

        var bossClear = NewRun(1, 10, true);
        var bossTimer = Timer();
        Equal(CaptureExitOutcome.Completed, CaptureSetOutcome.FinalizeExit(bossClear, bossTimer, false, false),
            "end10 observed boss clear completes on exit without generic duty callback");
        Equal(4, bossClear.Kills(), "successful exit retains all previous kills");
        Equal(3, bossClear.CurrentFloorSet()!.FirstFloor()!.Kills, "successful exit preserves earlier floor events");
        Equal(true, bossClear.CurrentFloorSet()!.Completed, "boss evidence is stored as completed on exit");
        Equal(false, bossClear.CurrentFloorSet()!.Failed, "successful exit is not stored as failed");
        Equal(true, SaveSlot.IsValid(bossClear), "boss-based exit remains replayable");
        var finalized = JsonSerializer.Serialize(bossClear);
        Equal(CaptureExitOutcome.Completed, CaptureSetOutcome.FinalizeExit(bossClear, Timer(), false, true),
            "later duplicate failure cannot replace finalized completion");
        Equal(finalized, JsonSerializer.Serialize(bossClear), "duplicate finalization does not rewrite events or timings");

        var unknown = NewRun(1, 10, false);
        Equal(CaptureExitOutcome.Interrupted, CaptureSetOutcome.FinalizeExit(unknown, Timer(), false, false),
            "unobserved outcome stays incomplete instead of becoming a failure");
        Equal(false, unknown.CurrentFloorSet()!.Completed, "unknown outcome earns no completion");
        Equal(false, unknown.CurrentFloorSet()!.Failed, "unknown outcome does not destroy continuation eligibility");
        Equal(3, unknown.Kills(), "unknown exit preserves observed kills");
        Equal(true, unknown.CaptureNotes.Count > 0, "unknown exit records missing evidence");

        var explicitFailure = NewRun(1, 10, true);
        Equal(CaptureExitOutcome.Failed, CaptureSetOutcome.FinalizeExit(explicitFailure, Timer(), false, true),
            "explicit failure takes precedence over boss evidence");
        Equal(false, explicitFailure.CurrentFloorSet()!.Completed, "failed exit gets no completed set");
        Equal(true, explicitFailure.CurrentFloorSet()!.Failed, "explicit failure is retained");
        Equal(0, explicitFailure.KOs, "caller retains responsibility for applying failure penalty once");
        Equal(CaptureExitOutcome.Failed, CaptureSetOutcome.FinalizeExit(explicitFailure, Timer(), true, false),
            "previous confirmed failure is not silently reclassified");

        var nativeSuccess = NewRun(9, 9, false);
        Equal(CaptureExitOutcome.Completed, CaptureSetOutcome.FinalizeExit(nativeSuccess, Timer(), true, false),
            "native success resolves missing final floor update on exit");
        Equal(10, nativeSuccess.CurrentFloorNumber(), "native completion supplies actual set endpoint");
        Equal(3, nativeSuccess.Kills(), "native completion invents no boss kills");
        Equal(true, SaveSlot.IsValid(nativeSuccess), "native success recovery remains valid");
        foreach (var dungeon in new[] { DeepDungeon.EurekaOrthos, DeepDungeon.PilgrimsTraverse })
        {
            var stone99 = NewRun(99, 99, true, dungeon);
            Equal(CaptureExitOutcome.Interrupted, CaptureSetOutcome.FinalizeExit(stone99, Timer(), false, false),
                $"{dungeon} floor99 boss is not proof of final100 completion");
            Equal(99, stone99.CurrentFloorNumber(), $"{dungeon} boss evidence invents no future floor");
        }
        return checks;
    }
}