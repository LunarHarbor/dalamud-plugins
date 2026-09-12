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
        if (set.Completed && previous.CurrentFloorNumber() + 1 == floor && floor % 10 == 1)
            return CaptureStartKind.Continue;
        if (set.Failed && set.FirstFloor()?.Number == floor && floor < ScoreEngine.ChallengeStarts(dungeon))
            return CaptureStartKind.Retry;
        return CaptureStartKind.New;
    }
}