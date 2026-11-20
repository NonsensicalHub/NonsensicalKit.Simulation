using Frame.BizKit.WMS.Config;
using Frame.BizKit.WMS.RealTimeAlarm;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Frame.RealTimeAlarm
{
    /// <summary>
    /// 把报警快照差分成卡片记录。与页面是否显示无关。
    /// </summary>
    public sealed class AlarmEpisodeStore
    {
        private static readonly string[] TimeFormats =
        {
            "yyyy-MM-ddTHH:mm:ss.fff",
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-dd HH:mm:ss"
        };

        private static readonly Comparison<AlarmEpisode> ActiveComparer = CompareActive;
        private static readonly StringBuilder DescriptionBuilder = new(128);

        private readonly AlarmCategoryCatalog _stackerCatalog;
        private readonly AlarmCategoryCatalog _lifterCatalog;
        private readonly Dictionary<string, DeviceSlot> _devices = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<AlarmEpisode> _resolved = new();
        private readonly List<AlarmEpisode> _activeSort = new();
        private readonly HashSet<string> _present = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _reliable = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _nameScratch = new(16);

        public AlarmEpisodeStore(AlarmCategoryCatalog stackerCatalog, AlarmCategoryCatalog lifterCatalog = null)
        {
            _stackerCatalog = stackerCatalog ?? new AlarmCategoryCatalog();
            _lifterCatalog = lifterCatalog ?? new AlarmCategoryCatalog();
        }

        /// <summary>
        /// 返回这拍是否改了卡片。没变化时调用方不必刷新界面。
        /// completeSnapshot 为 false 时只更新报文里的设备，不把没出现的设备标成采集中断。
        /// </summary>
        public bool Apply(MqttAlarmAggregatePayload payload, bool completeSnapshot = true)
        {
            if (payload == null)
                return false;

            bool changed = false;
            DateTime? publishTime = ParseTime(payload.PublishTime);
            _present.Clear();

            if (payload.Devices != null)
            {
                for (int i = 0; i < payload.Devices.Count; i++)
                {
                    MqttAlarmDevicePayload device = payload.Devices[i];
                    if (device == null || string.IsNullOrEmpty(device.DeviceCode))
                        continue;

                    _present.Add(device.DeviceCode);
                    bool firstSight = !_devices.TryGetValue(device.DeviceCode, out DeviceSlot slot);
                    if (firstSight)
                        _devices.Add(device.DeviceCode, slot = new DeviceSlot());

                    DateTime readTime = ParseTime(device.ReadTime) ?? publishTime ?? DateTime.Now;
                    if (ApplyDevice(slot, device, readTime, firstSight))
                        changed = true;
                }
            }

            if (completeSnapshot && MarkMissingDevices())
                changed = true;

            return changed;
        }

        public int PurgeResolved(DateTime now, int keepSeconds)
        {
            if (keepSeconds < 1)
                keepSeconds = 1;

            DateTime cutoff = now.AddSeconds(-keepSeconds);
            return _resolved.RemoveAll(e => e.ResolvedAt.HasValue && e.ResolvedAt.Value < cutoff);
        }

        public List<RealTimeAlarmInfo> CopySnapshot()
        {
            _activeSort.Clear();
            foreach (DeviceSlot slot in _devices.Values)
            {
                List<AlarmEpisode> episodes = slot.Episodes;
                for (int i = 0; i < episodes.Count; i++)
                    _activeSort.Add(episodes[i]);
            }

            _activeSort.Sort(ActiveComparer);

            var list = new List<RealTimeAlarmInfo>(_activeSort.Count + _resolved.Count);
            for (int i = 0; i < _activeSort.Count; i++)
                list.Add(_activeSort[i].ToInfo(_nameScratch));
            for (int i = 0; i < _resolved.Count; i++)
                list.Add(_resolved[i].ToInfo(_nameScratch));
            return list;
        }

        /// <summary>
        /// 在线历史表：用当前卡片快照分页（含进行中与未过期的已恢复）。
        /// </summary>
        public (List<List<string>> rows, int totalCount) CopyHistoryPage(
            int page, int pageSize, IReadOnlyList<string> titles)
        {
            List<RealTimeAlarmInfo> snapshot = CopySnapshot();
            int total = snapshot.Count;
            if (pageSize < 1)
                pageSize = 1;
            if (page < 1)
                page = 1;

            var rows = new List<List<string>> { new List<string>(titles) };
            int start = (page - 1) * pageSize;
            for (int i = start; i < start + pageSize && i < total; i++)
            {
                RealTimeAlarmInfo info = snapshot[i];
                string handledAt = !string.IsNullOrEmpty(info.ProcessDateTime)
                    ? info.ProcessDateTime
                    : (info.ResolutionDateTime ?? string.Empty);
                string status = info.Status == 2
                    ? "已处理"
                    : (info.DataInterrupted ? "采集中断" : "未处理");
                rows.Add(new List<string>
                {
                    info.Timestamp ?? string.Empty,
                    handledAt,
                    info.DeviceID ?? string.Empty,
                    info.AlarmType ?? string.Empty,
                    info.AlarmDevice ?? string.Empty,
                    status
                });
            }

            return (rows, total);
        }

        private bool MarkMissingDevices()
        {
            bool changed = false;
            foreach (var pair in _devices)
            {
                if (_present.Contains(pair.Key))
                    continue;

                List<AlarmEpisode> episodes = pair.Value.Episodes;
                for (int i = 0; i < episodes.Count; i++)
                {
                    if (episodes[i].DataInterrupted)
                        continue;
                    episodes[i].DataInterrupted = true;
                    episodes[i].InvalidateView();
                    changed = true;
                }
            }

            return changed;
        }

        private bool ApplyDevice(DeviceSlot slot, MqttAlarmDevicePayload device, DateTime readTime, bool firstSight)
        {
            bool changed = false;
            string deviceName = string.IsNullOrEmpty(device.DeviceName) ? device.DeviceCode : device.DeviceName;
            AlarmCategoryCatalog catalog = null;
            if (device.DeviceCode.StartsWith("DDJ", StringComparison.OrdinalIgnoreCase))
                catalog = _stackerCatalog;
            else if (device.DeviceCode.StartsWith("TSJ", StringComparison.OrdinalIgnoreCase))
                catalog = _lifterCatalog;
            List<AlarmEpisode> episodes = slot.Episodes;

            for (int i = 0; i < episodes.Count; i++)
            {
                AlarmEpisode episode = episodes[i];
                if (!string.Equals(episode.DeviceName, deviceName, StringComparison.Ordinal))
                {
                    episode.DeviceName = deviceName;
                    episode.InvalidateView();
                    changed = true;
                }

                episode.FrozenNames.Clear();
            }

            _reliable.Clear();
            List<MqttAlarmPointPayload> points = device.Points;
            if (points != null)
            {
                for (int i = 0; i < points.Count; i++)
                {
                    MqttAlarmPointPayload point = points[i];
                    if (point == null || string.IsNullOrEmpty(point.PointCode))
                        continue;

                    AlarmBit bit = Classify(point.Value);
                    if (bit == AlarmBit.Unknown)
                        continue;

                    _reliable.Add(point.PointCode);
                    string group = point.PointCode;
                    string category = null;
                    bool grouped = false;
                    AlarmCategoryEntry entry = null;
                    if (catalog != null && catalog.TryGet(point.PointCode, out entry))
                    {
                        grouped = true;
                        group = entry.Category;
                        category = entry.Category;
                    }

                    if (bit == AlarmBit.Active)
                    {
                        if (Activate(slot, device.DeviceCode, deviceName, group, category, grouped,
                                point.PointCode, ResolvePointName(point, grouped, entry), readTime, firstSight))
                            changed = true;
                    }
                    else if (slot.ByGroup.TryGetValue(group, out AlarmEpisode existing))
                    {
                        if (existing.FrozenNames.Count == 0)
                            existing.FrozenNames.AddRange(existing.ActivePoints.Values);
                        if (existing.ActivePoints.Remove(point.PointCode))
                        {
                            existing.InvalidateView();
                            changed = true;
                        }
                    }
                }
            }

            for (int i = episodes.Count - 1; i >= 0; i--)
            {
                AlarmEpisode episode = episodes[i];
                if (episode.ActivePoints.Count == 0)
                {
                    Resolve(slot, episode, readTime);
                    changed = true;
                    continue;
                }

                bool missing = false;
                foreach (string code in episode.ActivePoints.Keys)
                {
                    if (_reliable.Contains(code))
                        continue;
                    missing = true;
                    break;
                }

                if (episode.DataInterrupted == missing)
                    continue;

                episode.DataInterrupted = missing;
                episode.InvalidateView();
                changed = true;
            }

            return changed;
        }

        private bool Activate(DeviceSlot slot, string deviceCode, string deviceName, string group, string category,
            bool grouped, string pointCode, string pointName, DateTime readTime, bool firstSight)
        {
            if (!slot.ByGroup.TryGetValue(group, out AlarmEpisode episode))
            {
                episode = new AlarmEpisode
                {
                    EpisodeId = Guid.NewGuid().ToString("N"),
                    DeviceCode = deviceCode,
                    DeviceName = deviceName,
                    GroupKey = group,
                    CategoryName = category,
                    Grouped = grouped,
                    OccurredAt = readTime,
                    ObservedAtConnect = firstSight
                };
                episode.ActivePoints[pointCode] = pointName;
                slot.ByGroup.Add(group, episode);
                slot.Episodes.Add(episode);
                return true;
            }

            if (episode.ActivePoints.TryGetValue(pointCode, out string current)
                && string.Equals(current, pointName, StringComparison.Ordinal))
                return false;

            episode.ActivePoints[pointCode] = pointName;
            episode.InvalidateView();
            return true;
        }

        private void Resolve(DeviceSlot slot, AlarmEpisode episode, DateTime readTime)
        {
            if (episode.ActivePoints.Count > 0 && episode.FrozenNames.Count == 0)
                episode.FrozenNames.AddRange(episode.ActivePoints.Values);

            episode.ActivePoints.Clear();
            episode.ResolvedAt = readTime;
            episode.DataInterrupted = false;
            episode.InvalidateView();
            slot.ByGroup.Remove(episode.GroupKey);
            slot.Episodes.Remove(episode);
            // 最新恢复的排在已恢复区最前，免去整表排序。
            _resolved.Insert(0, episode);
        }

        private static string ResolvePointName(MqttAlarmPointPayload point, bool grouped, AlarmCategoryEntry entry)
        {
            if (!string.IsNullOrEmpty(point.PointName))
                return point.PointName;
            if (grouped && entry != null && !string.IsNullOrEmpty(entry.PointName))
                return entry.PointName;
            return point.PointCode;
        }

        private static DateTime? ParseTime(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            if (DateTime.TryParseExact(text, TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime time))
                return time;
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind | DateTimeStyles.AllowWhiteSpaces, out time))
                return time;
            return null;
        }

        private static AlarmBit Classify(string value)
        {
            if (string.IsNullOrEmpty(value))
                return AlarmBit.Unknown;

            if (value.Length == 1)
            {
                char c = value[0];
                if (c == '1')
                    return AlarmBit.Active;
                if (c == '0')
                    return AlarmBit.Clear;
                return AlarmBit.Unknown;
            }

            if (value.Equals("true", StringComparison.OrdinalIgnoreCase))
                return AlarmBit.Active;
            if (value.Equals("false", StringComparison.OrdinalIgnoreCase))
                return AlarmBit.Clear;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                return number != 0 ? AlarmBit.Active : AlarmBit.Clear;
            return AlarmBit.Unknown;
        }

        private static int CompareActive(AlarmEpisode a, AlarmEpisode b)
        {
            int time = b.OccurredAt.CompareTo(a.OccurredAt);
            return time != 0 ? time : string.CompareOrdinal(a.DeviceCode, b.DeviceCode);
        }

        private enum AlarmBit : byte
        {
            Unknown,
            Active,
            Clear
        }

        private sealed class DeviceSlot
        {
            public readonly Dictionary<string, AlarmEpisode> ByGroup = new(StringComparer.Ordinal);
            public readonly List<AlarmEpisode> Episodes = new();
        }

        private sealed class AlarmEpisode
        {
            public string EpisodeId;
            public string DeviceCode;
            public string DeviceName;
            public string GroupKey;
            public string CategoryName;
            public bool Grouped;
            public DateTime OccurredAt;
            public DateTime? ResolvedAt;
            public bool ObservedAtConnect;
            public bool DataInterrupted;
            public readonly Dictionary<string, string> ActivePoints = new(StringComparer.OrdinalIgnoreCase);
            public readonly List<string> FrozenNames = new();

            private RealTimeAlarmInfo _view;
            private bool _viewDirty = true;

            public void InvalidateView() => _viewDirty = true;

            public RealTimeAlarmInfo ToInfo(List<string> nameScratch)
            {
                if (!_viewDirty && _view != null)
                    return _view;

                bool resolved = ResolvedAt.HasValue;
                nameScratch.Clear();
                if (resolved)
                    nameScratch.AddRange(FrozenNames);
                else
                    nameScratch.AddRange(ActivePoints.Values);

                string title = Grouped && !string.IsNullOrEmpty(CategoryName)
                    ? CategoryName
                    : (nameScratch.Count > 0 ? nameScratch[0] : DeviceCode);

                _view ??= new RealTimeAlarmInfo();
                _view.ActivePointNames = new List<string>(nameScratch);

                _view.EpisodeId = EpisodeId;
                _view.Timestamp = OccurredAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                _view.ResolutionDateTime = resolved
                    ? ResolvedAt.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                    : string.Empty;
                _view.AlarmDevice = DeviceName;
                _view.AlarmType = title;
                _view.AlarmDescription = BuildDescription(title, _view.ActivePointNames);
                _view.Status = resolved ? 2 : 0;
                _view.DeviceID = DeviceCode;
                _view.CategoryName = CategoryName;
                _view.DataInterrupted = !resolved && DataInterrupted;
                _view.ObservedAtConnect = ObservedAtConnect;
                _viewDirty = false;
                return _view;
            }

            private string BuildDescription(string title, List<string> names)
            {
                if (!Grouped || names.Count == 0)
                {
                    string text = title;
                    if (ObservedAtConnect)
                        text += "（接入时已处于报警）";
                    return text;
                }

                DescriptionBuilder.Clear();
                DescriptionBuilder.Append(title).Append('（').Append(names.Count).Append("）：");
                int shown = Math.Min(3, names.Count);
                for (int i = 0; i < shown; i++)
                {
                    if (i > 0)
                        DescriptionBuilder.Append('、');
                    DescriptionBuilder.Append(names[i]);
                }

                if (names.Count > 3)
                    DescriptionBuilder.Append(" 等");
                if (ObservedAtConnect)
                    DescriptionBuilder.Append("（接入时已处于报警）");
                return DescriptionBuilder.ToString();
            }
        }
    }
}
