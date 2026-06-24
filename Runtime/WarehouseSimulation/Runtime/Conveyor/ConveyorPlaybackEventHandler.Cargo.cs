using System;
using System.Collections.Generic;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;
using NonsensicalKit.Simulation.WarehouseSimulation.Core;
using NonsensicalKit.Simulation.WarehouseSimulation.Model;
using NonsensicalKit.Simulation.WarehouseSimulation.Playback;
namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime
{
    public sealed partial class ConveyorPlaybackEventHandler
    {
        private void ShowAtInfeed(int jobId, int infeedPortIndex)
        {
            var nodeId = ResolveInfeedNodeId(infeedPortIndex);
            if (string.IsNullOrEmpty(nodeId))
            {
                return;
            }
            m_cargoRegistry.Acquire(jobId);
            SnapCargoToNode(jobId, nodeId);
        }
        private void SnapCargoToPickup(int jobId, int pickupNodeIndex)
        {
            m_cargoRegistry.Acquire(jobId);
            SnapCargoToNode(jobId, LogicalIdAt(pickupNodeIndex));
        }
        private void PlaceCargo(int jobId, Vector3 position)
        {
            if (!m_cargoRegistry.TryGet(jobId, out var cargo))
            {
                cargo = m_cargoRegistry.Acquire(jobId);
            }
            if (cargo == null)
            {
                return;
            }
            if (cargo.parent != null)
            {
                cargo.SetParent(null, true);
            }
            cargo.gameObject.SetActive(true);
            cargo.position = position;
        }
        private void DetachCargo(int jobId)
        {
            if (m_cargoRegistry.TryGet(jobId, out var cargo) && cargo != null)
            {
                cargo.gameObject.SetActive(false);
            }
        }
        private void SnapCargoToNode(int jobId, string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId) || !m_cargoRegistry.TryGet(jobId, out var cargo))
            {
                if (string.IsNullOrEmpty(nodeId))
                {
                    return;
                }
                cargo = m_cargoRegistry.Acquire(jobId);
            }
            if (cargo == null || !_anchorIndex.TryGet(nodeId, out var tf))
            {
                return;
            }
            if (cargo.parent != null)
            {
                cargo.SetParent(null, true);
            }
            cargo.gameObject.SetActive(true);
            cargo.position = tf.position;
        }
        private bool TryGetAnchor(int nodeIndex, out Transform transform)
        {
            transform = null;
            var nodeId = LogicalIdAt(nodeIndex);
            return !string.IsNullOrEmpty(nodeId) && _anchorIndex.TryGet(nodeId, out transform);
        }
        private string LogicalIdAt(int nodeIndex)
        {
            var map = Bindings.ConveyorMap;
            if (map?.Nodes == null || nodeIndex < 0 || nodeIndex >= map.Nodes.Length)
            {
                return string.Empty;
            }

            var logicalId = SimEntityNaming.FormatLogicalId(map.Nodes[nodeIndex], nodeIndex);
            return logicalId == "—" ? string.Empty : logicalId;
        }
        private string ResolveInfeedNodeId(int infeedPortIndex)
        {
            if (_topology == null || infeedPortIndex < 0 || infeedPortIndex >= _topology.InfeedNodeIndices.Count)
            {
                return string.Empty;
            }
            return LogicalIdAt(_topology.InfeedNodeIndices[infeedPortIndex]);
        }

        private void SnapCargoToOutfeed(int jobId, int outfeedPortIndex)
        {
            var nodeId = ResolveOutfeedNodeId(outfeedPortIndex);
            if (string.IsNullOrEmpty(nodeId))
            {
                return;
            }

            m_cargoRegistry.Acquire(jobId);
            SnapCargoToNode(jobId, nodeId);
        }

        private string ResolveOutfeedNodeId(int outfeedPortIndex)
        {
            if (_topology?.OutfeedNodeIndices == null
                || outfeedPortIndex < 0
                || outfeedPortIndex >= _topology.OutfeedNodeIndices.Count)
            {
                return string.Empty;
            }

            return LogicalIdAt(_topology.OutfeedNodeIndices[outfeedPortIndex]);
        }
    }
}
