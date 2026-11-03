using System;
using System.Collections.Generic;
using NonsensicalKit.Core.Service.Config;
using UnityEngine;
namespace Frame.BizKit.WMS.Config
{
    [CreateAssetMenu(fileName = "LifterAlarmCategoryConfig", menuName = "ScriptableObjects/LifterAlarmCategoryConfig")]
    public class LifterAlarmCategoryConfig : ConfigObject
    {
        public LifterAlarmCategoryData data = new();

        public override ConfigData GetData()
        {
            return data;
        }

        public override void SetData(ConfigData cd)
        {
            data = cd as LifterAlarmCategoryData;
        }
    }

    [Serializable]
    public class LifterAlarmCategoryData : ConfigData
    {
        public List<AlarmCategoryEntry> Points = new();
    }
}
