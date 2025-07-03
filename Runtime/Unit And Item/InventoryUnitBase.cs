using NonsensicalKit.Core.Service;
using NonsensicalKit.Simulation.Inventory;
using UnityEngine;

namespace NonsensicalKit.Simulation
{
    public abstract class InventoryUnitBase : UnitBase
    {
        [SerializeField] private int m_inventorySize;

        protected InventorySystem InventorySystem;

        protected override void Awake()
        {
            base.Awake();

            InventorySystem = ServiceCore.Get<InventorySystem>();
            InventorySystem.AddInventoryEntity(new InventoryData(m_unitID, m_inventorySize));
        }

        protected override void OnDestroy()
        {
            InventorySystem.RemoveInventoryEntity(m_unitID);
        }

        public bool StoreItem(string itemId, int num)
        {
            if (CanStore(itemId, num))
            {
                DoStore(itemId, num);
                return true;
            }
            else
            {
                return false;
            }
        }

        public bool TakeItem(string itemId, int num)
        {
            if (CanTake(itemId, num))
            {
                DoTake(itemId, num);
                return true;
            }
            else
            {
                return false;
            }
        }


        public bool CanStore(string itemId, int num)
        {
            return InventorySystem.CanStore(m_unitID, itemId, num);
        }

        public bool CanTake(string itemId, int num)
        {
            return InventorySystem.CanTake(m_unitID, itemId, num);
        }

        protected virtual void DoStore(string itemId, int num)
        {
            InventorySystem.StoreItem(m_unitID, itemId, num);
        }

        protected virtual void DoTake(string itemId, int num)
        {
            InventorySystem.TakeItem(m_unitID, itemId, num);
        }
    }
}
