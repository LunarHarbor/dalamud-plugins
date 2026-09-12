using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace DeepDungeonTracker;

public static class LocalStream
{
    private static JsonSerializerOptions Options => new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault };

    public static string? LastError { get; private set; }

    private static readonly object FileSync = new();
    private static readonly HashSet<string> UnreadablePaths = new(StringComparer.OrdinalIgnoreCase);

    public static Task Save<T>(string directory, string fileName, T data)
    {
        // Serialize before opening the destination.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(data, LocalStream.Options);
        var path = Path.GetFullPath(Path.Combine(directory, fileName));
        lock (FileSync)
        {
            Directory.CreateDirectory(directory);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes);
                    stream.Flush(true);
                }
                // A recovered save must not replace its good backup with the unreadable primary.
                if (File.Exists(path)) File.Replace(temporary, path, UnreadablePaths.Contains(path) ? null : path + ".bak");
                else File.Move(temporary, path);
                UnreadablePaths.Remove(path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        return Task.CompletedTask;
    }

    public static T? Load<T>(string directory, string fileName, Func<T, bool>? validate = null)
    {
        var path = Path.GetFullPath(Path.Combine(directory, fileName));
        lock (FileSync)
        {
            if (!File.Exists(path)) return default;
            T Read(string source)
            {
                var value = JsonSerializer.Deserialize<T>(File.ReadAllText(source));
                if (value is null || (validate != null && !validate(value)))
                    throw new JsonException("The saved data is empty or invalid.");
                return value;
            }

            try
            {
                var value = Read(path);
                UnreadablePaths.Remove(path);
                return value;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
            {
                UnreadablePaths.Add(path);
                LastError = $"Could not read {Path.GetFileName(path)}: {e.Message}";
                // Leave malformed input untouched while loading, and validate the previous save too.
                try { return File.Exists(path + ".bak") ? Read(path + ".bak") : default; }
                catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException or JsonException)
                { return default; }
            }
        }
    }
    public static bool Delete(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        if (File.Exists(path))
        {
            File.Delete(path);
            return true;
        }
        return false;
    }

    public static bool Exists(string directory) => Directory.Exists(directory);

    public static bool Exists(string directory, string fileName) => File.Exists(Path.Combine(directory, fileName));

    public static bool Copy(string sourceDirectory, string destDirectory, string sourceFileName, string destFileName)
    {
        if (!LocalStream.Exists(sourceDirectory))
            Directory.CreateDirectory(sourceDirectory);

        if (!LocalStream.Exists(destDirectory))
            Directory.CreateDirectory(destDirectory);

        if (LocalStream.Exists(sourceDirectory, sourceFileName))
        {
            File.Copy(Path.Combine(sourceDirectory, sourceFileName), Path.Combine(destDirectory, destFileName), true);
            return true;
        }
        return false;
    }

    public static bool Move(string sourceDirectory, string destDirectory, string sourceFileName, string destFileName)
    {
        if (!LocalStream.Exists(sourceDirectory))
            Directory.CreateDirectory(sourceDirectory);

        if (!LocalStream.Exists(destDirectory))
            Directory.CreateDirectory(destDirectory);

        if (LocalStream.Exists(sourceDirectory, sourceFileName))
        {
            File.Move(Path.Combine(sourceDirectory, sourceFileName), Path.Combine(destDirectory, destFileName), true);
            return true;
        }
        return false;
    }

    public static string[] GetFileNamesFromDirectory(string directory)
    {
        try { return LocalStream.Exists(directory) ? Directory.EnumerateFiles(directory).ToArray() : []; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LastError = $"Could not list saved captures: {e.Message}";
            return [];
        }
    }

    public static string[] GetCaptureFileNames(string directory, string backupsDirectory) =>
        GetFileNamesFromDirectory(backupsDirectory).Where(path => IsExtension(path, ".json"))
            .Concat(GetFileNamesFromDirectory(directory).Where(path =>
            {
                var name = Path.GetFileName(path);
                return IsExtension(name, ".json") &&
                    (name.StartsWith("capture-", StringComparison.OrdinalIgnoreCase) ||
                     name.StartsWith("attempt-", StringComparison.OrdinalIgnoreCase) ||
                     name.StartsWith("archive-", StringComparison.OrdinalIgnoreCase));
            })).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToArray();
    public static void OpenFolder(string directory)
    {
        if (!LocalStream.Exists(directory))
            Directory.CreateDirectory(directory);

        Process.Start(new ProcessStartInfo { FileName = directory, UseShellExecute = true });
    }

    public static string FormatFileName(string fileName, bool includeExtension) => includeExtension ? Path.GetFileName(fileName) : Path.GetFileNameWithoutExtension(fileName);

    public static bool IsExtension(string filename, string extension) => Path.GetExtension(filename).Equals(extension, StringComparison.OrdinalIgnoreCase);
}
