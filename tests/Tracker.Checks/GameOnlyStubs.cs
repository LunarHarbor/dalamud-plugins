namespace DeepDungeonTracker;

// Game-only timer stubs.
public sealed class BossStatusTimerData { public void TimerEnd() => throw new NotSupportedException(); }
public sealed class BossStatusTimerManager
{
    public BossStatusTimerManager(BossStatusTimerData data, Action action) { }
}
