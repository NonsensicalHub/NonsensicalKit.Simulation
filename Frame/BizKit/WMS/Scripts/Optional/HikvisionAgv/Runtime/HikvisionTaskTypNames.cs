using System.Collections.Generic;

namespace HikvisionAgv
{
    /// <summary>
    /// taskTyp → 中文名。来源：Docs/任务类型说明.md（SimExportData Task_Template）。
    /// </summary>
    public static class HikvisionTaskTypNames
    {
        private static readonly Dictionary<string, string> Names = new()
        {
            ["A01"] = "默认的搬运货架",
            ["A02"] = "默认的搬运货架(软取消)",
            ["A03"] = "默认的搬回货架(软取消)",
            ["A06"] = "电梯主任务",
            ["A07"] = "电梯副任务",
            ["A11"] = "默认的回梯模板",
            ["A12"] = "默认的小车跨楼层",
            ["A13"] = "默认搬运巷道入库模板",
            ["A14"] = "默认搬运巷道出库模板",
            ["A51"] = "默认的搬运货架到工作台",
            ["CS03"] = "测试3",
            ["DZCCS"] = "库区到第一个电子秤",
            ["DZCCS2"] = "电子秤连续搬运测试A点到B点",
            ["DZCCS3"] = "第三个电子秤到库区",
            ["ELCK"] = "二楼出库",
            ["ELRK"] = "二楼入库",
            ["F11"] = "高位货架到工作台",
            ["F12"] = "工作台到高位货架",
            ["F13"] = "巷道到工作台",
            ["F14"] = "工作台到巷道",
            ["F20"] = "叉车电梯主任务",
            ["F201"] = "叉车电梯副任务",
            ["F61"] = "默认的回梯模板(叉车)",
            ["F62"] = "默认的小车跨楼层(叉车)",
            ["FFSY001"] = "风淋口主料桶入口到投料站入口",
            ["FFSY019"] = "WMS进料区到清洗机进料口",
            ["FFSY02"] = "投料站出口到清洗机入口",
            ["FFSY0211"] = "人工称量间到投料站入口1",
            ["FFSY09"] = "投料站出口到库区",
            ["FFSY11"] = "清洗机出口突起位到暂存位",
            ["LLER01"] = "二楼出库1",
            ["LLSR01"] = "三楼出库1",
            ["SLCK"] = "三楼出库",
            ["SLRK"] = "三楼入库",
            ["SY001"] = "清洗机到1号投料站",
            ["SY008"] = "库区到暂存位，库区到人工称量区",
            ["SY015"] = "库区到电子秤称量",
            ["SY016"] = "电子秤称量到库区",
            ["SY017"] = "电子秤A点搬到B点",
            ["SY018"] = "电子秤B点搬到C点",
            ["SY019"] = "清洗机进料口到清洗机清洗口",
            ["SY02"] = "输送线出口到清洗站",
            ["SY09"] = "输送线出口到暂存区",
            ["SY10"] = "输送线到清洗机",
            ["SY11"] = "清洗机到暂存位",
            ["SY121"] = "库区-临时暂存位到1号投料站",
            ["SY14"] = "库区到清洗站",
            ["T01"] = "潜伏车搬运货架(接驳)",
            ["T02"] = "叉车搬运容器(接驳)",
            ["T03"] = "接驳区补/减空货架",
            ["T11"] = "默认的接驳主任务(入库)",
            ["T12"] = "默认的接驳主任务(出库)",
            ["YLSS"] = "一楼输送到地面"
        };

        public static string DisplayName(string taskTyp)
        {
            if (string.IsNullOrEmpty(taskTyp))
                return "-";
            return Names.TryGetValue(taskTyp, out var name) ? name : taskTyp;
        }
    }

    /// <summary>
    /// queryTaskStatus.taskStatus：1-已创建，2-正在执行，5-取消/完成，9-已结束。
    /// </summary>
    public static class HikvisionTaskStatusCodes
    {
        public const string Created = "1";
        public const string Running = "2";
        public const string CancelOrDone = "5";
        public const string Finished = "9";

        public static string DisplayName(string taskStatus) => taskStatus switch
        {
            Created => "已创建",
            Running => "正在执行",
            CancelOrDone => "取消/完成",
            Finished => "已结束",
            _ => string.IsNullOrEmpty(taskStatus) ? "-" : $"状态{taskStatus}"
        };

        /// <summary>是否可作为面板“当前任务”（执行中优先，其次已创建）。</summary>
        public static bool IsActive(string taskStatus) =>
            taskStatus == Running || taskStatus == Created;

        public static int ActivePriority(string taskStatus) => taskStatus switch
        {
            Running => 2,
            Created => 1,
            _ => 0
        };
    }
}
