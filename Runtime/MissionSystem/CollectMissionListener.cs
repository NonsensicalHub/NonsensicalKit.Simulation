using System.Collections.Generic;
using NonsensicalKit.Core;
using NonsensicalKit.Core.Log;
using NonsensicalKit.Core.Service;
using NonsensicalKit.Simulation.Inventory;
using UnityEngine;

namespace NonsensicalKit.Simulation.Mission
{
    /// <summary>
    /// 收集任务监听器
    /// </summary>
    public class CollectMissionListener : NonsensicalMono
    {
        [SerializeField] private CollectMissionData[] m_datas;
        [SerializeField] private string  m_backpackID="backpack";
        private Dictionary<string, CollectMissionData> _missionData;
        private List<string> _listeningMission;

        private InventorySystem _inventorySystem;
        private MissionSystem _missionSystem;

        private void Awake()
        {
            _listeningMission = new List<string>();
            _missionData = new Dictionary<string, CollectMissionData>();
            foreach (var item in m_datas)
            {
                if (_missionData.ContainsKey(item.MissionID))
                {
                    LogCore.Warning($"收集任务ID配置重复{item.MissionID}");
                }
                else
                {
                    _missionData.Add(item.MissionID, item);
                }
            }

            _missionSystem = ServiceCore.Get<MissionSystem>();
            ServiceCore.SafeGet<InventorySystem>(OnGetInventorySystem);

            Subscribe<string>("StartMission", MissingTypeText.Collect, OnStartMission);
            Subscribe<string>("StopMission", MissingTypeText.Collect, OnStopMission);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (_inventorySystem != null)
            {
                _inventorySystem.RemoveListener(m_backpackID,OnItemChanged);
                _inventorySystem = null;
            }
        }

        private void OnGetInventorySystem(InventorySystem system)
        {
            _inventorySystem = system;
            _inventorySystem.AddListener(m_backpackID,OnItemChanged);
        }

        private void OnStartMission(string missionID)
        {
            if (_missionData.ContainsKey(missionID) == false)
            {
                LogCore.Warning($"收集任务{_missionSystem.GetMissionName(missionID)}未进行配置");
                return;
            }

            var collectMission = _missionData[missionID];
            if (IsCollectMissionCompleted(collectMission))
            {
                _missionSystem.MissionCompleted(missionID);
            }
            else
            {
                ListeningItems(collectMission);
            }
        }

        private bool IsCollectMissionCompleted(CollectMissionData collectMission)
        {
            foreach (var item in collectMission.ItemRequired)
            {
                if (_inventorySystem.GetItemCount(item.ItemID) < item.RequiredQuantity)
                {
                    return false;
                }
            }

            return true;
        }

        private void OnStopMission(string missionID)
        {
            StopListeningItems(missionID);
        }

        private void ListeningItems(CollectMissionData data)
        {
            if (_listeningMission.Contains(data.MissionID) == false)
            {
                _listeningMission.Add(data.MissionID);
            }
        }

        private void StopListeningItems(string missionID)
        {
            if (_listeningMission.Contains(missionID))
            {
                _listeningMission.Remove(missionID);
            }
        }


        private void OnItemChanged(ItemEntity[] entitys)
        {
            var list = _listeningMission.ToArray();
            foreach (var item in list)
            {
                if (IsCollectMissionCompleted(_missionData[item]))
                {
                    _missionSystem.MissionCompleted(item);
                }
            }
        }
    }
}
