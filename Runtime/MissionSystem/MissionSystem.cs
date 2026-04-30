using System;
using System.Collections.Generic;
using NonsensicalKit.Core;
using NonsensicalKit.Core.Log;
using NonsensicalKit.Core.Service;

namespace NonsensicalKit.Simulation.Mission
{
    /// <summary>
    /// 拾取 - 监听背包变动事件
    /// 人物移动 - 监听Player碰撞事件
    /// 物体移动 - 监听可移动物体碰撞事件
    /// 互动 - 监听可互动对象完成事件
    /// </summary>
    public class MissionSystem : IClassService
    {
        public bool IsReady { get; set; }

        public Action InitCompleted { get; set; }
        public Action OnMissionStatusChanged { get; set; }

        private readonly Dictionary<string, MissionObject> _missions;

        private readonly HashSet<string> _completedMissions;

        private bool _autoAccept;

        public MissionSystem()
        {
            _missions = new Dictionary<string, MissionObject>();
            _completedMissions = new HashSet<string>();
            IsReady = true;
        }

        /// <summary>
        /// 清空之前的任务，并配置新任务
        /// </summary>
        /// <param name="missions"></param>
        public void InitMission(MissionData[] missions)
        {
            ClearMission();
            if (missions == null || missions.Length == 0)
            {
                return;
            }

            foreach (var item in missions)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.ID))
                {
                    LogCore.Warning("存在非法任务配置（空任务或空ID），已跳过");
                    continue;
                }

                if (_missions.ContainsKey(item.ID))
                {
                    LogCore.Warning($"任务ID重复：{item.ID}");
                }
                else
                {
                    _missions.Add(item.ID, new MissionObject(item));
                }
            }
        }

        /// <summary>
        /// 开始执行所有任务
        /// </summary>
        public void AutoAccept()
        {
            _autoAccept = true;
            foreach (var item in _missions)
            {
                if (item.Value.Status != MissionStatus.Unaccepted)
                {
                    continue;
                }

                bool flag = true;
                foreach (var item2 in item.Value.Data.PremiseMissionIDs ?? Array.Empty<string>())
                {
                    if (_completedMissions.Contains(item2) == false)
                    {
                        flag = false;
                        break;
                    }
                }

                if (!flag)
                {
                    continue;
                }

                StartMission(item.Key);
            }
        }

        public void MissionCompleted(string missionID)
        {
            if (!TryGetMission(missionID, out var mission))
            {
                return;
            }

            if (mission.Status != MissionStatus.Accepted)
            {
                LogCore.Warning($"任务{mission.Data.Name}未接受，无法完成");
            }
            else
            {
                IOCC.PublishWithID("StopMission", mission.Data.Type, missionID);
                mission.Status = MissionStatus.Completed;
                OnMissionStatusChanged?.Invoke();
                _completedMissions.Add(missionID);
                if (_autoAccept)
                {
                    AutoAccept();
                }
            }
        }

        public void MissionFailed(string missionID)
        {
            if (!TryGetMission(missionID, out var mission))
            {
                return;
            }

            if (mission.Status != MissionStatus.Accepted)
            {
                LogCore.Warning($"任务{mission.Data.Name}未接受，无法失败");
            }
            else
            {
                IOCC.PublishWithID("StopMission", mission.Data.Type, missionID);
                mission.Status = MissionStatus.Failed;
                OnMissionStatusChanged?.Invoke();
            }
        }

        public string GetMissionName(string missionID)
        {
            if (_missions.TryGetValue(missionID, out var mission))
            {
                return mission.Data.Name;
            }
            else
            {
                return string.Empty;
            }
        }

        public List<MissionData> GetRunningMissions()
        {
            List<MissionData> missions = new List<MissionData>();

            foreach (var item in _missions)
            {
                if (item.Value.Status == MissionStatus.Accepted)
                {
                    missions.Add(item.Value.Data);
                }
            }

            return missions;
        }

        /// <summary>
        /// 开始执行任务
        /// </summary>
        /// <param name="missionID"></param>
        private void StartMission(string missionID)
        {
            if (!TryGetMission(missionID, out var mission))
            {
                return;
            }

            if (mission.Status != MissionStatus.Unaccepted && mission.Status != MissionStatus.Failed)
            {
                LogCore.Warning($"任务{mission.Data.Name}重复接受");
            }
            else
            {
                mission.Status = MissionStatus.Accepted;
                IOCC.PublishWithID("StartMission", mission.Data.Type, mission.Data.ID);
                OnMissionStatusChanged?.Invoke();
            }
        }

        /// <summary>
        /// 清空所有任务
        /// </summary>
        private void ClearMission()
        {
            foreach (var item in _missions)
            {
                IOCC.PublishWithID("StopMission", item.Value.Data.Type, item.Value.Data.ID);
            }

            _missions.Clear();
            OnMissionStatusChanged?.Invoke();
        }

        private bool TryGetMission(string missionID, out MissionObject mission)
        {
            if (string.IsNullOrWhiteSpace(missionID))
            {
                mission = null;
                LogCore.Warning("任务ID为空");
                return false;
            }

            if (_missions.TryGetValue(missionID, out mission))
            {
                return true;
            }

            LogCore.Warning($"任务ID不存在：{missionID}");
            return false;
        }
    }

    /// <summary>
    /// 没有提交步骤，如果有提交任务的需求，则应将最后一个步骤做成提交任务 
    /// </summary>
    public enum MissionStatus
    {
        Unaccepted, //未被接受
        Accepted, //已接受
        Completed, //已完成
        Failed //已失败
    }

    public class MissionObject
    {
        public MissionData Data;
        public MissionStatus Status;

        public MissionObject(MissionData data)
        {
            Data = data;
            Status = MissionStatus.Unaccepted;
        }
    }
}
