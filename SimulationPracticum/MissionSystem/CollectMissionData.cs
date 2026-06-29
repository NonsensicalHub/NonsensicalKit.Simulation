using System;
using UnityEngine;

namespace NonsensicalKit.Simulation.Mission
{
    [CreateAssetMenu(fileName = "CollectMissionData", menuName = "ScriptableObjects/CollectMissionData")]
    public class CollectMissionData : ScriptableObject
    {
        public string MissionID; //任务ID
        public ItemRequired[] ItemRequired;
    }

    [Serializable]
    public class ItemRequired
    {
        public string ItemID; //物品ID
        public int RequiredQuantity; //所需数量
    }
}
