using System;
using System.Collections.Generic;
using NaughtyAttributes;
using NonsensicalKit.Core.Service.Config;
using UnityEngine;


namespace Frame.BizKit.WMS.Config
{
[CreateAssetMenu(fileName = "DeviceIDConfig", menuName = "ScriptableObjects/DeviceIDConfig")]
public class DeviceIDConfig : ConfigObject
{
    public DeviceIDData data;
    [SerializeField, TextArea(1, 45),Label("批量添加设备ID")] private string m_strings;

    public override ConfigData GetData()
    {
        return data;
    }

    public override void SetData(ConfigData cd)
    {
        data = cd as DeviceIDData;
    }

    [Button("批量添加设备ID")]
    private void AddDeviceID()
    {
        var lines = m_strings.Split(new String[] { Environment.NewLine }, StringSplitOptions.None);
        foreach (string line in lines)
        {
            if (!string.IsNullOrEmpty(line))
            {
                data.m_DeviceIDs.Add(line);
            }
        }
    }
}

[Serializable]
public class DeviceIDData : ConfigData
{
    public List<string> m_DeviceIDs = new List<string>();
}
}
