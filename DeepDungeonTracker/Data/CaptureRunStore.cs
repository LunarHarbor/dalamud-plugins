using System;

namespace DeepDungeonTracker;

public sealed record CaptureStartResult(SaveSlot Save, string FileName, CaptureStartKind Kind);

// Shared by live capture and persisted lifecycle checks. Browsing selection never enters this path.
public static class CaptureRunStore
{
    public static CaptureStartResult Start(string directory, string characterKey,
        SaveSlotSelection.SaveSlotSelectionData? selection, DeepDungeon dungeon, int contentId,
        int floor, uint classJobId, int level, TimeSpan? elapsed, FloorSetTime timer)
    {
        ArgumentNullException.ThrowIfNull(timer);
        var fileName = selection?.DeepDungeon == dungeon
            ? SaveSlotSelection.GetSaveSlotFileName(characterKey, selection) : string.Empty;
        var selected = !string.IsNullOrEmpty(fileName);
        var previous = selected ? LocalStream.Load<SaveSlot>(directory, fileName, SaveSlot.IsValid) : null;
        var kind = CaptureResumePolicy.Classify(previous, dungeon, contentId, floor, classJobId, elapsed);
        timer.Start();
        SaveSlot save;
        if (kind == CaptureStartKind.Resume)
        {
            save = previous!;
            timer.Restore(save.CurrentFloorSet()!);
            save.Note("Tracking resumed within a set; events while the plugin was unloaded may be missing.");
        }
        else if (kind == CaptureStartKind.Continue)
        {
            save = previous!;
            var set = save.CurrentFloorSet()!;
            if (!set.Completed)
            {
                Archive(directory, save);
                var wasFailed = set.Failed;
                if (!set.Failed)
                {
                    timer.Restore(set);
                    CaptureFloorTransition.ReachCompletedSetEnd(save, timer);
                }
                set.ConfirmContinuation();
                save.Note("Entry to the next set confirmed the previous clear. Events missed before that entry could not be recovered.");
                if (wasFailed)
                    save.Note("An older capture incorrectly marked the preceding exit as failed. Its original capture was archived; any recorded KO penalty remains unverified.");
                timer.Start();
            }
            save.AddFloorSet(floor);
            save.ContentIdUpdate(contentId);
        }
        else if (kind == CaptureStartKind.Retry)
        {
            save = previous!;
            Archive(directory, save);
            save.ResetFloorSet();
            save.ContentIdUpdate(contentId);
        }
        else
        {
            if (previous != null) Archive(directory, previous);
            save = new(dungeon, contentId, classJobId, level);
            save.MarkCurrentSchema();
            save.AddFloorSet(floor);
            if (floor > 1)
                save.Note("Tracking began after floor 1; earlier floors and events are not in this capture.");
            if (floor % 10 != 1)
            {
                save.CurrentFloorSet()!.SetTimerKnown(false);
                save.Note("Tracking began partway through a set; earlier events are missing.");
            }
            if (!selected)
                save.Note("No game save slot was identified. This capture is preserved separately; associate it with the game slot in settings to retain it for later sets.");
        }
        return new(save, selected ? fileName : $"capture-{save.RunId}.json", kind);
    }

    private static void Archive(string directory, SaveSlot save) =>
        LocalStream.Save(directory, $"attempt-{Guid.NewGuid():N}.json", save).GetAwaiter().GetResult();
}
