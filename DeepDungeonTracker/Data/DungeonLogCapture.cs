using System;
using Dalamud.Game.Chat;
using Dalamud.Game.ClientState.Conditions;

namespace DeepDungeonTracker;

public sealed class DungeonLogCapture : IDisposable
{
    private readonly DataCommon Common;
    public DungeonLogCapture(DataCommon common)
    {
        this.Common = common;
        Service.ChatGui.LogMessage += this.OnMessage;
    }
    public void Dispose() => Service.ChatGui.LogMessage -= this.OnMessage;

    private void OnMessage(ILogMessage message)
    {
        if (!Service.Condition[ConditionFlag.InDeepDungeon] || this.Common.ContentId == 0) return;
        try
        {
            var type = message.LogMessageId;
            if (type == 7248) { this.Common.TransferenceInitiated(); return; }
            if (type is >= 7250 and <= 7253) { this.Common.AetherpoolObtained(); return; }
            if (type == 11247) { this.Common.CurrentSaveSlot?.CurrentFloor()?.TrapTriggered(Trap.Fae); return; }
            if (type == 11259)
            {
                this.Common.CurrentSaveSlot?.CurrentFloor()?.CandleLit();
                return;
            }
            if (type is 7220 or 7221 && message.TryGetIntParameter(0, out var obtained))
                this.Common.PomanderObtained(obtained);
            else if (type is 9206 or 9207 or 10285 or 10286 && message.TryGetIntParameter(0, out var stone))
                this.Common.StoneObtained(stone);
            else if (type == 7254 && message.TryGetIntParameter(1, out var used))
                this.Common.PomanderUsed(used);
            else if (type is 9209 or 10288 or 11250 or 11251 && message.TryGetIntParameter(1, out var burned))
                this.Common.StoneUsed(burned);
        }
        catch (Exception e) { CaptureDiagnostics.Report("Dungeon log capture failed", e); }
    }
}
