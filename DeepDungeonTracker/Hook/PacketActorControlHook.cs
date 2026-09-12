using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using DeepDungeonTracker.Event;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Network;
using System;

namespace DeepDungeonTracker.Hook;

public sealed unsafe class PacketActorControlHook : IDisposable
{
    private readonly Hook<PacketDispatcher.Delegates.HandleActorControlPacket> PacketHook;

    public PacketActorControlHook()
    {
        this.PacketHook = Service.GameInteropProvider.HookFromAddress<PacketDispatcher.Delegates.HandleActorControlPacket>(
            (nint)PacketDispatcher.MemberFunctionPointers.HandleActorControlPacket, this.ProcessPacket);
        try { this.PacketHook.Enable(); }
        catch { this.PacketHook.Dispose(); throw; }
    }

    public void Dispose() => this.PacketHook.Dispose();

    private void ProcessPacket(uint entityId, uint category, uint arg1, uint arg2, uint arg3, uint arg4,
        uint arg5, uint arg6, uint arg7, uint arg8, GameObjectId targetId, bool isRecorded)
    {
        this.PacketHook.Original(entityId, category, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, targetId, isRecorded);
        try
        {
            if (isRecorded || !Service.Condition[ConditionFlag.InDeepDungeon]) return;
            if (category == 0x6) CharacterKilledEvents.Publish(entityId);
            else if (category == 0x6D && arg2 == 0x4000_000E) NewFloorEvents.Publish();
            else if (category == 0x6D && arg2 == 0x4000_0005) DutyFailedEvents.Publish();
        }
        catch (Exception e) { CaptureDiagnostics.Report("Actor control capture failed", e); }
    }
}