using System;
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
    /// 输送线回放：按子任务时间轴与 <see cref="ConveyorSegmentScheduleEntry"/> 在任意仿真时刻定位料箱。
    /// </summary>
    public sealed partial class ConveyorPlaybackEventHandler : SimPlaybackEventHandlerBehaviour
    {
        [SerializeField, Label("硬件绑定")]
        private WarehouseSimulationBindingsAsset m_bindings;
        [SerializeField, Label("回放控制器（倍速）")]
        private WarehouseSimPlaybackController m_playback;
        [SerializeField, Label("回放事件源")]
        private SimPlaybackEventSource m_eventSource;
        [SerializeField, Label("料箱实例池")]
        private SimPlaybackCargoVisualRegistry m_cargoRegistry;
        [Header("场景锚点（可选）")]
        [SerializeField]
        private Transform[] m_nodeAnchors;
        [SerializeField, Label("锚点根节点（按子物体名称匹配逻辑 ID）")]
        private Transform m_anchorRoot;

        private ConveyorMapTopology _topology;
        private readonly SimPlaybackNodeAnchorIndex _anchorIndex = new();
        private readonly Dictionary<int, ConveyorSegmentScheduleEntry[]> _scheduleByJob = new();
        private readonly SimSubTaskPlaybackIndex _subTaskIndex = new();
        private readonly HashSet<int> _outboundJobIds = new();
        private readonly List<int> _evaluateJobIds = new();

        private IWarehouseSimulationBindings Bindings => m_bindings as IWarehouseSimulationBindings;

        private void Awake()
        {
            if (m_cargoRegistry == null)
            {
                m_cargoRegistry = GetComponent<SimPlaybackCargoVisualRegistry>();
            }

            if (m_eventSource == null && m_playback != null)
            {
                m_eventSource = m_playback.EventSource;
            }

            RebuildTopology();
            RebuildAnchorIndex();
        }

        public override void ResetPlaybackState()
        {
            m_cargoRegistry?.ReleaseAll();
            _scheduleByJob.Clear();
            _outboundJobIds.Clear();
            _subTaskIndex.Build(System.Array.Empty<SimSubTask>());
        }

        public override void OnPlaybackEvaluate(double simTime, IReadOnlyList<SimSubTask> subTasks)
        {
            if (!EnsureReady() || subTasks == null || subTasks.Count == 0)
            {
                return;
            }

            EnsureTimelineIndex(subTasks);
            _subTaskIndex.CopyJobIds(_evaluateJobIds);
            for (var i = 0; i < _evaluateJobIds.Count; i++)
            {
                var jobId = _evaluateJobIds[i];
                if (!_subTaskIndex.IsJobVisibleAt(jobId, simTime))
                {
                    m_cargoRegistry?.Release(jobId);
                    continue;
                }

                EvaluateJob(jobId, simTime, subTasks);
            }
        }

        private void EnsureTimelineIndex(IReadOnlyList<SimSubTask> subTasks)
        {
            if (_subTaskIndex.IsCurrentSource(subTasks))
            {
                return;
            }

            _subTaskIndex.Build(subTasks);
            _outboundJobIds.Clear();
            _outboundJobIds.UnionWith(SimSubTaskQuery.BuildOutboundJobIds(subTasks));
            RebuildScheduleCache(subTasks);
        }

        private bool EnsureReady()
        {
            if (Bindings == null || Bindings.ConveyorMap == null || m_cargoRegistry == null)
            {
                return false;
            }

            if (_topology == null && !RebuildTopology())
            {
                return false;
            }

            return true;
        }

        private bool RebuildTopology() =>
            SimPlaybackTopologyUtility.TryBuild(Bindings?.ConveyorMap, Bindings, out _topology, this);
    }
}
