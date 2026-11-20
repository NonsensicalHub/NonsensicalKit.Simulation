using System.Collections.Generic;
using NonsensicalKit.Core;
using UnityEngine;

namespace Frame.Sample.XCharts
{
public class ChartSimulationDataGenerator : MonoBehaviour
{
    private void Start()
    {
        ChartHelper[] chartHelpers = GetComponentsInChildren<ChartHelper>();


        foreach (ChartHelper chartHelper in chartHelpers)
        {
            List<double> data = new List<double>()
            {
                Random.Range(0, 100),
                Random.Range(0, 100),
                Random.Range(0, 100)
            };
            chartHelper.UpdateYData(0, data);
        }

        List<List<string>> data2 = new List<List<string>>()
        {
            new List<string>() { "物料名称", "料号", "货位占用", "实时库存", "库存占比" },
            new List<string>() { "晶圆", "LH1254520145sdsdsdsdsd", "500", "1200", "100" },
            new List<string>() { "晶圆", "LH1254520145sdsdsdsdsd", "500", "1200", "100" },
            new List<string>() { "晶圆", "LH1254520145sdsdsdsdsd", "500", "1200", "100" },
            new List<string>() { "晶圆", "LH1254520145sdsdsdsdsd", "500", "1200", "100" },
            new List<string>() { "晶圆", "LH1254520145sdsdsdsdsd", "500", "1200", "100" },
            new List<string>() { "晶圆", "LH1254520145sdsdsdsdsd", "500", "1200", "100" },
            new List<string>() { "晶圆", "LH1254520145sdsdsdsdsd", "500", "1200", "100" },
            new List<string>() { "晶圆", "LH1254520145sdsdsdsdsd", "500", "1200", "100" },
            new List<string>() { "晶圆", "LH1254520145sdsdsdsdsd", "500", "1200", "100" },
            new List<string>() { "晶圆", "LH1254520145sdsdsdsdsd", "500", "1200", "100" },
        };
        IOCC.PublishWithID("showTable","关键库存信息", data2, true);
    }
}
}
