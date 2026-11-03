using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using NaughtyAttributes;
using Newtonsoft.Json;
using NonsensicalKit.Core;
using NonsensicalKit.DigitalTwin.Warehouse;
using UnityEngine;

/// <summary>
/// 拉取 WMS 全部货位状态，写入 WarehouseManager 并同步 IOCC「cargoStatus」。
/// 按网格坐标缓存货位数据，外部通过 <see cref="GetCargoInfoByPosHandler"/>（参数 Int4）查询。
/// 仅在 <see cref="TwinDataSource.IsOnline"/> 时生效；离线模式由 <see cref="DemoWarehouseRandom"/> 灌假数据。
/// 大数据量时用分帧流式解析 + 分帧应用，避免主线程卡死；全程 PlayerLoop，兼容 WebGL。
/// </summary>

namespace Frame.BizKit.WMS.Network
{
public class TwinPosStatusRequest : TwinFetchRequestBase
{
    public const string GetCargoInfoByPosHandler = "GetCargoInfoByPos";

    private const string CargoStatusKey = "cargoStatus";
    private const string TwinPosStatusKey = "twinPosStatus";

    [SerializeField] private WarehouseManager m_warehouseManager;

    [SerializeField, Label("坐标是否 1 基（接口减 1）")]
    private bool m_oneBasedIndex = true;

    [SerializeField, Label("物流中心过滤（0=全部）")]
    private long m_warehouseIdFilter;

    [SerializeField, Label("每帧解析条数"), MinValue(32)]
    private int m_parseChunkSize = 256;

    [SerializeField, Label("每帧应用条数"), MinValue(32)]
    private int m_applyChunkSize = 512;

    private bool _warehouseReady;
    private Dictionary<Int4, TwinPosStatusItem> _itemByLocation = new();

    protected override string LogTag => "TwinPosStatus";

    protected override bool IsExtraReady => _warehouseReady;

    protected override string BuildUrl() => WmsUrls.GetTwinPosStatus(m_serverKey);

    protected override void Awake()
    {
        base.Awake();
        IOCC.AddHandler<Int4, TwinPosStatusItem>(GetCargoInfoByPosHandler, GetCargoInfoByPos);
        StartCoroutine(WaitWarehouseReady());
    }

    protected override void OnDestroy()
    {
        IOCC.RemoveHandler<Int4, TwinPosStatusItem>(GetCargoInfoByPosHandler, GetCargoInfoByPos);
        base.OnDestroy();
    }

    private void Reset()
    {
        m_fetchOnReady = true;
    }

    protected override UniTask OnDataFetchedAsync(string rawJson, CancellationToken cancellationToken)
    {
        return ProcessAsync(rawJson, cancellationToken);
    }

    private IEnumerator WaitWarehouseReady()
    {
        if (m_warehouseManager == null)
        {
            Debug.LogError($"[{LogTag}] 未绑定 WarehouseManager。", this);
            yield break;
        }

        while (!m_warehouseManager.Inited)
        {
            yield return null;
        }

        _warehouseReady = true;
    }

    private async UniTask ProcessAsync(string json, CancellationToken cancellationToken)
    {
        List<TwinPosStatusItem> items = await ParseResponseAsync(json, cancellationToken);
        if (items == null)
        {
            return;
        }

        await ApplyToWarehouseAsync(items, cancellationToken);
    }

    /// <summary>
    /// 流式读取 JSON，按块 yield，避免一次性 Deserialize 卡死主线程。
    /// </summary>
    private async UniTask<List<TwinPosStatusItem>> ParseResponseAsync(
        string json,
        CancellationToken cancellationToken)
    {
        int chunk = Mathf.Max(32, m_parseChunkSize);
        var items = new List<TwinPosStatusItem>(4096);
        bool? success = null;
        int? code = null;
        string message = null;
        bool hasData = false;

        try
        {
            using (var stringReader = new StringReader(json))
            using (var reader = new JsonTextReader(stringReader))
            {
                // 同步读 token，按块 Yield：全程主线程 PlayerLoop，WebGL 可用。
                if (!reader.Read() || reader.TokenType != JsonToken.StartObject)
                {
                    Debug.LogError($"[{LogTag}] 响应不是 JSON 对象。", this);
                    return null;
                }

                while (reader.Read())
                {
                    if (reader.TokenType == JsonToken.EndObject)
                    {
                        break;
                    }

                    if (reader.TokenType != JsonToken.PropertyName)
                    {
                        continue;
                    }

                    string prop = (string)reader.Value;
                    switch (prop)
                    {
                        case "success":
                            success = reader.ReadAsBoolean();
                            break;
                        case "code":
                            code = reader.ReadAsInt32();
                            break;
                        case "message":
                            message = reader.ReadAsString();
                            break;
                        case "data":
                            hasData = true;
                            if (!reader.Read() || reader.TokenType != JsonToken.StartArray)
                            {
                                Debug.LogError($"[{LogTag}] data 不是数组。", this);
                                return null;
                            }

                            int sinceYield = 0;
                            while (reader.Read())
                            {
                                if (reader.TokenType == JsonToken.EndArray)
                                {
                                    break;
                                }

                                if (reader.TokenType != JsonToken.StartObject)
                                {
                                    continue;
                                }

                                TwinPosStatusItem item = ReadPosStatusItem(reader);
                                if (item != null &&
                                    (m_warehouseIdFilter == 0 ||
                                     item.warehouse_id == m_warehouseIdFilter))
                                {
                                    items.Add(item);
                                }

                                if (++sinceYield >= chunk)
                                {
                                    sinceYield = 0;
                                    await UniTask.Yield(cancellationToken);
                                }
                            }

                            break;
                        default:
                            reader.Skip();
                            break;
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[{LogTag}] 响应解析异常: {e.Message}", this);
            return null;
        }

        if (!hasData)
        {
            Debug.LogError($"[{LogTag}] 响应解析失败（无 data）。", this);
            return null;
        }

        if (success == false && code != 200)
        {
            Debug.LogError(
                $"[{LogTag}] 接口失败 code={code} message={message}",
                this);
            return null;
        }

        if (m_log)
        {
            Debug.Log($"[{LogTag}] 解析完成 {items.Count} 条。", this);
        }

        return items;
    }

    /// <summary>
    /// 只读业务需要的字段，跳过其余属性，降低分配与解析开销。
    /// reader 进入时已位于 StartObject。
    /// </summary>
    private static TwinPosStatusItem ReadPosStatusItem(JsonTextReader reader)
    {
        var item = new TwinPosStatusItem();

        while (reader.Read())
        {
            if (reader.TokenType == JsonToken.EndObject)
            {
                break;
            }

            if (reader.TokenType != JsonToken.PropertyName)
            {
                continue;
            }

            string prop = (string)reader.Value;
            switch (prop)
            {
                case "warehouse_id":
                    item.warehouse_id = ReadInt64(reader);
                    break;
                case "pos_id":
                    item.pos_id = ReadInt64(reader);
                    break;
                case "pos_no":
                    item.pos_no = reader.ReadAsString();
                    break;
                case "pos_name":
                    item.pos_name = reader.ReadAsString();
                    break;
                case "row_no":
                    item.row_no = reader.ReadAsString();
                    break;
                case "column_no":
                    item.column_no = reader.ReadAsString();
                    break;
                case "layer_no":
                    item.layer_no = reader.ReadAsString();
                    break;
                case "depth_no":
                    item.depth_no = reader.ReadAsString();
                    break;
                case "has_goods":
                    item.has_goods = reader.ReadAsInt32() ?? 0;
                    break;
                case "goods_qty":
                    item.goods_qty = ReadDecimal(reader);
                    break;
                case "pos_type_id":
                    item.pos_type_id = reader.ReadAsInt32() ?? 0;
                    break;
                case "pos_type_name":
                    item.pos_type_name = reader.ReadAsString();
                    break;
                case "use_status":
                    item.use_status = reader.ReadAsString();
                    break;
                case "data_date":
                    item.data_date = reader.ReadAsString();
                    break;
                case "refresh_time":
                    item.refresh_time = reader.ReadAsString();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        return item;
    }

    private static long ReadInt64(JsonTextReader reader)
    {
        if (!reader.Read() || reader.Value == null)
        {
            return 0;
        }

        switch (reader.TokenType)
        {
            case JsonToken.Integer:
                return Convert.ToInt64(reader.Value, CultureInfo.InvariantCulture);
            case JsonToken.Float:
                return Convert.ToInt64(Convert.ToDouble(reader.Value, CultureInfo.InvariantCulture));
            case JsonToken.String:
                return long.TryParse((string)reader.Value, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out long parsed)
                    ? parsed
                    : 0;
            default:
                return 0;
        }
    }

    private static decimal ReadDecimal(JsonTextReader reader)
    {
        if (!reader.Read() || reader.Value == null)
        {
            return 0m;
        }

        switch (reader.TokenType)
        {
            case JsonToken.Float:
            case JsonToken.Integer:
                return Convert.ToDecimal(reader.Value, CultureInfo.InvariantCulture);
            case JsonToken.String:
                return decimal.TryParse((string)reader.Value, NumberStyles.Number,
                    CultureInfo.InvariantCulture, out decimal parsed)
                    ? parsed
                    : 0m;
            default:
                return 0m;
        }
    }

    private async UniTask ApplyToWarehouseAsync(
        List<TwinPosStatusItem> items,
        CancellationToken cancellationToken)
    {
        int chunk = Mathf.Max(32, m_applyChunkSize);
        var locations = new List<Int4>(items.Count);
        var shows = new List<bool>(items.Count);
        var visibleLocations = new List<Int4>(Mathf.Min(items.Count, 1024));
        var applied = new List<TwinPosStatusItem>(items.Count);
        var nextByLocation = new Dictionary<Int4, TwinPosStatusItem>(items.Count);

        int sinceYield = 0;
        for (int i = 0; i < items.Count; i++)
        {
            TwinPosStatusItem item = items[i];
            if (item == null)
            {
                continue;
            }

            if (!TryBuildLocation(item, out Int4 location))
            {
                if (m_log)
                {
                    Debug.LogWarning(
                        $"[{LogTag}] 坐标解析失败 pos_no={item.pos_no} " +
                        $"row={item.row_no} col={item.column_no} layer={item.layer_no} depth={item.depth_no}",
                        this);
                }

                continue;
            }

            bool hasGoods = item.HasGoods;
            locations.Add(location);
            shows.Add(hasGoods);
            applied.Add(item);
            nextByLocation[location] = item;
            if (hasGoods)
            {
                visibleLocations.Add(location);
            }

            if (++sinceYield >= chunk)
            {
                sinceYield = 0;
                await UniTask.Yield(cancellationToken);
            }
        }

        if (locations.Count == 0)
        {
            if (m_log)
            {
                Debug.LogWarning($"[{LogTag}] 无有效货位数据可应用。", this);
            }

            return;
        }

        // Clear 后只需打开有货货位；空位已由 ClearAllCargo 处理。
        m_warehouseManager.ClearAllCargo(false);
        await ApplyVisibleCargoAsync(visibleLocations, chunk, cancellationToken);

        Int4[] locArray = locations.ToArray();
        bool[] showArray = shows.ToArray();
        _itemByLocation = nextByLocation;
        IOCC.Set(CargoStatusKey, (locArray, showArray));
        IOCC.Set(TwinPosStatusKey, applied);

        if (m_log)
        {
            Debug.Log(
                $"[{LogTag}] 已更新 {locations.Count} 个货位状态（有货 {visibleLocations.Count}）。",
                this);
        }
    }

    private async UniTask ApplyVisibleCargoAsync(
        List<Int4> visibleLocations,
        int chunk,
        CancellationToken cancellationToken)
    {
        if (visibleLocations.Count == 0)
        {
            m_warehouseManager.ClearAllCargo(true);
            return;
        }

        var locBuffer = new Int4[Mathf.Min(chunk, visibleLocations.Count)];
        var showBuffer = new bool[locBuffer.Length];
        for (int i = 0; i < showBuffer.Length; i++)
        {
            showBuffer[i] = true;
        }

        for (int offset = 0; offset < visibleLocations.Count; offset += chunk)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int count = Mathf.Min(chunk, visibleLocations.Count - offset);
            if (count != locBuffer.Length)
            {
                locBuffer = new Int4[count];
                showBuffer = new bool[count];
                for (int i = 0; i < count; i++)
                {
                    showBuffer[i] = true;
                }
            }

            for (int i = 0; i < count; i++)
            {
                locBuffer[i] = visibleLocations[offset + i];
            }

            bool isLast = offset + count >= visibleLocations.Count;
            m_warehouseManager.SetCargoState(locBuffer, showBuffer, isLast);

            if (!isLast)
            {
                await UniTask.Yield(cancellationToken);
            }
        }
    }

    /// <summary>
    /// 按网格坐标取货位状态。坐标约定：X=层, Y=列, Z=排, W=深。未命中返回 null。
    /// </summary>
    public TwinPosStatusItem GetCargoInfoByPos(Int4 location)
    {
        return _itemByLocation != null && _itemByLocation.TryGetValue(location, out TwinPosStatusItem item)
            ? item
            : null;
    }

    private bool TryBuildLocation(TwinPosStatusItem item, out Int4 location)
    {
        location = default;
        if (!TryParseIndex(item.layer_no, out int layer) ||
            !TryParseIndex(item.column_no, out int column) ||
            !TryParseIndex(item.row_no, out int row) ||
            !TryParseIndex(item.depth_no, out int depth))
        {
            return false;
        }

        if (m_oneBasedIndex)
        {
            layer--;
            column--;
            row--;
            depth--;
        }

        if (layer < 0 || column < 0 || row < 0 || depth < 0)
        {
            return false;
        }

        // Int4: X=层, Y=列, Z=排, W=深
        location = new Int4(layer, column, row, depth);
        return true;
    }

    private static bool TryParseIndex(string value, out int index)
    {
        return int.TryParse(value, out index);
    }
}
}
