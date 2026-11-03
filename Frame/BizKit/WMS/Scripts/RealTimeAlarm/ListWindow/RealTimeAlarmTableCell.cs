using NonsensicalKit.Core;
using NonsensicalKit.UGUI.Table;
using UnityEngine;
using UnityEngine.EventSystems;

public class RealTimeAlarmTableCell : ScrollTableCell, IPointerClickHandler
{
    private bool _canClick = false;
    private int _rowIndex;

    public override void SetState(string text, int columnIndex, int rowIndex)
    {
        base.SetState(text, columnIndex, rowIndex);
        _rowIndex = rowIndex;
        if (text.Contains("未处理"))
        {
            m_txt_content.color = Color.red;
            _canClick = true;
        }
        else
        {
            m_txt_content.color = Color.black;
            _canClick = false;
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (_canClick)
        {
            IOCC.Publish("showRealTimeAlarm", _rowIndex);
        }
    }
}
