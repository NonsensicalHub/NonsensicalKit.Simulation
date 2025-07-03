using System;
using System.Collections.Generic;
using NonsensicalKit.Core.Log;
using NonsensicalKit.Core.Service;

namespace NonsensicalKit.Simulation.Inventory
{
    /// <summary>
    /// 库存系统
    /// </summary>
    public class InventorySystem : IClassService
    {
        public bool IsReady { get; private set; } = true;
        public Action InitCompleted { get; set; }

        private readonly Dictionary<string, ItemData> _items = new(); //物品的信息
        private readonly Dictionary<string, InventoryEntity> _inventories = new(); //当前管理的库存的实体信息

        public Action<ItemData, int> OnAbandonItem { get; set; }

        public ItemData this[string id] => _items.GetValueOrDefault(id);

        public void InitItems(IEnumerable<ItemData> items, bool clear = true)
        {
            if (clear)
            {
                _items.Clear();
            }

            foreach (var item in items)
            {
                _items.TryAdd(item.ID, item);
            }
        }

        public bool CanStore(string inventoryID, string itemID, int count)
        {
            if (_inventories.TryGetValue(inventoryID, out var inventory))
            {
                var emptyCount = inventory.GetItemEmptyCount(itemID);
                return emptyCount >= count;
            }
            else
            {
                LogCore.Warning("未配置的库存id:" + inventoryID);
            }

            return false;
        }

        public bool CanTake(string inventoryID, string itemID, int count)
        {
            if (_inventories.TryGetValue(inventoryID, out var inventory))
            {
                var emptyCount = inventory.GetItemEmptyCount(itemID);
                return emptyCount >= count;
            }
            else
            {
                LogCore.Warning("未配置的库存id:" + inventoryID);
            }

            return false;
        }

        public void AddInventoryEntity(InventoryData data)
        {
            if (_inventories.ContainsKey(data.ID) == false)
            {
                _inventories.Add(data.ID, new InventoryEntity(data));
            }
        }

        public void RemoveInventoryEntity(string id)
        {
            _inventories.Remove(id);
        }

        /// <summary>
        /// 添加指定库存的修改监听
        /// </summary>
        public void AddListener(string inventoryID, Action<ItemEntity[]> callback)
        {
            if (_inventories.TryGetValue(inventoryID, out var inventory))
            {
                inventory.InventoryChanged += callback;
            }
            else
            {
                LogCore.Warning("未配置的库存id:" + inventoryID);
            }
        }

        /// <summary>
        /// 移除指定库存的修改监听
        /// </summary>
        public void RemoveListener(string inventoryID, Action<ItemEntity[]> callback)
        {
            if (_inventories.TryGetValue(inventoryID, out var inventory))
            {
                inventory.InventoryChanged -= callback;
            }
            else
            {
                LogCore.Warning("未配置的库存id:" + inventoryID);
            }
        }

        /// <summary>
        /// 获取某个物品在所有库存中的总数量
        /// </summary>
        /// <param name="itemID"></param>
        /// <returns></returns>
        public int GetItemCount(string itemID)
        {
            int count = 0;
            foreach (var item in _inventories.Values)
            {
                count += item.GetItemCount(itemID);
            }

            return count;
        }

        /// <summary>
        /// 获取某个物品在特定库存中的数量
        /// </summary>
        /// <param name="itemID"></param>
        /// <param name="inventoryID"></param>
        /// <returns></returns>
        public int GetItemCount(string inventoryID, string itemID)
        {
            if (_inventories.ContainsKey(itemID))
            {
                return _inventories[inventoryID].GetItemCount(itemID);
            }
            else
            {
                return 0;
            }
        }

        public bool StoreItem(string inventoryID, string itemID, int count = 1, int index = -1)
        {
            if (_items.TryGetValue(itemID, out var item) == false)
            {
                LogCore.Warning("未配置的物品id:" + itemID);
                return false;
            }

            if (_inventories.TryGetValue(inventoryID, out var inventory))
            {
                return inventory.StoreItem(item, count, index);
            }
            else
            {
                LogCore.Warning("未配置的库存id:" + inventoryID);
                return false;
            }
        }

        public int TakeItem(string inventoryID, string itemID, int count = 1)
        {
            if (_items.TryGetValue(itemID, out var item) == false)
            {
                LogCore.Warning("未配置的物品id:" + itemID);
                return 0;
            }

            if (_inventories.TryGetValue(inventoryID, out var inventory))
            {
                return inventory.TakeItem(item, count);
            }
            else
            {
                LogCore.Warning("未配置的库存id:" + inventoryID);
                return 0;
            }
        }

        public void MoveItem(int oldIndex, string oldInventoryID, int newIndex, string newInventoryID)
        {
            if (!_inventories.ContainsKey(oldInventoryID) || !_inventories.ContainsKey(newInventoryID)) return;
            if (oldIndex == newIndex) return;

            var oldEntity = _inventories[oldInventoryID][oldIndex];
            var newEntity = _inventories[newInventoryID][newIndex];

            if (oldEntity == null || newEntity == null)
            {
                LogCore.Warning($"索引异常[{oldInventoryID}:{oldIndex},{newInventoryID}:{newIndex}]");
                return;
            }

            if (oldEntity.IsEmpty) return;
            if (newEntity.IsNotEmpty)
            {
                if (oldEntity.Data.ID == newEntity.Data.ID)
                {
                    //相同则尝试堆叠
                    if (oldEntity.Data.MaxStackNumber > 1)
                    {
                        var sum = oldEntity.StackNum + newEntity.StackNum;
                        var max = oldEntity.Data.MaxStackNumber;
                        if (sum > max)
                        {
                            //合计超过堆叠上限
                            oldEntity.StackNum = sum - max;
                            newEntity.StackNum = max;
                        }
                        else
                        {
                            //未超过堆叠上限
                            oldEntity.Data = null;
                            oldEntity.StackNum = 0;
                            newEntity.StackNum = sum;
                        }
                    }
                    else
                    {
                        //不能堆叠时不干任何事
                        return;
                    }
                }
                else
                {
                    //不同则交换
                    var tempData = oldEntity.Data;
                    var tempCount = oldEntity.StackNum;
                    oldEntity.Data = newEntity.Data;
                    oldEntity.StackNum = newEntity.StackNum;
                    newEntity.Data = tempData;
                    newEntity.StackNum = tempCount;
                }
            }
            else
            {
                //移动
                newEntity.Data = oldEntity.Data;
                newEntity.StackNum = oldEntity.StackNum;
                oldEntity.Data = null;
                oldEntity.StackNum = 0;
            }

            _inventories[oldInventoryID].Ping();
            if (oldInventoryID != newInventoryID)
            {
                _inventories[newInventoryID].Ping();
            }
        }

        public bool UseItem(string inventoryID, int index, int count = 1)
        {
            if (_inventories.TryGetValue(inventoryID, out var inventory))
            {
                return inventory.UseItem(index, count);
            }
            else
            {
                LogCore.Warning("未配置的库存id:" + inventoryID);
                return false;
            }
        }

        public void DeleteItem(string inventoryID, int index)
        {
            if (_inventories.TryGetValue(inventoryID, out var inventory))
            {
                inventory.DeleteItem(index);
            }
            else
            {
                LogCore.Warning("未配置的库存id:" + inventoryID);
            }
        }

        public void AbandonItem(ItemData item, int count = 1)
        {
            OnAbandonItem?.Invoke(item, count);
        }

        public ItemEntity[] GetItemEntity(string inventoryID)
        {
            if (_inventories.TryGetValue(inventoryID, out var inventory))
            {
                return inventory.GetEntities();
            }
            else
            {
                LogCore.Warning("未配置的库存id:" + inventoryID);
                return null;
            }
        }

        public ItemData GetItemData(string itemID)
        {
            return _items.GetValueOrDefault(itemID);
        }
    }
}
