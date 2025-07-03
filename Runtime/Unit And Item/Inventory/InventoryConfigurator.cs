using NonsensicalKit.Core.Service;
using UnityEngine;

namespace NonsensicalKit.Simulation.Inventory
{
    public class InventoryConfigurator : MonoBehaviour
    {
        [SerializeField] private ItemData[] m_items;

        private void Awake()
        {
            ServiceCore.Get<InventorySystem>().InitItems(m_items);
        }
    }
}
