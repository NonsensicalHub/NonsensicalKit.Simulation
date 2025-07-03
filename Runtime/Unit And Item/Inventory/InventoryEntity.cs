using System;
using NonsensicalKit.Core.Log;
using NonsensicalKit.Core.Service;

namespace NonsensicalKit.Simulation.Inventory
{
    public class InventoryEntity
    {
        public Action<ItemEntity[]> InventoryChanged { get; set; }
        private ItemEntity[] _items;
        private readonly string _inventoryId;
        private readonly InventorySystem _inventorySystem;

        public InventoryEntity(InventoryData data)
        {
            _inventorySystem = ServiceCore.Get<InventorySystem>();
            _inventoryId = data.ID;
            _items = new ItemEntity[data.Size];
            InitItems();
        }

        public InventoryEntity(InventoryData data, ItemEntity[] saveData)
        {
            _inventorySystem = ServiceCore.Get<InventorySystem>();
            _inventoryId = data.ID;
            _items = new ItemEntity[data.Size];
            InitItems(saveData);
        }

        private void InitItems(ItemEntity[] saveData = null)
        {
            if (saveData == null || saveData.Length != _items.Length)
            {
                for (int i = 0; i < _items.Length; i++)
                {
                    _items[i] = new ItemEntity(null, 0, _inventoryId, i);
                }
            }
            else
            {
                _items = saveData;
            }
        }

        public ItemEntity this[int index]
        {
            get
            {
                if (index >= 0 && index < _items.Length)
                {
                    return _items[index];
                }
                else
                {
                    return null;
                }
            }
        }

        public ItemEntity GetEntity(int index)
        {
            return this[index];
        }

        /// <summary>
        /// 获取某个物品在库存中的数量
        /// </summary>
        /// <param name="itemID"></param>
        /// <returns></returns>
        public int GetItemCount(string itemID)
        {
            int count = 0;
            foreach (var item in _items)
            {
                if (item.Is(itemID))
                {
                    count += item.StackNum;
                }
            }

            return count;
        }


        /// <summary>
        /// 获取某个物品在库存中还能够放置的数量
        /// </summary>
        /// <param name="itemID"></param>
        /// <returns></returns>
        public int GetItemEmptyCount(string itemID)
        {
            var maxStackSize = _inventorySystem[itemID].MaxStackNumber;
            int count = 0;
            foreach (var item in _items)
            {
                if (item.IsEmpty)
                {
                    count += maxStackSize;
                }
                else if (item.Is(itemID))
                {
                    count += maxStackSize - item.StackNum;
                }
            }

            return count;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="item"></param>
        /// <param name="count"></param>
        /// <param name="index">使用非法索引时（小于0或大于库存大小），会自动找到第一个空位</param>
        /// <param name="triggerEvent"></param>
        /// <returns></returns>
        public bool StoreItem(ItemData item, int count = 1, int index = -1, bool triggerEvent = true)
        {
            if (index < 0 || index >= _items.Length)
            {
                index = LocationEmptyCell(item.ID);
                if (index == -1)
                {
                    _inventorySystem.AbandonItem(item, count);
                    LogCore.Info("库存已满,无法继续添加物体");
                    return false;
                }
            }

            if (_items[index].IsEmpty)
            {
                _items[index].Data = item;
                _items[index].StackNum = count;
            }
            else if (_items[index].Data.ID != item.ID)
            {
                LogCore.Info("存储指定的单元格错误，应当执行交换逻辑");
                return false;
            }
            else
            {
                _items[index].StackNum += count;
            }

            if (_items[index].StackNum > item.MaxStackNumber)
            {
                int overflow = _items[index].StackNum - item.MaxStackNumber;
                _items[index].StackNum = item.MaxStackNumber;

                var nextIndex = LocationEmptyCell(item.ID, index);
                if (nextIndex == -1)
                {
                    _inventorySystem.AbandonItem(item, overflow);
                    LogCore.Info("库存已满,无法继续添加物体");
                    return false;
                }

                if (StoreItem(item, overflow, nextIndex, false) == false)
                {
                    return false;
                }
            }

            if (triggerEvent)
            {
                InventoryChanged?.Invoke(GetEntities());
            }

            return true;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns>实际获取到的数量</returns>
        public int TakeItem(ItemData item, int count = 1, bool triggerEvent = true)
        {
            if (count < 0)
            {
                return 0;
            }

            int takeCount = 0;
            int index = 0;
            while (takeCount < count)
            {
                index = LocationExistCell(item.ID, index);
                if (index == -1)
                {
                    break;
                }

                var lessCount = count - takeCount;
                if (_items[index].StackNum >= lessCount)
                {
                    _items[index].Take(lessCount);
                    takeCount += lessCount;
                }
                else
                {
                    _items[index].StackNum = 0;
                    takeCount += _items[index].StackNum;
                }
            }

            if (triggerEvent)
            {
                InventoryChanged?.Invoke(GetEntities());
            }

            return takeCount;
        }

        public void Ping()
        {
            InventoryChanged?.Invoke(GetEntities());
        }

        public bool UseItem(int index, int count = 1)
        {
            var entity = _items[index];
            if (entity.Data != null)
            {
                if (_items[index].StackNum >= count)
                {
                    _items[index].StackNum -= count;
                    if (_items[index].StackNum == 0)
                    {
                        _items[index].Data = null;
                    }

                    InventoryChanged?.Invoke(GetEntities());

                    return true;
                }
            }

            return false;
        }

        public void DeleteItem(int index)
        {
            var entity = _items[index];
            if (entity.IsNotEmpty)
            {
                if (entity.StackNum == 0)
                {
                    _items[index].Data = null;
                }

                _items[index].StackNum = 0;
                InventoryChanged?.Invoke(GetEntities());
            }
        }

        public ItemEntity[] GetEntities()
        {
            return _items.Clone() as ItemEntity[];
        }

        /// <summary>
        /// 找到第一个相同物品id且可继续堆叠的格子索引，如果没有符合条件的则返回第一个空格子，如果仍然没有符合条件的则返回-1
        /// TODO：不可堆叠时只需要考虑是否为空即可
        /// </summary>
        /// <returns></returns>
        private int LocationEmptyCell(string itemID, int startIndex = 0)
        {
            int count = 0;
            int index = startIndex;
            var data = _inventorySystem.GetItemData(itemID);
            var canStack = data.MaxStackNumber > 1;
            while (count < _items.Length)
            {
                if (_items[index].IsNotEmpty)
                {
                    if (itemID == _items[index].Data.ID)
                    {
                        if (canStack)
                        {
                            if (_items[index].CanStoreNum > 0)
                            {
                                return index;
                            }
                        }
                    }
                }
                else
                {
                    return index;
                }

                count++;
                index++;
                if (index == _items.Length)
                {
                    index = 0;
                }
            }

            return -1;
        }

        /// <summary>
        /// 找到第一个存在某个物品的格子
        /// </summary>
        /// <param name="itemID"></param>
        /// <param name="startIndex"></param>
        /// <returns></returns>
        private int LocationExistCell(string itemID, int startIndex = 0)
        {
            int count = 0;
            int index = startIndex;
            var maxStackNumber = _inventorySystem.GetItemData(itemID).MaxStackNumber;
            while (count < _items.Length)
            {
                if (_items[index].Is(itemID) && _items[index].StackNum < maxStackNumber)
                {
                    return index;
                }

                count++;
                index++;
                if (index == _items.Length)
                {
                    index = 0;
                }
            }

            return -1;
        }
    }
}
