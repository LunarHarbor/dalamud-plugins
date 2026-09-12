using System.Collections.Generic;

namespace DeepDungeonTracker;

public sealed class DeathLedger
{
    private readonly HashSet<uint> Recorded = [];
    private readonly HashSet<uint> SeenDead = [];
    public bool Record(uint id) => this.Recorded.Add(id);
    public void Observe(uint id, bool dead)
    {
        if (dead && this.Recorded.Contains(id)) this.SeenDead.Add(id);
        else if (!dead && this.SeenDead.Remove(id)) this.Recorded.Remove(id);
    }
    public void Clear() { this.Recorded.Clear(); this.SeenDead.Clear(); }
}
