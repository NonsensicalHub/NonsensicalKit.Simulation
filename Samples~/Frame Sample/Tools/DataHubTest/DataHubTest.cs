using NaughtyAttributes;
using NonsensicalKit.Core.Service;
using NonsensicalKit.DigitalTwin.Motion;
using UnityEngine;

public class DataHubTest : MonoBehaviour
{
    private int index = 0;
    
    [Button]
    public void SC2GoGo()
    {
        ServiceCore.Get<DataHub>().ReceivePoint(new PointData("测试","TPSC2J1","",0,index));
        ServiceCore.Get<DataHub>().ReceivePoint(new PointData("测试","TPSC2J2","",0,0));
        index++;
    }
    
    [Button]
    public void BeltRun()
    {
        ServiceCore.Get<DataHub>().ReceivePoint(new PointData("传送带测试","testBeltPoint","",0,1f));
    }
    
    [Button]
    public void BeltStop()
    {
        ServiceCore.Get<DataHub>().ReceivePoint(new PointData("传送带测试","testBeltPoint","",0,0f));
    }
}
