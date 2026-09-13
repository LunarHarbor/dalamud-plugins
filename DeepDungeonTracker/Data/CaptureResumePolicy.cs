using System;

namespace DeepDungeonTracker;

public enum CaptureStartKind { New, Resume, Continue, Retry }

public static class CaptureResumePolicy
{
    public static CaptureStartKind Classify(SaveSlot? previous, DeepDungeon dungeon, int contentId,
        int floor, uint classJobId, TimeSpan? elapsed)
    {
        if (previous == null || previous.DeepDungeon != dungeon || previous.ClassJobId != classJobId ||
            previous.CurrentFloorSet() is not { } set) return CaptureStartKind.New;
        if (!set.Completed && !set.Failed && previous.ContentId == contentId &&
            previous.CurrentFloorNumber() == floor && elapsed.HasValue &&
            elapsed.Value + TimeSpan.FromSeconds(5) >= set.Time()) return CaptureStartKind.Resume;
        // Called only with a capture loaded for the freshly observed game slot.
        // Entry to the following set proves the prior clear, even if its last updates were missed.
        if ((!set.Failed || (!set.FailureObserved && previous.CurrentFloor()?.BossDefeated == true)) &&
            (previous.CurrentFloorNumber() - 1) / 10 + 1 == (floor - 1) / 10 && floor % 10 == 1)
            return CaptureStartKind.Continue;
        if (set.Failed && set.FirstFloor()?.Number == floor && floor < ScoreEngine.ChallengeStarts(dungeon))
            return CaptureStartKind.Retry;
        return CaptureStartKind.New;
    }
}
