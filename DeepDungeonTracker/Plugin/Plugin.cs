using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Inventory.InventoryEventArgTypes;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using DeepDungeonTracker.Hook;
using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Chat;
using Dalamud.Game.DutyState;

namespace DeepDungeonTracker;

public sealed class Plugin : IDalamudPlugin
{
    public static string Name => "Deep Dungeon Tracker: Pilgrim & Party";

    private Commands Commands { get; }

    private WindowSystem WindowSystem { get; }

    private Configuration Configuration { get; }

    private Data Data { get; }


    private readonly PacketActorControlHook? _packetActorControlHook;

    private readonly DungeonLogCapture? _systemLogMessageHook;

    private readonly EffectResultPacketHook? _effectResultPacketHook;

    private readonly OpenTreasurePacketHook? _openTreasurePacketHook;

    private bool IsDisposed;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface?.Create<Service>();

        this.Configuration = LoadConfiguration();
        this.Configuration.Initialize(Service.PluginInterface);

        this.Data = new(pluginInterface?.UiBuilder!, this.Configuration);
        this.Commands = new(Plugin.Name, this.OnConfigCommand, this.OnMainCommand, this.OnTrackerCommand, this.OnTimeCommand, this.OnScoreCommand, this.OnLoadCommand);

        this.WindowSystem = new(Plugin.Name.Replace(" ", string.Empty, StringComparison.InvariantCultureIgnoreCase));
#pragma warning disable CA2000
        this.WindowSystem.AddWindow(new ConfigurationWindow(Plugin.Name, this.Configuration, this.MainWindowToggleVisibility, this.Data));
        this.WindowSystem.AddWindow(new MainWindow(Plugin.Name, this.Configuration, this.Data, this.OpenWindow<StatisticsWindow>));
        this.WindowSystem.AddWindow(new TrackerWindow(Plugin.Name, this.Configuration, this.Data));
        this.WindowSystem.AddWindow(new FloorSetTimeWindow(Plugin.Name, this.Configuration, this.Data));
        this.WindowSystem.AddWindow(new ScoreWindow(Plugin.Name, this.Configuration, this.Data));
        this.WindowSystem.AddWindow(new StatisticsWindow(Plugin.Name, this.Configuration, this.Data, this.MainWindowToggleVisibility, this.BossStatusTimerWindowToggleVisibility));
        this.WindowSystem.AddWindow(new BossStatusTimerWindow(Plugin.Name, this.Configuration, this.Data));
#pragma warning restore CA2000

        Service.PluginInterface.UiBuilder.DisableAutomaticUiHide = false;
        Service.PluginInterface.UiBuilder.DisableCutsceneUiHide = false;
        Service.PluginInterface.UiBuilder.DisableGposeUiHide = false;
        Service.PluginInterface.UiBuilder.DisableUserUiHide = false;

        Service.PluginInterface.UiBuilder.Draw += this.Draw;
        Service.PluginInterface.UiBuilder.OpenConfigUi += this.OpenConfigUi;
        Service.PluginInterface.UiBuilder.OpenMainUi += this.OpenMainUi;

        Service.Framework.Update += this.Update;
        Service.ClientState.Login += this.Login;
        Service.ClientState.TerritoryChanged += this.TerritoryChanged;
        Service.Condition.ConditionChange += this.ConditionChange;
        Service.ChatGui.ChatMessage += this.ChatMessage;
        Service.DutyState.DutyStarted += this.DutyStarted;
        Service.DutyState.DutyCompleted += this.DutyCompleted;
        Service.GameInventory.InventoryChangedRaw += this.InventoryChangedRaw;

        _packetActorControlHook = CreateHook(() => new PacketActorControlHook(), "Death events");
        _systemLogMessageHook = CreateHook(() => new DungeonLogCapture(this.Data.Common), "Dungeon items");
        _effectResultPacketHook = CreateHook(() => new EffectResultPacketHook(), "Regen potions");
        _openTreasurePacketHook = CreateHook(() => new OpenTreasurePacketHook(this.Data.Common), "Bronze coffers");
    }

    private static Configuration LoadConfiguration()
    {
        try { return Service.PluginInterface.GetPluginConfig() as Configuration ?? new(); }
        catch (Exception e)
        {
            CaptureDiagnostics.Report("Configuration could not be loaded; defaults are in use", e);
            return new();
        }
    }

    private static void DisposeComponent(IDisposable? component)
    {
        try { component?.Dispose(); }
        catch (Exception e) { CaptureDiagnostics.Report($"Disposal failed for {component?.GetType().Name}", e); }
    }

    private static T? CreateHook<T>(Func<T> factory, string channel) where T : class
    {
        try { return factory(); }
        catch (Exception e)
        {
            CaptureDiagnostics.Report(channel + " unavailable", e);
            return null;
        }
    }

    public void Dispose()
    {
        if (this.IsDisposed) return;
        this.IsDisposed = true;
        Service.PluginInterface.UiBuilder.Draw -= this.Draw;
        Service.PluginInterface.UiBuilder.OpenConfigUi -= this.OpenConfigUi;
        Service.PluginInterface.UiBuilder.OpenMainUi -= this.OpenMainUi;

        Service.Framework.Update -= this.Update;
        Service.ClientState.Login -= this.Login;
        Service.ClientState.TerritoryChanged -= this.TerritoryChanged;
        Service.Condition.ConditionChange -= this.ConditionChange;
        Service.ChatGui.ChatMessage -= this.ChatMessage;
        Service.DutyState.DutyStarted -= this.DutyStarted;
        Service.DutyState.DutyCompleted -= this.DutyCompleted;
        Service.GameInventory.InventoryChangedRaw -= this.InventoryChangedRaw;

        DisposeComponent(this._packetActorControlHook);
        DisposeComponent(this._systemLogMessageHook);
        DisposeComponent(this._effectResultPacketHook);
        DisposeComponent(this._openTreasurePacketHook);

        foreach (var window in this.WindowSystem.Windows) DisposeComponent(window as IDisposable);
        this.WindowSystem.RemoveAllWindows();

        DisposeComponent(this.Commands);
        DisposeComponent(this.Data);
    }

    private void OnConfigCommand(string command, string args) => this.OpenConfigUi();

    private void OnMainCommand(string command, string args) => this.MainWindowToggleVisibility();

    private void OnTrackerCommand(string command, string args)
    {
        this.Configuration.Tracker.Show = !this.Configuration.Tracker.Show;
        this.Configuration.Save();
    }

    private void OnTimeCommand(string command, string args)
    {
        this.Configuration.FloorSetTime.Show = !this.Configuration.FloorSetTime.Show;
        this.Configuration.Save();
    }

    private void OnScoreCommand(string command, string args)
    {
        this.Configuration.Score.Show = !this.Configuration.Score.Show;
        this.Configuration.Save();
    }

    private void OnLoadCommand(string command, string args)
    {
        if (!this.IsWindowOpen<StatisticsWindow>())
        {
            if (!this.Data.IsInsideDeepDungeon)
                this.Data.Common.LoadDeepDungeonData(false, true);

            this.Data.Statistics.Load(this.Data.Common.CurrentSaveSlot, this.OpenWindow<StatisticsWindow>);
        }
        else
            this.CloseWindow<StatisticsWindow>();
    }

    private void MainWindowToggleVisibility()
    {
        if (!this.IsWindowOpen<MainWindow>())
            this.OpenWindow<MainWindow>();
        else
            this.CloseWindow<MainWindow>();
    }

    private void BossStatusTimerWindowToggleVisibility()
    {
        if (!this.IsWindowOpen<BossStatusTimerWindow>())
            this.OpenWindow<BossStatusTimerWindow>();
        else
            this.CloseWindow<BossStatusTimerWindow>();
    }

    private void Draw() => this.WindowSystem.Draw();

    private void OpenConfigUi() => this.WindowSystem.Windows.FirstOrDefault(x => x is ConfigurationWindow)!.Toggle();

    private void OpenMainUi() => this.MainWindowToggleVisibility();

    private void Update(IFramework framework)
    {
        try { this.Data.Update(this.Configuration); }
        catch (Exception e) { CaptureDiagnostics.Report("Tracking update failed", e); }
    }

    private void Login() => this.Data.Login();

    private void TerritoryChanged(uint territoryType) => this.Data.TerritoryChanged(territoryType);

    private void ConditionChange(ConditionFlag flag, bool value) => this.Data.ConditionChange(flag, value);

    private void ChatMessage(IHandleableChatMessage message) => this.Data.ChatMessage(message.Message.TextValue);

    private void DutyStarted(IDutyStateEventArgs args) => this.Data.DutyStarted(args.TerritoryType.RowId);

    private void DutyCompleted(IDutyStateEventArgs args) => this.Data.DutyCompleted();

    private void InventoryChangedRaw(IReadOnlyCollection<InventoryEventArgs> events) => this.Data.InventoryChangedRaw(events);

    private void OpenWindow<T>() where T : Window => this.WindowSystem.Windows.FirstOrDefault(x => x is T)!.IsOpen = true;

    private void CloseWindow<T>() where T : Window => this.WindowSystem.Windows.FirstOrDefault(x => x is T)!.IsOpen = false;

    private bool IsWindowOpen<T>() where T : Window => this.WindowSystem.Windows.FirstOrDefault(x => x is T)!.IsOpen;
}
