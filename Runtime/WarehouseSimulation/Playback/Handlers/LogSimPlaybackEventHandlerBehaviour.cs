using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Playback
{
    /// <summary>调试用途：将回放事件逐条输出到 Unity Console。</summary>
    [AddComponentMenu("Warehouse Simulation/Log Playback Event Handler")]
    public sealed class LogSimPlaybackEventHandlerBehaviour : SimPlaybackEventHandlerBehaviour
    {
        public override void OnPlaybackEvent(SimPlaybackEvent evt)
        {
            Debug.Log($"{SimPlaybackLog.Tag} {SimPlaybackLog.FormatEvent(evt)}");
        }
    }
}
