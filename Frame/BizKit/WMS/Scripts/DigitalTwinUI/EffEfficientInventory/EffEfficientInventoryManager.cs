using Cysharp.Threading.Tasks;
using Frame.BizKit.WMS.Network;
using Frame.Core.Config;
using NonsensicalKit.Core;
using NonsensicalKit.Core.DagLogicNode;
using NonsensicalKit.UGUI;
using NonsensicalKit.UGUI.Table;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Frame.BizKit.WMS
{
public class EffEfficientInventoryManager : NonsensicalUI
{
    private const string TwinPosStatusKey = "twinPosStatus";
    private const string OfflineSearchHandler = "searchInventoryCargoLocation";

    [SerializeField] private string m_multHmID;
    [SerializeField] private ScrollView m_scrollViewEx;
    [SerializeField] private Button m_execute;
    [SerializeField] private Button m_addRule;
    [SerializeField] private GameObject m_maskUI;
    [SerializeField] private TwinNearExpiryRequest m_nearExpiryRequest;

    [SerializeField, Tooltip("与 TwinPosStatusRequest 一致：接口坐标是否 1 基")]
    private bool m_oneBasedIndex = true;

    private readonly List<EffEfficientInventoryConfig> _configs = new();

    private WarehouseInventoryMultiHighlight MultHm { get; set; }

    private bool _isExecuting;

    protected override void Awake()
    {
        base.Awake();
        if (m_nearExpiryRequest == null)
            m_nearExpiryRequest = GetComponent<TwinNearExpiryRequest>();

        InitTable();
        m_addRule.onClick.AddListener(AddRule);
        m_execute.onClick.AddListener(Execute);

        m_maskUI?.SetActive(false);

        Subscribe<EffEfficientInventoryConfig>("deleteEfIConfig", DeleteConfig);

        Subscribe((int)DagLogicNodeEnum.NodeExit, "库存管理", OnNodeExit);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        m_execute.onClick.RemoveAllListeners();
        m_addRule.onClick.RemoveAllListeners();
    }

    #region Publish

    public void Open(string libID)
    {
        m_multHmID = libID;
        OpenController();
    }

    public void Open()
    {
        m_multHmID = Execute<string>("getCurrentLibraryName");
        OpenController();
    }

    public void Close()
    {
        CloseSelf();
        MultHm?.ExitReview();
        MultHm = null;
        m_multHmID = string.Empty;
    }

    public void Search()
    {
        Execute();
    }

    public void SwitchHandle(bool isOn)
    {
        if (isOn)
            Open();
        else
        {
            Close();
        }
    }

    #endregion

    private WarehouseInventoryMultiHighlight GetMultHm() => IOCC.Get<WarehouseInventoryMultiHighlight>(m_multHmID);
    private void OnNodeExit() => Close();

    private void OpenController()
    {
        OpenSelf();
        MultHm = GetMultHm();
        if (MultHm == null)
        {
            Debug.Log($"当前所在库:{m_multHmID}, 当存在多个库区的时候,请注意设置当前库ID");
        }
    }

    private void InitTable()
    {
        m_scrollViewEx.SetUpdateFunc((index, rectTransform) =>
        {
            try
            {
                if (_configs.Count == 0) return;
                rectTransform.GetComponent<RuleElement>().SetValue(_configs[index]);
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
        });

        m_scrollViewEx.SetItemCountFunc(() => _configs.Count);
    }

    /// <summary>
    /// 执行检索（按钮入口，内部转 UniTask）。
    /// </summary>
    private void Execute()
    {
        if (_isExecuting || MultHm == null)
        {
            return;
        }

        if (_configs is not { Count: > 0 }) return;

        // 临期基准日固定为当天，无需用户配置。
        ApplyTodayAsNearDate();
        ExecuteAsync().Forget();
    }

    /// <summary>
    /// 添加规则
    /// </summary>
    private void AddRule()
    {
        var temp = new EffEfficientInventoryConfig()
        {
            ID = Guid.NewGuid().ToString(),
            Index = (_configs.Count + 1).ToString(),
            NearDate = TodayDateString(),
            ThresholdDate = "30"
        };
        temp.SaveColor(Color.cyan);
        _configs.Add(temp);

        m_scrollViewEx.UpdateData();
    }

    /// <summary>
    /// 删除规则项 
    /// </summary>
    private void DeleteConfig(EffEfficientInventoryConfig rule)
    {
        var a = _configs.Find(x => x.ID == rule.ID);
        if (a == null) return;
        _configs.Remove(a);
        var index = 1;
        foreach (var config in _configs)
        {
            config.Index = (index++).ToString();
        }

        m_scrollViewEx.UpdateData();
    }

    private async UniTaskVoid ExecuteAsync()
    {
        _isExecuting = true;
        m_maskUI?.SetActive(true);


        await SearchAndApplyHighlightAsync();

        _isExecuting = false;
        m_maskUI?.SetActive(false);
    }


    /// <summary>
    /// 检索货位并应用多色高亮。在线走 TwinNearExpiry；离线走随机演示。
    /// </summary>
    private async UniTask SearchAndApplyHighlightAsync()
    {
        Int4[][] locationBatches = TwinDataSource.IsOnline
            ? await SearchOnlineBatchesAsync()
            : SearchOfflineBatches();

        if (locationBatches == null)
        {
            return;
        }

        var colors = _configs.Select(x => x.ToColor()).ToArray();
        WarehouseInventoryMultiHighlight highlight = MultHm;
        if (highlight == null)
        {
            Debug.LogWarning($"{nameof(EffEfficientInventoryManager)} 未找到 {nameof(WarehouseInventoryMultiHighlight)}。");
            return;
        }

        highlight.ApplySearchHighlight(locationBatches, colors);

        // 等底层协程跑完，再关遮罩。
        await UniTask.WaitUntil(
            () => !highlight.IsHighlightUpdating,
            cancellationToken: this.GetCancellationTokenOnDestroy());
    }

    private async UniTask<Int4[][]> SearchOnlineBatchesAsync()
    {
        if (m_nearExpiryRequest == null)
        {
            Debug.LogWarning($"{nameof(EffEfficientInventoryManager)} 缺少 {nameof(TwinNearExpiryRequest)}。", this);
            return null;
        }

        string dataDate = _configs.Count > 0 ? _configs[0].NearDate : TodayDateString();
        List<TwinNearExpiryItem> items = await m_nearExpiryRequest.SearchAsync(dataDate);
        if (items == null || items.Count == 0)
        {
            Debug.Log($"{nameof(EffEfficientInventoryManager)} 近效期接口无数据。", this);
            return EmptyBatches(_configs.Count);
        }

        return BuildBatchesFromNearExpiry(items);
    }

    private Int4[][] SearchOfflineBatches()
    {
        return Execute<int, Int4[][]>(OfflineSearchHandler, _configs.Count);
    }

    /// <summary>
    /// 按规则临期天数（升序）将货位分到互斥批次：days ≤ 最小命中阈值 → 对应规则颜色。
    /// </summary>
    private Int4[][] BuildBatchesFromNearExpiry(List<TwinNearExpiryItem> items)
    {
        int batchCount = _configs.Count;
        var buckets = new List<Int4>[batchCount];
        var seen = new HashSet<Int4>[batchCount];
        for (int i = 0; i < batchCount; i++)
        {
            buckets[i] = new List<Int4>();
            seen[i] = new HashSet<Int4>();
        }

        var rules = new List<(int index, int threshold)>(batchCount);
        for (int i = 0; i < batchCount; i++)
        {
            if (!int.TryParse(_configs[i].ThresholdDate, NumberStyles.Integer, CultureInfo.InvariantCulture, out int threshold) ||
                threshold < 0)
            {
                threshold = 30;
            }

            rules.Add((i, threshold));
        }

        rules.Sort((a, b) => a.threshold.CompareTo(b.threshold));

        if (!TryBuildPosLookup(out Dictionary<long, TwinPosStatusItem> byPosId,
                out Dictionary<string, TwinPosStatusItem> byPosNo))
        {
            Debug.LogWarning(
                $"{nameof(EffEfficientInventoryManager)} 未找到 twinPosStatus，无法将货位映射到网格坐标。",
                this);
            return EmptyBatches(batchCount);
        }

        for (int i = 0; i < items.Count; i++)
        {
            TwinNearExpiryItem item = items[i];
            if (item == null)
                continue;

            int batchIndex = -1;
            for (int r = 0; r < rules.Count; r++)
            {
                if (item.days_to_expiry <= rules[r].threshold)
                {
                    batchIndex = rules[r].index;
                    break;
                }
            }

            if (batchIndex < 0)
                continue;

            if (!TryResolveLocation(item, byPosId, byPosNo, out Int4 location))
                continue;

            if (seen[batchIndex].Add(location))
                buckets[batchIndex].Add(location);
        }

        var result = new Int4[batchCount][];
        for (int i = 0; i < batchCount; i++)
            result[i] = buckets[i].ToArray();
        return result;
    }

    private static bool TryBuildPosLookup(
        out Dictionary<long, TwinPosStatusItem> byPosId,
        out Dictionary<string, TwinPosStatusItem> byPosNo)
    {
        byPosId = null;
        byPosNo = null;
        if (!IOCC.TryGet(TwinPosStatusKey, out List<TwinPosStatusItem> statuses) || statuses == null)
            return false;

        byPosId = new Dictionary<long, TwinPosStatusItem>(statuses.Count);
        byPosNo = new Dictionary<string, TwinPosStatusItem>(statuses.Count);
        for (int i = 0; i < statuses.Count; i++)
        {
            TwinPosStatusItem s = statuses[i];
            if (s == null)
                continue;

            if (s.pos_id != 0 && !byPosId.ContainsKey(s.pos_id))
                byPosId[s.pos_id] = s;

            if (!string.IsNullOrEmpty(s.pos_no) && !byPosNo.ContainsKey(s.pos_no))
                byPosNo[s.pos_no] = s;
        }

        return byPosId.Count > 0 || byPosNo.Count > 0;
    }

    private bool TryResolveLocation(
        TwinNearExpiryItem item,
        Dictionary<long, TwinPosStatusItem> byPosId,
        Dictionary<string, TwinPosStatusItem> byPosNo,
        out Int4 location)
    {
        location = default;
        TwinPosStatusItem match = null;
        if (item.pos_id != 0)
            byPosId.TryGetValue(item.pos_id, out match);

        if (match == null && !string.IsNullOrEmpty(item.pos_no))
            byPosNo.TryGetValue(item.pos_no, out match);

        return match != null && TryBuildLocation(match, out location);
    }

    private bool TryBuildLocation(TwinPosStatusItem item, out Int4 location)
    {
        location = default;
        if (!TryParseIndex(item.layer_no, out int layer) ||
            !TryParseIndex(item.column_no, out int column) ||
            !TryParseIndex(item.row_no, out int row) ||
            !TryParseIndex(item.depth_no, out int depth))
            return false;

        if (m_oneBasedIndex)
        {
            layer--;
            column--;
            row--;
            depth--;
        }

        if (layer < 0 || column < 0 || row < 0 || depth < 0)
            return false;

        location = new Int4(layer, column, row, depth);
        return true;
    }

    private static bool TryParseIndex(string value, out int index)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out index);
    }

    private static Int4[][] EmptyBatches(int count)
    {
        var result = new Int4[count][];
        for (int i = 0; i < count; i++)
            result[i] = Array.Empty<Int4>();
        return result;
    }

    private void RestoreHighlightNormal() => MultHm?.RestoreNormal();

    private void ApplyTodayAsNearDate()
    {
        var today = TodayDateString();
        foreach (var config in _configs)
        {
            config.NearDate = today;
        }
    }

    private static string TodayDateString() => DateTime.Now.ToString("yyyy-MM-dd");
}
}
