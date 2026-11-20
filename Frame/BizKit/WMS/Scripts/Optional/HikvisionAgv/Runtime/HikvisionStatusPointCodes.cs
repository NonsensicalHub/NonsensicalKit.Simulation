using System;

namespace HikvisionAgv
{
    /// <summary>与 PartConfig / PointDataType 序一致：Bool=0, Int=1, Float=2, String=3。</summary>
    public enum HikPointValueType
    {
        Bool = 0,
        Int = 1,
        Float = 2,
        String = 3
    }

    /// <summary>
    /// PartConfig.pointConfigs 的唯一有序定义。Index 即 pointConfigs 下标。
    /// </summary>
    public readonly struct HikPointDef
    {
        public readonly int Index;
        public readonly string Code;
        public readonly string Name;
        public readonly HikPointValueType ValueType;

        public HikPointDef(int index, string code, string name, HikPointValueType valueType)
        {
            Index = index;
            Code = code;
            Name = name;
            ValueType = valueType;
        }
    }

    /// <summary>
    /// 海康 AGV DataHub PointCode，须与 PartConfig.pointConfigs 顺序一致。
    /// </summary>
    public static class HikvisionStatusPointCodes
    {
        public static readonly HikPointDef PosX =
            new(0, "PosX", "平面X", HikPointValueType.Float);

        public static readonly HikPointDef PosY =
            new(1, "PosY", "平面Y", HikPointValueType.Float);

        public static readonly HikPointDef RobotDir =
            new(2, "RobotDir", "朝向", HikPointValueType.Float);

        public static readonly HikPointDef Speed =
            new(3, "Speed", "速度", HikPointValueType.Float);

        public static readonly HikPointDef Status =
            new(4, "Status", "状态", HikPointValueType.String);

        public static readonly HikPointDef Stop =
            new(5, "Stop", "暂停", HikPointValueType.Int);

        public static readonly HikPointDef Battery =
            new(6, "Battery", "电量", HikPointValueType.Float);

        public static readonly HikPointDef ExclType =
            new(7, "ExclType", "排除", HikPointValueType.Int);

        public static readonly HikPointDef PodCode =
            new(8, "PodCode", "货架号", HikPointValueType.String);

        public static readonly HikPointDef PodDir =
            new(9, "PodDir", "货架朝向", HikPointValueType.Float);

        public static readonly HikPointDef MapCode =
            new(10, "MapCode", "地图", HikPointValueType.String);

        public static readonly HikPointDef TaskCode =
            new(11, "TaskCode", "任务号", HikPointValueType.String);

        public static readonly HikPointDef TaskStatus =
            new(12, "TaskStatus", "任务状态", HikPointValueType.String);

        public static readonly HikPointDef TaskTyp =
            new(13, "TaskTyp", "任务类型", HikPointValueType.String);

        /// <summary>完整 PartConfig 顺序（Index 0～13）。</summary>
        public static readonly HikPointDef[] All =
        {
            PosX, PosY, RobotDir, Speed, Status, Stop, Battery, ExclType,
            PodCode, PodDir, MapCode, TaskCode, TaskStatus, TaskTyp
        };

        /// <summary>状态接口写入的点位（不含任务）。</summary>
        public static readonly HikPointDef[] StatusPoints =
        {
            PosX, PosY, RobotDir, Speed, Status, Stop, Battery, ExclType,
            PodCode, PodDir, MapCode
        };

        /// <summary>任务接口写入的点位。</summary>
        public static readonly HikPointDef[] TaskPoints =
        {
            TaskCode, TaskStatus, TaskTyp
        };

        public static string DisplayName(string pointCode)
        {
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Code == pointCode)
                    return All[i].Name;
            }

            return pointCode;
        }

        public static bool TryGetDef(string pointCode, out HikPointDef def)
        {
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Code == pointCode)
                {
                    def = All[i];
                    return true;
                }
            }

            def = default;
            return false;
        }
    }
}
