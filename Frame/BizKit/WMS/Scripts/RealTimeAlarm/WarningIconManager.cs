using System.Collections.Generic;
using NonsensicalKit.Core;
using NonsensicalKit.Tools.ObjectPool;
using NonsensicalKit.UGUI;
using UnityEngine;

namespace Frame.BizKit.WMS.RealTimeAlarm
{
public class WarningIconManager : NonsensicalMono
{
    [SerializeField] private WarningIcon m_prefab;
    private readonly Dictionary<Transform, WarningIcon> _actives = new();

    private ComponentPoolMk2<WarningIcon> _pool;

    private void Awake()
    {
        _pool = new ComponentPoolMk2<WarningIcon>(m_prefab, OnIconCreate);

        Subscribe<Transform, string,string>("SwitchWarningIconState", OnSwitchWarningIconState);
    }

    private void OnIconCreate(ComponentPool<WarningIcon> pool, WarningIcon follow)
    {
        follow.transform.SetParent(transform);
    }

    private void OnSwitchWarningIconState(Transform trans, string deviceName, string errorCode)
    {
        if (string.IsNullOrEmpty(deviceName))
        {
            return;
        }
        if (errorCode!="0"&&string.IsNullOrEmpty(errorCode)==false)
        {
            if (_actives.ContainsKey(trans))
            {
                return;
            }

            var newIcon = _pool.New();
            newIcon.SetState(trans,deviceName,errorCode);
            _actives.Add(trans, newIcon);
        }
        else
        {
            if (_actives.TryGetValue(trans, out var active) == false)
            {
                return;
            }

            _pool.Store(active);
            _actives.Remove(trans);
        }
    }
}
}
