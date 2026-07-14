using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;
using NonsensicalKit.Simulation.WarehouseSimulation.Playback;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime
{
    /// <summary>
    /// 堆垛机回放：将子任务时间轴聚合为宏观任务，驱动 JointController 堆垛机 Rig。
    /// </summary>
    public sealed class StackerPlaybackEventHandler : SimPlaybackEventHandlerBehaviour
    {
        [SerializeField, Label("硬件绑定")]
        private DefaultWarehouseSimulationBindingsAsset m_bindings;

        [SerializeField, Label("回放控制器（倍速）")]
        private WarehouseSimPlaybackController m_playback;

        [SerializeField, Label("货位坐标索引")]
        private WarehouseManagerBinPositionIndex m_positionIndex;

        [Header("堆垛机机组")]
        [SerializeField, Label("堆垛机绑定")]
        private JointControllerStackerJobRigPlayback[] m_stackers;

        [SerializeField, Label("取货默认层（地图未标层时）")]
        private int m_pickupLevel;

        [Header("料箱（与输送线共用实例池）")]
        [SerializeField, Label("料箱实例池")]
        private SimPlaybackCargoVisualRegistry m_cargoRegistry;

        private readonly SimSubTaskPlaybackIndex _subTaskIndex = new();
        private readonly Dictionary<int, JointControllerStackerJobRigPlayback> _executors = new();
        private readonly List<JointControllerStackerJobRigPlayback> _executorList = new();
        private ConveyorMapTopology _topology;
        private bool _loggedEvaluateSkip;
        private bool _rigsInitialized;

        private void Awake()
        {
            if (m_cargoRegistry == null)
            {
                m_cargoRegistry = GetComponent<SimPlaybackCargoVisualRegistry>();
            }

            RebuildTopology();
            RebuildExecutorIndex();
        }

        public override void ResetPlaybackState()
        {
            StopAllRigMotion();
            ConfigureAllRigs();
        }

        public override void OnPlaybackEvaluate(double simTime, IReadOnlyList<SimSubTask> subTasks)
        {
            EnsureExecutorsConfigured();

            if (subTasks == null || subTasks.Count == 0)
            {
                return;
            }

            if (!ValidateEvaluateReady(out var skipReason))
            {
                LogEvaluateSkipOnce(skipReason);
                return;
            }

            _loggedEvaluateSkip = false;

            if (!_subTaskIndex.IsCurrentSource(subTasks))
            {
                _subTaskIndex.Build(subTasks);
            }

            if (_executorList.Count == 0)
            {
                LogEvaluateSkipOnce("未绑定任何堆垛机执行器，回放不会更新堆垛机姿态。");
                return;
            }

            for (var i = 0; i < _executorList.Count; i++)
            {
                var executor = _executorList[i];
                if (executor == null)
                {
                    continue;
                }

                var stackerId = executor.StackerId;
                if (StackerJobPlaybackResolver.TryBuildActiveContext(
                        subTasks,
                        _subTaskIndex,
                        stackerId,
                        simTime,
                        out var context))
                {
                    executor.Evaluate(in context);
                    continue;
                }

                if (HasActiveStackerSubTask(subTasks, stackerId, simTime))
                {
                    LogEvaluateSkipOnce(
                        $"堆垛机 {stackerId} 在 t={simTime:F2}s 有活动子任务但无法构建回放上下文，本帧跳过 RestoreIdle。");
                    continue;
                }

                if (StackerJobPlaybackResolver.TryGetLastCompletedJob(
                        subTasks,
                        _subTaskIndex,
                        stackerId,
                        simTime,
                        out var last))
                {
                    executor.RestoreIdle(simTime, in last, hasLastTask: true);
                }
                else
                {
                    executor.RestoreIdle(simTime, default, hasLastTask: false);
                }
            }
        }

        private void StopAllRigMotion()
        {
            for (var i = 0; i < _executorList.Count; i++)
            {
                _executorList[i]?.StopMotion();
            }
        }

        private bool ValidateEvaluateReady(out string skipReason)
        {
            if (m_bindings == null)
            {
                skipReason = "未配置「硬件绑定」，堆垛机回放不会更新。";
                return false;
            }

            if (m_positionIndex == null)
            {
                skipReason = "未配置「货位坐标索引」，堆垛机回放不会更新。";
                return false;
            }

            if (!m_positionIndex.IsReady && !m_positionIndex.TryLoad())
            {
                skipReason = "货位坐标索引尚未加载，堆垛机回放不会更新。";
                return false;
            }

            skipReason = null;
            return true;
        }

        private void EnsureExecutorsConfigured()
        {
            if (_topology == null)
            {
                RebuildTopology();
            }

            if (_executors.Count == 0 || !_rigsInitialized)
            {
                RebuildExecutorIndex();
            }

            ConfigureAllRigs();
        }

        private void ConfigureAllRigs()
        {
            if (m_stackers == null)
            {
                return;
            }

            for (var i = 0; i < m_stackers.Length; i++)
            {
                var rig = m_stackers[i];
                if (rig == null)
                {
                    continue;
                }

                rig.Configure(
                    m_bindings,
                    m_playback,
                    m_positionIndex,
                    m_pickupLevel,
                    _topology,
                    m_cargoRegistry,
                    m_playback?.EventSource?.GridConfig);
            }
        }

        private void RebuildExecutorIndex()
        {
            _executors.Clear();
            _executorList.Clear();
            _rigsInitialized = false;
            if (m_stackers == null)
            {
                return;
            }

            for (var i = 0; i < m_stackers.Length; i++)
            {
                var rig = m_stackers[i];
                if (rig == null)
                {
                    continue;
                }

                rig.Configure(
                    m_bindings,
                    m_playback,
                    m_positionIndex,
                    m_pickupLevel,
                    _topology,
                    m_cargoRegistry,
                    m_playback?.EventSource?.GridConfig);

                if (!rig.TryInitialize())
                {
                    continue;
                }

                _executors[rig.StackerId] = rig;
                _executorList.Add(rig);
            }

            _rigsInitialized = _executorList.Count > 0;
            if (_executorList.Count == 0)
            {
                SimPlaybackLog.Warn(
                    "未成功绑定任何堆垛机 Rig（请检查 m_stackers、关节控制器引用及 m_stackerId）。",
                    this);
            }
        }

        private bool RebuildTopology()
        {
            _topology = null;
            return SimPlaybackTopologyUtility.TryBuild(m_bindings?.ConveyorMap, m_bindings, out _topology, this);
        }

        private static bool HasActiveStackerSubTask(
            IReadOnlyList<SimSubTask> subTasks,
            int stackerId,
            double simTime) =>
            SimSubTaskQuery.TryGetActiveForStacker(subTasks, stackerId, simTime, out _);

        private void LogEvaluateSkipOnce(string reason)
        {
            if (_loggedEvaluateSkip)
            {
                return;
            }

            _loggedEvaluateSkip = true;
            SimPlaybackLog.Warn($"StackerHandler {reason}", this);
        }
    }
}
