using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NaughtyAttributes;
using NonsensicalKit.Core.Service.Config;
using UnityEngine;


namespace Frame.BizKit.WMS.Config
{
[CreateAssetMenu(fileName = "AlarmDetailsConfig", menuName = "ScriptableObjects/AlarmDetailsConfig")]
public class AlarmDetailsConfig : ConfigObject
{
    public AlarmDetailsData data;
    [SerializeField] private bool m_editorMode;

    [SerializeField, TextArea(5, 75), ShowIf("m_editorMode"), Label("批量添加,多数据以换行区分,code与详情以空格区分")]
    private string m_text;

    [SerializeField, ShowIf("m_editorMode")]
    private string m_editorDeviceType;

    public override ConfigData GetData()
    {
        return data;
    }

    public override void SetData(ConfigData cd)
    {
        data = cd as AlarmDetailsData;
    }

    [Button("批量添加")]
    private void Add()
    {
        var lines = m_text.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
        foreach (var line in lines)
        {
            if (!string.IsNullOrEmpty(line))
            {
                var temp = Regex.Split(line, "\\s+");
                if (temp.Length < 2) continue;
                var code = temp[0].Trim();
                var details = temp[^1].Trim();

                data[m_editorDeviceType]?.m_AlarmDetails.Add(new AlarmDetailsElement()
                    { m_Code = code, m_Details = details });
            }
        }
    }
}

[Serializable]
public class AlarmDetailsData : ConfigData
{
    public List<AlarmDetailsElementData> m_ListAlarmDetailsElementData = new List<AlarmDetailsElementData>();

    public AlarmDetailsElementData this[string deviceType]
    {
        get
        {
            if (Data == null)
            {
                Data = new Dictionary<string, AlarmDetailsElementData>();
                foreach (var alarmDetailsElement in m_ListAlarmDetailsElementData)
                {
                    Data[alarmDetailsElement.m_DeviceType] = alarmDetailsElement;
                }
            }

            return Data.GetValueOrDefault(deviceType);
        }
    }

    private Dictionary<string, AlarmDetailsElementData> Data { get; set; }
}

[Serializable]
public class AlarmDetailsElementData
{
    public string m_DeviceType;
    public List<AlarmDetailsElement> m_AlarmDetails = new List<AlarmDetailsElement>();

    public AlarmDetailsElement this[string code]
    {
        get
        {
            if (Elements == null)
            {
                Elements = new Dictionary<string, AlarmDetailsElement>();
                foreach (var alarmDetailsElement in m_AlarmDetails)
                {
                    Elements[alarmDetailsElement.m_Code] = alarmDetailsElement;
                }
            }

            return Elements.GetValueOrDefault(code) ;
        }
    }


    private Dictionary<string, AlarmDetailsElement> Elements { get; set; }
}

[Serializable]
public class AlarmDetailsElement
{
    public string m_Code;
    public string m_Details;
    public string m_Resolution;
}
}
