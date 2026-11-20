using System.Collections.Generic;
using System.Globalization;
using Frame.BizKit.WMS.Equipment;

namespace EquipmentStatus
{
    public static class LifterEquipmentStatusBuilder
    {
        public static (SimpleInfo, Dictionary<string, string>) Build(
            string deviceCode, string deviceName, EquipmentStatusCache cache)
        {
            bool fault = EquipmentStatusFormatUtil.IsTruthy(cache.Get(deviceCode, "Fault"));
            bool run = EquipmentStatusFormatUtil.IsTruthy(cache.Get(deviceCode, "RunStatus"));
            string floor = cache.Get(deviceCode, "CurrentFloor");
            string liftStatus = cache.Get(deviceCode, "LiftStatus");
            string process = cache.Get(deviceCode, "ProcessStatus");

            string processText = MapProcessStatus(process);
            string liftText = MapLiftStatus(liftStatus);
            string floorText = MapCurrentFloor(floor);

            int level = fault ? 2 : (run ? 1 : 0);
            string runText = fault ? "故障" : (run ? "正常" : "静止");

            var simple = EquipmentStatusFormatUtil.MakeSimple("工作状态", processText, "运行状态", runText, level);

            var dic = new Dictionary<string, string>
            {
                ["DeviceCode"] = deviceCode,
                ["名称"] = EquipmentStatusFormatUtil.ResolveName(deviceCode, deviceName, cache),
                ["运行状态"] = runText,
                ["当前楼层"] = floorText,
                ["升降状态"] = liftText,
                ["流程状态"] = processText,
                ["平台占用"] = EquipmentStatusFormatUtil.FormatBool(
                    cache.Get(deviceCode, "PlatformOccupied"), "占用", "空闲")
            };

          //  EquipmentStatusFormatUtil.TryAddLabeled(dic, deviceCode, cache, EquipmentKind.Lifter, "Mode", "CabinPlatform", "ActualSpeed", "InverterFault", "PlatformFault");

            return (simple, dic);
        }

        /// <summary>升降状态：1上升，2下降，3静止。</summary>
        public static string MapLiftStatus(string liftStatus) => liftStatus switch
        {
            "1" => "上升",
            "2" => "下降",
            "3" => "静止",
            _ => string.IsNullOrEmpty(liftStatus) ? "-" : $"状态{liftStatus}"
        };

        /// <summary>当前楼层标定：1层=0，2层=50，3层=100；中间值为层间过渡。</summary>
        public static string MapCurrentFloor(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return "-";
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                return raw;

            return v switch
            {
                0 => "1层",
                50 => "2层",
                100 => "3层",
                _ when v > 0 && v < 50 => $"1→2层({v})",
                _ when v > 50 && v < 100 => $"2→3层({v})",
                _ => $"标定{v}"
            };
        }

        /// <summary>流程：1校位接货…5任务结束。</summary>
        public static string MapProcessStatus(string process) => process switch
        {
            "1" => "校位接货楼层",
            "2" => "到达校位楼层",
            "3" => "到轿厢内",
            "4" => "到目标楼层",
            "5" => "任务结束",
            "0" => "空闲",
            _ => string.IsNullOrEmpty(process) ? "-" : $"流程{process}"
        };
    }
}
