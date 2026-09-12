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

    public TimeSpan TotalTime => this.StartTime is { } start
        ? new(Math.Clamp(((this.PauseTime ?? DateTime.Now) - start).Ticks, 0, FloorSetTime.InstanceTime.Ticks))
        : TimeSpan.Zero;

    public TimeSpan CurrentFloorTime => new(Math.Max(0, this.TotalTime.Ticks - this.PreviousFloorsTime.Sum(x => x.Ticks))
        / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond);

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
