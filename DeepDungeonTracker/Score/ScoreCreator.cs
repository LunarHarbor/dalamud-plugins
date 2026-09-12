namespace DeepDungeonTracker;

public static class ScoreCreator
{
    public static Score? Create(SaveSlot saveSlot, bool isDutyComplete)
    {
        return saveSlot != null && saveSlot.DeepDungeon != DeepDungeon.None
            ? new Score(saveSlot, isDutyComplete) : null;
    }
}
