using System;
using System.Collections.Generic;
using System.Text;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Config
{
    /// <summary>
    /// 流程段的库号限定。空或 null 表示不限制，入库和出库可以使用全部仓库。
    /// </summary>
    public static class SimWarehouseRestriction
    {
        public static bool IsRestricted(int[] warehouseIds) =>
            warehouseIds != null && warehouseIds.Length > 0;

        public static bool Allows(int[] warehouseIds, int warehouseId)
        {
            if (!IsRestricted(warehouseIds))
            {
                return true;
            }

            for (var i = 0; i < warehouseIds.Length; i++)
            {
                if (warehouseIds[i] == warehouseId)
                {
                    return true;
                }
            }

            return false;
        }

        public static int[] Copy(int[] warehouseIds) => Normalize(warehouseIds);

        public static int[] Normalize(int[] warehouseIds)
        {
            if (warehouseIds == null || warehouseIds.Length == 0)
            {
                return null;
            }

            var unique = new List<int>(warehouseIds.Length);
            for (var i = 0; i < warehouseIds.Length; i++)
            {
                var id = warehouseIds[i];
                if (id < 0 || Contains(unique, id))
                {
                    continue;
                }

                unique.Add(id);
            }

            if (unique.Count == 0)
            {
                return null;
            }

            unique.Sort();
            return unique.ToArray();
        }

        public static bool Same(int[] left, int[] right)
        {
            var normalizedLeft = Normalize(left);
            var normalizedRight = Normalize(right);
            if (normalizedLeft == null || normalizedRight == null)
            {
                return normalizedLeft == null && normalizedRight == null;
            }

            if (normalizedLeft.Length != normalizedRight.Length)
            {
                return false;
            }

            for (var i = 0; i < normalizedLeft.Length; i++)
            {
                if (normalizedLeft[i] != normalizedRight[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>预览用，例如「0、1」。</summary>
        public static string Format(int[] warehouseIds)
        {
            var normalized = Normalize(warehouseIds);
            if (normalized == null)
            {
                return null;
            }

            var text = new StringBuilder();
            for (var i = 0; i < normalized.Length; i++)
            {
                if (i > 0)
                {
                    text.Append('、');
                }

                text.Append(normalized[i]);
            }

            return text.ToString();
        }

        /// <summary>编辑框用，例如「0, 1」。</summary>
        public static string FormatList(int[] warehouseIds)
        {
            var normalized = Normalize(warehouseIds);
            if (normalized == null)
            {
                return string.Empty;
            }

            var text = new StringBuilder();
            for (var i = 0; i < normalized.Length; i++)
            {
                if (i > 0)
                {
                    text.Append(", ");
                }

                text.Append(normalized[i]);
            }

            return text.ToString();
        }

        public static int[] Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var parts = text.Split(
                new[] { ',', '，', ';', '；', ' ', '、', '\t' },
                StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return null;
            }

            var ids = new int[parts.Length];
            var count = 0;
            for (var i = 0; i < parts.Length; i++)
            {
                if (int.TryParse(parts[i].Trim(), out var id) && id >= 0)
                {
                    ids[count++] = id;
                }
            }

            if (count == 0)
            {
                return null;
            }

            if (count != ids.Length)
            {
                Array.Resize(ref ids, count);
            }

            return Normalize(ids);
        }

        private static bool Contains(List<int> values, int id)
        {
            for (var i = 0; i < values.Count; i++)
            {
                if (values[i] == id)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
