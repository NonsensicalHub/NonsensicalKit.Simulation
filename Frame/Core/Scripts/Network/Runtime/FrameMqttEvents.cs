using System;

/// <summary>
/// MQTT 相关 IOCC 事件名（放在 Core，避免 RecordPlayback 硬依赖 Frame.Core.Mqtt）。
/// </summary>

namespace Frame.Core.Network
{
public static class FrameMqttEvents
{
    public const string UseOfflineData = "MQTTUseOfflineData";
}
}
