using System;

namespace DeepDungeonTracker;

public sealed class Score(SaveSlot saveSlot, bool isDutyComplete)
{
    public ScoreBreakdown? Breakdown { get; private set; }
    public string? UnavailableReason { get; private set; }
    public int StartingFloorNumber => saveSlot.StartingFloorNumber();
    public int CurrentFloorNumber => saveSlot.CurrentFloorNumber();
    public int TotalReachedFloors => CurrentFloorNumber - StartingFloorNumber + 1;
    public bool IsDutyComplete => saveSlot.CurrentFloorSet()?.Completed ?? isDutyComplete;
    public int CurrentLevel => saveSlot.CurrentLevel;
    public int AetherpoolArm => saveSlot.AetherpoolArm;
    public int AetherpoolArmor => saveSlot.AetherpoolArmor;
    public int BaseScore => Breakdown is { BonusUnits: <= 0 } b ? b.Bonus : 0;
    public int CharacterScore => Breakdown?.Character ?? 0;
    public int FloorScore => Breakdown?.Floors ?? 0;
    public int MapScore => Breakdown?.Maps ?? 0;
    public int CofferScore => Breakdown?.Coffers ?? 0;
    public int NPCScore => Breakdown?.NPCs ?? 0;
    public int DreadBeastScore => Breakdown?.DreadBeasts ?? 0;
    public int MimicgoraScore => Breakdown?.Mimics ?? 0;
    public int EnchantmentScore => Breakdown?.Enchantments ?? 0;
    public int TrapScore => Breakdown?.Traps ?? 0;
    public int TimeBonusScore => Breakdown?.Time ?? 0;
    public int DeathScore => Breakdown?.Deaths ?? 0;
    public int NonKillScore => TotalScore - KillScore;
    public int KillScore => Breakdown?.Kills ?? 0;
    public int TotalScore => Breakdown?.Total ?? 0;

    public void TotalScoreCalculation(bool calculateScore, ScoreCalculationType calculationType)
    {
        Breakdown = null;
        UnavailableReason = null;
        if (!calculateScore) { UnavailableReason = "Capture is not active."; return; }
        try
        {
            var run = saveSlot.Snapshot();
            int? projected = calculationType switch
            {
                ScoreCalculationType.ScoreWindowFloor => CurrentFloorNumber < ScoreEngine.ChallengeStarts(run.Dungeon)
                    ? ScoreEngine.ChallengeStarts(run.Dungeon) - 1 : ScoreEngine.MaximumFloor(run.Dungeon),
                ScoreCalculationType.LastFloor => ScoreEngine.MaximumFloor(run.Dungeon),
                _ => null,
            };
            if (projected.HasValue)
                run = run with { Arm = 99, Armor = 99, Level = ScoreEngine.MaximumLevel(run.Dungeon) };
            var result = ScoreEngine.Calculate(run, projected);
            _ = result.Total;
            Breakdown = result;
        }
        catch (ArgumentException e) { UnavailableReason = e.Message; }
        catch (OverflowException) { UnavailableReason = "The capture contains invalid score totals."; }
    }
}
