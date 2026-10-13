#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Playback;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Editor
{
    [CustomEditor(typeof(SimPlaybackEventSource))]
    public sealed class SimPlaybackEventSourceEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var source = (SimPlaybackEventSource)target;
            var events = source.Events?.Count ?? 0;
            var subTasks = source.SubTasks?.Count ?? 0;
            var initial = source.InitialOccupiedSlots?.Count ?? 0;
            EditorGUILayout.HelpBox(
                $"回放数据由仿真运行器写入，不在 Inspector 展开。\n" +
                $"事件 {events}，子任务 {subTasks}，初始占用 {initial}，仿真时长 {source.TotalSimSeconds:0.##}s。",
                MessageType.Info);
        }
    }
}
#endif
