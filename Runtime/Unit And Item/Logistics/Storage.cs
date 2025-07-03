using UnityEngine;

namespace NonsensicalKit.Simulation.Logistics
{
    public class Storage : InventoryUnitBase
    {
        [SerializeField] private Transform m_accessPoint;

        public Vector3 AccessPos => m_accessPoint.position;
        public Quaternion AccessRot => m_accessPoint.rotation;
    }
}
