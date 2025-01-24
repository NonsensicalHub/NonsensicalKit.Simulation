using System;
using NonsensicalKit.Core.Log;
using NonsensicalKit.Core.Service;

namespace NonsensicalKit.Simulation.Inventory
{
    public class InventoryEntity
    {
        public Action<ItemEntity[]> InventoryChanged { get; set; }
        private ItemEntity[] _items;
        private string _inventoryId;

        public InventoryEntity(InventoryData data)
        {
            _inventoryId = data.ID;
            _items = new ItemEntity[data.InitialSize];
            InitEntity();
        }

        public InventoryEntity(string id, int size)
        {
            _inventoryId = id;
            _items = new ItemEntity[size];
            InitEntity();
        }

        private void InitEntity()
        {
            for (int i = 0; i < _items.Length; i++)
            {
                _items[i] = new ItemEntity(null, 0, _inventoryId, i);
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
            if (index >= 0 && index < _items.Length)
            {
                return _items[index];
            }
            else
            {
                return null;
            }
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
                if (item.Data != null && item.Data.ID == itemID)
                {
                    count += item.StackNum;
                }
            }

            return count;
        }

        public bool AddItem(ItemData item, int count = 1, int index = -1, bool triggerEvent = true)
        {
            if (index < 0 || index >= _items.Length)
            {
                index = LocationLegalCell(item.ID);
                if (index == -1)
                {
                    ServiceCore.Get<InventorySystem>().AbandonItem(item, count);
                    LogCore.Info("库存已满,无法继续添加物体");
                    return false;
                }
            }

            if (_items[index].Data == null)
            {
                _items[index].Data = item;
                _items[index].StackNum = count;
            }
            else
            {
                _items[index].StackNum += count;
            }

            if (_items[index].StackNum > item.MaxStackNumber)
            {
                int overflow = _items[index].StackNum - item.MaxStackNumber;
                _items[index].StackNum = item.MaxStackNumber;


                var nextIndex = LocationLegalCell(item.ID, index);
                if (nextIndex == -1)
                {
                    ServiceCore.Get<InventorySystem>().AbandonItem(item, overflow);
                    LogCore.Info("库存已满,无法继续添加物体");
                    return false;
                }

                if (AddItem(item, overflow, nextIndex, false) == false)
                {
                    return false;
                }
            }

            if (triggerEvent)
            {
                InventoryChanged?.Invoke(GetEntitys());
            }

            return true;
        }

        public void RingIt()
        {
            InventoryChanged?.Invoke(GetEntitys());
        }

        public bool UseItem(int index, int count = 1)
        {
            var entity = _items[index];
            if (entity.Data != null)
            {
                if (_items[index].StackNum >= count)
                {
                    _items[index].StackNum -= count;
                    if (_items[index].StackNum < 0)
                    {
                        _items[index].Data = null;
                        _items[index].StackNum = 0;
                    }

                    InventoryChanged?.Invoke(GetEntitys());
                }
            }

            return false;
        }

        public void RemoveItem(int index)
        {
            var entity = _items[index];
            if (entity.Data != null)
            {
                _items[index].Data = null;
                _items[index].StackNum = 0;
                InventoryChanged?.Invoke(GetEntitys());
            }
        }

        public ItemEntity[] GetEntitys()
        {
            return _items.Clone() as ItemEntity[];
        }

        /// <summary>
        /// 找到第一个相同物品id且可继续堆叠的格子索引，如果没有符合条件的则返回第一个空格子，如果仍然没有符合条件的则返回-1
        /// TODO：不可堆叠时只需要考虑是否为空即可
        /// </summary>
        /// <param name="itemID"></param>
        /// <returns></returns>
        private int LocationLegalCell(string itemID, int startIndex = 0)
        {
            int count = 0;
            int index = startIndex;
            int firstEmpty = -1;
            while (count < _items.Length)
            {
                if (_items[index].Data != null)
                {
                    if (itemID == _items[index].Data.ID)
                    {
                        if (_items[index].StackNum < _items[index].Data.MaxStackNumber)
                        {
                            return index;
                        }
                    }
                }
                else if (firstEmpty == -1)
                {
                    firstEmpty = index;
                }

                count++;
                index++;
                if (index == _items.Length)
                {
                    index = 0;
                }
            }

            return firstEmpty;
        }
    }
}
