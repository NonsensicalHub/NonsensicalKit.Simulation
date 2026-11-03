using System;
using System.Collections.Generic;
using System.Globalization;
using NaughtyAttributes;
using NonsensicalKit.Core;
using NonsensicalKit.Core.Timer;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Frame.RealTimeAlarm
{
    /// <summary>
    /// 离线 Mock：实时报警列表与详情。在线时跳过，由真实报警接口接管。
    /// </summary>
    public class RealTimeAlarmSimulationDataGenerator : MockDataProviderBase
    {
        [SerializeField,InfoBox("离线实时报警列表与详情数据生成。在线时跳过")] private int m_maxDataCount = 15;
        [SerializeField] private int m_requestInterval = 10;

        private readonly List<RealTimeAlarmInfo> _datas = new();
        private readonly List<List<string>> _alarmList = new();
        private IDPack _timerID;
        private List<string> _alarmTitleList;

        protected override void OnMockEnabled()
        {
            StopMockTimer();
            _timerID = IOCC.Get<TimerSystem>("Timer")
                .AddTimerTask(CreateData, m_requestInterval, 0, TimeUnit.Second, true);
            AddHandler<RealTimeAlarmInfo, List<(string, string)>>("getSolution", GetSolution);
            AddHandler<int, List<(string, string)>>("getSolutionByInt", GetSolution);
            AddHandler<int, int, (List<List<string>>, int)>(RealTimeAlarmInfo.HistoryListHandler, GetAlarmData);
            AddHandler<List<RealTimeAlarmInfo>>(RealTimeAlarmInfo.SnapshotHandler, GetSnapshot);

            _alarmTitleList = new List<string>
            {
                "报警时间",
                "处理时间",
                "故障代码",
                "故障简述",
                "故障位置",
                "状态"
            };
        }

        protected override void OnMockDisabled()
        {
            StopMockTimer();
            RemoveHandler<RealTimeAlarmInfo, List<(string, string)>>("getSolution", GetSolution);
            RemoveHandler<int, List<(string, string)>>("getSolutionByInt", GetSolution);
            RemoveHandler<int, int, (List<List<string>>, int)>(RealTimeAlarmInfo.HistoryListHandler, GetAlarmData);
            RemoveHandler<List<RealTimeAlarmInfo>>(RealTimeAlarmInfo.SnapshotHandler, GetSnapshot);
            base.OnMockDisabled();
        }

        private void StopMockTimer()
        {
            if (_timerID.id != 0 && IOCC.TryGet<TimerSystem>("Timer", out var timer))
            {
                timer.DeleteTimeTask(_timerID.id);
                _timerID = default;
            }
        }

        private void CreateData(int _)
        {
            RealTimeAlarmInfo temp = new()
            {
                Timestamp = DateTime.Now.ToString(CultureInfo.InvariantCulture),
                ProcessDateTime = DateTime.Now.ToString(CultureInfo.InvariantCulture),
                ResolutionDateTime = DateTime.Now.ToString(CultureInfo.InvariantCulture),
                AlarmDevice = "设备名称",
                Status = Random.Range(0, 3),
                AlarmDescription = "报警详情",
                AlarmType = "报警简讯",
                DeviceID = "AB" + Random.Range(100, 1000)
            };

            if (_datas.Count > m_maxDataCount)
            {
                _datas.RemoveAt(0);
            }

            _datas.Add(temp);
            Publish<List<RealTimeAlarmInfo>>("refreshRealTimeAlarmTable", _datas);
        }

        private List<RealTimeAlarmInfo> GetSnapshot()
        {
            return new List<RealTimeAlarmInfo>(_datas);
        }

        private List<(string, string)> GetSolution(RealTimeAlarmInfo alarmCode)
        {
            return new List<(string, string)>
            {
                ("报警设备:", $"{alarmCode.AlarmDevice}_{alarmCode.DeviceID}"),
                ("故障代码:", alarmCode.AlarmType),
                ("故障详情:", alarmCode.AlarmDescription),
                ("解决方案:", "依据故障信息查询出的解决方法")
            };
        }

        private List<(string, string)> GetSolution(int index)
        {
            return new List<(string, string)>
            {
                ("报警设备:", $"设备名称_{"AB" + Random.Range(100, 1000)}"),
                ("故障代码:", "报警简讯"),
                ("故障详情:", "报警详情"),
                ("解决方案:", "依据故障信息查询出的解决方法")
            };
        }

        /// <summary>
        /// 获取报警列表数据
        /// </summary>
        /// <param name="page"></param>
        /// <param name="pageSize"></param>
        /// <returns></returns>
        private (List<List<string>>, int) GetAlarmData(int page, int pageSize)
        {
            _alarmList.Clear();
            _alarmList.Add(_alarmTitleList);
            const int totalCount = 100;

            int startIndex = (page - 1) * pageSize;
            for (int i = startIndex; i < startIndex + pageSize && i < totalCount; i++)
            {
                _alarmList.Add(new List<string>
                {
                    DateTime.Now.AddDays(-i).ToString("MM月dd日 HH:mm:ss"),
                    DateTime.Now.AddDays(-i).AddHours(1).ToString("MM月dd日 HH:mm:ss"),
                    $"ERR{i:D4}",
                    $"模拟故障描述 {i}",
                    $"位置 {i % 10 + 1}",
                    i % 2 == 0 ? "已处理" : "未处理"
                });
            }

            return (_alarmList, totalCount);
        }
    }
}
