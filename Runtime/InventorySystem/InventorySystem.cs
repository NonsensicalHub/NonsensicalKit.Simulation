using NonsensicalKit.Core.Log;
using NonsensicalKit.Core.Service;
using System;
using System.Collections.Generic;

namespace NonsensicalKit.Temp.InventorySystem
{
    /// <summary>
    /// 库存系统
    /// </summary>
    public class InventorySystem : IClassService
    {
        private const string DefaultID = "Default Inventory";

        public bool IsReady { get; set; }
        public Action InitCompleted { get; set; }

        private Dictionary<string, ItemData> _items;    //当前场景包括的物品的信息
        private Dictionary<string, InventoryEntity> _inventorys;

        public InventorySystem()
        {
            _items = new Dictionary<string, ItemData>();
            _inventorys = new Dictionary<string, InventoryEntity>();
        }

        public void Init(IEnumerable<InventoryData> inventorys, IEnumerable<ItemData> items)
        {
            foreach (var item in inventorys)
            {
                if (_inventorys.ContainsKey(item.ID) == false)
                {
                    _inventorys.Add(item.ID, new InventoryEntity(item));
                }
            }
            if (_inventorys.Count == 0)
            {
                //保证至少要有一个默认库存
                _inventorys.Add(DefaultID, new InventoryEntity(DefaultID, 50));
            }

            foreach (var item in items)
            {
                if (_items.ContainsKey(item.ID) == false)
                {
                    _items.Add(item.ID, item);
                }
            }
            IsReady = true;
            InitCompleted?.Invoke();
        }

        public void AddListener(Action<ItemEntity[]> callback, string inventorysID = DefaultID)
        {
            if (_inventorys.ContainsKey(inventorysID))
            {
                _inventorys[DefaultID].InventoryChanged += callback;
            }
            else
            {
                LogCore.Warning("未配置的库存id:" + inventorysID);
                return;
            }
        }

        public void RemoveListener(Action<ItemEntity[]> callback, string inventorysID = DefaultID)
        {
            if (_inventorys.ContainsKey(inventorysID))
            {
                _inventorys[DefaultID].InventoryChanged -= callback;
            }
            else
            {
                LogCore.Warning("未配置的库存id:" + inventorysID);
                return;
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
            foreach (var item in _inventorys.Values)
            {
                count += item.GetItemCount(itemID);
            }
            return count;
        }

        /// <summary>
        /// 获取某个物品在特定库存中的数量
        /// </summary>
        /// <param name="itemID"></param>
        /// <param name="inventorysID"></param>
        /// <returns></returns>
        public int GetItemCount(string inventorysID, string itemID)
        {
            if (_inventorys.ContainsKey(itemID))
            {
                return _inventorys[inventorysID].GetItemCount(itemID);
            }
            else
            {
                return 0;
            }
        }

        public bool AddItem(string itemID, int count = 1, int index = -1, string inventorysID = DefaultID)
        {
            if (_items.ContainsKey(itemID) == false)
            {
                LogCore.Warning("未配置的物品id:" + itemID);
                return false;
            }

            if (_inventorys.ContainsKey(inventorysID))
            {
                return _inventorys[inventorysID].AddItem(_items[itemID], count, index, true);
            }
            else
            {
                LogCore.Warning("未配置的库存id:" + inventorysID);
                return false;
            }
        }

        public void MoveItem(int oldIndex, string oldInventorysID, int newIndex, string newInventorysID)
        {
            if (_inventorys.ContainsKey(oldInventorysID)&& _inventorys.ContainsKey(newInventorysID))
            {
                var oldEntity = _inventorys[oldInventorysID][oldIndex];
                var newEntity = _inventorys[newInventorysID][newIndex];

                if (oldEntity==null||newEntity==null)
                {
                    LogCore.Warning($"索引异常[{oldInventorysID}:{oldIndex},{newInventorysID}:{newIndex}]" );
                    return ;
                }
                if (oldEntity.Data != null)
                {
                    if (newEntity.Data != null)
                    {
                        if (oldEntity.Data.ID == newEntity.Data.ID)
                        {
                            //相同则尝试堆叠
                            if (oldEntity.Data.MaxStackNumber>1)
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
                    _inventorys[oldInventorysID].RingIt();
                    if (oldInventorysID!= newInventorysID)
                    {
                        _inventorys[newInventorysID].RingIt();
                    }
                }
            }
        }

        public bool UseItem(int index, int count = 1, string inventorysID = DefaultID)
        {
            if (_inventorys.ContainsKey(inventorysID))
            {
                return _inventorys[inventorysID].UseItem(index, count);
            }
            else
            {
                LogCore.Warning("未配置的库存id:" + inventorysID);
                return false;
            }
        }

        public void RemoveItem(int index, string inventorysID = DefaultID)
        {
            if (_inventorys.ContainsKey(inventorysID))
            {
                _inventorys[inventorysID].RemoveItem(index);
            }
            else
            {
                LogCore.Warning("未配置的库存id:" + inventorysID);
                return;
            }
        }

        public void AbandonItem(ItemData item, int count = 1)
        {
            //舍弃添加时多余的物品
        }

        public ItemEntity[] GetItemEntity(string inventorysID = DefaultID)
        {
            if (_inventorys.ContainsKey(inventorysID))
            {
                return _inventorys[inventorysID].GetEntitys();
            }
            else
            {
                LogCore.Warning("未配置的库存id:" + inventorysID);
                return null;
            }
        }
    }
}
