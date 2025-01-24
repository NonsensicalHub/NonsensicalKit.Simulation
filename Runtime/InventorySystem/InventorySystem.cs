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
        private const string DefaultID = "Default Inventory";

        public bool IsReady { get; set; }
        public Action InitCompleted { get; set; }

        private readonly Dictionary<string, ItemData> _items; //当前场景包括的物品的信息
        private readonly Dictionary<string, InventoryEntity> _inventories;

        public InventorySystem()
        {
            _items = new Dictionary<string, ItemData>();
            _inventories = new Dictionary<string, InventoryEntity>();
        }

        public void Init(IEnumerable<InventoryData> inventories, IEnumerable<ItemData> items)
        {
            foreach (var item in inventories)
            {
                if (_inventories.ContainsKey(item.ID) == false)
                {
                    _inventories.Add(item.ID, new InventoryEntity(item));
                }
            }

            if (_inventories.Count == 0)
            {
                //保证至少要有一个默认库存
                _inventories.Add(DefaultID, new InventoryEntity(DefaultID, 50));
            }

            foreach (var item in items)
            {
                _items.TryAdd(item.ID, item);
            }

            IsReady = true;
            InitCompleted?.Invoke();
        }

        public void AddListener(Action<ItemEntity[]> callback, string inventoryID = DefaultID)
        {
            if (_inventories.ContainsKey(inventoryID))
            {
                _inventories[DefaultID].InventoryChanged += callback;
            }
            else
            {
                LogCore.Warning("未配置的库存id:" + inventoryID);
            }
        }

        public void RemoveListener(Action<ItemEntity[]> callback, string inventoryID = DefaultID)
        {
            if (_inventories.ContainsKey(inventoryID))
            {
                _inventories[DefaultID].InventoryChanged -= callback;
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

        public bool AddItem(string itemID, int count = 1, int index = -1, string inventoryID = DefaultID)
        {
            if (_items.TryGetValue(itemID, out var item) == false)
            {
                LogCore.Warning("未配置的物品id:" + itemID);
                return false;
            }

            if (_inventories.TryGetValue(inventoryID, out var inventory))
            {
                return inventory.AddItem(item, count, index);
            }
            else
            {
                LogCore.Warning("未配置的库存id:" + inventoryID);
                return false;
            }
        }

        public void MoveItem(int oldIndex, string oldInventoryID, int newIndex, string newInventoryID)
        {
            if (_inventories.ContainsKey(oldInventoryID) && _inventories.ContainsKey(newInventoryID))
            {
                var oldEntity = _inventories[oldInventoryID][oldIndex];
                var newEntity = _inventories[newInventoryID][newIndex];

                if (oldEntity == null || newEntity == null)
                {
                    LogCore.Warning($"索引异常[{oldInventoryID}:{oldIndex},{newInventoryID}:{newIndex}]");
                    return;
                }

                if (oldEntity.Data != null)
                {
                    if (newEntity.Data != null)
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

                    _inventories[oldInventoryID].RingIt();
                    if (oldInventoryID != newInventoryID)
                    {
                        _inventories[newInventoryID].RingIt();
                    }
                }
            }
        }

        public bool UseItem(int index, int count = 1, string inventoryID = DefaultID)
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

        public void RemoveItem(int index, string inventoryID = DefaultID)
        {
            if (_inventories.TryGetValue(inventoryID, out var inventory))
            {
                inventory.RemoveItem(index);
            }
            else
            {
                LogCore.Warning("未配置的库存id:" + inventoryID);
            }
        }

        public void AbandonItem(ItemData item, int count = 1)
        {
            //舍弃添加时多余的物品
        }

        public ItemEntity[] GetItemEntity(string inventoryID = DefaultID)
        {
            if (_inventories.TryGetValue(inventoryID, out var inventory))
            {
                return inventory.GetEntitys();
            }
            else
            {
                LogCore.Warning("未配置的库存id:" + inventoryID);
                return null;
            }
        }
    }
}
