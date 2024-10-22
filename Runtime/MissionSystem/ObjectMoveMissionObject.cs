using UnityEngine;

namespace NonsensicalKit.Simulation.Mission
{
    public class ObjectMoveMissionObject : MonoBehaviour
    {
        [SerializeField] private string m_objectID;

        public string ObjectID => m_objectID;
    }
}
