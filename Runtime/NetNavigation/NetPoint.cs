using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace NonsensicalKit.Simulation.NetNavigation
{
    public class NetPoint : MonoBehaviour
    {
        [SerializeField] public List<NetPath> m_Path;

        public Net Net { get; set; }


        private void OnDrawGizmos()
        {
            Gizmos.DrawSphere(transform.position, 0.1f);
        }
    }

    public enum PathType
    {
        Straight,
        Bezier
    }

    [Serializable]
    public class NetPath
    {
        public NetPoint Target;
        public PathType Type;
        [FormerlySerializedAs("StartControlPointOffset1")] [FormerlySerializedAs("MiddlePos1")] public Vector3 StartControlPointOffset;
        [FormerlySerializedAs("EndControlPointOffset2")] [FormerlySerializedAs("MiddlePos2")] public Vector3 EndControlPointOffset;
    }
}
