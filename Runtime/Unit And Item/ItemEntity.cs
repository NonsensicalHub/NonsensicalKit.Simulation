namespace NonsensicalKit.Simulation
{
    /// <summary>
    /// 物品实体,代表的是库存中的一个格子，不一定真的有物品
    /// </summary>
    public class ItemEntity
    {
        public readonly string InventoryID;
        public readonly int InventoryIndex;
        
        public ItemData Data;
        public int StackNum;

        public bool IsEmpty => Data == null;
        public bool IsNotEmpty => Data != null;
        public int CanStoreNum => Data.MaxStackNumber - StackNum;

        public ItemEntity(ItemData data, int count, string inventoryID, int inventoryIndex)
        {
            Data = data;
            StackNum = count;
            InventoryID = inventoryID;
            InventoryIndex = inventoryIndex;
        }

        public void Take(int num)
        {
            StackNum -= num;
            if (StackNum<=0)
            {
                Clear();
            }
        }

        public void Clear()
        {
            StackNum = 0;
            Data = null;
        }
        
        public bool Is(string id)
        {
            return Data != null && Data.ID == id;
        }
    }
}
