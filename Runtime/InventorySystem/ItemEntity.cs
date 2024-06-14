using System;

namespace NonsensicalKit.Temp.InventorySystem
{
    /// <summary>
    /// 捡起的物品实体
    /// </summary>
    public class ItemEntity
    {
        public ItemData Data;
        public int StackNum;
        public string InventoryID;
        public int InventoryIndex;

        public ItemEntity(ItemData data, int count,string inventoryID,int inventoryIndex)
        {
            Data = data;
            StackNum = count;
            InventoryID = inventoryID;
            InventoryIndex = inventoryIndex;
        }
    }
}
