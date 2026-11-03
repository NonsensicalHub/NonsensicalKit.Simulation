using System.Collections.Generic;
using Frame.RealTimeAlarm;
using NonsensicalKit.Core.Service;
using NonsensicalKit.Core.Service.Config;
using NonsensicalKit.Tools.ObjectPool;
using NonsensicalKit.UGUI;
using UnityEngine;

public class WarningDetail : NonsensicalUI
{
    /// <summary>在线详情默认只展示第一个报警点的排查文案；改为更大值即可显示多个。</summary>
    private const int TroubleshootingPointLimit = 1;

    [SerializeField] private GameObject m_warningDetailCellPrefab;
    [SerializeField] private Transform m_pool;

    private GameObjectPoolMk2 _pool;
    private readonly AlarmCategoryCatalog _guideCatalog = new();

    protected override void Awake()
    {
        base.Awake();
        Subscribe<RealTimeAlarmInfo>("showRealTimeAlarmDetail", OnShow);
        Subscribe<int>("showRealTimeAlarm", OnShow);
        InitScrollView();
        ServiceCore.SafeGet<ConfigService>(OnConfigReady);
    }

    private void OnConfigReady(ConfigService config)
    {
        _guideCatalog.Bind(null);
        if (config == null)
            return;

        if (config.TryGetConfig<StackerAlarmCategoryData>(out var stacker) && stacker?.Points != null)
            _guideCatalog.AddRange(stacker.Points);
        if (config.TryGetConfig<LifterAlarmCategoryData>(out var lifter) && lifter?.Points != null)
            _guideCatalog.AddRange(lifter.Points);
    }

    private void InitScrollView()
    {
        _pool = new GameObjectPoolMk2(m_warningDetailCellPrefab, resetAction: OnCellReset, initAction: OnCreateCell);
    }

    private void OnShow(int obj)
    {
        var resolve = Execute<int, List<(string, string)>>("getSolutionByInt", obj);
        ShowDetail(resolve);
    }

    private void OnShow(RealTimeAlarmInfo info)
    {
        if (info != null && !string.IsNullOrEmpty(info.EpisodeId))
        {
            ShowDetail(BuildEpisodeDetail(info));
            return;
        }

        var resolve = Execute<RealTimeAlarmInfo, List<(string, string)>>("getSolution", info);
        ShowDetail(resolve);
    }

    private List<(string, string)> BuildEpisodeDetail(RealTimeAlarmInfo info)
    {
        string resolvedAt = string.IsNullOrEmpty(info.ResolutionDateTime) ? "—" : info.ResolutionDateTime;
        string status = info.DataInterrupted
            ? "采集中断"
            : info.Status == 2 ? "已恢复" : "报警中";
        var rows = new List<(string, string)>
        {
            ("发生时间:", info.Timestamp),
            ("报警设备:", info.AlarmDevice),
            ("报警描述:", info.AlarmDescription),
            ("解决时间:", resolvedAt),
            ("状态:", status)
        };

        if (info.ActivePointNames != null)
        {
            for (int i = 0; i < info.ActivePointNames.Count; i++)
                rows.Add(($"报警点{i + 1}:", $"<color=red>{info.ActivePointNames[i]}</color>"));
        }

        AppendTroubleshooting(rows, info.ActivePointNames, TroubleshootingPointLimit);
        return rows;
    }

    /// <summary>
    /// 按故障名称追加可能原因与解决方案。maxPoints 预留多点展示；当前为 1。
    /// </summary>
    private void AppendTroubleshooting(List<(string, string)> rows, List<string> pointNames, int maxPoints)
    {
        if (rows == null || pointNames == null || pointNames.Count == 0 || maxPoints < 1)
            return;

        int count = Mathf.Min(maxPoints, pointNames.Count);
        bool multi = count > 1;
        for (int i = 0; i < count; i++)
        {
            string faultName = pointNames[i];
            if (string.IsNullOrEmpty(faultName))
                continue;

            string causes = "暂无排查资料";
            string solutions = "暂无排查资料";
            if (_guideCatalog.TryGetByName(faultName, out AlarmCategoryEntry entry))
            {
                if (!string.IsNullOrWhiteSpace(entry.PossibleCauses))
                    causes = entry.PossibleCauses.Trim();
                if (!string.IsNullOrWhiteSpace(entry.Solutions))
                    solutions = entry.Solutions.Trim();
            }

            string causeLabel = multi ? $"可能原因({faultName}):" : "可能原因:";
            string solutionLabel = multi ? $"解决方案({faultName}):" : "解决方案:";
            rows.Add((causeLabel, causes));
            rows.Add((solutionLabel, solutions));
        }
    }

    private void ShowDetail(List<(string, string)> info)
    {
        _pool.Clear();
        if (info is { Count: > 0 })
        {
            foreach (var tuple in info)
            {
                _pool.New().GetComponent<WarningDetailCell>()?.SetText(tuple);
            }
        }

        OpenSelf();
    }

    private void OnCreateCell(GameObject cell)
    {
        cell.transform.SetParent(m_warningDetailCellPrefab.transform.parent, false);
        cell.SetActive(true);
    }

    private void OnCellReset(GameObject cell)
    {
        cell.transform.SetParent(m_pool);
        cell.SetActive(false);
    }
}
