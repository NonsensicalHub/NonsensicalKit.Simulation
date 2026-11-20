using NonsensicalKit.Core;
using UnityEngine;

namespace Frame.BizKit.WMS
{
public class FloorController : MonoBehaviour
{
    public static void SwitchToFloor(int floor)
    {
        switch (floor)
        {
            case 0:
                IOCC.Publish("显示一楼");
                IOCC.Publish("隐藏二楼");
                IOCC.Publish("隐藏三楼");
                break;
            case 1:
                IOCC.Publish("显示二楼");
                IOCC.Publish("隐藏三楼");
                break;
            case 2:
                IOCC.Publish("显示二楼");
                IOCC.Publish("显示三楼");
                break;
        }
    }
}
}
