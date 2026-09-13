using DeepDungeonTracker;

internal static class CapturePendingCompletionChecks
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
        void ThrowsStorage(Action action, string label)
        {
            try { action(); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { checks++; return; }
            throw new Exception(label + ": expected a storage exception");
        }
        SaveSlot NewRun()
        {
            var save = new SaveSlot(DeepDungeon.PilgrimsTraverse, 60041, 24, 100);
            save.MarkCurrentSchema();
            save.AddFloorSet(1);
            save.CurrentFloor()!.EnemyKilled();
            save.CurrentFloor()!.EnemyKilled();
            for (var floor = 1; floor < 10; floor++)
            {
                save.CurrentFloor()!.MarkCleared();
                save.AddFloor();
            }
            return save;
        }
        FloorSetTime Timer()
        {
            var timer = new FloorSetTime();
            timer.Start();
            timer.Synchronize(TimeSpan.FromMinutes(12));
            timer.Pause();
            return timer;
        }

        var directory = Path.Combine(Path.GetTempPath(), "ddt-pending-completion-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var exitedAt = new DateTime(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);
            var first = NewRun();
            var firstTimer = Timer();
            CaptureSetOutcome.FinalizeExit(first, firstTimer, false, false);
            LocalStream.Save(directory, "slot-one.json", first).GetAwaiter().GetResult();
            var pending = new CapturePendingCompletion(first, firstTimer, "slot-one.json", 1281, exitedAt);
            var second = NewRun();
            second.CurrentFloor()!.EnemyKilled();
            LocalStream.Save(directory, "slot-two.json", second).GetAwaiter().GetResult();
            var browsed = LocalStream.Load<SaveSlot>(directory, "slot-two.json", SaveSlot.IsValid)!;
            var secondBytes = File.ReadAllText(Path.Combine(directory, "slot-two.json"));
            Equal(false, ReferenceEquals(pending.Save, browsed), "browsing another capture does not replace pending source");
            Equal(true, ReferenceEquals(pending.Save, first), "pending completion retains original capture reference");
            Equal(false, pending.TryComplete(directory, 1282, exitedAt.AddSeconds(1)), "wrong-territory completion is rejected");
            Equal(false, pending.TryComplete(directory, 1281, exitedAt.AddSeconds(16)), "completion after15seconds is rejected");
            Equal(false, pending.TryComplete(directory, 1281, exitedAt.AddSeconds(-1)), "callback before recorded exit is rejected");
            Equal(false, first.CurrentFloorSet()!.Completed, "rejected callbacks do not change outcome");
            Equal(true, pending.TryComplete(directory, 1281, exitedAt.AddSeconds(2)), "matching late completion is persisted");
            var completed = LocalStream.Load<SaveSlot>(directory, "slot-one.json", SaveSlot.IsValid)!;
            Equal(true, completed.CurrentFloorSet()!.Completed, "late callback finalizes original file");
            Equal(2, completed.Kills(), "late completion retains all observed kills");
            Equal(ScoreEngine.Calculate(completed.Snapshot()).Total, completed.Score(), "late completion persists recomputed total");
            Equal(secondBytes, File.ReadAllText(Path.Combine(directory, "slot-two.json")), "browsed second slot remains byte-for-byte unchanged");
            Equal(false, browsed.CurrentFloorSet()!.Completed, "browsed second run is not completed by old callback");
            var firstBytes = File.ReadAllText(Path.Combine(directory, "slot-one.json"));
            Equal(false, pending.TryComplete(directory, 1281, exitedAt.AddSeconds(3)), "successful duplicate callback is ignored");
            Equal(firstBytes, File.ReadAllText(Path.Combine(directory, "slot-one.json")), "duplicate writes no changes");

            var failed = NewRun();
            failed.CurrentFloorSet()!.Fail();
            LocalStream.Save(directory, "failed.json", failed).GetAwaiter().GetResult();
            var failedBytes = File.ReadAllText(Path.Combine(directory, "failed.json"));
            var failedPending = new CapturePendingCompletion(failed, Timer(), "failed.json", 1281, exitedAt);
            Equal(false, failedPending.TryComplete(directory, 1281, exitedAt.AddSeconds(2)), "explicit failure rejects later completion");
            Equal(failedBytes, File.ReadAllText(Path.Combine(directory, "failed.json")), "rejected failure callback writes nothing");
            var alreadyCompleted = NewRun();
            alreadyCompleted.CurrentFloorSet()!.Complete(TimeSpan.FromMinutes(12));
            LocalStream.Save(directory, "complete.json", alreadyCompleted).GetAwaiter().GetResult();
            Equal(false, new CapturePendingCompletion(alreadyCompleted, Timer(), "complete.json", 1281, exitedAt)
                .TryComplete(directory, 1281, exitedAt.AddSeconds(2)), "already completed input is rejected");

            var missing = NewRun();
            Equal(false, new CapturePendingCompletion(missing, Timer(), "missing.json", 1281, exitedAt)
                .TryComplete(directory, 1281, exitedAt.AddSeconds(2)), "missing checkpoint rejects delayed completion");
            Equal(false, missing.CurrentFloorSet()!.Completed, "missing checkpoint does not mutate original run");
            Equal(false, File.Exists(Path.Combine(directory, "missing.json")), "missing checkpoint is not recreated");

            var replaced = NewRun();
            LocalStream.Save(directory, "replaced.json", replaced).GetAwaiter().GetResult();
            var replacedPending = new CapturePendingCompletion(replaced, Timer(), "replaced.json", 1281, exitedAt);
            LocalStream.Save(directory, "replaced.json", NewRun()).GetAwaiter().GetResult();
            var replacementBytes = File.ReadAllText(Path.Combine(directory, "replaced.json"));
            Equal(false, replacedPending.TryComplete(directory, 1281, exitedAt.AddSeconds(2)), "reassigned slot rejects previous run callback");
            Equal(false, replaced.CurrentFloorSet()!.Completed, "reassigned slot does not mutate previous run");
            Equal(replacementBytes, File.ReadAllText(Path.Combine(directory, "replaced.json")), "reassigned slot remains byte-for-byte unchanged");

            var retrySave = NewRun();
            LocalStream.Save(directory, "retry.json", retrySave).GetAwaiter().GetResult();
            var retry = new CapturePendingCompletion(retrySave, Timer(), "retry.json", 1281, exitedAt);
            var blockedBackup = Path.Combine(directory, "retry.json.bak");
            Directory.CreateDirectory(blockedBackup);
            ThrowsStorage(() => retry.TryComplete(directory, 1281, exitedAt.AddSeconds(2)), "storage failure reaches caller");
            Directory.Delete(blockedBackup);
            Equal(true, retry.TryComplete(directory, 1281, exitedAt.AddSeconds(3)), "failed completion write can retry within bounded window");
            Equal(true, LocalStream.Load<SaveSlot>(directory, "retry.json", SaveSlot.IsValid)!.CurrentFloorSet()!.Completed,
                "retried completion reaches pinned filename");

            var retryReplacedSave = NewRun();
            LocalStream.Save(directory, "retry-replaced.json", retryReplacedSave).GetAwaiter().GetResult();
            var retryReplaced = new CapturePendingCompletion(retryReplacedSave, Timer(), "retry-replaced.json", 1281, exitedAt);
            var retryReplacedBackup = Path.Combine(directory, "retry-replaced.json.bak");
            Directory.CreateDirectory(retryReplacedBackup);
            ThrowsStorage(() => retryReplaced.TryComplete(directory, 1281, exitedAt.AddSeconds(2)), "replacement test first write fails");
            Directory.Delete(retryReplacedBackup);
            LocalStream.Save(directory, "retry-replaced.json", NewRun()).GetAwaiter().GetResult();
            var retryReplacementBytes = File.ReadAllText(Path.Combine(directory, "retry-replaced.json"));
            Equal(false, retryReplaced.TryComplete(directory, 1281, exitedAt.AddSeconds(3)), "retry checks the current destination run identity again");
            Equal(retryReplacementBytes, File.ReadAllText(Path.Combine(directory, "retry-replaced.json")), "retry preserves replacement capture");
        }
        finally
        {
            var resolved = Path.GetFullPath(directory);
            if (!resolved.StartsWith(Path.Combine(Path.GetTempPath(), "ddt-pending-completion-"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unexpected temporary pending-completion check directory.");
            Directory.Delete(resolved, true);
        }
        return checks;
    }
}