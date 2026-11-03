using System;
using NonsensicalKit.Core.Service.Config;
using UnityEngine;

/// <summary>
/// 全局数据源模式配置（与 URL/MQTT 解耦）：仅配置启动默认状态。
/// 运行时切换请用 <see cref="TwinDataSource.SetMode"/>，不会写回本资产。
/// </summary>
[CreateAssetMenu(fileName = "TwinDataSourceConfig", menuName = "ScriptableObjects/TwinDataSourceConfig")]
public class TwinDataSourceConfig : ConfigObject
{
    public TwinDataSourceData data;

    public override ConfigData GetData()
    {
        return data;
    }

    public override void SetData(ConfigData cd)
    {
        data = cd as TwinDataSourceData;
    }
}

/// <summary>
/// 全局数据源模式：离线走本地 Demo/模拟，在线走 Twin 中台等真实接口。
/// </summary>
public enum TwinDataSourceMode
{
    [InspectorName("离线（Demo / Mock）")]
    Offline = 0,

    [InspectorName("在线（真实接口）")]
    Online = 1,
}

[Serializable]
public class TwinDataSourceData : ConfigData
{
    [Tooltip("启动时的默认数据源：离线=本地 Demo/Mock，在线=真实接口。运行时用 TwinDataSource.SetMode 切换，不写回配置。")]
    public TwinDataSourceMode Mode = TwinDataSourceMode.Offline;
}
