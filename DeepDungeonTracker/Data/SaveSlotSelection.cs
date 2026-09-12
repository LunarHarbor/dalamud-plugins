using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DeepDungeonTracker;

public class SaveSlotSelection
{
    public record SaveSlotSelectionData(DeepDungeon DeepDungeon = DeepDungeon.None, int SaveSlotNumber = 0);

    private static string FileName => $"_{nameof(SaveSlotSelection)}.json";

    private Dictionary<string, SaveSlotSelectionData> Data { get; }
    private string Directory { get; }
    private Action<Exception>? ReportError { get; }
    private SaveSlotSelectionData? CurrentSelectionData { get; set; }

    public SaveSlotSelection(string directory, Action<Exception>? reportError = null)
    {
        this.Directory = directory;
        this.ReportError = reportError;
        this.Data = LocalStream.Load<Dictionary<string, SaveSlotSelectionData>>(directory, FileName,
            values => values.All(entry => IsValidKey(entry.Key) && IsValidSelection(entry.Value))) ?? [];
    }

    private static bool IsValidKey(string key) => !string.IsNullOrWhiteSpace(key) && key.Length > 3 &&
        key.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private static bool IsValidSelection(SaveSlotSelectionData? data) => data != null &&
        Enum.IsDefined(data.DeepDungeon) && data.DeepDungeon != DeepDungeon.None && data.SaveSlotNumber is >= 0 and <= 2;

    public static string GetSaveSlotFileName(string key, SaveSlotSelectionData? data, bool last = false) =>
        IsValidKey(key) && IsValidSelection(data) && data!.SaveSlotNumber is 1 or 2
            ? $"{key}-dd{(int)data.DeepDungeon}s{data.SaveSlotNumber}{(last ? "Last" : string.Empty)}.json" : string.Empty;

    public void ResetSelectionData() => this.CurrentSelectionData = null;

    public void SetSelectionData(DeepDungeon deepDungeon, int saveSlotNumber)
    {
        var selection = new SaveSlotSelectionData(deepDungeon, saveSlotNumber);
        this.CurrentSelectionData = IsValidSelection(selection) ? selection : null;
    }

    // A remembered browsing choice is not evidence of the slot used by the current game duty.
    public SaveSlotSelectionData? GetCaptureSelectionData() => this.CurrentSelectionData;

    public SaveSlotSelectionData? GetSelectionData(string key) => this.Data.GetValueOrDefault(key);

#pragma warning disable CA1024
    public IReadOnlyDictionary<string, SaveSlotSelectionData> GetData() => new ReadOnlyDictionary<string, SaveSlotSelectionData>(this.Data);
#pragma warning restore CA1024

    public void Save(string key)
    {
        if (IsValidKey(key) && this.CurrentSelectionData != null)
            this.Data[key] = this.CurrentSelectionData;
        try { LocalStream.Save(this.Directory, FileName, this.Data).GetAwaiter().GetResult(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            if (this.ReportError == null) throw;
            this.ReportError(e);
        }
    }
}
