using UnityEngine;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Runtime
{
    public sealed partial class ConveyorPlaybackEventHandler
    {
        private void RebuildAnchorIndex()
        {
            _anchorIndex.Clear();
            if (m_nodeAnchors == null)
            {
                return;
            }

            if (m_anchorRoot != null)
            {
                var children = m_anchorRoot.GetComponentsInChildren<Transform>(true);
                for (var i = 0; i < children.Length; i++)
                {
                    var anchor = children[i];
                    if (anchor == null || anchor == m_anchorRoot)
                    {
                        continue;
                    }

                    var anchorName = anchor.gameObject.name;
                    if (string.IsNullOrWhiteSpace(anchorName))
                    {
                        continue;
                    }

                    _anchorIndex.Register(anchorName, anchor);
                }
            }

            for (var i = 0; i < m_nodeAnchors.Length; i++)
            {
                var anchor = m_nodeAnchors[i];
                if (anchor == null)
                {
                    continue;
                }

                var anchorName = anchor.gameObject.name;
                if (string.IsNullOrWhiteSpace(anchorName))
                {
                    continue;
                }

                _anchorIndex.Register(anchorName, anchor);
            }
        }
    }
}
