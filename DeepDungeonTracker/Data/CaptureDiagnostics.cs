using System;
using System.Collections.Generic;

namespace DeepDungeonTracker;

public static class CaptureDiagnostics
{
    private static readonly Dictionary<string, string> Failures = [];
    public static IEnumerable<string> Issues => Failures.Values;

    public static void Report(string channel, Exception error)
    {
        if (Failures.ContainsKey(channel)) return;
        Failures[channel] = channel + ": " + error.Message;
        Service.PluginLog.Error(error, channel);
    }
}
