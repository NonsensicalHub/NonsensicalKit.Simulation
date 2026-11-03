using System;
using System.Collections.Generic;
using NonsensicalKit.Core.Service.Config;
using UnityEngine;

namespace Frame.RealTimeAlarm
{
    [CreateAssetMenu(fileName = "StackerAlarmCategoryConfig", menuName = "ScriptableObjects/StackerAlarmCategoryConfig")]
    public class StackerAlarmCategoryConfig : ConfigObject
    {
        public StackerAlarmCategoryData data = new();

        public override ConfigData GetData()
        {
            return data;
        }

        public override void SetData(ConfigData cd)
        {
            data = cd as StackerAlarmCategoryData;
        }
    }

    [Serializable]
    public class StackerAlarmCategoryData : ConfigData
    {
        public List<AlarmCategoryEntry> Points = new();
    }

    /// <summary>
    /// 报警点分类与排查资料。堆垛机、提升机等设备共用。
    /// </summary>
    [Serializable]
    public class AlarmCategoryEntry
    {
        public string PointCode;
        public string Category;
        public string PointName;
        [TextArea(2, 6)]
        public string PossibleCauses;
        [TextArea(3, 12)]
        public string Solutions;
    }

    /// <summary>
    /// 按 PointCode / 故障名称查分类与排查文案。未收录的点不合并。
    /// </summary>
    public sealed class AlarmCategoryCatalog
    {
        private readonly Dictionary<string, AlarmCategoryEntry> _byCode = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, AlarmCategoryEntry> _byName = new(StringComparer.OrdinalIgnoreCase);

        public int Count => _byCode.Count;

        public void Bind(IList<AlarmCategoryEntry> points)
        {
            _byCode.Clear();
            _byName.Clear();
            if (points != null)
                AddRange(points);
        }

        public void AddRange(IList<AlarmCategoryEntry> points)
        {
            if (points == null)
                return;

            for (int i = 0; i < points.Count; i++)
            {
                AlarmCategoryEntry entry = points[i];
                if (entry == null || string.IsNullOrEmpty(entry.PointCode) || string.IsNullOrEmpty(entry.Category))
                    continue;

                _byCode[entry.PointCode.Trim()] = entry;
                if (!string.IsNullOrEmpty(entry.PointName))
                    _byName[entry.PointName.Trim()] = entry;
            }
        }

        public bool TryGet(string pointCode, out AlarmCategoryEntry entry)
        {
            entry = null;
            if (string.IsNullOrEmpty(pointCode))
                return false;
            return _byCode.TryGetValue(pointCode.Trim(), out entry);
        }

        public bool TryGetByName(string faultName, out AlarmCategoryEntry entry)
        {
            entry = null;
            if (string.IsNullOrEmpty(faultName))
                return false;
            return _byName.TryGetValue(faultName.Trim(), out entry);
        }
    }
}
