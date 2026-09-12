using System;
using System.Collections.Generic;
using System.Linq;

namespace DeepDungeonTracker;

public class FloorSetTime
{
    private static TimeSpan InstanceTime => new(0, 59, 59);

    private DateTime? StartTime { get; set; }

    private DateTime? PauseTime { get; set; }

    public ICollection<TimeSpan> PreviousFloorsTime { get; } = [];

    public TimeSpan TotalTime => new(Math.Min(((this.PauseTime ?? DateTime.Now) - (this.StartTime ?? DateTime.Now)).Ticks, FloorSetTime.InstanceTime.Ticks));

    public TimeSpan CurrentFloorTime => new((this.TotalTime - new TimeSpan(this.PreviousFloorsTime.Sum(x => x.Ticks))).Ticks / 10000000 * 10000000);

    public TimeSpan Average => new(this.TotalTime.Ticks / (this.PreviousFloorsTime.Count + 1));

    public void Start()
    {
        this.StartTime = DateTime.Now;
        this.PauseTime = null;
        this.PreviousFloorsTime.Clear();
    }

    public void Synchronize(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero || elapsed > InstanceTime + TimeSpan.FromSeconds(1) || this.PauseTime.HasValue) return;
        this.StartTime = DateTime.Now - elapsed;
    }

    public void Restore(FloorSet set)
    {
        this.Start();
        foreach (var floor in set.Floors.Take(Math.Max(0, set.Floors.Count - 1)))
            this.PreviousFloorsTime.Add(floor.Time);
        this.Synchronize(set.Time());
    }

    public void Pause()
    {
        if (!this.PauseTime.HasValue)
            this.PauseTime = DateTime.Now;
    }

    public TimeSpan AddFloor()
    {
        var time = new TimeSpan(this.CurrentFloorTime.Ticks);
        this.PreviousFloorsTime.Add(time);
        return time;
    }
}
