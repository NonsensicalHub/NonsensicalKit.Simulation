using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NonsensicalKit.Core;
using NonsensicalKit.Core.Service;
using NonsensicalKit.Core.Service.Config;
using NonsensicalKit.UGUI.Table;
using TMPro;
using UnityEngine;

public enum DetailLabelEvent
{
    ShowDetailLabels,
    HideDetailLabels,
    InitSampleConfig,
    SelectName,
}

//详情标签面板
public class DetailLabelPanel : NonsensicalMono
{
    [SerializeField] private ScrollViewEx m_nameScrollView;
    [SerializeField] private SimpleGroup m_labelGroup;

    private List<InfoObject> _objs = new();

    private Dictionary<string, DetailLabelInfo> _infos = new();

    private int _selectIndex;

    private bool _refreshFlag;

    private void Awake()
    {
        ServiceCore.SafeGet<ConfigService>(SafeGetConfig);
        Subscribe<JArray>(DetailLabelEvent.ShowDetailLabels, OnGetJson);
        Subscribe(DetailLabelEvent.HideDetailLabels, HideLabels);
        Subscribe<int>(DetailLabelEvent.SelectName, OnSelectName);
        Subscribe<List<DetailLabelInfo>>(DetailLabelEvent.InitSampleConfig, OnGetSampleConfig);

        m_nameScrollView.SetUpdateFunc(OnUpdateName);
        m_nameScrollView.SetItemCountFunc(() => _objs.Count);
        m_labelGroup.OnNewObject.AddListener(OnNewLabel);
    }
    
    private void OnEnable()
    {
        NonsensicalInstance.Instance.DelayDoIt(0, () =>
        {
            if (gameObject.activeInHierarchy)
            {
                m_nameScrollView.UpdateData(false);
            }
        });
    }
    
    private void HideLabels()
    {
        _objs = new();
        _refreshFlag = true;
        _selectIndex = 0;
        if (gameObject.activeInHierarchy)
            m_nameScrollView.UpdateData();
    }

    private void OnGetJson(JArray array)
    {
        if (array == null || array.Count == 0)
        {
            HideLabels();
            return;
        }

        _objs = new();
        _refreshFlag = true;
        foreach (var token in array)
        {
            var obj = token as JObject;
            InfoObject infoObj = new();
            bool first = true;
            foreach (var pair in obj)
            {
                if (_infos.TryGetValue(pair.Key, out var info))
                {
                    if (first)
                    {
                        first = false;
                        infoObj.Name = pair.Value.ToString();
                    }
                    
                    infoObj.Keys.Add(pair.Key);
                    var value = pair.Value.ToString();
                    for (int i = 0; i < info.ConvertKey.Length; i++)
                    {
                        if (info.ConvertKey[i] == value)
                        {
                            value = info.ConvertValue[i];
                            break;
                        }
                    }

                    value += info.Unit;
                    infoObj.Values.Add(value);
                }
            }

            _objs.Add(infoObj);
        }

        if (_objs.Count == 0)
        {
            HideLabels();
            return;
        }

        if (gameObject.activeInHierarchy)
            m_nameScrollView.UpdateData();

        m_labelGroup.Create(_objs[0].Keys.Count);
    }

    private void OnSelectName(int index)
    {
        if (index < 0 || index >= _objs.Count)
            return;

        _selectIndex = index;
        m_labelGroup.Create(_objs[index].Keys.Count);
    }
    
    private void OnUpdateName(int index, RectTransform rect)
    {
        if (index < 0 || index >= _objs.Count)
        {
            return;
        }

        var element = rect.GetComponent<DetailLabelElement>();
        element.Init(_objs[index].Name,index);
        if (_refreshFlag && index == 0)
        {
            _refreshFlag = false;
            element.On();
        }
    }

    private void OnNewLabel(int index, GameObject go)
    {
        if (_selectIndex < 0 || _selectIndex >= _objs.Count)
        {
            return;
        }

        go.transform.GetChild(0).GetComponent<TMP_Text>().text = _objs[_selectIndex].Keys[index];
        go.transform.GetChild(1).GetComponent<TMP_Text>().text = _objs[_selectIndex].Values[index];
    }

    private void SafeGetConfig(ConfigService configService)
    {
        var infos = configService.GetConfigs<DetailLabelInfo>();
        foreach (var info in infos)
        {
            _infos[info.Name] = info;
        }
    }

    private void OnGetSampleConfig(List<DetailLabelInfo> infos)
    {
        foreach (var info in infos)
        {
            _infos[info.Name] = info;
        }
    }
}

public class InfoObject
{
    public string Name;
    public List<string> Keys = new();
    public List<string> Values = new();
}
