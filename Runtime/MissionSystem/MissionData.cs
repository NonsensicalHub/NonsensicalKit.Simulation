using UnityEngine;

namespace NonsensicalKit.Simulation.Mission
{
    [CreateAssetMenu(fileName = "MissionData", menuName = "ScriptableObjects/MissionData")]
    public class MissionData : ScriptableObject
    {
        public string ID;           //任务ID
        public string Path;         //任务树路径
        public string Type;         //任务类型
        public string Name;         //任务名称

        [TextArea(3, 100)]
        public string Describe;     //详细描述
        public string Overview;     //概述

        public string[] PremiseMissionIDs;   //前提任务
    }
}
