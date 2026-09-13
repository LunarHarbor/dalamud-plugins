using System;

namespace DeepDungeonTracker;

public enum CaptureExitOutcome { Completed, Failed, Interrupted }

public static class CaptureSetOutcome
{
    public static CaptureExitOutcome FinalizeExit(SaveSlot save, FloorSetTime timer,
        bool completionObserved, bool confirmedFailure)
    {
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(timer);
        var set = save.CurrentFloorSet();
        if (set == null) return CaptureExitOutcome.Interrupted;
        if (set.Completed) return CaptureExitOutcome.Completed;
        if (set.Failed) return CaptureExitOutcome.Failed;

        var bossClearedFinalFloor = set.CurrentFloor() is { BossDefeated: true } floor && floor.IsLastFloor();
        var completed = !confirmedFailure && (completionObserved || bossClearedFinalFloor) &&
            CaptureFloorTransition.ReachCompletedSetEnd(save, timer);
        timer.Pause();
        save.CurrentFloor()?.TimeUpdate(timer.CurrentFloorTime);
        if (completed)
        {
            set.Complete(timer.TotalTime);
            return CaptureExitOutcome.Completed;
        }
        if (confirmedFailure)
        {
            set.Fail();
            return CaptureExitOutcome.Failed;
        }
        save.Note("Dungeon exit was observed without a confirmed completion or failure; the capture remains incomplete.");
        return CaptureExitOutcome.Interrupted;
    }
}