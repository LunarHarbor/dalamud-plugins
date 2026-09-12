using DeepDungeonTracker;

public static class TimerReviewChecks
{
    public static int Run()
    {
        var timer = new FloorSetTime();
        if (timer.TotalTime != TimeSpan.Zero || timer.CurrentFloorTime != TimeSpan.Zero)
            throw new Exception("An unstarted timer must be zero.");
        timer.Start();
        timer.PreviousFloorsTime.Add(TimeSpan.FromMinutes(10));
        timer.Synchronize(TimeSpan.FromMinutes(9));
        if (timer.CurrentFloorTime != TimeSpan.Zero)
            throw new Exception("A corrected duty clock must not produce a negative floor duration.");
        timer.Synchronize(TimeSpan.FromMinutes(11));
        if (timer.CurrentFloorTime < TimeSpan.FromMinutes(1))
            throw new Exception("Floor timing must recover when the duty clock advances.");
        return 3;
    }
}
