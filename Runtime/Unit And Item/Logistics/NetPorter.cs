using System.Collections.Generic;
using NonsensicalKit.Simulation.NetNavigation;
using UnityEngine;
using UnityEngine.Serialization;

namespace NonsensicalKit.Simulation.Logistics
{
    public class NetPorter : Porter
    {
        [SerializeField] private Net m_net;
        [SerializeField] private bool m_rotateOnNode;
        [SerializeField] private LineRenderer m_lineRenderer;
        [SerializeField] private NetMover m_mover;

        public override void MoveToTarget(Vector3 target, Quaternion rotation)
        {
            //TODO
        }

        public void MoveToPos(Vector3 pos)
        {
            m_mover.Move(pos);
        }

        public void Control()
        {
            m_state = PorterState.Control;
        }

        public void UnControl()
        {
            m_state = PorterState.Idle;
            StartNewMission();
        }
    }
}
