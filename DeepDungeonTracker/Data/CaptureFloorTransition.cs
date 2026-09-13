using System;

namespace DeepDungeonTracker;

public static class CaptureFloorTransition
{
    public static bool ReachCompletedSetEnd(SaveSlot save, FloorSetTime timer)
    {
        if (save.CurrentFloorSet() is not { Completed: false, Failed: false }) return false;
        var end = (save.CurrentFloorNumber() - 1) / 10 * 10 + 10;
        if (save.CurrentFloorNumber() == end) return true;
        save.Note("The set cleared before its final floor update was captured. Missing floor events could not be recovered.");
        return Advance(save, timer, end);
    }

    public static bool Advance(SaveSlot save, FloorSetTime timer, int observedFloor)
    {
        var current = save.CurrentFloorNumber();
        if (save.CurrentFloorSet() is not { Completed: false, Failed: false } || observedFloor <= current ||
            observedFloor > ScoreEngine.MaximumFloor(save.DeepDungeon) || (observedFloor - 1) / 10 != (current - 1) / 10)
            return false;
        if (observedFloor > current + 1)
            save.Note($"Floor transitions {current} to {observedFloor} were missed. Intervening floors contain no event observations; their elapsed time is combined on floor {current}.");
        while (save.CurrentFloorNumber() < observedFloor)
        {
            save.CurrentFloor()!.MarkCleared();
            save.CurrentFloor()!.TimeUpdate(timer.AddFloor());
            save.UpdateCurrentFloorScore(ScoreEngine.Calculate(save.Snapshot()).Total);
            save.AddFloor();
        }
        return true;
    }
}
