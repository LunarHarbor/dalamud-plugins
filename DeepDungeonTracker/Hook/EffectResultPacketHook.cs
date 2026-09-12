using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using DeepDungeonTracker.Event;
using System;

namespace DeepDungeonTracker.Hook
{
    public sealed unsafe class EffectResultPacketHook : IDisposable
    {
        private delegate void HandleEffectResultPacketDelegate(uint param1, byte* param2, byte param3);

        private readonly Hook<HandleEffectResultPacketDelegate>? _effectResultPacketHookDelegate;

        public EffectResultPacketHook()
        {
            _effectResultPacketHookDelegate =
                Service.GameInteropProvider.HookFromAddress<HandleEffectResultPacketDelegate>(
                    Service.SigScanner.ScanText(
                        "48 8B C4 44 88 40 ?? 89 48"),
                    HandleEffectResultPacketDetour);
            try { _effectResultPacketHookDelegate.Enable(); }
            catch { _effectResultPacketHookDelegate.Dispose(); throw; }
        }

        public void Dispose()
        {
            _effectResultPacketHookDelegate?.Dispose();
        }

        private void HandleEffectResultPacketDetour(uint param1, byte* param2, byte param3)
        {
            _effectResultPacketHookDelegate!.Original(param1, param2, param3);
            try
            {

                if (!Service.Condition[ConditionFlag.InDeepDungeon])
                    return;

                if (param2 == null || param1 != Service.ObjectTable.LocalPlayer?.EntityId) return;
                var id = *(ushort*)(param2 + 30);
                if (id == 648)
                    RegenPotionConsumedEvents.Publish();
            }
            catch (Exception e) { CaptureDiagnostics.Report("EffectResultPacketHook capture failed", e); }

        }
    }
}
