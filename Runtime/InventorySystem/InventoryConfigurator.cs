using NonsensicalKit.Core.Service;
using UnityEngine;

namespace NonsensicalKit.Simulation.Inventory
{
    public class InventoryConfigurator : MonoBehaviour
    {
        [SerializeField] private InventoryData[] m_inventorys;
        [SerializeField] private ItemData[] m_items;

        private void Awake()
        {
            ServiceCore.Get<InventorySystem>().Init( m_inventorys, m_items);
        }
    }
}
