using System;
using System.Globalization;
using NonsensicalKit.UGUI;
using TMPro;
using UnityEngine;
using ZTools;

/// <summary>
/// 使用示例
/// </summary>
public class ZCalendarHandle : NonsensicalUI
{
    public ZCalendar zCalendar;

    private string _selectTime;
    [SerializeField] private TMP_Text startTime;
    [SerializeField] private TMP_Text endTime;

    private ZCalendarModel _zModel;

    public Func<string, string> SetTime;

    public string SelectTime
    {
        get => _selectTime;
        private set
        {
            _selectTime = value;
            SetTime?.Invoke(_selectTime);
        }
    }

    protected override void Awake()
    {
        zCalendar.onDayRefresh.AddListener(ZCalendar_UpdateDateEvent);
        zCalendar.onDayValueChanged.AddListener(ZCalendar_ChoiceDayEvent);
        zCalendar.onRangeTimeValueChanged.AddListener(ZCalendar_RangeTimeEvent);
        zCalendar.onComplete.AddListener(ZCalendar_CompleteEvent);

        _zModel = GetComponent<ZCalendarModel>();
        startTime.text = DateTime.Now.ToString(CultureInfo.InvariantCulture);
        endTime.text = DateTime.Now.ToString(CultureInfo.InvariantCulture);
        SetTime = v => v;
    }

    public string GetDatesWithTime()
    {
        return $"{startTime.text:yyyy-MM-dd HH:mm:ss}+{endTime.text:yyyy-MM-dd HH:mm:ss}";
    }

    public string GetDatesWithoutTime()
    {
        return $"{startTime.text:yyyy - MM - dd}+{endTime.text:yyyy - MM - dd}";
    }

    public string GetDate()
    {
        return $"{startTime.text:yyyy - MM - dd}";
    }

    public string GetDay()
    {
        return SelectTime;
    }

    #region 显隐控制

    protected override void OpenSelf()
    {
        base.OpenSelf();
        zCalendar.Show();
    }

    protected override void CloseSelf()
    {
        base.CloseSelf();
        zCalendar.Hide();
    }

    #endregion

    private void ZCalendar_CompleteEvent()
    {
        Debug.Log("ZCalendar加载结束");
        Debug.Log($"当前时间{zCalendar.CrtTime.Day}");
    }

    private void ZCalendar_RangeTimeEvent(DateTime arg1, DateTime arg2)
    {
        if (_zModel.timeChoice)
        {
            Debug.Log($"选择的时间区间：{arg1:yyyy-MM-dd HH:mm:ss}到{arg2:yyyy-MM-dd HH:mm:ss}");
            startTime.text = arg1.ToString("yyyy-MM-dd HH:mm:ss");
            endTime.text = arg2.ToString("yyyy-MM-dd HH:mm:ss");
        }
        else
        {
            Debug.Log($"选择的日期区间：{arg1:yyyy-MM-dd}到{arg2:yyyy-MM-dd}");
            startTime.text = arg1.ToString("yyyy-MM-dd");
            endTime.text = arg2.ToString("yyyy-MM-dd");
        }

        SelectTime = $"{startTime.text:yyyy - MM - dd HH: mm: ss}+{endTime.text:yyyy - MM - dd HH: mm: ss}";
    }

    private void ZCalendar_ChoiceDayEvent(DateTime obj)
    {
        if (_zModel.timeChoice)
        {
            zCalendar.isFirstSelect = true;
            Debug.Log($"选择的时间：{obj:yyyy-MM-dd HH:mm:ss}");
            startTime.text = obj.ToString("yyyy-MM-dd HH:mm:ss");
            endTime.text = string.Empty;
        }
        else
        {
            Debug.Log($"选择的日期：{obj:yyyy-MM-dd}");
            startTime.text = obj.ToString("yyyy-MM-dd");
        }

        SelectTime = startTime.text;
    }

    private void ZCalendar_UpdateDateEvent(DateTime obj)
    {
    }
}
