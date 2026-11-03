using System;
using System.Collections.Generic;

/// <summary>
/// 录制回放解析规则（由 JSON TextAsset 反序列化）。
/// 行结构与 IOCC 事件名固定，不在此配置：
/// - 行：time(23)|ticks(18)|key(5)|event(2)|[MQ:变长频道| ]正文
/// - MQ → MQTTMessage，SR → SignalRMessage
/// 仅配置各事件正文解析方式（因项目而异）。
/// </summary>
[Serializable]
public class RecordParseConfig
{
    public List<RecordEventRule> events = new();
}

[Serializable]
public class RecordEventRule
{
    /// <summary>录制行事件类型，如 MQ / SR / EV</summary>
    public string type;

    /// <summary>为 true 时跳过该事件</summary>
    public bool ignore;

    /// <summary>
    /// 正文（payload）解析模式：
    /// raw | split | lastSeparator | regex
    /// MQ 的频道字段已在行解析阶段剥离，此处只处理正文。
    /// </summary>
    public string payloadMode = "raw";

    /// <summary>split 模式最多切成几段（0 表示不限制）</summary>
    public int payloadMaxParts;

    /// <summary>regex 模式的正则，可用命名组或序号捕获</summary>
    public string payloadPattern;

    public bool trimParts = true;
}
