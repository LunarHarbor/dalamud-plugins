using System.Text.Json;
using DeepDungeonTracker;

public static class UiSaveReviewChecks
{
    public static int Run()
    {
        var checks = 0;
        void Equal<T>(T expected, T actual, string label)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception($"{label}: expected {expected}, got {actual}");
            checks++;
        }

        var directory = Path.Combine(Path.GetTempPath(), "ddt-ui-save-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            LocalStream.Save(directory, "recovery.json", "first").GetAwaiter().GetResult();
            LocalStream.Save(directory, "recovery.json", "second").GetAwaiter().GetResult();
            var recoveryPath = Path.Combine(directory, "recovery.json");
            File.WriteAllText(recoveryPath, "null");
            Equal("first", LocalStream.Load<string>(directory, "recovery.json"), "null primary recovers backup");
            Equal("null", File.ReadAllText(recoveryPath), "null primary is untouched on load");
            LocalStream.Save(directory, "recovery.json", "third").GetAwaiter().GetResult();
            Equal("first", LocalStream.Load<string>(directory, "recovery.json.bak"), "repair does not overwrite good backup with null");
            Equal("third", LocalStream.Load<string>(directory, "recovery.json"), "recovery checkpoint saved");
            LocalStream.Save(directory, "recovery.json", "fourth").GetAwaiter().GetResult();
            Equal("third", LocalStream.Load<string>(directory, "recovery.json.bak"), "normal backup rotation resumes after repair");
            File.WriteAllText(recoveryPath, "\"invalid\"");
            Equal("third", LocalStream.Load<string>(directory, "recovery.json", value => value != "invalid"), "structurally invalid primary recovers valid backup");
            File.WriteAllText(recoveryPath + ".bak", "\"invalid\"");
            Equal<string?>(null, LocalStream.Load<string>(directory, "recovery.json", value => value != "invalid"), "backup uses same validation as primary");

            const string character = "Synthetic-World";
            var selection = new SaveSlotSelection(directory);
            Equal<SaveSlotSelection.SaveSlotSelectionData?>(null, selection.GetSelectionData(character), "installation has no inferred historical slot");
            var slotOne = new SaveSlotSelection.SaveSlotSelectionData(DeepDungeon.PilgrimsTraverse, 1);
            var slotTwo = slotOne with { SaveSlotNumber = 2 };
            var fileOne = SaveSlotSelection.GetSaveSlotFileName(character, slotOne);
            var fileTwo = SaveSlotSelection.GetSaveSlotFileName(character, slotTwo);
            Equal(false, fileOne == fileTwo, "two game slots use independent capture destinations");
            Equal<SaveSlot?>(null, LocalStream.Load<SaveSlot>(directory, fileOne), "first installed slot has no fabricated prior capture");
            Equal<SaveSlot?>(null, LocalStream.Load<SaveSlot>(directory, fileTwo), "second installed slot has no fabricated prior capture");
            SaveSlot PartialRun(int floor)
            {
                var run = new SaveSlot(DeepDungeon.PilgrimsTraverse, classJobId: 19, currentLevel: 100);
                run.MarkCurrentSchema();
                run.AddFloorSet(floor);
                run.Note("Tracking began after this game save had already started.");
                return run;
            }
            var first = PartialRun(31);
            var second = PartialRun(51);
            selection.SetSelectionData(slotOne.DeepDungeon, slotOne.SaveSlotNumber);
            selection.Save(character);
            LocalStream.Save(directory, fileOne, first).GetAwaiter().GetResult();
            selection.SetSelectionData(slotTwo.DeepDungeon, slotTwo.SaveSlotNumber);
            selection.Save(character);
            LocalStream.Save(directory, fileTwo, second).GetAwaiter().GetResult();
            Equal(first.RunId, LocalStream.Load<SaveSlot>(directory, fileOne)!.RunId, "slot two capture leaves slot one identity intact");
            Equal(second.RunId, LocalStream.Load<SaveSlot>(directory, fileTwo)!.RunId, "slot two retains independent run identity");
            var reload = new SaveSlotSelection(directory);
            Equal(slotTwo, reload.GetSelectionData(character), "last browsing selection survives reload");
            Equal<SaveSlotSelection.SaveSlotSelectionData?>(null, reload.GetCaptureSelectionData(), "historical slot is never inferred after reload");
            reload.Save(character);
            Equal<SaveSlotSelection.SaveSlotSelectionData?>(null, reload.GetCaptureSelectionData(), "saving metadata cannot fabricate current slot evidence");
            reload.SetSelectionData(slotOne.DeepDungeon, slotOne.SaveSlotNumber);
            reload.Save(character);
            Equal(slotOne, reload.GetCaptureSelectionData(), "explicit observed slot is available for capture");
            Equal(31, LocalStream.Load<SaveSlot>(directory, SaveSlotSelection.GetSaveSlotFileName(character, reload.GetSelectionData(character)))!.CurrentFloorNumber(), "switching back loads only first slot history");
            reload.ResetSelectionData();
            Equal<SaveSlotSelection.SaveSlotSelectionData?>(null, reload.GetCaptureSelectionData(), "leaving duty clears current slot evidence");
            Equal(slotOne, reload.GetSelectionData(character), "reset retains historical browsing selection");
            Equal<SaveSlotSelection.SaveSlotSelectionData?>(null, reload.GetCaptureSelectionData(), "reading history does not restore current evidence");
            reload.SetSelectionData(slotOne.DeepDungeon, 3);
            reload.Save(character);
            Equal(slotOne, reload.GetSelectionData(character), "invalid slot cannot replace remembered choice");
            Equal(string.Empty, SaveSlotSelection.GetSaveSlotFileName(character, slotOne with { SaveSlotNumber = 0 }), "unknown game slot has no normal save destination");
            Equal(string.Empty, SaveSlotSelection.GetSaveSlotFileName("../Synthetic", slotOne), "invalid character key cannot create a path");
            reload.SetSelectionData(DeepDungeon.HeavenOnHigh, 2);
            reload.Save("Other-World");
            Equal(slotOne, reload.GetSelectionData(character), "second character keeps separate selection");
            File.WriteAllText(Path.Combine(directory, "_SaveSlotSelection.json"), "{\"Synthetic-World\":null}");
            Equal(slotOne, new SaveSlotSelection(directory).GetSelectionData(character), "null metadata entry falls back instead of crashing menu");

            var backupsDirectory = Path.Combine(directory, "Backups");
            Directory.CreateDirectory(backupsDirectory);
            foreach (var name in new[] { "capture-one.json", "attempt-two.json", "archive-three.json", "capture-four.json.bak", "unrelated.json" })
                File.WriteAllText(Path.Combine(directory, name), "{}");
            File.WriteAllText(Path.Combine(backupsDirectory, "named-backup.json"), "{}");
            var capturePaths = LocalStream.GetCaptureFileNames(directory, backupsDirectory);
            Equal(4, capturePaths.Length, "browser includes unassociated and archived runs but excludes unrelated data");
            Equal(true, capturePaths.Contains(Path.Combine(directory, "capture-one.json")), "anonymous run stays accessible after restart");
            Equal(true, capturePaths.Contains(Path.Combine(backupsDirectory, "named-backup.json")), "browser preserves full backup destination");
            Equal(true, new BossStatusTimerData().IsValid(), "active boss timer is valid before an end time exists");
            Equal(false, JsonSerializer.Deserialize<BossStatusTimerData>("{\"Combat\":null}")!.IsValid(), "null combat timer rejected");
            Equal(false, JsonSerializer.Deserialize<BossStatusTimerData>("{\"Medicated\":[null]}")!.IsValid(), "null timer list entry rejected");
            Equal(false, JsonSerializer.Deserialize<BossStatusTimerData>("{\"Combat\":{\"Start\":\"2026-01-02T00:00:00\",\"End\":\"2026-01-01T00:00:00\"}}")!.IsValid(), "negative boss duration rejected");
            var timers = new BossStatusTimerData();
            timers.VulnerabilityUp.Add(new(BossStatusTimer.VulnerabilityUp));
            timers.Update(3);
            timers.Update(1);
            Equal((byte)3, timers.VulnerabilityUp.Single().Stacks, "timer keeps maximum observed vulnerability stacks");

            var config = new Configuration
            {
                Main = null!, FloorSetTime = null!, Statistics = null!, BossStatusTimer = null!,
                Tracker = new() { Scale = float.NaN, FontType = (FontType)999,
                    Fields = [new() { Index = 8, Show = false }, null!, new() { Index = 8 }, new() { Index = 99 }] },
                Score = new() { Scale = 0, ScoreCalculationType = (ScoreCalculationType)999 }
            };
            config.Normalize();
            Equal(1f, config.Main.Scale, "null config section restored");
            Equal(1f, config.Tracker.Scale, "nonfinite scale repaired");
            Equal(0.25f, config.Score.Scale, "zero scale clamped");
            Equal(FontType.Default, config.Tracker.FontType, "invalid font repaired");
            Equal(ScoreCalculationType.CurrentFloor, config.Score.ScoreCalculationType, "invalid score mode repaired");
            Equal(14, config.Tracker.Fields.Length, "missing fields restored and duplicates removed");
            Equal(8, config.Tracker.Fields[0].Index, "valid custom field order preserved");
            Equal(false, config.Tracker.Fields[0].Show, "valid custom field visibility preserved");
            config.Normalize();
            Equal(14, config.Tracker.Fields.Select(field => field.Index).Distinct().Count(), "config repair is idempotent");
        }
        finally
        {
            var resolved = Path.GetFullPath(directory);
            if (!resolved.StartsWith(Path.Combine(Path.GetTempPath(), "ddt-ui-save-checks-"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unexpected temporary UI/save check directory.");
            Directory.Delete(resolved, true);
        }
        return checks;
    }
}

// Only the configuration persistence host is stubbed; repair logic uses the production model.
namespace Dalamud.Configuration
{
    public interface IPluginConfiguration { int Version { get; set; } }
}
namespace Dalamud.Plugin
{
    public interface IDalamudPluginInterface { void SavePluginConfig(object configuration); }
}
