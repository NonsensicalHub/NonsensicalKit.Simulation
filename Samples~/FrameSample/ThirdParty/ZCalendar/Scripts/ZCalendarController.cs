/*
 * Created by JacobKay - 2022.08.24
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ZTools
{
    public class ZCalendarController
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public int Day { get; set; }

        /// <summary>
        /// 当前是否在区间选择状态
        /// </summary>
        private bool _isInRange;

        public bool IsInRange => _isInRange;

        private DateTime _now;
        private int _days;

        /// <summary>
        /// 当前选中的位置
        /// </summary>
        public Vector3 Pos;
        public bool IsInit;
        public ZCalendar ZCalendar;
        public ZCalendarModel ZCalendarModel;
        public DateTime NowTime = DateTime.Today;

        private int _lastMonthEmptyDays;
        private bool _isShow = true;

        /// <summary>
        /// 保存文字颜色
        /// </summary>
        public Color GreyColor;

        public static readonly ChineseLunisolarCalendar Cncld = new ChineseLunisolarCalendar();

        /// <summary>
        /// 农历月
        /// </summary>
        public static readonly string[] LunarMonths =
            { "正", "二", "三", "四", "五", "六", "七", "八", "九", "十", "十一", "腊" };

        public static readonly string[] LunarDaysT = { "初", "十", "廿", "三" };

        /// <summary>
        /// 农历日
        /// </summary>
        public static readonly string[] LunarDays =
            { "一", "二", "三", "四", "五", "六", "七", "八", "九", "十" };

        private DateTime _monthFirstDay;
        public int CrtDayIndex;

        private readonly List<DateTime> _dayItemList = new List<DateTime>(2);
        private ZCalendarDayItem _crtDayItem;

        private const float HideOffsetY = 5000f;
        private const int CalendarCellCount = 42;

        /// <summary>
        /// 初始化
        /// </summary>
        public void Init()
        {
            ZCalendarModel.zCalendarController = this;
            ZCalendarModel.Init();
            if (ZCalendarModel.isStaticCalendar) return;

            if (ZCalendarModel.isPopupCalendar)
            {
                ZCalendarModel.btnClose.onClick.AddListener(Hide);
            }

            ZCalendarModel.btnLastYear.onClick.AddListener(LastYear);
            ZCalendarModel.btnNextYear.onClick.AddListener(NextYear);
            ZCalendarModel.btnLastMonth.onClick.AddListener(LastMonth);
            ZCalendarModel.btnNextMonth.onClick.AddListener(NextMonth);
        }

        /// <summary>
        /// 按照规定时间初始化日历
        /// </summary>
        public void RefreshDate(DateTime date)
        {
            _now = date;
            RebuildCalendar();
            MarkInitComplete();
        }

        /// <summary>
        /// 按照指定时间初始化区间时间
        /// </summary>
        public void RefreshDate(DateTime startDate, DateTime endDate)
        {
            _now = DateTime.Today;
            ResetDayItems();
            ChangeRangeType(startDate);
            ChangeRangeType(endDate);
            UpdateYear();
            UpdateMonth();
            UpdateDays();
            UpdateDate();
            MarkInitComplete();
        }

        private void LastYear() => ShiftAndRebuild(_now.AddYears(-1));
        private void NextYear() => ShiftAndRebuild(_now.AddYears(1));
        public void LastMonth() => ShiftAndRebuild(_now.AddMonths(-1));
        private void NextMonth() => ShiftAndRebuild(_now.AddMonths(1));

        private void ShiftAndRebuild(DateTime next)
        {
            _now = next;
            RebuildCalendar();
        }

        private void RebuildCalendar()
        {
            ResetDayItems();
            UpdateYear();
            UpdateMonth();
            UpdateDays();
            UpdateDate();
        }

        private void MarkInitComplete()
        {
            if (IsInit) return;
            IsInit = true;
            ZCalendar.DateComplete();
        }

        /// <summary>
        /// 如果是区间日历，选择时间时，需要判断当前日期选择状态
        /// </summary>
        public void ChangeRangeType(DateTime dayItem)
        {
            _isInRange = !_isInRange;
            if (_dayItemList.Count >= 2)
            {
                _dayItemList.Clear();
            }

            if (_dayItemList.Count == 0)
            {
                _dayItemList.Add(dayItem);
            }
            else if (DateTime.Compare(_dayItemList[0], dayItem) <= 0)
            {
                _dayItemList.Add(dayItem);
            }
            else
            {
                _dayItemList.Insert(0, dayItem);
            }

            if (!_isInRange)
            {
                ZCalendar.RangeCalendar(_dayItemList[0], _dayItemList[1]);
                ZCalendar.isFirstSelect = false;
            }
        }

        /// <summary>
        /// 显示日历
        /// </summary>
        public void Show()
        {
            if (_isShow) return;
            _isShow = true;
            ZCalendar.transform.localPosition = Pos;
        }

        /// <summary>
        /// 隐藏日历
        /// </summary>
        public void Hide()
        {
            if (!_isShow || _isInRange) return;
            _isShow = false;
            ZCalendar.transform.localPosition = new Vector3(Pos.x, HideOffsetY, Pos.z);
        }

        void UpdateYear()
        {
            Year = _now.Year;
        }

        void UpdateMonth()
        {
            Month = _now.Month;
        }

        void UpdateDays()
        {
            _days = DateTime.DaysInMonth(Year, Month);
            if (Day == 0)
            {
                Day = _now.Day;
            }
            else if (Day > _days)
            {
                Day = _days;
            }
        }

        void UpdateDate()
        {
            ZCalendarModel.SetTimeTxt(Year, Month);
            FillLastMonth();
            for (int i = 0; i < _days; i++)
            {
                AddDayItem(_monthFirstDay.AddDays(i));
            }

            FillNextMonth();
        }

        void FillLastMonth()
        {
            _monthFirstDay = new DateTime(Year, Month, 1);
            _lastMonthEmptyDays = (int)_monthFirstDay.DayOfWeek;

            if (ZCalendarModel.autoFillDate)
            {
                for (int i = _lastMonthEmptyDays; i > 0; i--)
                {
                    AddDayItem(_monthFirstDay.AddDays(-i));
                }
            }
            else
            {
                for (int i = 0; i < _lastMonthEmptyDays; i++)
                {
                    _crtDayItem = ZCalendarModel.Instantiate();
                    _crtDayItem.zCalendarController = this;
                    _crtDayItem.CloseClickAble();
                }
            }
        }

        void FillNextMonth()
        {
            int nextMonthDays = CalendarCellCount - (_lastMonthEmptyDays + _days);
            if (nextMonthDays == 0) return;

            if (ZCalendarModel.autoFillDate)
            {
                DateTime lastDay = _monthFirstDay.AddDays(_days);
                for (int i = 0; i < nextMonthDays; i++)
                {
                    AddDayItem(lastDay.AddDays(i));
                }
            }
            else
            {
                for (int i = CalendarCellCount - nextMonthDays; i < CalendarCellCount; i++)
                {
                    ZCalendarModel.RecycleItem(i);
                }
            }
        }

        void AddDayItem(DateTime dateTime)
        {
            _crtDayItem = ZCalendarModel.Instantiate();
            _crtDayItem.zCalendarController = this;
            _crtDayItem.Init(dateTime, _now);
            ZCalendar.UpdateDate(_crtDayItem.DateTime);

            // 区间时间选择一个以后，在翻页的时候，需要更新
            if (!_isInRange && _dayItemList.Count > 0)
            {
                _crtDayItem.IsRangeDayItem(_dayItemList[0], _dayItemList[1]);
            }
        }

        void ResetDayItems()
        {
            CrtDayIndex = 0;
            var list = ZCalendarModel.dayItemList;
            for (int i = 0; i < list.Count; i++)
            {
                list[i].Cleanup();
            }
        }
    }
}
