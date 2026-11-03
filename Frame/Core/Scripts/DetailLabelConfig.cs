using NonsensicalKit.Core.Service.Config;
using UnityEngine;

[CreateAssetMenu(fileName = "DetailLabelConfig", menuName = "Config/DetailLabelConfig")]
public class DetailLabelConfig : ConfigObject
{
    [SerializeField] private DetailLabelInfo _data;

    public override ConfigData GetData()
    {
        return _data;
    }

    public override void SetData(ConfigData cd)
    {
        if (cd is DetailLabelInfo info)
        {
            _data = info;
        }
    }
}

public class DetailLabelInfo : ConfigData
{
    public string Name;
    public string Unit;
    public string[] ConvertKey;
    public string[] ConvertValue;
}
