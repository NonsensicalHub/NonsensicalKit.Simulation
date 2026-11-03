using System;
using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>
/// 设备 MQTT 设备报文（与 Excel「设备MQTT对照」列定义一致）。
/// </summary>

namespace Frame.Core.Mqtt
{
[Serializable]
public class MqttDevicePayload
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
    public List<MqttPointPayload> Points = new();
}

[Serializable]
public class MqttPointPayload
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
