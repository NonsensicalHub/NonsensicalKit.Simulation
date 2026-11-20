    using System.Collections.Generic;
using NaughtyAttributes;
using NonsensicalKit.Core;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// 多色盘点高亮 Demo：从 IOCC 取全库货物，显式建多批，每批一种颜色。
/// WarehouseInventoryReview 会按批次数动态 Instantiate 盘点库层。
/// </summary>
public class DemoWarehouseInventoryHighlight : MonoBehaviour
{
    #region 字段

    [SerializeField] private WarehouseInventoryReview m_review;

    [SerializeField, Tooltip("每批高亮颜色；批次数 = 数组长度")]
    private Color[] m_batchColors =
    {
        new Color(0.15f, 0.75f, 0.95f, 1f),
        new Color(0.95f, 0.55f, 0.15f, 1f),
        new Color(0.35f, 0.85f, 0.35f, 1f),
        new Color(0.9f, 0.3f, 0.55f, 1f),
    };

    [SerializeField, Range(0.05f, 1f), Tooltip("有货格位被选入高亮的概率（再均分到各批）")]
    private float m_pickChance = 0.5f;

    #endregion

    #region 编辑器按钮

    [Button("多色随机高亮")]
    public void SetMultiColorHighlight()
    {
        if (m_review == null)
        {
            Debug.LogWarning($"{nameof(DemoWarehouseInventoryHighlight)} 未配置 {nameof(WarehouseInventoryReview)}。");
            return;
        }

        if (!m_review.TryGetCargoStatus(out Int4[] locations, out bool[] cargoStates))
        {
            Debug.LogWarning($"{nameof(DemoWarehouseInventoryHighlight)} IOCC 中尚无货位状态。");
            return;
        }

        if (m_batchColors == null || m_batchColors.Length == 0)
        {
            Debug.LogWarning($"{nameof(DemoWarehouseInventoryHighlight)} 未配置批次颜色。");
            return;
        }

        WarehouseInventoryHighlightBatch[] batches = BuildBatches(locations, cargoStates, m_batchColors, m_pickChance);
        int nonEmpty = 0;
        for (int i = 0; i < batches.Length; i++)
        {
            if (batches[i] != null && batches[i].IsValid)
            {
                nonEmpty++;
            }
        }

        if (nonEmpty == 0)
        {
            Debug.Log($"{nameof(DemoWarehouseInventoryHighlight)} 本轮未抽中任何格位。");
            return;
        }

        Debug.Log($"{nameof(DemoWarehouseInventoryHighlight)} 生成 {nonEmpty}/{batches.Length} 批，交由 Review 动态建层。");
        m_review.ReplaceHighlight(batches);
    }

    [Button("恢复正常")]
    public void RestoreNormal()
    {
        m_review?.RestoreNormal();
    }

    [Button("退出盘点")]
    public void ExitReview()
    {
        m_review?.ExitReview();
    }

    #endregion

    #region 私有辅助

    /// <summary>
    /// 有货格随机抽取后，轮流分到各批（互斥），保证批次数 = 颜色数。
    /// </summary>
    private WarehouseInventoryHighlightBatch[] BuildBatches(
        Int4[] locations,
        bool[] cargoStates,
        Color[] batchColors,
        float pickChance)
    {
        Int4[][] batchLocations = BuildBatchLocations(locations, cargoStates, batchColors.Length, pickChance);
        return WarehouseInventoryHighlightBatch.CreateAllHighlighted(batchLocations, batchColors);
    }

    /// <summary>
    /// 与 <see cref="BuildBatches"/> 相同的抽样与分批规则，只返回各批格位 Int4[]。
    /// </summary>
    private Int4[][] BuildBatchLocations(
        Int4[] locations,
        bool[] cargoStates,
        int batchCount,
        float pickChance)
    {
        var perBatchLocs = new List<Int4>[batchCount];
        for (int b = 0; b < batchCount; b++)
        {
            perBatchLocs[b] = new List<Int4>();
        }

        int cursor = 0;
        for (int i = 0; i < cargoStates.Length; i++)
        {
            if (!cargoStates[i] || Random.value > pickChance)
            {
                continue;
            }

            int batchIndex = cursor % batchCount;
            perBatchLocs[batchIndex].Add(locations[i]);
            cursor++;
        }

        var result = new Int4[batchCount][];
        for (int b = 0; b < batchCount; b++)
        {
            result[b] = perBatchLocs[b].ToArray();
        }

        return result;
    }

    #endregion
}
