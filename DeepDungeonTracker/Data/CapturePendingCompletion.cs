using System;

namespace DeepDungeonTracker;

public sealed class CapturePendingCompletion
{
    private readonly FloorSetTime Timer;
    private readonly string FileName;
    private readonly uint TerritoryType;
    private readonly DateTime ExitedAt;
    private bool CompletionApplied;
    private bool Persisted;

    public SaveSlot Save { get; }

    public CapturePendingCompletion(SaveSlot save, FloorSetTime timer, string fileName, uint territoryType, DateTime exitedAt)
    {
        this.Save = save ?? throw new ArgumentNullException(nameof(save));
        this.Timer = timer ?? throw new ArgumentNullException(nameof(timer));
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        this.FileName = fileName;
        this.TerritoryType = territoryType;
        this.ExitedAt = exitedAt;
    }

    public bool TryComplete(string directory, uint territoryType, DateTime now)
    {
        var elapsed = now - this.ExitedAt;
        if (this.Persisted || territoryType != this.TerritoryType || elapsed < TimeSpan.Zero ||
            elapsed > TimeSpan.FromSeconds(15) || this.Save.CurrentFloorSet() is not { Failed: false } set)
            return false;
        var checkpoint = LocalStream.Load<SaveSlot>(directory, this.FileName, SaveSlot.IsValid);
        if (checkpoint == null || !string.Equals(checkpoint.RunId, this.Save.RunId, StringComparison.Ordinal))
            return false;
        if (!this.CompletionApplied)
        {
            if (set.Completed || CaptureSetOutcome.FinalizeExit(this.Save, this.Timer, true, false) != CaptureExitOutcome.Completed)
                return false;
            this.CompletionApplied = true;
        }
        this.Save.UpdateCurrentFloorScore(ScoreEngine.Calculate(this.Save.Snapshot()).Total);
        LocalStream.Save(directory, this.FileName, this.Save).GetAwaiter().GetResult();
        this.Persisted = true;
        return true;
    }
}