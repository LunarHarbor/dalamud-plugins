using Dalamud.Bindings.ImGui;
using System;
using System.Diagnostics;
using System.Text.Json;

namespace DeepDungeonTracker;

public sealed class ConfigurationWindow : WindowEx, IDisposable
{
    private Data Data { get; }
    private int ObservedScore;
    private int ObservedKills;
    private FloorSet? ResultSet;

    private Action MainWindowToggleVisibility { get; }

    private string[] FieldNames { get; }

    private static JsonSerializerOptions Options => new() { WriteIndented = true, };

    public ConfigurationWindow(string id, Configuration configuration, Action mainWindowToggleVisibility, Data data) : base(id, configuration, ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.Data = data;
        this.MainWindowToggleVisibility = mainWindowToggleVisibility;
        this.FieldNames = ["Kills", "Mimics", "Mandragoras", "Mimicgoras", "NPCs/Dread Beasts", "Coffers", "Enchantments", "Traps", "Deaths", "Regen Potions", "Potsherds/Fragments", "Lurings", "Maps", "Time Bonuses"];
        this.SizeConstraints = new() { MaximumSize = new(600.0f, 600.0f) };
    }

    public void Dispose() { }

    public override void Draw()
    {
        if (ImGui.BeginTabBar("Tab Bar"))
        {
            if (ImGui.BeginTabItem("General"))
            {
                this.General();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Main"))
            {
                this.Main();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Tracker"))
            {
                this.Tracker();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Floor Set Time"))
            {
                this.FloorSetTime();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Score"))
            {
                this.Score();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Statistics"))
            {
                this.Statistics();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Boss Status Timer"))
            {
                this.BossStatusTimer();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Validation"))
            {
                this.Validation();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
        ImGui.Separator();
        this.Button(this.Configuration.Reset, "Reset all settings to default", true);
        ImGui.SameLine();
    }

    private void General()
    {
        ImGui.Text($"Click to open the");
        ImGui.SameLine();
        this.SmallButton(() => this.MainWindowToggleVisibility(), "Main Window");
        ImGui.SameLine();
        ImGui.Text($"or type {Commands.MainCommand}");

        if (ImGui.CollapsingHeader("Tracking and save files"))
        {
            ImGui.TextWrapped("Tracking supports solo and parties of up to four. Keep the map open for map reveal and passage counters. Distant kills and a partner's bronze coffers may be missing from the client.");
            ImGui.Spacing();
            ImGui.TextWrapped("Score values are estimates from captured events. Pilgrim's Traverse uses a provisional Eureka Orthos model. Shortcut starts are supported; a partial capture cannot reconstruct earlier activity.");
            ImGui.Spacing();
            ImGui.TextWrapped("Runs are saved locally in the PilgrimDuo subfolder. Validation contains result comparisons, capture notes, and exports without character names. The original tracker save files are kept separately.");
        }
    }

    private void Validation()
    {
        var common = this.Data.Common;
        ImGui.TextWrapped("Compare captured events with the final game result here. This does not change the events used by the estimate.");
        ImGui.Spacing();
        ImGui.TextWrapped("Pilgrim's Traverse and party scoring still need real result samples. The model uses EO-derived weights, entry party size, and observed party deaths. Export a run after the result screen for calibration.");
        foreach (var issue in CaptureDiagnostics.Issues)
            ImGui.TextWrapped(issue);
        if (LocalStream.LastError != null) ImGui.TextWrapped(LocalStream.LastError);
        if (common.StorageError != null) ImGui.TextWrapped(common.StorageError);
        var slot = common.CurrentSaveSlot;
        if (slot?.CurrentFloorSet() is not { } set)
        {
            ImGui.TextDisabled("Enter a dungeon or load a saved run to compare results.");
            return;
        }
        ImGui.Separator();
        if (!ReferenceEquals(this.ResultSet, set))
        {
            this.ResultSet = set;
            this.ObservedScore = set.ObservedScore ?? 0;
            this.ObservedKills = set.ObservedKills ?? 0;
        }
        ImGui.Text($"{slot.DeepDungeon} / floor {slot.CurrentFloorNumber()} / entry party {set.PartySize}");
        ImGui.TextWrapped(slot.DeepDungeon == DeepDungeon.PilgrimsTraverse
            ? ScoreEngine.PilgrimRulesVersion : ScoreEngine.RulesVersion);
        var estimate = new Score(slot, true);
        estimate.TotalScoreCalculation(true, ScoreCalculationType.CurrentFloor);
        if (estimate.Breakdown is { } score)
        {
            ImGui.Text($"Current-floor estimate: {score.Total:N0}");
            if (set.ObservedScore is { } actual)
                ImGui.Text($"Game result: {actual:N0}   Difference: {score.Total - actual:+#,0;-#,0;0}");
            if (ImGui.CollapsingHeader("Score breakdown"))
            {
                ImGui.Text($"Character {score.Character:N0} / Floors {score.Floors:N0} / Kills {score.Kills:N0}");
                ImGui.Text($"Bonus units {score.BonusUnits:N0} x {score.Multiplier} = {score.Bonus:N0}");
                ImGui.Text($"Party contribution {score.Party:N0} / Time {score.Time:N0} / Bosses {score.Bosses:N0}");
            }
        }
        else ImGui.TextWrapped(estimate.UnavailableReason ?? "No estimate available.");
        ImGui.Text($"Captured kills: {slot.Kills():N0}   Game kills: {set.ObservedKills?.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) ?? "—"}");
        if (ImGui.CollapsingHeader("Enter a result manually"))
        {
            ImGui.TextWrapped("Use the final values displayed by the game if automatic capture did not read them.");
            ImGui.InputInt("Game score", ref this.ObservedScore);
            ImGui.InputInt("Game kills", ref this.ObservedKills);
            ImGui.BeginDisabled(this.ObservedScore <= 0 || this.ObservedKills < 0);
            if (ImGui.Button("Record result")) common.RecordObservedResult(this.ObservedScore, this.ObservedKills);
            ImGui.EndDisabled();
        }
        if (slot.CaptureNotes.Count > 0 && ImGui.CollapsingHeader("Capture notes"))
            foreach (var note in slot.CaptureNotes) ImGui.TextWrapped(note);
        if (ImGui.Button("Export run")) common.ExportCapture();
        if (common.LastExportPath != null)
        {
            ImGui.SameLine();
            if (ImGui.Button("Open exports")) LocalStream.OpenFolder(System.IO.Path.GetDirectoryName(common.LastExportPath)!);
            ImGui.TextDisabled("Exported locally without character names or IDs.");
        }
        if (this.Data.IsInsideDeepDungeon && ImGui.CollapsingHeader("Associate game save slot"))
        {
            ImGui.TextWrapped("Choose the in-game slot used for this run so the next set can resume it. An existing capture in that slot is backed up before replacement.");
            if (ImGui.Button("Use slot 1")) common.SelectCaptureSlot(1);
            ImGui.SameLine();
            if (ImGui.Button("Use slot 2")) common.SelectCaptureSlot(2);
        }
    }

    private void Main()
    {
        var config = this.Configuration.Main;
        this.CheckBox(config.SolidBackground, x => config.SolidBackground = x, "Solid Background");
        this.DragFloat(config.Scale, x => config.Scale = x, "Scale", 0.01f, 0.25f, 2.0f, "%.2f");
    }

    private void Tracker()
    {
        var config = this.Configuration.Tracker;
        this.CheckBox(config.Lock, x => config.Lock = x, "Lock");
        ImGui.SameLine();
        this.CheckBox(config.SolidBackground, x => config.SolidBackground = x, "Solid Background");
        this.CheckBox(config.Show, x => config.Show = x, "Show");
        WindowEx.Tooltip("You need to be either inside a Deep Dungeon or in the area outside to get into it.");
        ImGui.SameLine();
        this.CheckBox(config.ShowInBetweenFloors, x => config.ShowInBetweenFloors = x, "Show in between floors");
        this.CheckBox(config.ShowFloorEffectPomanders, x => config.ShowFloorEffectPomanders = x, "Show floor effect pomanders");
        WindowEx.Tooltip("Show pomander icons (at the top left of the window) representing their effect on the current floor.");
        this.Combo(config.FontType, x => config.FontType = x, "Font");
        this.DragFloat(config.Scale, x => config.Scale = x, "Scale", 0.01f, 0.25f, 2.0f, "%.2f");
        this.CheckBox(config.IsFloorNumberVisible, x => config.IsFloorNumberVisible = x, "##IsFloorNumberVisible");
        ImGui.SameLine();
        this.ColorEdit4(config.FloorNumberColor, x => config.FloorNumberColor = x, "Floor");
        ImGui.SameLine();
        this.CheckBox(config.IsSetNumberVisible, x => config.IsSetNumberVisible = x, "##IsSetNumberVisible");
        ImGui.SameLine();
        this.ColorEdit4(config.SetNumberColor, x => config.SetNumberColor = x, "Set");
        ImGui.SameLine();
        this.CheckBox(config.IsTotalNumberVisible, x => config.IsTotalNumberVisible = x, "##IsTotalNumberVisible");
        ImGui.SameLine();
        this.ColorEdit4(config.TotalNumberColor, x => config.TotalNumberColor = x, "Total");

        var left = ImGui.GetCursorPosX();
        for (var i = 0; i < config.Fields?.Length; i++)
        {
            var field = config.Fields[i];
            var fieldName = this.FieldNames[field.Index];
            var lastIndex = config.Fields.Length - 1;

            this.ArrowButton(() =>
            {
                var source = i;
                var target = i > 0 ? i - 1 : lastIndex;
                (config.Fields[source], config.Fields[target]) = (config.Fields[target], config.Fields[source]);
            }, $"##Up{fieldName}", ImGuiDir.Up, true);
            ImGui.SameLine();

            this.ArrowButton(() =>
            {
                var source = i;
                var target = i < lastIndex ? i + 1 : 0;
                (config.Fields[source], config.Fields[target]) = (config.Fields[target], config.Fields[source]);
            }, $"##Down{fieldName}", ImGuiDir.Down, true);
            ImGui.SameLine();

            this.CheckBox(field.Show, x => field.Show = x, $"#{i + 1:D2} {fieldName}");
        }
    }

    private void FloorSetTime()
    {
        var config = this.Configuration.FloorSetTime;
        this.CheckBox(config.Lock, x => config.Lock = x, "Lock");
        ImGui.SameLine();
        this.CheckBox(config.SolidBackground, x => config.SolidBackground = x, "Solid Background");
        this.CheckBox(config.Show, x => config.Show = x, "Show");
        WindowEx.Tooltip("You need to be either inside a Deep Dungeon or in the area outside to get into it.");
        ImGui.SameLine();
        this.CheckBox(config.ShowInBetweenFloors, x => config.ShowInBetweenFloors = x, "Show in between floors");
        ImGui.SameLine();
        this.CheckBox(config.ShowTitle, x => config.ShowTitle = x, "Show title");
        this.CheckBox(config.ShowFloorTime, x => config.ShowFloorTime = x, "Show floor time");
        this.DragFloat(config.Scale, x => config.Scale = x, "Scale", 0.01f, 0.25f, 2.0f, "%.2f");
        this.ColorEdit4(config.PreviousFloorTimeColor, x => config.PreviousFloorTimeColor = x, "Previous Floor Time");
        ImGui.SameLine();
        this.ColorEdit4(config.CurrentFloorTimeColor, x => config.CurrentFloorTimeColor = x, "Current Floor Time");
        this.ColorEdit4(config.AverageTimeColor, x => config.AverageTimeColor = x, "Average Time");
        ImGui.SameLine();
        this.ColorEdit4(config.RespawnTimeColor, x => config.RespawnTimeColor = x, "Respawn Time");
    }

    private void Score()
    {
        var config = this.Configuration.Score;
        this.CheckBox(config.Lock, x => config.Lock = x, "Lock");
        ImGui.SameLine();
        this.CheckBox(config.SolidBackground, x => config.SolidBackground = x, "Solid Background");
        this.CheckBox(config.Show, x => config.Show = x, "Show");
        WindowEx.Tooltip("You need to be either inside a Deep Dungeon or in the area outside to get into it.");
        ImGui.SameLine();
        this.CheckBox(config.ShowInBetweenFloors, x => config.ShowInBetweenFloors = x, "Show in between floors");
        ImGui.SameLine();
        this.CheckBox(config.ShowTitle, x => config.ShowTitle = x, "Show title");
        this.Combo(config.FontType, x => config.FontType = x, "Font");
        this.Combo(config.ScoreCalculationType, x => config.ScoreCalculationType = x, "Score Calculation");
        WindowEx.Tooltip(
            "Current Floor: Use captured progress, current character level, and observed Aetherpool.\n\n" +
            "Score Window Floor: Project floor and kill scaling to the next normal/challenge score endpoint, assuming maximum character level and Aetherpool.\n\n" +
            "Last Floor: Project the same values to the dungeon's final floor.\n\n" +
            "Projected modes do not award unobserved map clears, boss defeats, or completed sets. All modes remain estimates.");
        this.DragFloat(config.Scale, x => config.Scale = x, "Scale", 0.01f, 0.25f, 2.0f, "%.2f");
        this.CheckBox(config.IsFlyTextScoreVisible, x => config.IsFlyTextScoreVisible = x, "##IsFlyTextScoreVisible");
        WindowEx.Tooltip("When the score changes, a Fly Text will be shown.");
        ImGui.SameLine();
        this.ColorEdit4(config.FlyTextScoreColor, x => config.FlyTextScoreColor = x, "Fly Text Score");
        ImGui.SameLine();
        this.ColorEdit4(config.TotalScoreColor, x => config.TotalScoreColor = x, "Total Score");
    }

    private void Statistics()
    {
        var config = this.Configuration.Statistics;
        this.CheckBox(config.SolidBackground, x => config.SolidBackground = x, "Solid Background");
        this.DragFloat(config.Scale, x => config.Scale = x, "Scale", 0.01f, 0.25f, 2.0f, "%.2f");
        this.ColorEdit4(config.FloorTimeColor, x => config.FloorTimeColor = x, "Floor Time");
        ImGui.SameLine();
        this.ColorEdit4(config.ScoreColor, x => config.ScoreColor = x, "Score");
        ImGui.SameLine();
        this.ColorEdit4(config.SummarySelectionColor, x => config.SummarySelectionColor = x, "Summary Selection");
        this.CheckBox(config.ShowThreeRoomsFloor, x => config.ShowThreeRoomsFloor = x, "Show floor with 3 Rooms");
        ImGui.SameLine();
        this.CheckBox(config.ShowFourRoomsFloor, x => config.ShowFourRoomsFloor = x, "Show floor with 4 Rooms");
        this.CheckBox(config.ShowFiveRoomsFloor, x => config.ShowFiveRoomsFloor = x, "Show floor with 5 Rooms");
        ImGui.SameLine();
        this.CheckBox(config.ShowSixRoomsFloor, x => config.ShowSixRoomsFloor = x, "Show floor with 6 Rooms");
        this.CheckBox(config.ShowSevenRoomsFloor, x => config.ShowSevenRoomsFloor = x, "Show floor with 7 Rooms");
        ImGui.SameLine();
        this.CheckBox(config.ShowEightRoomsFloor, x => config.ShowEightRoomsFloor = x, "Show floor with 8 Rooms");
    }

    private void BossStatusTimer()
    {
        var config = this.Configuration.BossStatusTimer;
        this.CheckBox(config.SolidBackground, x => config.SolidBackground = x, "Solid Background");
        this.DragFloat(config.Scale, x => config.Scale = x, "Scale", 0.01f, 0.25f, 2.0f, "%.2f");
        this.CheckBox(config.IsStartTimeVisible, x => config.IsStartTimeVisible = x, "##IsStartTimeVisible");
        ImGui.SameLine();
        this.ColorEdit4(config.StartTimeColor, x => config.StartTimeColor = x, "Start Time");
        ImGui.SameLine();
        this.CheckBox(config.IsEndTimeVisible, x => config.IsEndTimeVisible = x, "##IsEndTimeVisible");
        ImGui.SameLine();
        this.ColorEdit4(config.EndTimeColor, x => config.EndTimeColor = x, "End Time");
        ImGui.SameLine();
        this.ColorEdit4(config.TotalTimeColor, x => config.TotalTimeColor = x, "Total Time");
    }
}
