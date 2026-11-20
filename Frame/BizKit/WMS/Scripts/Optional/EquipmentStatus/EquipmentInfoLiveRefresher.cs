using System.Collections;
using Frame.BizKit.WMS.Equipment;
using NonsensicalKit.Core;
using NonsensicalKit.Core.DagLogicNode;
using UnityEngine;

namespace EquipmentStatus
{
/// <summary>
/// 设备管理：当前类型展示时，每秒委托 <see cref="EquipmentInfoManager.RefreshVisible"/> 刷新。
/// </summary>
public class EquipmentInfoLiveRefresher : NonsensicalMono
{
    [SerializeField] private EquipmentInfoManager m_infoManager;
    [SerializeField] private float m_intervalSeconds = 1f;
    [SerializeField] private bool m_log;

    private string _activeType;
    private Coroutine _loop;

    private void Awake()
    {
        if (m_infoManager == null)
            m_infoManager = FindFirstObjectByType<EquipmentInfoManager>();

        Subscribe<string>("showEquipment", OnShowEquipment);
        Subscribe((int)DagLogicNodeEnum.NodeExit, "设备管理", OnEquipmentPanelExit);
    }

    protected override void OnDestroy()
    {
        StopLoop();
        base.OnDestroy();
    }

    private void OnShowEquipment(string type)
    {
        _activeType = type;
        if (string.IsNullOrEmpty(type))
        {
            StopLoop();
            return;
        }

        StartLoop();
    }

    private void OnEquipmentPanelExit()
    {
        _activeType = null;
        StopLoop();
    }

    private void StartLoop()
    {
        StopLoop();
        RefreshOnce();
        _loop = StartCoroutine(RefreshLoop());
    }

    private void StopLoop()
    {
        if (_loop == null)
            return;
        StopCoroutine(_loop);
        _loop = null;
    }

    private IEnumerator RefreshLoop()
    {
        float interval = Mathf.Max(0.2f, m_intervalSeconds);
        var wait = new WaitForSecondsRealtime(interval);
        while (!string.IsNullOrEmpty(_activeType))
        {
            yield return wait;
            if (string.IsNullOrEmpty(_activeType))
                break;
            RefreshOnce();
        }

        _loop = null;
    }

    private void RefreshOnce()
    {
        if (string.IsNullOrEmpty(_activeType))
            return;

        if (m_infoManager == null)
            m_infoManager = FindFirstObjectByType<EquipmentInfoManager>();

        if (m_infoManager == null)
            return;

        m_infoManager.RefreshVisible(_activeType);

        if (m_log)
            Debug.Log($"[EquipmentInfoLiveRefresher] RefreshVisible type={_activeType}", this);
    }
}
}
