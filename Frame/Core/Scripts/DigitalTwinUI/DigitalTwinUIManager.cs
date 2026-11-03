using System;
using System.Collections.Generic;
using NonsensicalKit.Core;
using NonsensicalKit.Core.Log;
using NonsensicalKit.Core.Service;
using NonsensicalKit.Tools.ObjectPool;
using UnityEngine;


namespace Frame.Core.DigitalTwinUI
{
[ServicePrefab("Services/DigitalTwinUIManager")]
public class DigitalTwinUIManager : NonsensicalMono, IMonoService
{
    private const string RegisterUIPointKey = "registerUIPoint";

    [SerializeField] private DigitalTwinUIPrefabConfig[] m_prefabs;
    [SerializeField] private Canvas m_canvas;

    private Dictionary<string, GameObjectPoolMk2> _pools;
    private readonly Dictionary<string, UIPoint> _uiPoints = new();
    private readonly Dictionary<string, GameObject> _uiPointsObjs = new();
    private readonly Dictionary<string, string> _uiPointTypes = new();
    private bool _listenerReady;

    public bool IsReady => _listenerReady;

    public Action InitCompleted { get; set; }

    private void Awake()
    {
        _pools = new Dictionary<string, GameObjectPoolMk2>();
        foreach (var item in m_prefabs)
        {
            if (_pools.ContainsKey(item.Type))
            {
                LogCore.Warning("重复的UI类型");
                continue;
            }

            _pools.Add(item.Type, new GameObjectPoolMk2(item.Prefab, OnReset, OnInit));
        }

        IOCC.AddListener<UIPoint>(RegisterUIPointKey, OnRegisterUIPointMessage);
        _listenerReady = true;
        InitCompleted?.Invoke();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (_listenerReady)
            IOCC.RemoveListener<UIPoint>(RegisterUIPointKey, OnRegisterUIPointMessage);
        _listenerReady = false;
        _uiPoints.Clear();
        _uiPointsObjs.Clear();
        _uiPointTypes.Clear();
    }

    private void OnInit(GameObject UIbase)
    {
        UIbase.SetActive(true);
    }

    private void OnReset(GameObject UIbase)
    {
        UIbase.SetActive(false);
    }

    public void RegisterUIPoint(UIPoint uiPoint)
    {
        CacheUIPoint(uiPoint);
        IOCC.Set(RegisterUIPointKey, uiPoint);
    }

    public bool TryGetUIPoint(string id, out UIPoint uiPoint)
    {
        if (string.IsNullOrEmpty(id))
        {
            uiPoint = null;
            return false;
        }

        return _uiPoints.TryGetValue(id, out uiPoint);
    }

    private void OnRegisterUIPointMessage(UIPoint uiPoint)
    {
        CacheUIPoint(uiPoint);
    }

    private void CacheUIPoint(UIPoint uiPoint)
    {
        if (uiPoint == null || string.IsNullOrEmpty(uiPoint.m_IconID))
            return;

        _uiPoints[uiPoint.m_IconID] = uiPoint;
    }

    public void Register(DigitalTwinUIInfo info)
    {
        if (_pools.ContainsKey(info.Type))
        {
            if (!_uiPointsObjs.TryGetValue(info.ID, out var go))
            {
                go = _pools[info.Type].New();
                go.transform.SetParent(m_canvas.transform);
                _uiPointsObjs.Add(info.ID, go);
                _uiPointTypes[info.ID] = info.Type;
            }

            if (go.TryGetComponent<IDigitalTwinUI>(out var v))
            {
                v.Init(info.Point, info);

                if (TryGetUIPoint(info.ID, out var uiPoint))
                    IOCC.Set(RegisterUIPointKey, uiPoint);
            }
            else
            {
                LogCore.Error("预制体顶节点为挂载所需的组件");
            }
        }
        else
        {
            LogCore.Warning($"类型：{info.Type} 的预制体尚未配置");
        }
    }

    /// <summary>场景点销毁时归还对象池并清缓存。</summary>
    public void Unregister(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return;
        }

        if (_uiPointsObjs.TryGetValue(id, out var go) && go != null)
        {
            if (_uiPointTypes.TryGetValue(id, out var type) && _pools.TryGetValue(type, out var pool))
            {
                pool.Store(go);
            }
            else
            {
                Destroy(go);
            }

            _uiPointsObjs.Remove(id);
        }

        _uiPointTypes.Remove(id);
        _uiPoints.Remove(id);
    }
}

[Serializable]
public class DigitalTwinUIPrefabConfig
{
    public string Type;
    public GameObject Prefab;
}
}
