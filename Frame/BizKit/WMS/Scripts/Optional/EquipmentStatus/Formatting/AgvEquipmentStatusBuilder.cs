using System;
using System.Collections.Generic;
using System.Globalization;
using Frame.BizKit.WMS.Equipment;
using HikvisionAgv;

namespace EquipmentStatus
{
    public static class AgvEquipmentStatusBuilder
    {
        public static (SimpleInfo, Dictionary<string, string>) Build(
            string deviceCode, string deviceName, EquipmentStatusCache cache)
        {
            string status = cache.Get(deviceCode, "Status");
            string stop = cache.Get(deviceCode, "Stop");
            string battery = cache.Get(deviceCode, "Battery");
            string taskCode = cache.Get(deviceCode, "TaskCode");
            string taskStatus = cache.Get(deviceCode, "TaskStatus");
            string taskTyp = cache.Get(deviceCode, "TaskTyp");
            string posX = cache.Get(deviceCode, "PosX");
            string posY = cache.Get(deviceCode, "PosY");
            string map = cache.Get(deviceCode, "MapCode");
            string pod = cache.Get(deviceCode, "PodCode");

            bool paused = EquipmentStatusFormatUtil.IsTruthy(stop);
            int level = HikvisionRobotStatusCodes.PanelLevel(status);
            if (paused)
                level = level < 1 ? 1 : level;

            string runText = HikvisionRobotStatusCodes.DisplayName(status);
            if (paused && status != "5")
            {
                runText = string.IsNullOrEmpty(status) || runText == "-"
                    ? "机器人暂停"
                    : $"{runText}(暂停)";
            }

            string taskStatusText = HikvisionTaskStatusCodes.DisplayName(taskStatus);
            string taskTypText = HikvisionTaskTypNames.DisplayName(taskTyp);
            string questText = string.IsNullOrEmpty(taskCode)
                ? "-"
                : $"{taskCode} [{taskStatusText}]";

            var simple = EquipmentStatusFormatUtil.MakeSimple(
                "任务信息", questText, "完成情况", runText, level);

            var dic = new Dictionary<string, string>
            {
                ["DeviceCode"] = deviceCode,
                ["名称"] = EquipmentStatusFormatUtil.ResolveName(deviceCode, deviceName, cache),
                ["运行状态"] = runText,
                ["电量"] = FormatBattery(battery),
                ["当前位置"] = FormatPosition(map, posX, posY),
                ["当前执行任务"] = string.IsNullOrEmpty(taskCode)
                    ? "无当前任务"
                    : $"{taskCode} ({taskStatusText})",
                ["任务类型"] = string.IsNullOrEmpty(taskTyp) ? "-" : $"{taskTypText} ({taskTyp})",
                ["货架号"] = string.IsNullOrEmpty(pod) || pod == "0" ? "-" : pod
            };

            return (simple, dic);
        }

        private static string FormatBattery(string battery)
        {
            if (string.IsNullOrEmpty(battery))
                return "-";
            if (float.TryParse(battery, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                return $"{v:0.#}%";
            return battery.EndsWith("%", StringComparison.Ordinal) ? battery : battery + "%";
        }

        private static string FormatPosition(string map, string x, string y)
        {
            string mapPart = string.IsNullOrEmpty(map) ? "" : $"[{map}] ";
            if (string.IsNullOrEmpty(x) && string.IsNullOrEmpty(y))
                return string.IsNullOrEmpty(map) ? "-" : map;
            return $"{mapPart}({EquipmentStatusFormatUtil.OrDash(x)}, {EquipmentStatusFormatUtil.OrDash(y)})";
        }
    }
}
