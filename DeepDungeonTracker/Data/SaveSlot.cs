using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;

namespace DeepDungeonTracker;

public class SaveSlot(DeepDungeon deepDungeon = DeepDungeon.None, int contentId = 0, uint classJobId = 0, int currentLevel = 0)
{
    [JsonInclude]
    public int SchemaVersion { get; private set; }

    [JsonInclude]
    public string RunId { get; private set; } = Guid.NewGuid().ToString("N");

    [JsonInclude]
    public Collection<string> CaptureNotes { get; private set; } = [];

    public void MarkCurrentSchema() => this.SchemaVersion = 2;

    public void Note(string note) { if (!this.CaptureNotes.Contains(note)) this.CaptureNotes.Add(note); }

    public ScoreRun Snapshot() => new(this.DeepDungeon, this.CurrentLevel, this.AetherpoolArm,
        this.AetherpoolArmor, this.KOs, this.FloorSets.Select((set, i) =>
        {
            var legacyComplete = this.SchemaVersion < 2 && !set.Failed && i < this.FloorSets.Count - 1;
            return new ScoreSet(set.PartySize, set.Completed || legacyComplete,
                set.TimeBonus, set.Floors.Select((f, j) => new ScoreFloor(f.Number,
                    f.Cleared || (this.SchemaVersion < 2 && (legacyComplete || j < set.Floors.Count - 1)),
                    f.BossDefeated || (this.SchemaVersion < 2 && f.Kills > 0 && ScoreEngine.IsBossFloor(this.DeepDungeon, f.Number)),
                    f.Kills, f.Mimics, f.Mandragoras, f.NPCs, f.DreadBeasts,
                    f.Coffers.Count, f.Enchantments.Count, f.Traps.Count, f.Deaths, f.Map,
                    f.LocalDeaths, f.Candles, f.Pomanders.Count(p => p == Pomander.JuniperIncense))).ToArray());
        }).ToArray(), this.SchemaVersion);

    public static bool IsValid(SaveSlot? save)
    {
        if (save == null || save.SchemaVersion is < 0 or > 2 || string.IsNullOrWhiteSpace(save.RunId) ||
            save.CaptureNotes == null || save.CaptureNotes.Any(note => note == null) || save.FloorSets == null)
            return false;
        try
        {
            foreach (var set in save.FloorSets)
            {
                if (set == null || set.Floors == null || (set.Completed && set.Failed) ||
                    set.ObservedScore is < 0 || set.ObservedKills is < 0 || set.BossClearTime < TimeSpan.Zero ||
                    (set.BossStatusTimerData is { } timers && !timers.IsValid()))
                    return false;
                foreach (var floor in set.Floors)
                {
                    if (floor == null || floor.Time < TimeSpan.Zero || floor.Time > TimeSpan.FromHours(1) ||
                        floor.CairnOfPassageKills < 0 || floor.RegenPotions < 0 ||
                        floor.MapData == null || floor.MapData.RoomIds == null ||
                        floor.MapData.RoomIds.Count != MapData.Length * MapData.Length || !Enum.IsDefined(floor.MapData.FloorType) ||
                        floor.Coffers == null || floor.Coffers.Any(item => !Enum.IsDefined(item)) ||
                        floor.Enchantments == null || floor.Enchantments.Any(item => !Enum.IsDefined(item)) ||
                        floor.EnchantmentsSerenized == null || floor.EnchantmentsSerenized.Any(item => !Enum.IsDefined(item)) ||
                        floor.Traps == null || floor.Traps.Any(item => !Enum.IsDefined(item)) ||
                        floor.Pomanders == null || floor.Pomanders.Any(item => !Enum.IsDefined(item)))
                        return false;
                }
            }
            _ = ScoreEngine.Calculate(save.Snapshot()).Total;
            _ = save.Score();
            _ = save.Time();
            return true;
        }
        catch (ArgumentException) { return false; }
        catch (OverflowException) { return false; }
    }

    [JsonInclude]
    public DeepDungeon DeepDungeon { get; private set; } = deepDungeon;

    [JsonInclude]
    public int ContentId { get; private set; } = contentId;

    [JsonInclude]
    public uint ClassJobId { get; private set; } = classJobId;

    [JsonInclude]
    public int CurrentLevel { get; private set; } = currentLevel;

    [JsonInclude]
    public int AetherpoolArm { get; private set; }

    [JsonInclude]
    public int AetherpoolArmor { get; private set; }

    [JsonIgnore]
    private int _KOs;

    [JsonInclude]
    public int KOs { get { return this._KOs; } private set { this._KOs = Math.Min(value, 99); } }

    [JsonInclude]
    public Collection<FloorSet> FloorSets { get; private set; } = [];

    public void KOed() => this.KOs++;

    public TimeSpan Time() => new(this.FloorSets.Sum(x => x.Time().Ticks));

    public int Score() => this.FloorSets.Sum(x => x.Score());

    public void UpdateCurrentFloorScore(int total)
    {
        if (this.CurrentFloor() is { } floor)
            floor.ScoreUpdate(checked(total - (this.Score() - floor.Score)));
    }

    public int Kills() => this.FloorSets.Sum(x => x.Kills());

    public int CairnOfPassageKills() => this.FloorSets.Sum(x => x.CairnOfPassageKills());

    public int Mimics() => this.FloorSets.Sum(x => x.Mimics());

    public int Mandragoras() => this.FloorSets.Sum(x => x.Mandragoras());

    public int NPCs() => this.FloorSets.Sum(x => x.NPCs());

    public int DreadBeasts() => this.FloorSets.Sum(x => x.DreadBeasts());

    public int Coffers() => this.FloorSets.Sum(x => x.Coffers());

    public int Enchantments() => this.FloorSets.Sum(x => x.Enchantments());

    public int Traps() => this.FloorSets.Sum(x => x.Traps());

    public int Pomanders() => this.FloorSets.Sum(x => x.Pomanders());

    public int Deaths() => this.FloorSets.Sum(x => x.Deaths());

    public int RegenPotions() => this.FloorSets.Sum(x => x.RegenPotions());

    public int Potsherds() => this.FloorSets.Sum(x => x.Potsherds());

    public int Lurings() => this.FloorSets.Sum(x => x.Lurings());

    public int Maps() => this.FloorSets.Sum(x => x.Maps());

    public int HallOfFallacies() => this.FloorSets.Sum(x => x.HallOfFallacies());

    public int ThreeRoomsFloor() => this.FloorSets.Sum(x => x.ThreeRoomsFloor());

    public int FourRoomsFloor() => this.FloorSets.Sum(x => x.FourRoomsFloor());

    public int FiveRoomsFloor() => this.FloorSets.Sum(x => x.FiveRoomsFloor());

    public int SixRoomsFloor() => this.FloorSets.Sum(x => x.SixRoomsFloor());

    public int SevenRoomsFloor() => this.FloorSets.Sum(x => x.SevenRoomsFloor());

    public int EightRoomsFloor() => this.FloorSets.Sum(x => x.EightRoomsFloor());

    public int TimeBonuses() => this.FloorSets.Sum(x => x.TimeBonus ? 1 : 0);

    public FloorSet? CurrentFloorSet() => this.FloorSets.LastOrDefault();

    public Floor? CurrentFloor() => this.CurrentFloorSet()?.CurrentFloor();

    public void AddFloor() => this.CurrentFloorSet()?.AddFloor(this.CurrentFloorNumber() + 1);

    public void AddFloorSet(int floorNumber)
    {
        var floorSet = new FloorSet();
        this.FloorSets.Add(floorSet);
        floorSet.AddFloor(floorNumber);
    }

    public void ResetFloorSet()
    {
        if (this.FloorSets.Count == 0)
            return;

        var firstFloorNumber = this.CurrentFloorSet()?.FirstFloor()?.Number ?? 0;
        this.FloorSets.RemoveAt(this.FloorSets.Count - 1);
        this.AddFloorSet(firstFloorNumber);
    }

    public void ContentIdUpdate(int contentId) => this.ContentId = contentId;

    public int StartingFloorNumber() => this.FloorSets?.FirstOrDefault()?.FirstFloor()?.Number ?? 0;

    public int CurrentFloorNumber() => this.CurrentFloor()?.Number ?? 0;

    public void CurrentLevelUpdate(int level) { if (level > 0) this.CurrentLevel = level; }

    public void AetherpoolUpdate(int arm, int armor)
    {
        this.AetherpoolArm = Math.Max(this.AetherpoolArm, arm);
        this.AetherpoolArmor = Math.Max(this.AetherpoolArmor, armor);
    }

    public bool IsSpecialBossFloor(Floor? floor) => (this.DeepDungeon is DeepDungeon.EurekaOrthos or DeepDungeon.PilgrimsTraverse && floor?.Number == 99);

    public void AdditionalKills(int flootSetIndex, int kills)
    {
        if (kills == 0)
            return;

        var flootSet = flootSetIndex < this.FloorSets.Count ? this.FloorSets[flootSetIndex] : null;
        if (flootSet == null)
            return;

        var firstFloor = flootSet.FirstFloor();
        for (var i = 0; i < kills; i++)
            firstFloor?.EnemyKilled();

        for (var i = 0; i > kills; i--)
            firstFloor?.EnemyUnkilled();
    }

    public void AdditionalMimicKills(int flootSetIndex, int kills)
    {
        if (kills <= 0)
            return;

        var flootSet = flootSetIndex < this.FloorSets.Count ? this.FloorSets[flootSetIndex] : null;
        if (flootSet == null)
            return;

        var firstFloor = flootSet.FirstFloor();
        for (var i = 0; i < kills; i++)
            firstFloor?.MimicKilled();
    }

    static public void Copy(SaveSlot? source, SaveSlot? dest, int maxFloor)
    {
        if (source == null || dest == null)
            return;

        dest.DeepDungeon = source.DeepDungeon;
        dest.ContentId = source.ContentId;
        dest.ClassJobId = source.ClassJobId;
        dest.CurrentLevel = source.CurrentLevel;
        dest.AetherpoolArm = source.AetherpoolArm;
        dest.AetherpoolArmor = source.AetherpoolArmor;
        dest.KOs = source.KOs;
        dest.SchemaVersion = source.SchemaVersion;
        dest.RunId = source.RunId;
        dest.CaptureNotes = new(source.CaptureNotes);
        dest.FloorSets = new(source.FloorSets.ToList().Where(x => x.FirstFloor()?.Number <= maxFloor).ToList());
    }
}
