namespace Frame.RealTimeAlarm
{
    public class RealTimeAlarmInfo
    {
        /// <summary>
        /// 当前报警列表查询。离线模拟和在线记录都可以注册，卡片列表不依赖具体实现类。
        /// </summary>
        public const string SnapshotHandler = "getRealTimeAlarmSnapshot";

        /// <summary>
        /// 历史报警分页查询。在线由 AlarmEpisodeService 占位，离线由模拟器提供。
        /// </summary>
        public const string HistoryListHandler = "getAlarmList";

        /// <summary>
        /// 报警时间
        /// </summary>
        public string Timestamp;

        /// <summary>
        /// 处理时间
        /// </summary>
        public string ProcessDateTime;

        /// <summary>
        /// 解决时间
        /// </summary>
        public string ResolutionDateTime;

        /// <summary>
        /// 报警设备/位置/名称
        /// </summary>
        public string AlarmDevice;

        /// <summary>
        /// 报警简讯信息/代码
        /// </summary>
        public string AlarmType;

        /// <summary>
        /// 报警详细信息
        /// </summary>
        public string AlarmDescription;

        /// <summary>
        /// 处理状态
        /// </summary>
        public int Status;

        /// <summary>
        /// 报警设备ID
        /// </summary>
        public string DeviceID;

        /// <summary>
        /// 在线报警记录键。离线模拟数据留空，卡片按原样展示。
        /// </summary>
        public string EpisodeId;

        /// <summary>
        /// 堆垛机故障字类别。未合并的报警留空。
        /// </summary>
        public string CategoryName;

        /// <summary>
        /// 当前仍在报警的点名。已恢复时是恢复前的点名。
        /// </summary>
        public System.Collections.Generic.List<string> ActivePointNames;

        /// <summary>
        /// 设备或报警点本拍没有采到，不能当成已恢复。
        /// </summary>
        public bool DataInterrupted;

        /// <summary>
        /// 接入后第一拍就已经在报警，发生时间不是真实起点。
        /// </summary>
        public bool ObservedAtConnect;
    }
}
