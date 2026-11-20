using System.Collections;
using NaughtyAttributes;
using NonsensicalKit.Core;
using NonsensicalKit.DigitalTwin.Warehouse;
using UnityEngine;

namespace Frame.Sample.WmsDemo
{
public class DemoWarehouseRandom : MonoBehaviour
{
    [SerializeField] private WarehouseManager m_warehouseManager;

    private void Awake()
    {
        StartCoroutine(SetTestState());
    }


    private IEnumerator SetTestState()
    {
        while (m_warehouseManager.Inited == false)
        {
            yield return null;
        }

        var i4 = new Int4[10 * 62 * 58];
        var b4 = new bool[10 * 62 * 58];
        int index = 0;
        for (int row = 0; row < 58; row++)
        {
            for (int column = 0; column < 62; column++)
            {
                for (int level = 0; level < 10; level++)
                {
                    i4[index] = new Int4(level, column, row, 0);

                    b4[index] = Random.Range(0, 2) == 1;
                    index++;
                }
            }
        }

        m_warehouseManager.SetCargoState(i4, b4, true);
        IOCC.Set("cargoStatus", (i4, b4));
    }
}
}
