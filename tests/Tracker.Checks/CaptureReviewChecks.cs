using DeepDungeonTracker;

internal static class CaptureReviewChecks
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
        CaptureStartKind Kind(SaveSlot? previous, int floor, uint job = 24, int content = 60044,
            DeepDungeon dungeon = DeepDungeon.PilgrimsTraverse, TimeSpan? elapsed = null) =>
            CaptureResumePolicy.Classify(previous, dungeon, content, floor, job, elapsed);
        SaveSlot RunAt(int floor, int content = 60044, uint job = 24)
        {
            var save = new SaveSlot(DeepDungeon.PilgrimsTraverse, content, job, 100);
            save.MarkCurrentSchema();
            save.AddFloorSet(floor);
            return save;
        }

        Equal(CaptureStartKind.New, Kind(null, 41, content: 60045), "first installation at a set boundary starts a capture");
        Equal(CaptureStartKind.New, Kind(null, 57, content: 60046), "first installation during a set starts a capture");
        var slotOne = RunAt(31);
        slotOne.CurrentFloor()!.EnemyKilled();
        slotOne.CurrentFloor()!.TimeUpdate(TimeSpan.FromMinutes(4));
        Equal(CaptureStartKind.Resume, Kind(slotOne, 31, elapsed: TimeSpan.FromMinutes(5)), "observed same-slot in-progress capture resumes");
        Equal(CaptureStartKind.New, Kind(slotOne, 31, elapsed: TimeSpan.FromSeconds(1)), "new instance clock cannot reuse old attempt");
        Equal(CaptureStartKind.New, Kind(slotOne, 31), "unknown instance clock cannot claim matching attempt");
        Equal(CaptureStartKind.New, Kind(slotOne, 31, job: 19, elapsed: TimeSpan.FromMinutes(5)), "another job cannot inherit in-progress capture");
        Equal(CaptureStartKind.New, Kind(slotOne, 31, content: 60034, dungeon: DeepDungeon.EurekaOrthos,
            elapsed: TimeSpan.FromMinutes(5)), "another dungeon cannot inherit matching floor capture");
        Equal(CaptureStartKind.Continue, Kind(slotOne, 41, content: 60045), "observed same-slot next set proves traversal despite missed end updates");
        Equal(1, slotOne.Kills(), "capture classification does not alter existing slot events");

        var completed = RunAt(40);
        completed.CurrentFloorSet()!.SetPartySize(2);
        completed.CurrentFloorSet()!.Complete(TimeSpan.FromMinutes(20));
        Equal(CaptureStartKind.Continue, Kind(completed, 41, content: 60045), "same-slot next set continues history");
        Equal(CaptureStartKind.New, Kind(completed, 41, job: 19, content: 60045), "different job does not inherit completed history");
        Equal(CaptureStartKind.New, Kind(completed, 51, content: 60046), "skipped set cannot be invented");
        Equal(2, completed.CurrentFloorSet()!.PartySize, "classification preserves historical entry party size");
        var failed = RunAt(11, 60042);
        failed.CurrentFloorSet()!.Fail();
        Equal(CaptureStartKind.Retry, Kind(failed, 11, content: 60042), "eligible same-slot retry preserves earlier sets");
        Equal(CaptureStartKind.New, Kind(failed, 11, content: 60042, job: 19), "retry does not mix jobs");
        var challenge = RunAt(31);
        challenge.CurrentFloorSet()!.Fail();
        Equal(CaptureStartKind.New, Kind(challenge, 31), "failed challenge cannot become retry history");

        var gapRun = RunAt(31);
        gapRun.CurrentFloor()!.EnemyKilled();
        var gapTimer = new FloorSetTime();
        gapTimer.Start();
        gapTimer.Synchronize(TimeSpan.FromMinutes(3));
        Equal(true, CaptureFloorTransition.Advance(gapRun, gapTimer, 34), "missed transitions resume at observed floor");
        Equal(34, gapRun.CurrentFloorNumber(), "later events go to actual floor");
        Equal(1, gapRun.Kills(), "gap recovery invents no kills");
        Equal(0, gapRun.CurrentFloor()!.Kills, "actual floor starts with no invented events");
        Equal(3, gapRun.CurrentFloorSet()!.Floors.Count(f => f.Cleared), "director proves earlier floors were traversed");
        Equal(1, gapRun.CaptureNotes.Count, "missing floor evidence stays explicit");
        Equal(false, gapRun.CurrentFloorSet()!.TimeBonus, "gap recovery invents no speed award");
        Equal(false, gapRun.CurrentFloorSet()!.Floors.Any(f => f.BossDefeated), "gap recovery invents no boss result");
        Equal(false, CaptureFloorTransition.Advance(gapRun, gapTimer, 34), "duplicate transition has no effect");
        Equal(false, CaptureFloorTransition.Advance(gapRun, gapTimer, 41), "transition cannot cross set boundary");
        Equal(34, gapRun.CurrentFloorNumber(), "invalid transition leaves capture unchanged");
        Equal(true, SaveSlot.IsValid(gapRun), "recovered incomplete capture remains replayable");

        var pendingComplete = RunAt(99, content: 60050);
        var finalTimer = new FloorSetTime();
        finalTimer.Start();
        finalTimer.Synchronize(TimeSpan.FromMinutes(20));
        Equal(true, CaptureFloorTransition.ReachCompletedSetEnd(pendingComplete, finalTimer), "completion then exit resolves pending last floor");
        Equal(100, pendingComplete.CurrentFloorNumber(), "confirmed final duty reaches floor100");
        pendingComplete.CurrentFloorSet()!.Complete(finalTimer.TotalTime);
        pendingComplete.CurrentFloorSet()!.Fail();
        Equal(true, pendingComplete.CurrentFloorSet()!.Completed, "confirmed completion survives later exit failure fallback");
        Equal(false, pendingComplete.CurrentFloorSet()!.Failed, "confirmed completion is never recorded as failure");
        Equal(0, pendingComplete.Kills(), "completion does not invent missed final boss kills");
        Equal(true, pendingComplete.CaptureNotes.Count > 0, "missing final floor observations remain explicit");
        Equal(true, SaveSlot.IsValid(pendingComplete), "pending completion recovery preserves valid save");
        Equal(false, CaptureFloorTransition.ReachCompletedSetEnd(pendingComplete, finalTimer), "duplicate completion does not change finalized set");

        var directory = Path.Combine(Path.GetTempPath(), "ddt-capture-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var key = "Synthetic Adventurer-SyntheticWorld";
            var selections = new SaveSlotSelection(directory);
            selections.SetSelectionData(DeepDungeon.PilgrimsTraverse, 1);
            selections.Save(key);
            var firstPath = SaveSlotSelection.GetSaveSlotFileName(key, selections.GetCaptureSelectionData());
            LocalStream.Save(directory, firstPath, slotOne).GetAwaiter().GetResult();
            selections.SetSelectionData(DeepDungeon.PilgrimsTraverse, 2);
            selections.Save(key);
            var secondPath = SaveSlotSelection.GetSaveSlotFileName(key, selections.GetCaptureSelectionData());
            LocalStream.Save(directory, secondPath, completed).GetAwaiter().GetResult();
            Equal(false, firstPath == secondPath, "two existing game slots have distinct capture destinations");
            var reload = new SaveSlotSelection(directory);
            Equal(2, reload.GetSelectionData(key)!.SaveSlotNumber, "last viewed slot survives reload for browsing");
            Equal(null, reload.GetCaptureSelectionData(), "persisted last slot is not live duty identity");
            var orphan = RunAt(31);
            LocalStream.Save(directory, $"capture-{orphan.RunId}.json", orphan).GetAwaiter().GetResult();
            Equal(slotOne.RunId, LocalStream.Load<SaveSlot>(directory, firstPath, SaveSlot.IsValid)!.RunId,
                "installing in unknown slot leaves first existing capture intact");
            Equal(completed.RunId, LocalStream.Load<SaveSlot>(directory, secondPath, SaveSlot.IsValid)!.RunId,
                "installing in unknown slot leaves second existing capture intact");
        }
        finally
        {
            var resolved = Path.GetFullPath(directory);
            if (!resolved.StartsWith(Path.Combine(Path.GetTempPath(), "ddt-capture-checks-"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unexpected temporary capture check directory.");
            Directory.Delete(resolved, true);
        }
        return count;
    }
}
