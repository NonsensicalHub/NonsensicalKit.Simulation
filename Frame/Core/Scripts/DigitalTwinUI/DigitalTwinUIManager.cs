using System;
using System.Collections.Generic;
using NonsensicalKit.Core;
using NonsensicalKit.Core.Log;
using NonsensicalKit.Core.Service;
using NonsensicalKit.Tools.ObjectPool;
using UnityEngine;

[ServicePrefab("Services/DigitalTwinUIManager")]
public class DigitalTwinUIManager : NonsensicalMono, IMonoService
{
    private const string RegisterUIPointKey = "registerUIPoint";

    [SerializeField] private DigitalTwinUIPrefabConfig[] m_prefabs;
    [SerializeField] private Canvas m_canvas;

    private Dictionary<string, GameObjectPoolMk2> _pools;
    private readonly Dictionary<string, UIPoint> _uiPoints = new();
    private readonly Dictionary<string, GameObject> _uiPointsObjs = new();
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

        // 先挂监听并缓存，避免 UIPoint 早于各 UI Init 发出的注册丢失
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
    }

    private void OnInit(GameObject UIbase)
    {
        UIbase.SetActive(true);
    }

    private void OnReset(GameObject UIbase)
    {
        UIbase.SetActive(false);
    }

    /// <summary>
    /// 供 UIPoint 直接注册，不依赖 IOCC 时序。
    /// </summary>
    public void RegisterUIPoint(UIPoint uiPoint)
    {
        CacheUIPoint(uiPoint);
        // 同步广播，已就绪的 UI 监听者可立即收到
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
            }

            if (go.TryGetComponent<IDigitalTwinUI>(out var v))
            {
                v.Init(info.Point, info);

                // Init 里才 AddListener，补发已缓存的 UIPoint，避免时序丢失
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
}

[Serializable]
public class DigitalTwinUIPrefabConfig
{
    public string Type;
    public GameObject Prefab;
}
