using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using System.Text.Json;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;

namespace DeepDungeonTracker;

public sealed unsafe class DataCommon : IDisposable
{
    private readonly DeathLedger RecordedDeaths = new();
    private DateTime NextCheckpoint = DateTime.MinValue;
    private readonly HashSet<uint> DefeatedBosses = [];
    public string? LastExportPath { get; private set; }
    public string? StorageError { get; private set; }

    private string CharacterName { get; set; } = string.Empty;

    private string ServerName { get; set; } = string.Empty;

    public bool IsTransferenceInitiated { get; private set; }

    public bool IsBronzeCofferOpened { get; set; }

    public bool IsSupportedParty { get; private set; }

    public bool EnableFlyTextScore { get; private set; }

    private bool IsCairnOfPassageActivated { get; set; }

    public bool ShowFloorSetTimeValues { get; private set; }

    private bool IsBossDead { get; set; }

    private bool WasMagiciteUsed { get; set; }

    private bool WasScoreWindowShown { get; set; }
    
    private bool IsEnchantmentsLoaded { get; set; }

    private HashSet<uint> CairnOfPassageKillIds { get; set; } = [];

    private Dictionary<uint, Enemy> NearbyEnemies { get; set; } = [];

    public DeepDungeon DeepDungeon { get; private set; }

    private DutyStatus DutyStatus { get; set; }

    public SaveSlot? CurrentSaveSlot { get; private set; }

    public SaveSlotSelection SaveSlotSelection { get; } = new();

    public FloorSetTime FloorSetTime { get; private set; } = new();

    public FloorEffect FloorEffect { get; private set; } = new();

    public BossStatusTimerManager? BossStatusTimerManager { get; set; }

    public Score? Score { get; private set; }

    private string CharacterKey => $"{this.CharacterName}-{this.ServerName}";

    public bool IsInDeepDungeonRegion => this.DeepDungeon != DeepDungeon.None;

    public int ContentId => this.CurrentSaveSlot?.ContentId ?? 0;

    public int TotalScore => this.Score?.TotalScore ?? 0;

    public bool IsLastFloor => this.CurrentSaveSlot?.CurrentFloor()?.IsLastFloor() ?? false;

    private bool IsEurekaOrthosFloor99 => this.DeepDungeon == DeepDungeon.EurekaOrthos &&
                                          this.CurrentSaveSlot?.CurrentFloorNumber() == 99;

    public bool IsSpecialBossFloor => this.IsEurekaOrthosFloor99 ||
        (this.DeepDungeon == DeepDungeon.PilgrimsTraverse && this.CurrentSaveSlot?.CurrentFloorNumber() == 99);

    public bool IsBossFloor => this.IsLastFloor || this.IsSpecialBossFloor;
    
    private static Pomander[] SharedPomanders =>
    [
        Pomander.Safety, Pomander.Sight, Pomander.Strength, Pomander.Steel, Pomander.Affluence, Pomander.Flight,
        Pomander.Alteration, Pomander.Purity, Pomander.Fortune, Pomander.Witching, Pomander.Serenity,
        Pomander.Intuition, Pomander.Raising
    ];

    public void Dispose()
    {
        this.Checkpoint(force: true);
        this.BossStatusTimerManager?.Dispose();
    }

    public void Checkpoint(bool force = false)
    {
        if (this.CurrentSaveSlot?.CurrentFloorSet() is not { Completed: false, Failed: false } ||
            !IsValidContent(this.CurrentSaveSlot.DeepDungeon, this.ContentId)) return;
        if (!force && DateTime.UtcNow < this.NextCheckpoint) return;
        this.NextCheckpoint = DateTime.UtcNow.AddSeconds(15);
        this.CurrentSaveSlot.CurrentFloor()?.TimeUpdate(this.FloorSetTime.CurrentFloorTime);
        this.SaveDeepDungeonData();
    }

    public void ResetCharacterData()
    {
        this.CharacterName = string.Empty;
        this.ServerName = string.Empty;
        this.FloorSetTime = new();
        this.FloorEffect = new();
    }

    public void EnteringDeepDungeon()
    {
        this.IsTransferenceInitiated = false;
        this.IsBronzeCofferOpened = false;
        this.IsSupportedParty = ServiceUtility.IsSupportedParty;
        this.RecordedDeaths.Clear();
        this.DefeatedBosses.Clear();
        this.EnableFlyTextScore = false;
        this.IsCairnOfPassageActivated = false;
        this.IsBossDead = false;
        this.WasMagiciteUsed = false;
        this.WasScoreWindowShown = false;
        this.IsEnchantmentsLoaded = false;
        this.NearbyEnemies = [];
        this.CairnOfPassageKillIds = [];
        this.DutyStatus = DutyStatus.None;
        this.CurrentSaveSlot?.ContentIdUpdate(0);
    }

    public void ExitingDeepDungeon()
    {
        this.EnableFlyTextScore = false;
        if (this.DutyStatus == DutyStatus.None && this.CurrentSaveSlot != null)
            this.DutyFailed();
        this.SaveDeepDungeonData();
    }

    public void EnteringCombat()
    {
        if (this.IsBossFloor)
            this.BossStatusTimerManager =
                this.CurrentSaveSlot?.CurrentFloorSet()?.StartBossStatusTimer(this.ExitingCombat);
    }

    public void ExitingCombat()
    {
        if (this.IsBossFloor)
            this.CurrentSaveSlot?.CurrentFloorSet()?.EndBossStatusTimer();
    }

    public static string GetSaveSlotFileName(string key, SaveSlotSelection.SaveSlotSelectionData? data) =>
        data != null ? $"{key}-dd{(int)data.DeepDungeon}s{data.SaveSlotNumber}.json" : string.Empty;

    public static string GetLastSaveFileName(string key, SaveSlotSelection.SaveSlotSelectionData? data) =>
        data != null ? $"{key}-dd{(int)data.DeepDungeon}s{data.SaveSlotNumber}Last.json" : string.Empty;

    public string GetSaveSlotFileName(SaveSlotSelection.SaveSlotSelectionData? data) =>
        DataCommon.GetSaveSlotFileName(this.CharacterKey, data);

    public string GetLastSaveFileName(SaveSlotSelection.SaveSlotSelectionData? data) =>
        DataCommon.GetLastSaveFileName(this.CharacterKey, data);

    private void SaveDeepDungeonData()
    {
        if (this.CurrentSaveSlot == null || !IsValidContent(this.CurrentSaveSlot.DeepDungeon, this.ContentId)) return;
        var data = this.SaveSlotSelection.GetSelectionData(this.CharacterKey);
        var fileName = data?.DeepDungeon == this.CurrentSaveSlot.DeepDungeon && data.SaveSlotNumber is 1 or 2
            ? DataCommon.GetSaveSlotFileName(this.CharacterKey, data)
            : $"capture-{this.CurrentSaveSlot.RunId}.json";
        try
        {
            LocalStream.Save(ServiceUtility.ConfigDirectory, fileName, this.CurrentSaveSlot).GetAwaiter().GetResult();
            this.StorageError = null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            this.StorageError = e.Message;
            Service.PluginLog.Error(e, "Could not save the dungeon capture.");
        }
    }

    public void ExportCapture()
    {
        if (this.CurrentSaveSlot == null) return;
        var capture = new
        {
            Rules = this.CurrentSaveSlot.DeepDungeon == DeepDungeon.PilgrimsTraverse ? ScoreEngine.PilgrimRulesVersion : ScoreEngine.RulesVersion,
            Run = this.CurrentSaveSlot.Snapshot(),
            Notes = this.CurrentSaveSlot.CaptureNotes.ToArray(),
            Results = this.CurrentSaveSlot.FloorSets.Select(s => new { s.ObservedScore, s.ObservedKills }).ToArray(),
        };
        var directory = Path.Combine(ServiceUtility.ConfigDirectory, "Exports");
        var file = $"run-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json";
        try
        {
            LocalStream.Save(directory, file, capture).GetAwaiter().GetResult();
            this.LastExportPath = Path.Combine(directory, file);
            this.StorageError = null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        { this.StorageError = e.Message; Service.PluginLog.Error(e, "Capture export failed."); }
    }

    public SaveSlot? LoadDeepDungeonData(bool showFloorSetTimeValues, string fileName)
    {
        this.ShowFloorSetTimeValues = showFloorSetTimeValues;
        this.CurrentSaveSlot = LocalStream.Load<SaveSlot>(ServiceUtility.ConfigDirectory, fileName);
        return this.CurrentSaveSlot;
    }

    public void LoadDeepDungeonData(bool showFloorSetTimeValues, bool ignoreDeepDungeonRegion = false)
    {
        var data = this.SaveSlotSelection.GetSelectionData(this.CharacterKey);
        var fileName = DataCommon.GetSaveSlotFileName(this.CharacterKey, data);
        this.CurrentSaveSlot =
            ((!ignoreDeepDungeonRegion && (data?.DeepDungeon == this.DeepDungeon)) || ignoreDeepDungeonRegion) &&
            (data?.SaveSlotNumber != 0)
                ? this.LoadDeepDungeonData(showFloorSetTimeValues, fileName)
                : new();
    }

    public void CheckForSaveSlotSelection()
    {
        var saveSlotNumber = NodeUtility.SaveSlotNumber(Service.GameGui);
        if (saveSlotNumber != 0)
            this.SaveSlotSelection.SetSelectionData(this.DeepDungeon, saveSlotNumber);
    }

    public void ResetSaveSlotSelection() => this.SaveSlotSelection.SetSelectionData(this.DeepDungeon, 0);

    public void CharacterUpdate()
    {
        var characterName = this.CharacterName;
        if (string.IsNullOrWhiteSpace(this.CharacterName))
            this.CharacterName = Service.ObjectTable.LocalPlayer?.Name.ToString() ?? string.Empty;

        var serverName = this.ServerName;
        if (string.IsNullOrWhiteSpace(this.ServerName))
            this.ServerName = Service.ObjectTable.LocalPlayer?.HomeWorld.Value.Name.ToString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(characterName) && !string.IsNullOrWhiteSpace(this.CharacterName) &&
            string.IsNullOrWhiteSpace(serverName) && !string.IsNullOrWhiteSpace(this.ServerName))
        {
            if (this.IsInDeepDungeonRegion)
            {

                this.SaveSlotSelection.ResetSelectionData();
                if (!this.SaveSlotSelection.GetData().ContainsKey(this.CharacterKey))
                {
                    this.SaveSlotSelection.SetSelectionData(this.DeepDungeon, 0);
                    this.SaveSlotSelection.Save(this.CharacterKey);
                }

                this.LoadDeepDungeonData(false);
            }
        }
    }

    public void CheckForSolo()
    {
        this.IsSupportedParty = ServiceUtility.IsSupportedParty;
        var set = this.CurrentSaveSlot?.CurrentFloorSet();
        if (set != null && !set.Completed && !set.Failed && set.PartySize != ServiceUtility.PartySize)
            this.CurrentSaveSlot?.Note("Party size changed during the set; the entry size is retained for scoring.");
        // Deduplicate deaths until resurrection.
        foreach (var character in Service.ObjectTable.OfType<ICharacter>())
            this.RecordedDeaths.Observe(character.EntityId, character.IsDead);
    }

    public void CheckForCharacterStats()
    {
        this.CurrentSaveSlot?.CurrentLevelUpdate(Service.ObjectTable.LocalPlayer?.Level ?? 0);
        var director = EventFramework.Instance()->GetContentDirector();
        if (director == null || !this.CheckForValidContent((int)director->ContentId)) return;
        var dungeon = (InstanceContentDeepDungeon*)director;
        if (this.DutyStatus == DutyStatus.None && dungeon->ContentTimeLeft > 0 && dungeon->ContentTimeLeft <= 3600)
        {
            this.FloorSetTime.Synchronize(TimeSpan.FromSeconds(3600 - dungeon->ContentTimeLeft));
            this.CurrentSaveSlot?.CurrentFloorSet()?.SetTimerKnown(true);
        }
        if (dungeon->WeaponLevel <= 99 && dungeon->ArmorLevel <= 99)
            this.CurrentSaveSlot?.AetherpoolUpdate(dungeon->WeaponLevel, dungeon->ArmorLevel);
    }

    public void CheckForBossKilled(DataText dataText)
    {
        ArgumentNullException.ThrowIfNull(dataText);
        if (!this.IsBossFloor || this.IsBossDead) return;
        foreach (var character in Service.ObjectTable.OfType<ICharacter>())
        {
            if (!character.IsDead || character.ObjectKind != ObjectKind.BattleNpc) continue;
            bool isBoss = this.DeepDungeon == DeepDungeon.PilgrimsTraverse
                ? PilgrimData.IsBoss(character.NameId, this.CurrentSaveSlot?.CurrentFloorNumber() ?? 0)
                : dataText.IsBoss(character.Name.TextValue).Item1;
            if (!isBoss || !this.DefeatedBosses.Add(character.NameId)) continue;
            this.CurrentSaveSlot?.CurrentFloor()?.EnemyKilled();
            var required = this.DeepDungeon == DeepDungeon.PilgrimsTraverse &&
                this.CurrentSaveSlot?.CurrentFloorNumber() == 99 ? 2 : 1;
            if (this.DefeatedBosses.Count < required) continue;
            this.IsBossDead = true;
            this.CurrentSaveSlot?.CurrentFloor()?.MarkBossDefeated();
            this.CurrentSaveSlot?.CurrentFloorSet()?.MarkBossTime(this.FloorSetTime.TotalTime);
            if (this.IsLastFloor) this.DutyCompleted();
        }
    }

    public void CheckForMapReveal()
    {
        var currentFloor = this.CurrentSaveSlot?.CurrentFloor();

        if (this.IsLastFloor || (currentFloor?.Map ?? false))
            return;

        if (MapUtility.IsMapFullyRevealed(currentFloor?.MapData ?? new()))
            currentFloor?.MapFullyRevealed();
    }

    public void CheckForTimeBonus()
    {
        if (this.DutyStatus != DutyStatus.None)
            return;

        this.CurrentSaveSlot?.CurrentFloorSet()?.CheckForTimeBonus(this.FloorSetTime.TotalTime);
    }

    public void CheckForCairnOfPassageActivation(DataText dataText)
    {
        if (this.IsCairnOfPassageActivated || (this.CurrentSaveSlot?.CurrentFloor()?.IsLastFloor() ?? false))
            return;

        foreach (var enemy in Service.ObjectTable)
        {
            var character = enemy as ICharacter;
            if (character == null)
                continue;

            if (character.IsDead && (character.ObjectKind == ObjectKind.BattleNpc) &&
                (character.StatusFlags.HasFlag(StatusFlags.Hostile) ||
                 (dataText?.IsMandragora(character.Name.TextValue).Item1 ?? false)))
            {
                if (!this.IsCairnOfPassageActivated && this.CairnOfPassageKillIds.Add(character.EntityId))
                {
                    this.CurrentSaveSlot?.CurrentFloor()?.CairnOfPassageShines();
                    break;
                }
            }
        }

        this.IsCairnOfPassageActivated = NodeUtility.CairnOfPassageActivation(Service.GameGui);
    }

    public void CheckForBossStatusTimer(bool inCombat)
    {
        if (inCombat && this.IsBossFloor)
        {
            unsafe
            {
                var enemy = Service.ObjectTable
                    .Where(x => ((FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)x.Address)->GetIsTargetable())
                    .MaxBy(x => (x as ICharacter)?.MaxHp) as IBattleChara;
                this.BossStatusTimerManager?.Update(enemy);
            }
        }
    }

    public void CheckForScoreWindowKills()
    {
        if (this.WasScoreWindowShown || this.CurrentSaveSlot == null) return;
        var kills = NodeUtility.ScoreWindowKills(Service.GameGui);
        var points = NodeUtility.ScoreWindowScorePoints(Service.GameGui);
        if (!kills.Item1 || !points.Item1 || points.Item2 < 0 || kills.Item2 < 0) return;
        this.WasScoreWindowShown = true;
        this.CurrentSaveSlot.CurrentFloorSet()?.RecordResult(points.Item2, kills.Item2);
        if (kills.Item2 != this.CurrentSaveSlot.Kills())
            this.CurrentSaveSlot.Note("The game result and captured kill count differ. Events outside client range may be missing.");
        this.SaveDeepDungeonData();
    }

    public void CheckForSaveSlotDeletion()
    {
        var result = NodeUtility.SaveSlotDeletion(Service.GameGui);
        var moveSaveSlots = new bool[] { result.Item1, result.Item2 };
        for (var i = 0; i < moveSaveSlots.Length; i++)
        {
            if (moveSaveSlots[i])
            {
                var saveSlotNumber = i + 1;
                var saveSlotFileName = this.GetSaveSlotFileName(new(this.DeepDungeon, saveSlotNumber));
                if (LocalStream.Exists(ServiceUtility.ConfigDirectory, saveSlotFileName))
                {
                    if (LocalStream.Move(ServiceUtility.ConfigDirectory, ServiceUtility.ConfigDirectory,
                            saveSlotFileName, this.GetLastSaveFileName(new(this.DeepDungeon, saveSlotNumber))))
                    {
                        var data = this.SaveSlotSelection.GetSelectionData(this.CharacterKey);
                        if (data?.DeepDungeon == this.DeepDungeon && data.SaveSlotNumber == saveSlotNumber)
                        {
                            this.ResetSaveSlotSelection();
                            this.SaveSlotSelection.Save(this.CharacterKey);
                        }
                    }
                }
            }
        }
    }

    public void DeepDungeonUpdate(DataText dataText, uint territoryType)
    {
        var deepDungeon = this.DeepDungeon;
        if (dataText?.IsPalaceOfTheDeadRegion(territoryType) ?? false)
        {
            this.DeepDungeon = DeepDungeon.PalaceOfTheDead;
        }
        else if (dataText?.IsHeavenOnHighRegion(territoryType) ?? false)
        {
            this.DeepDungeon = DeepDungeon.HeavenOnHigh;
        }
        else if (dataText?.IsEurekaOrthosRegion(territoryType) ?? false)

        {
            this.DeepDungeon = DeepDungeon.EurekaOrthos;
        }
        else if (dataText?.IsPilgrimsTraverseRegion(territoryType) ?? false)
            this.DeepDungeon = DeepDungeon.PilgrimsTraverse;
        else
            this.DeepDungeon = DeepDungeon.None;

        if (this.IsInDeepDungeonRegion && this.DeepDungeon != deepDungeon)
            this.LoadDeepDungeonData(false);
    }

    public TimeSpan GetRespawnTime()
    {
        var floorNumber = this.CurrentSaveSlot?.CurrentFloorNumber();

        if (this.IsBossFloor)
            return default;

        if (this.DeepDungeon == DeepDungeon.PalaceOfTheDead)
        {
            if (floorNumber >= 1 && floorNumber <= 9)
                return TimeSpan.FromSeconds(40);
            else if (floorNumber >= 11 && floorNumber <= 39)
                return TimeSpan.FromMinutes(1);
            else if (floorNumber >= 41 && floorNumber <= 49)
                return TimeSpan.FromMinutes(2);
            else if (floorNumber >= 51 && floorNumber <= 89)
                return TimeSpan.FromMinutes(1);
            else if (floorNumber >= 91 && floorNumber <= 99)
                return TimeSpan.FromMinutes(2);
            else if (floorNumber >= 101 && floorNumber <= 149)
                return TimeSpan.FromMinutes(1.5);
            else if (floorNumber >= 151 && floorNumber <= 199)
                return TimeSpan.FromMinutes(5);
        }
        else if (this.DeepDungeon == DeepDungeon.HeavenOnHigh)
        {
            if (floorNumber >= 1 && floorNumber <= 29)
                return TimeSpan.FromMinutes(1);
            else if (floorNumber >= 31 && floorNumber <= 99)
                return TimeSpan.FromMinutes(10);
        }
        else if (this.DeepDungeon == DeepDungeon.EurekaOrthos)
        {
            if (floorNumber >= 1 && floorNumber <= 29)
                return TimeSpan.FromMinutes(1);
            else if (floorNumber >= 31 && floorNumber <= 98)
                return TimeSpan.FromMinutes(10);
        }

        return default;
    }

    public void EnchantmentMessageReceived(DataText dataText, string message)
    {
        if (!this.IsEnchantmentsLoaded)
            return;
        
        var result = dataText?.IsEnchantment(message) ?? new();
        if (result.Item1)
            this.CurrentSaveSlot?.CurrentFloor()
                ?.EnchantmentAffected((Enchantment)(result.Item2! - TextIndex.BlindnessEnchantment));
    }

    public void TrapMessageReceived(DataText dataText, string message)
    {
        var result = dataText?.IsTrap(message) ?? new();
        if (result.Item1)
            this.CurrentSaveSlot?.CurrentFloor()?.TrapTriggered((Trap)(result.Item2! - TextIndex.LandmineTrap));
    }

    public void CheckForEnemyKilled(DataText dataText, string name, uint id)
    {
        ArgumentNullException.ThrowIfNull(dataText);
        var currentFloor = this.CurrentSaveSlot?.CurrentFloor();

        currentFloor?.EnemyKilled();
        var npc = Service.ObjectTable.SearchById(id) as ICharacter;
        if (this.DeepDungeon == DeepDungeon.PilgrimsTraverse && npc?.NameId == 14267)
            currentFloor?.MandragoraKilled();
        else if (this.DeepDungeon == DeepDungeon.PilgrimsTraverse && PilgrimData.IsMimic(npc?.NameId ?? 0))
            currentFloor?.MimicKilled();
        else if (dataText.IsMimic(name).Item1)
            currentFloor?.MimicKilled();
        else if (dataText.IsMandragora(name).Item1)
            currentFloor?.MandragoraKilled();
        else if (dataText.IsNPC(name).Item1)
            currentFloor?.NPCKilled();
        else if (dataText.IsDreadBeast(name).Item1)
            currentFloor?.DreadBeastKilled();
    }

    public void CheckForPlayerKilled(ICharacter character)
    {
        ArgumentNullException.ThrowIfNull(character);
        var localId = Service.ObjectTable.LocalPlayer?.EntityId;
        if (character.EntityId == localId || Service.PartyList.Any(p => p.EntityId == character.EntityId))
        {
            this.CurrentSaveSlot?.CurrentFloor()?.PlayerKilled();
            if (character.EntityId == localId)
                this.CurrentSaveSlot?.CurrentFloor()?.LocalPlayerKilled();
        }
    }

    public void CharacterKilled(DataText dataText, uint entityId)
    {
        ArgumentNullException.ThrowIfNull(dataText);
        if (this.CurrentSaveSlot?.CurrentFloorSet() is not { Completed: false, Failed: false }) return;
        var character = Service.ObjectTable.SearchById(entityId) as ICharacter;
        if (character == null) { this.CurrentSaveSlot.Note("A death event could not be resolved to a visible actor."); return; }
        if (!this.RecordedDeaths.Record(entityId)) return;
        var name = character.Name.TextValue;
        if ((character.ObjectKind == ObjectKind.BattleNpc) && (character.StatusFlags.HasFlag(StatusFlags.Hostile) || dataText.IsMandragora(name).Item1 ||
            (this.DeepDungeon == DeepDungeon.PilgrimsTraverse && character.NameId == 14267)))
        {
            if (!this.IsBossFloor)
                this.CheckForEnemyKilled(dataText, name, entityId);
        }
        else if (character.ObjectKind == ObjectKind.Pc)
            this.CheckForPlayerKilled(character);
    }

    private void FloorScoreUpdate(int? additional = null) => this.CurrentSaveSlot?.CurrentFloor()
        ?.ScoreUpdate(this.TotalScore - this.CurrentSaveSlot.Score() + (additional ?? 0));

    public void CalculateScore(ScoreCalculationType scoreCalculationType)
    {
        this.Score = ScoreCreator.Create(this.CurrentSaveSlot ?? new(), true);
        this.Score?.TotalScoreCalculation(true, scoreCalculationType);
    }

    public void StartFirstFloor(int contentId, DataText dataText)
    {
        ArgumentNullException.ThrowIfNull(dataText);
        if (!this.CheckForValidContent(contentId) || !ServiceUtility.IsSupportedParty) return;
        if (this.ContentId != 0) return;
        var director = EventFramework.Instance()->GetContentDirector();
        if (director == null || director->ContentId != contentId) return;
        var dungeon = (InstanceContentDeepDungeon*)director;
        var floorNumber = (int)dungeon->Floor;
        if (floorNumber < 1 || floorNumber > ScoreEngine.MaximumFloor(this.DeepDungeon)) return;
        dataText.LoadEnchantments();
        this.IsEnchantmentsLoaded = true;
        this.FloorEffect = new();
        this.FloorSetTime.Start();
        this.ShowFloorSetTimeValues = true;
        this.SaveSlotSelection.Save(this.CharacterKey);
        var selection = this.SaveSlotSelection.GetSelectionData(this.CharacterKey);
        var selected = selection?.DeepDungeon == this.DeepDungeon && selection.SaveSlotNumber is 1 or 2;
        SaveSlot? previous = selected
            ? LocalStream.Load<SaveSlot>(ServiceUtility.ConfigDirectory, this.GetSaveSlotFileName(selection)) : null;
        if (previous?.DeepDungeon == this.DeepDungeon && previous.CurrentFloorSet() is { Completed: false, Failed: false } &&
            previous.ContentId == contentId && previous.CurrentFloorNumber() == floorNumber &&
            previous.ClassJobId == Service.ObjectTable.LocalPlayer?.ClassJob.Value.RowId)
        {
            this.CurrentSaveSlot = previous;
            this.FloorSetTime.Restore(previous.CurrentFloorSet()!);
            this.CurrentSaveSlot.Note("Tracking resumed within a set; events while the plugin was unloaded may be missing.");
        }
        else if (previous?.DeepDungeon == this.DeepDungeon && previous.CurrentFloorSet()?.Completed == true &&
            previous.CurrentFloorNumber() + 1 == floorNumber && floorNumber % 10 == 1)
        {
            this.CurrentSaveSlot = previous;
            this.CurrentSaveSlot.AddFloorSet(floorNumber);
            this.CurrentSaveSlot.ContentIdUpdate(contentId);
        }
        else if (previous?.DeepDungeon == this.DeepDungeon && previous.CurrentFloorSet()?.Failed == true &&
            previous.CurrentFloorSet()?.FirstFloor()?.Number == floorNumber &&
            floorNumber < ScoreEngine.ChallengeStarts(this.DeepDungeon))
        {
            this.CurrentSaveSlot = previous;
            // Archive before rollback.
            LocalStream.Save(ServiceUtility.ConfigDirectory, $"attempt-{Guid.NewGuid():N}.json", previous).GetAwaiter().GetResult();
            this.CurrentSaveSlot.ResetFloorSet();
            this.CurrentSaveSlot.ContentIdUpdate(contentId);
        }
        else
        {
            this.CurrentSaveSlot = new(this.DeepDungeon, contentId,
                Service.ObjectTable.LocalPlayer?.ClassJob.Value.RowId ?? 0,
                Service.ObjectTable.LocalPlayer?.Level ?? 0);
            this.CurrentSaveSlot.MarkCurrentSchema();
            this.CurrentSaveSlot.AddFloorSet(floorNumber);
            if (floorNumber % 10 != 1)
            {
                this.CurrentSaveSlot.CurrentFloorSet()?.SetTimerKnown(false);
                this.CurrentSaveSlot.Note("Tracking began partway through a set; earlier events are missing.");
            }
            if (!selected)
                this.CurrentSaveSlot.Note("No game save slot was identified. This capture is preserved separately; select the slot in settings before the next set to resume.");
        }
        var entryPartySize = 0;
        foreach (var member in dungeon->Party)
            if (member.EntityId != 0 && member.EntityId != 0xE0000000) entryPartySize++;
        if (previous != this.CurrentSaveSlot || previous?.CurrentFloorSet()?.Time() == TimeSpan.Zero)
            this.CurrentSaveSlot.CurrentFloorSet()?.SetPartySize(entryPartySize is >= 1 and <= 4 ? entryPartySize : ServiceUtility.PartySize);
        if (ServiceUtility.PartySize > 1)
            this.CurrentSaveSlot.Note("Party estimate: distant events may be missing; the current candidate counts all observed party deaths.");
        if (this.DeepDungeon == DeepDungeon.PilgrimsTraverse)
            this.CurrentSaveSlot.Note("Pilgrim scoring uses an unverified EO-derived candidate. Juniper kills, candle effects, and final encounter weights need result samples.");
        this.CheckForCharacterStats();
        this.EnableFlyTextScore = true;
        this.SaveDeepDungeonData();
    }

    public void EnsureStarted(DataText dataText)
    {
        if (this.ContentId != 0) return;
        var director = EventFramework.Instance()->GetContentDirector();
        if (director != null) this.StartFirstFloor((int)director->ContentId, dataText);
    }

    public void RecordObservedResult(int score, int kills)
    {
        if (score <= 0 || kills < 0) return;
        this.CurrentSaveSlot?.CurrentFloorSet()?.RecordResult(score, kills);
        this.SaveDeepDungeonData();
    }

    public void SelectCaptureSlot(int number)
    {
        if (this.CurrentSaveSlot == null || number is < 1 or > 2) return;
        var file = this.GetSaveSlotFileName(new(this.DeepDungeon, number));
        if (LocalStream.Exists(ServiceUtility.ConfigDirectory, file))
            LocalStream.Copy(ServiceUtility.ConfigDirectory, ServiceUtility.ConfigDirectory,
                file, $"archive-{Guid.NewGuid():N}.json");
        this.SaveSlotSelection.SetSelectionData(this.DeepDungeon, number);
        this.SaveSlotSelection.Save(this.CharacterKey);
        this.SaveDeepDungeonData();
    }

    public void CheckForFloorChange()
    {
        if (this.ContentId == 0 || this.DutyStatus != DutyStatus.None) return;
        var director = EventFramework.Instance()->GetContentDirector();
        if (director == null || director->ContentId != this.ContentId) return;
        var floor = (int)((InstanceContentDeepDungeon*)director)->Floor;
        if (floor == this.CurrentSaveSlot?.CurrentFloorNumber() + 1)
        {
            this.IsTransferenceInitiated = true;
            this.StartNextFloor();
        }
        else if (floor > this.CurrentSaveSlot?.CurrentFloorNumber() + 1)
            this.CurrentSaveSlot?.Note("The client skipped a floor transition; capture is incomplete.");
    }

    public void StartNextFloor()
    {
        // Retry when the director catches up with the network event.
        var director = EventFramework.Instance()->GetContentDirector();
        if (director == null || director->ContentId != this.ContentId ||
            ((InstanceContentDeepDungeon*)director)->Floor != this.CurrentSaveSlot?.CurrentFloorNumber() + 1) return;
        if (this.ContentId != 0 && this.IsTransferenceInitiated)
        {
            this.CurrentSaveSlot?.CurrentFloor()?.MarkCleared();
            this.IsBossDead = false;
            this.DefeatedBosses.Clear();
            this.RecordedDeaths.Clear();
            this.IsTransferenceInitiated = false;
            this.IsCairnOfPassageActivated = false;
            this.CairnOfPassageKillIds = [];
            var time = this.FloorSetTime.AddFloor();
            this.CurrentSaveSlot?.CurrentFloor()?.TimeUpdate(time);
            this.CalculateScore(ScoreCalculationType.CurrentFloor);
            this.FloorScoreUpdate();
            this.CurrentSaveSlot?.AddFloor();
            this.SaveDeepDungeonData();

            var floorEffect = new FloorEffect
            {
                ShowPomanderOfAffluence = this.FloorEffect.IsPomanderOfAffluenceUsed,
                ShowPomanderOfFlight = this.FloorEffect.IsPomanderOfFlightUsed,
                ShowPomanderOfAlteration = this.FloorEffect.IsPomanderOfAlterationUsed
            };
            this.FloorEffect = floorEffect;
        }
    }

    public bool CheckForValidContent(int contentId) => IsValidContent(this.DeepDungeon, contentId);

    private static bool IsValidContent(DeepDungeon dungeon, int contentId) => dungeon switch
    {
        DeepDungeon.PalaceOfTheDead => contentId is >= 60001 and <= 60020,
        DeepDungeon.HeavenOnHigh => contentId is >= 60021 and <= 60030,
        DeepDungeon.EurekaOrthos => contentId is >= 60031 and <= 60040,
        DeepDungeon.PilgrimsTraverse => contentId is >= 60041 and <= 60050,
        _ => false
    };

    public void DutyStarted(DataText dataText) => this.EnsureStarted(dataText);

    public void DutyCompleted()
    {
        if (this.DutyStatus != DutyStatus.None || this.CurrentSaveSlot == null) return;
        this.CheckForCharacterStats();
        this.CurrentSaveSlot.CurrentFloorSet()?.Complete(this.FloorSetTime.TotalTime);
        this.DutyStatus = DutyStatus.Complete;
        this.FloorSetTime.Pause();
        this.CurrentSaveSlot.CurrentFloor()?.TimeUpdate(this.FloorSetTime.CurrentFloorTime);
        this.CalculateScore(ScoreCalculationType.CurrentFloor);
        this.FloorScoreUpdate();
        this.SaveDeepDungeonData();
    }

    public void DutyFailed()
    {
        if (this.DutyStatus != DutyStatus.None || this.CurrentSaveSlot == null) return;
        this.DutyStatus = DutyStatus.Failed;
        this.CurrentSaveSlot.CurrentFloorSet()?.Fail();
        if (this.CurrentSaveSlot.CurrentFloorNumber() >= ScoreEngine.ChallengeStarts(this.CurrentSaveSlot.DeepDungeon))
            this.CurrentSaveSlot.KOed();
        this.FloorSetTime.Pause();
        this.CurrentSaveSlot.CurrentFloor()?.TimeUpdate(this.FloorSetTime.CurrentFloorTime);
        this.CalculateScore(ScoreCalculationType.CurrentFloor);
        this.FloorScoreUpdate();
        this.SaveDeepDungeonData();
    }

    public void RegenPotionConsumed() => this.CurrentSaveSlot?.CurrentFloor()?.RegenPotionConsumed();

    public void BronzeChestOpened(Coffer coffer) => this.CurrentSaveSlot?.CurrentFloor()?.CofferOpened(coffer);

    public void PomanderObtained(int itemId)
    {
        var pomander = PilgrimData.MapPomander(itemId);
        if (pomander.HasValue) this.CurrentSaveSlot?.CurrentFloor()?.CofferOpened((Coffer)pomander.Value);
        else this.CurrentSaveSlot?.Note($"Unmapped pomander item {itemId}; its coffer was not classified.");
    }

    public void AetherpoolObtained()
    {
        if (!this.IsLastFloor)
            this.CurrentSaveSlot?.CurrentFloor()?.CofferOpened(Coffer.Aetherpool);
    }

    public void StoneObtained(int itemId)
    {
        if (this.DeepDungeon == DeepDungeon.HeavenOnHigh)
        {
            MagiciteObtained(itemId);
        }
        else if (this.DeepDungeon == DeepDungeon.EurekaOrthos)
        {
            DemicloneObtained(itemId);
        }
        else if (this.DeepDungeon == DeepDungeon.PilgrimsTraverse)
            this.CurrentSaveSlot?.CurrentFloor()?.CofferOpened(Coffer.JuniperIncense);
    }

    public void MagiciteObtained(int itemId) =>
        this.CurrentSaveSlot?.CurrentFloor()?.CofferOpened(itemId - 1 + Coffer.InfernoMagicite);

    public void DemicloneObtained(int itemId) =>
        this.CurrentSaveSlot?.CurrentFloor()?.CofferOpened(itemId - 1 + Coffer.UneiDemiclone);

    public void PomanderUsed(int itemId)
    {
        var mapped = PilgrimData.MapPomander(itemId);
        if (!mapped.HasValue) { this.CurrentSaveSlot?.Note($"Unmapped pomander item {itemId}."); return; }
        var pomander = mapped.Value;
        if (pomander == Pomander.Safety) this.FloorEffect.ShowPomanderOfSafety = true;
        else if (pomander == Pomander.Affluence) this.FloorEffect.IsPomanderOfAffluenceUsed = true;
        else if (pomander == Pomander.Flight) this.FloorEffect.IsPomanderOfFlightUsed = true;
        else if (pomander == Pomander.Alteration) this.FloorEffect.IsPomanderOfAlterationUsed = true;
        this.CurrentSaveSlot?.CurrentFloor()?.PomanderUsed(pomander);
    }

    public void StoneUsed(int itemId)
    {
        if (this.DeepDungeon == DeepDungeon.HeavenOnHigh)
        {
            this.MagiciteUsed(itemId);
        }
        else if (this.DeepDungeon == DeepDungeon.EurekaOrthos)
        {
            this.DemicloneUsed(itemId);
        }
        else if (this.DeepDungeon == DeepDungeon.PilgrimsTraverse)
            this.CurrentSaveSlot?.CurrentFloor()?.PomanderUsed(Pomander.JuniperIncense);
    }

    public void MagiciteUsed(int itemId)
    {
        this.SpecialPomanderUsed(Coffer.InfernoMagicite, itemId);
        this.WasMagiciteUsed = true;
    }

    public void DemicloneUsed(int itemId) => this.SpecialPomanderUsed(Coffer.UneiDemiclone, itemId);

    private void SpecialPomanderUsed(Coffer baseSpecialPomander, int itemId)
    {
        itemId = itemId - 1 + (int)baseSpecialPomander;

        var pomander = (Pomander)itemId;
        this.CurrentSaveSlot?.CurrentFloor()?.PomanderUsed(pomander);
    }

    public void TransferenceInitiated()
    {
        this.WasMagiciteUsed = false;
        this.WasScoreWindowShown = false;
        this.NearbyEnemies = [];
        this.IsTransferenceInitiated = true;
    }
}
