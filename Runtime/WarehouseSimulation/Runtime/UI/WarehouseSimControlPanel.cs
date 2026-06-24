using NaughtyAttributes;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Playback;
using NonsensicalKit.Simulation.WarehouseSimulation.Runtime.Runner;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime.UI
{
    /// <summary>
    /// 场景级 UI 根组件：统一引用仿真运行器、回放控制器与各 ControlPart，便于一次性挂载整套控制面板。
    /// </summary>
    public sealed class WarehouseSimControlPanel : MonoBehaviour
    {
        [SerializeField, Label("仿真运行器")]
        private WarehouseSimRunner m_runner;

        [SerializeField, Label("回放控制器")]
        private WarehouseSimPlaybackController m_playback;

        [SerializeField, Label("仿真控制")]
        private WarehouseSimRunControlPart m_runControl;

        [SerializeField, Label("回放控制")]
        private WarehouseSimPlaybackControlPart m_playbackControl;

        [SerializeField, Label("进度控制")]
        private WarehouseSimProgressControlPart m_progressControl;

        public WarehouseSimRunner Runner => m_runner;
        public WarehouseSimPlaybackController Playback => m_playback;
        public WarehouseSimRunControlPart RunControl => m_runControl;
        public WarehouseSimPlaybackControlPart PlaybackControl => m_playbackControl;
        public WarehouseSimProgressControlPart ProgressControl => m_progressControl;
    }
}
