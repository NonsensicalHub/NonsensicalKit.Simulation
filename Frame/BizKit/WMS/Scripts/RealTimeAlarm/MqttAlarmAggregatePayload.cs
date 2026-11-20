using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Frame.RealTimeAlarm
{
    /// <summary>
    /// 全场报警汇总 ALARM/ALL。根上没有 DeviceCode，现有设备报文解析器不会收它。
    /// </summary>
    [Serializable]
    public class MqttAlarmAggregatePayload
    {
        [JsonProperty("TopicType")]
        public string TopicType;

        [JsonProperty("AggScope")]
        public string AggScope;

        [JsonProperty("PublishTime")]
        public string PublishTime;

        [JsonProperty("DeviceCount")]
        public int DeviceCount;

        [JsonProperty("Devices")]
        public List<MqttAlarmDevicePayload> Devices = new();
    }

    [Serializable]
    public class MqttAlarmDevicePayload
    {
        [JsonProperty("DeviceCode")]
        public string DeviceCode;

        [JsonProperty("DeviceName")]
        public string DeviceName;

        [JsonProperty("TopicType")]
        public string TopicType;

        [JsonProperty("ReadTime")]
        public string ReadTime;

        [JsonProperty("Points")]
        public List<MqttAlarmPointPayload> Points = new();
    }

    [Serializable]
    public class MqttAlarmPointPayload
    {
        [JsonProperty("PointCode")]
        public string PointCode;

        [JsonProperty("PointName")]
        public string PointName;

        [JsonProperty("Value")]
        public string Value;

        [JsonProperty("DataType")]
        public string DataType;
    }
}
