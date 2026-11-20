using NonsensicalKit.Core;
using NonsensicalKit.Core.DagLogicNode;
using TMPro;
using UnityEngine;

namespace Frame.BizKit.WMS
{
public class SwitchFloor : NonsensicalMono
{
    [SerializeField] private int m_defaultFloor = 2;
    [SerializeField] private TMP_Dropdown m_dropdown;
    private int _indexTemp;

    private void Awake()
    {
        if (m_dropdown == null) m_dropdown = GetComponent<TMP_Dropdown>();
        Subscribe("WillBeUnloadScene", UnloadSceneCompleted);
        Subscribe(DagLogicNodeEnum.NodeEnter, "数字库区", OnEnter);
    }

    private void OnEnter()
    {
        SwitchToFloor(_indexTemp);
    }

    private void UnloadSceneCompleted()
    {
        _indexTemp = m_defaultFloor;
        m_dropdown.SetValueWithoutNotify(m_defaultFloor);
    }

    public void SwitchToFloor(int floor)
    {
        FloorController.SwitchToFloor(floor);

        _indexTemp = floor;
    }
}
}
