using System.Collections.Generic;
using Frame.BizKit.WMS.Equipment;

namespace EquipmentStatus
{
    public static class StackerEquipmentStatusBuilder
    {
        public static (SimpleInfo, Dictionary<string, string>) Build(
            string deviceCode, string deviceName, EquipmentStatusCache cache)
        {
            bool fault = EquipmentStatusFormatUtil.IsTruthy(cache.Get(deviceCode, "Fault"));
            bool runStop = EquipmentStatusFormatUtil.IsTruthy(cache.Get(deviceCode, "RunStop"));
            bool autoManual = EquipmentStatusFormatUtil.IsTruthy(cache.Get(deviceCode, "AutoManual"));
            string taskNo = cache.Get(deviceCode, "TaskNo");
            string travelCol = cache.Get(deviceCode, "TravelCol");
            string liftLayer = cache.Get(deviceCode, "LiftLayer");

            // RunStop：空闲/非空闲；Fault：1故障0正常；AutoManual：1自动0手动
            string idleText = runStop ? "非空闲" : "空闲";
            string faultText = fault ? "故障" : "正常";
            string autoText = autoManual ? "自动" : "手动";

            int level = fault ? 2 : (runStop ? 1 : 0);
            string runText = fault ? "故障" : idleText;

            var simple = EquipmentStatusFormatUtil.MakeSimple(
                "任务信息",
                string.IsNullOrEmpty(taskNo) ? "-" : taskNo,
                "运行状态",
                runText,
                level);

            var dic = new Dictionary<string, string>
            {
                ["DeviceCode"] = deviceCode,
                ["名称"] = EquipmentStatusFormatUtil.ResolveName(deviceCode, deviceName, cache),
                ["运行状态"] = idleText,
                ["自动手动"] = autoText,
                ["故障"] = faultText,
                ["载货"] = EquipmentStatusFormatUtil.FormatBool(
                    cache.Get(deviceCode, "LoadStatus"), "有货", "无货"),
                ["当前位置"] =
                    $"列{EquipmentStatusFormatUtil.OrDash(travelCol)} / 层{EquipmentStatusFormatUtil.OrDash(liftLayer)}",
                ["当前执行任务"] = EquipmentStatusFormatUtil.OrDash(taskNo)
            };

            EquipmentStatusFormatUtil.TryAddLabeled(
                dic, deviceCode, cache, EquipmentKind.Stacker,
                "StartLayer", "StartCol", "StartRow", "TargetLayer", "TargetCol", "TargetRow",
                "ForkSinglePos", "ForkDoublePos");

            return (simple, dic);
        }
    }
}
