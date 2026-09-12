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
    private bool IsInside { get; set; }
    private bool HasStartedCapture { get; set; }
    private bool CompletionPending { get; set; }
    private string? CaptureFileName { get; set; }
    private string? InitializedCharacterKey { get; set; }
    public bool IsCapturing => this.IsInside && this.HasStartedCapture && this.DutyStatus == DutyStatus.None &&
        this.CurrentSaveSlot?.CurrentFloorSet() is { Completed: false, Failed: false };
    private readonly HashSet<uint> DefeatedBosses = [];
    public string? LastExportPath { get; private set; }
    public string? StorageError { get; private set; }

    private string CharacterName { get; set; } = string.Empty;

    private string ServerName { get; set; } = string.Empty;

    public bool IsTransferenceInitiated { get; private set; }

    private DateTime BronzeCofferPendingUntil { get; set; }
    public bool IsBronzeCofferOpened
    {
        get => DateTime.UtcNow < this.BronzeCofferPendingUntil;
        set => this.BronzeCofferPendingUntil = value ? DateTime.UtcNow.AddSeconds(5) : DateTime.MinValue;
    }

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

    public SaveSlotSelection SaveSlotSelection { get; } = new(ServiceUtility.ConfigDirectory, e => CaptureDiagnostics.Report("Save slot selection failed", e));

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
        if (!this.IsCapturing ||
            !IsValidContent(this.CurrentSaveSlot!.DeepDungeon, this.ContentId)) return;
        if (!force && DateTime.UtcNow < this.NextCheckpoint) return;
        this.NextCheckpoint = DateTime.UtcNow.AddSeconds(15);
        this.CurrentSaveSlot.CurrentFloor()?.TimeUpdate(this.FloorSetTime.CurrentFloorTime);
        this.SaveDeepDungeonData();
    }

    public void ResetCharacterData()
    {
        this.Checkpoint(force: true);
        this.CharacterName = string.Empty;
        this.ServerName = string.Empty;
        this.InitializedCharacterKey = null;
        this.HasStartedCapture = false;
        this.CaptureFileName = null;
        this.CurrentSaveSlot = null;
        this.Score = null;
        this.SaveSlotSelection.ResetSelectionData();
        this.FloorSetTime = new();
        this.FloorEffect = new();
    }

    public void EnteringDeepDungeon()
    {
        this.IsInside = true;
        this.HasStartedCapture = false;
        this.CompletionPending = false;
        this.CurrentSaveSlot = null;
        this.Score = null;
        this.CaptureFileName = null;
        this.NextCheckpoint = DateTime.MinValue;
        this.BossStatusTimerManager?.Dispose();
        this.BossStatusTimerManager = null;
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
    }

    public void ExitingDeepDungeon()
    {
        this.EnableFlyTextScore = false;
        if (this.CompletionPending && this.IsCapturing &&
            CaptureFloorTransition.ReachCompletedSetEnd(this.CurrentSaveSlot!, this.FloorSetTime))
            this.DutyCompleted();
        if (this.IsCapturing && !this.CompletionPending) this.DutyFailed();
        if (this.HasStartedCapture) this.SaveDeepDungeonData();
        this.IsInside = false;
        this.HasStartedCapture = false;
        this.SaveSlotSelection.ResetSelectionData();
        this.IsBronzeCofferOpened = false;
        this.BossStatusTimerManager?.Dispose();
        this.BossStatusTimerManager = null;
    }

    public void EnteringCombat()
    {
        if (!this.IsCapturing || !this.IsBossFloor) return;
        this.BossStatusTimerManager?.Dispose();
        this.BossStatusTimerManager = this.CurrentSaveSlot?.CurrentFloorSet()?.StartBossStatusTimer(this.ExitingCombat);
    }

    public void ExitingCombat()
    {
        if (this.IsBossFloor)
            this.CurrentSaveSlot?.CurrentFloorSet()?.EndBossStatusTimer();
    }

    public static string GetSaveSlotFileName(string key, SaveSlotSelection.SaveSlotSelectionData? data) =>
        SaveSlotSelection.GetSaveSlotFileName(key, data);

    public static string GetLastSaveFileName(string key, SaveSlotSelection.SaveSlotSelectionData? data) =>
        SaveSlotSelection.GetSaveSlotFileName(key, data, last: true);

    public string GetSaveSlotFileName(SaveSlotSelection.SaveSlotSelectionData? data) =>
        DataCommon.GetSaveSlotFileName(this.CharacterKey, data);

    public string GetLastSaveFileName(SaveSlotSelection.SaveSlotSelectionData? data) =>
        DataCommon.GetLastSaveFileName(this.CharacterKey, data);

    private void SaveDeepDungeonData()
    {
        if (this.CurrentSaveSlot == null || !IsValidContent(this.CurrentSaveSlot!.DeepDungeon, this.ContentId)) return;
        var fileName = this.CaptureFileName ?? $"capture-{this.CurrentSaveSlot.RunId}.json";
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
        if (this.IsInside && this.HasStartedCapture) return this.CurrentSaveSlot;
        this.ShowFloorSetTimeValues = showFloorSetTimeValues;
        this.CurrentSaveSlot = LocalStream.Load<SaveSlot>(ServiceUtility.ConfigDirectory, fileName, SaveSlot.IsValid);
        this.CaptureFileName = this.CurrentSaveSlot != null ? fileName : null;
        return this.CurrentSaveSlot;
    }

    public void LoadDeepDungeonData(bool showFloorSetTimeValues, bool ignoreDeepDungeonRegion = false)
    {
        if (this.IsInside && this.HasStartedCapture) return;
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
        var player = Service.ObjectTable.LocalPlayer;
        if (player == null) return;
        this.CharacterName = player.Name.TextValue;
        this.ServerName = player.HomeWorld.Value.Name.ExtractText();
        if (string.IsNullOrWhiteSpace(this.CharacterName) || string.IsNullOrWhiteSpace(this.ServerName) ||
            this.InitializedCharacterKey == this.CharacterKey) return;
        this.InitializedCharacterKey = this.CharacterKey;
        this.SaveSlotSelection.ResetSelectionData();
        if (this.IsInDeepDungeonRegion) this.LoadDeepDungeonData(false);
    }

    public void CheckForSolo()
    {
        this.IsSupportedParty = ServiceUtility.IsSupportedParty;
        var set = this.CurrentSaveSlot?.CurrentFloorSet();
        if (this.IsCapturing && set != null && set.PartySize != ServiceUtility.PartySize)
            this.CurrentSaveSlot?.Note("Party size changed during the set; the entry size is retained for scoring.");
        // Deduplicate deaths until resurrection.
        foreach (var character in Service.ObjectTable.OfType<ICharacter>())
            this.RecordedDeaths.Observe(character.EntityId, character.IsDead);
    }

    public void CheckForCharacterStats()
    {
        if (!this.IsCapturing) return;
        this.CurrentSaveSlot?.CurrentLevelUpdate(Service.ObjectTable.LocalPlayer?.Level ?? 0);
        var dungeon = this.GetDungeonDirector(this.ContentId);
        if (dungeon == null) return;
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
        if (!this.IsCapturing || !this.IsBossFloor || this.IsBossDead) return;
        foreach (var character in Service.ObjectTable.OfType<ICharacter>())
            if (character.IsDead && character.ObjectKind == ObjectKind.BattleNpc)
                this.RecordBossDeath(dataText, character);
    }

    private void RecordBossDeath(DataText dataText, ICharacter character)
    {
        var isBoss = this.DeepDungeon == DeepDungeon.PilgrimsTraverse
            ? PilgrimData.IsBoss(character.NameId, this.CurrentSaveSlot?.CurrentFloorNumber() ?? 0)
            : dataText.IsBoss(character.Name.TextValue).Item1;
        if (this.IsBossDead || !isBoss || !this.DefeatedBosses.Add(character.NameId)) return;
        this.CurrentSaveSlot?.CurrentFloor()?.EnemyKilled();
        var required = this.DeepDungeon == DeepDungeon.PilgrimsTraverse &&
            this.CurrentSaveSlot?.CurrentFloorNumber() == 99 ? 2 : 1;
        if (this.DefeatedBosses.Count < required) return;
        this.IsBossDead = true;
        this.CurrentSaveSlot?.CurrentFloor()?.MarkBossDefeated();
        this.CurrentSaveSlot?.CurrentFloorSet()?.MarkBossTime(this.FloorSetTime.TotalTime);
    }

    public void CheckForMapReveal()
    {
        var currentFloor = this.CurrentSaveSlot?.CurrentFloor();

        if (!this.IsCapturing || this.IsLastFloor || (currentFloor?.Map ?? false))
            return;

        if (MapUtility.IsMapFullyRevealed(currentFloor?.MapData ?? new()))
            currentFloor?.MapFullyRevealed();
    }

    public void CheckForTimeBonus()
    {
        if (!this.IsCapturing)
            return;

        this.CurrentSaveSlot?.CurrentFloorSet()?.CheckForTimeBonus(this.FloorSetTime.TotalTime);
    }

    public void CheckForCairnOfPassageActivation(DataText dataText)
    {
        if (!this.IsCapturing || this.IsCairnOfPassageActivated || (this.CurrentSaveSlot?.CurrentFloor()?.IsLastFloor() ?? false))
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
        if (!this.IsCapturing || !inCombat || !this.IsBossFloor) return;
        var enemy = Service.ObjectTable.OfType<IBattleChara>()
            .Where(x => x.ObjectKind == ObjectKind.BattleNpc && x.IsTargetable && x.StatusFlags.HasFlag(StatusFlags.Hostile))
            .MaxBy(x => x.MaxHp);
        this.BossStatusTimerManager?.Update(enemy);
    }

    public void CheckForScoreWindowKills()
    {
        if (!this.HasStartedCapture || this.WasScoreWindowShown || this.CurrentSaveSlot == null) return;
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
        if (!this.IsCapturing || !this.IsEnchantmentsLoaded)
            return;
        
        var result = dataText?.IsEnchantment(message) ?? new();
        if (result.Item1)
            this.CurrentSaveSlot?.CurrentFloor()
                ?.EnchantmentAffected((Enchantment)(result.Item2! - TextIndex.BlindnessEnchantment));
    }

    public void TrapMessageReceived(DataText dataText, string message)
    {
        if (!this.IsCapturing) return;
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
        if (!this.IsCapturing) return;
        var character = Service.ObjectTable.SearchById(entityId) as ICharacter;
        if (character == null) { this.CurrentSaveSlot!.Note("A death event could not be resolved to a visible actor."); return; }
        if (!this.RecordedDeaths.Record(entityId)) return;
        var name = character.Name.TextValue;
        if ((character.ObjectKind == ObjectKind.BattleNpc) && (character.StatusFlags.HasFlag(StatusFlags.Hostile) || dataText.IsMandragora(name).Item1 ||
            (this.DeepDungeon == DeepDungeon.PilgrimsTraverse && character.NameId == 14267)))
        {
            if (this.IsBossFloor) this.RecordBossDeath(dataText, character);
            else this.CheckForEnemyKilled(dataText, name, entityId);
        }
        else if (character.ObjectKind == ObjectKind.Pc)
            this.CheckForPlayerKilled(character);
    }

    private void FloorScoreUpdate(int? additional = null) =>
        this.CurrentSaveSlot?.UpdateCurrentFloorScore(this.TotalScore + (additional ?? 0));

    public void CalculateScore(ScoreCalculationType scoreCalculationType)
    {
        this.Score = ScoreCreator.Create(this.CurrentSaveSlot ?? new(), true);
        this.Score?.TotalScoreCalculation(true, scoreCalculationType);
    }

    public void StartFirstFloor(int contentId, DataText dataText)
    {
        ArgumentNullException.ThrowIfNull(dataText);
        if (!this.IsInside || this.HasStartedCapture || !this.CheckForValidContent(contentId) ||
            !ServiceUtility.IsSupportedParty || this.InitializedCharacterKey != this.CharacterKey) return;
        var dungeon = this.GetDungeonDirector(contentId);
        if (dungeon == null || Service.ObjectTable.LocalPlayer is not { } player || player.Level < 1 || player.ClassJob.RowId == 0) return;
        var floorNumber = (int)dungeon->Floor;
        if (floorNumber < 1 || floorNumber > ScoreEngine.MaximumFloor(this.DeepDungeon)) return;
        dataText.LoadEnchantments();
        this.IsEnchantmentsLoaded = true;
        this.FloorEffect = new();
        this.FloorSetTime.Start();
        this.ShowFloorSetTimeValues = true;
        this.SaveSlotSelection.Save(this.CharacterKey);
        var selection = this.SaveSlotSelection.GetCaptureSelectionData();
        var selected = selection?.DeepDungeon == this.DeepDungeon && selection.SaveSlotNumber is 1 or 2;
        SaveSlot? previous = selected
            ? LocalStream.Load<SaveSlot>(ServiceUtility.ConfigDirectory, this.GetSaveSlotFileName(selection), SaveSlot.IsValid) : null;
        var elapsed = dungeon->ContentTimeLeft is > 0 and <= 3600
            ? TimeSpan.FromSeconds(3600 - dungeon->ContentTimeLeft) : (TimeSpan?)null;
        var startKind = CaptureResumePolicy.Classify(previous, this.DeepDungeon, contentId, floorNumber,
            player.ClassJob.Value.RowId, elapsed);
        if (startKind == CaptureStartKind.Resume)
        {
            this.CurrentSaveSlot = previous!;
            this.FloorSetTime.Restore(previous!.CurrentFloorSet()!);
            this.CurrentSaveSlot.Note("Tracking resumed within a set; events while the plugin was unloaded may be missing.");
        }
        else if (startKind == CaptureStartKind.Continue)
        {
            this.CurrentSaveSlot = previous!;
            this.CurrentSaveSlot.AddFloorSet(floorNumber);
            this.CurrentSaveSlot.ContentIdUpdate(contentId);
        }
        else if (startKind == CaptureStartKind.Retry)
        {
            this.CurrentSaveSlot = previous!;
            // Archive before rollback.
            LocalStream.Save(ServiceUtility.ConfigDirectory, $"attempt-{Guid.NewGuid():N}.json", previous).GetAwaiter().GetResult();
            this.CurrentSaveSlot.ResetFloorSet();
            this.CurrentSaveSlot.ContentIdUpdate(contentId);
        }
        else
        {
            if (previous != null)
                LocalStream.Save(ServiceUtility.ConfigDirectory, $"attempt-{Guid.NewGuid():N}.json", previous).GetAwaiter().GetResult();
            this.CurrentSaveSlot = new(this.DeepDungeon, contentId,
                Service.ObjectTable.LocalPlayer?.ClassJob.Value.RowId ?? 0,
                Service.ObjectTable.LocalPlayer?.Level ?? 0);
            this.CurrentSaveSlot.MarkCurrentSchema();
            this.CurrentSaveSlot.AddFloorSet(floorNumber);
            if (floorNumber > 1)
                this.CurrentSaveSlot.Note("Tracking began after floor 1; earlier floors and events are not in this capture.");
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
        if (startKind != CaptureStartKind.Resume)
            this.CurrentSaveSlot.CurrentFloorSet()?.SetPartySize(entryPartySize is >= 1 and <= 4 ? entryPartySize : ServiceUtility.PartySize);
        if (ServiceUtility.PartySize > 1)
            this.CurrentSaveSlot.Note("Party estimate: distant events may be missing; the current candidate counts all observed party deaths.");
        if (this.DeepDungeon == DeepDungeon.PilgrimsTraverse)
            this.CurrentSaveSlot.Note("Pilgrim scoring uses an unverified EO-derived candidate. Juniper kills, candle effects, and final encounter weights need result samples.");
        this.HasStartedCapture = true;
        this.CaptureFileName = selected ? this.GetSaveSlotFileName(selection) : $"capture-{this.CurrentSaveSlot.RunId}.json";
        this.IsBossDead = this.CurrentSaveSlot.CurrentFloor()?.BossDefeated ?? false;
        this.CheckForCharacterStats();
        this.EnableFlyTextScore = true;
        this.SaveDeepDungeonData();
    }

    public void EnsureStarted(DataText dataText)
    {
        if (!this.IsInside || this.HasStartedCapture) return;
        var dungeon = this.GetDungeonDirector();
        if (dungeon != null) this.StartFirstFloor((int)dungeon->ContentId, dataText);
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
        var file = this.GetSaveSlotFileName(new(this.CurrentSaveSlot.DeepDungeon, number));
        if (string.IsNullOrWhiteSpace(file)) return;
        try
        {
            if (LocalStream.Exists(ServiceUtility.ConfigDirectory, file) &&
                !LocalStream.Copy(ServiceUtility.ConfigDirectory, ServiceUtility.ConfigDirectory,
                    file, $"archive-{Guid.NewGuid():N}.json"))
            {
                this.StorageError = "The destination slot could not be archived; its capture was left unchanged.";
                return;
            }
            this.SaveSlotSelection.SetSelectionData(this.CurrentSaveSlot.DeepDungeon, number);
            this.CaptureFileName = file;
            this.SaveSlotSelection.Save(this.CharacterKey);
            this.SaveDeepDungeonData();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            this.StorageError = e.Message;
            Service.PluginLog.Error(e, "Could not associate the capture with a save slot.");
        }
    }

    public void CheckForFloorChange()
    {
        if (!this.IsCapturing) return;
        var dungeon = this.GetDungeonDirector(this.ContentId);
        if (dungeon == null) return;
        if (dungeon->Floor > this.CurrentSaveSlot?.CurrentFloorNumber()) this.StartNextFloor();
        if (this.CompletionPending && this.IsLastFloor) this.DutyCompleted();
    }

    public void StartNextFloor()
    {
        if (!this.IsCapturing) return;
        var dungeon = this.GetDungeonDirector(this.ContentId);
        if (dungeon == null) return;
        var gap = dungeon->Floor - this.CurrentSaveSlot!.CurrentFloorNumber();
        if (!CaptureFloorTransition.Advance(this.CurrentSaveSlot, this.FloorSetTime, dungeon->Floor)) return;
        this.IsBossDead = false;
        this.IsBronzeCofferOpened = false;
        this.BossStatusTimerManager?.Dispose();
        this.BossStatusTimerManager = null;
        this.DefeatedBosses.Clear();
        this.RecordedDeaths.Clear();
        this.IsTransferenceInitiated = false;
        this.IsCairnOfPassageActivated = false;
        this.CairnOfPassageKillIds = [];
        this.CalculateScore(ScoreCalculationType.CurrentFloor);
        this.FloorEffect = new FloorEffect
        {
            ShowPomanderOfAffluence = gap == 1 && this.FloorEffect.IsPomanderOfAffluenceUsed,
            ShowPomanderOfFlight = gap == 1 && this.FloorEffect.IsPomanderOfFlightUsed,
            ShowPomanderOfAlteration = gap == 1 && this.FloorEffect.IsPomanderOfAlterationUsed
        };
        this.SaveDeepDungeonData();
    }
    private InstanceContentDeepDungeon* GetDungeonDirector(int expectedContentId = 0)
    {
        var framework = EventFramework.Instance();
        if (framework == null) return null;
        var director = framework->GetContentDirector();
        if (director == null || !this.CheckForValidContent((int)director->ContentId) ||
            (expectedContentId != 0 && director->ContentId != expectedContentId)) return null;
        return (InstanceContentDeepDungeon*)director;
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
        if (!this.IsCapturing || this.CurrentSaveSlot == null) return;
        if (!this.IsLastFloor) { this.CompletionPending = true; return; }
        this.CompletionPending = false;
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
        if (!this.IsCapturing || this.CurrentSaveSlot == null) return;
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

    public void RegenPotionConsumed() { if (this.IsCapturing) this.CurrentSaveSlot?.CurrentFloor()?.RegenPotionConsumed(); }

    public void BronzeChestOpened(Coffer coffer) { if (this.IsCapturing) this.CurrentSaveSlot?.CurrentFloor()?.CofferOpened(coffer); }

    public void PomanderObtained(int itemId)
    {
        if (!this.IsCapturing) return;
        this.IsBronzeCofferOpened = false;
        var pomander = PilgrimData.MapPomander(itemId);
        if (pomander.HasValue) this.CurrentSaveSlot?.CurrentFloor()?.CofferOpened((Coffer)pomander.Value);
        else this.CurrentSaveSlot?.Note($"Unmapped pomander item {itemId}; its coffer was not classified.");
    }

    public void AetherpoolObtained()
    {
        if (!this.IsCapturing) return;
        this.IsBronzeCofferOpened = false;
        if (!this.IsLastFloor)
            this.CurrentSaveSlot?.CurrentFloor()?.CofferOpened(Coffer.Aetherpool);
    }

    public void StoneObtained(int itemId)
    {
        if (!this.IsCapturing) return;
        this.IsBronzeCofferOpened = false;
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
        if (!this.IsCapturing) return;
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
        if (!this.IsCapturing) return;
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
        if (!this.IsCapturing) return;
        this.WasMagiciteUsed = false;
        this.WasScoreWindowShown = false;
        this.NearbyEnemies = [];
        this.IsTransferenceInitiated = true;
    }
}
