using System;
using System.Collections.Generic;
using NaughtyAttributes;
using NonsensicalKit.Core.Service.Config;
using UnityEngine;


[CreateAssetMenu(fileName = "RealTimeAlarmInfo", menuName = "ScriptableObjects/RealTimeAlarmInfo")]
public class RealTimeSctObj : ConfigObject
{
    public RealTimeAlarmDevicesInfos deviceInfos = new();

    public override ConfigData GetData()
    {
        deviceInfos.dic_deviceIDToDeviceName = new();
        deviceInfos.dic_selfIDToDeviceID = new();
        foreach (var item in deviceInfos.list_deviceInfos)
        {
            deviceInfos.dic_deviceIDToDeviceName.Add(item.m_DeviceID, item.m_deviceName);
            deviceInfos.dic_selfIDToDeviceID.Add(item.m_selfID, item.m_DeviceID);
        }

        return deviceInfos;
    }

    public override void SetData(ConfigData cd)
    {
        deviceInfos = cd as RealTimeAlarmDevicesInfos;
    }

#if UNITY_EDITOR

    [SerializeField] private InformativeGenerateTemplates[] temps;

    [Button("生成")]
    private void Generate()
    {
        deviceInfos.list_deviceInfos.Clear();
        foreach (var item in temps)
        {
            for (int i = 0; i < item.count; i++)
            {
                deviceInfos.list_deviceInfos.Add(new RealTimeAlarmPosInfo()
                {
                    m_selfID = item.Prefix + (item.indexB + i).ToString("1000"),
                    m_DeviceID = item.Prefix + (item.indexB + i).ToString("1000"),
                    m_deviceName = item.deviceName,
                    m_equipmentType = item.m_equipmentType
                });
            }
        }
    }
#endif
}

[Serializable]
public class RealTimeAlarmDevicesInfos : ConfigData
{
    public List<RealTimeAlarmPosInfo> list_deviceInfos = new();
    public Dictionary<string, string> dic_deviceIDToDeviceName;
    public Dictionary<string, string> dic_selfIDToDeviceID;
}

[Serializable]
public class RealTimeAlarmPosInfo
{
    public string m_selfID;
    public string m_deviceName;

    /// <summary>
    /// todo:跟SN码一致
    /// </summary>
    public string m_DeviceID;

    public int m_equipmentType;
}

[Serializable]
public class InformativeGenerateTemplates
{
    public string Prefix;
    public int indexB;
    public int selfIDBegin;
    public string deviceName;
    public int count;
    public int m_equipmentType;
}
