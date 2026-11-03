using System;
using UnityEngine;
using UnityEngine.Events;

namespace ZTools
{
    [RequireComponent(typeof(ZCalendarModel))]
    public class ZCalendar : MonoBehaviour
    {
        /// <summary>
        /// 数据更新时，可获取到每一个日期，并对其进行操作
        /// </summary>
        public class UpdateDateEvent : UnityEvent<DateTime>
        {
        }

        public UpdateDateEvent m_DayRefresh = new UpdateDateEvent();

        /// <summary>
        /// 可以获取到点击的某一天
        /// </summary>
        public class ChoiceDayEvent : UnityEvent<DateTime>
        {
        }

        public ChoiceDayEvent m_DayValueChanged = new ChoiceDayEvent();

        /// <summary>
        /// 选择区间时间事件
        /// </summary>
        public class RangeTimeEvent : UnityEvent<DateTime, DateTime>
        {
        }

        public RangeTimeEvent m_RangeTimeEvent = new RangeTimeEvent();

        /// <summary>
        /// 日历加载结束
        /// </summary>
        public class CompleteEvent : UnityEvent
        {
        }

        public CompleteEvent m_completeEvent = new CompleteEvent();

        /// <summary>
        /// 获取当前选中的天对象
        /// </summary>
        public DateTime CrtTime { get; set; }

        private ZCalendarModel _zCalendarModel;
        private ZCalendarController _zCalendarController;

        public CompleteEvent onComplete
        {
            get => m_completeEvent;
            set => m_completeEvent = value;
        }

        public RangeTimeEvent onRangeTimeValueChanged
        {
            get => m_RangeTimeEvent;
            set => m_RangeTimeEvent = value;
        }

        public ChoiceDayEvent onDayValueChanged
        {
            get => m_DayValueChanged;
            set => m_DayValueChanged = value;
        }

        public UpdateDateEvent onDayRefresh
        {
            get => m_DayRefresh;
            set => m_DayRefresh = value;
        }

        public event Action<DateTime> DayValueChangedDayItemListenerEvent;
        public event Action CusClear;
        public event Action<DateTime, DateTime> RangeTimeChangedDayItemListenerEvent;
        public event Action OnHideEvent;

        public bool isFirstSelect { get; set; }

        private DateTime _rangeStartTime;
        private DateTime _rangeEndTime;
        private (int Hour, int Minute, int Second) _firstTime;

        private void Start()
        {
            _zCalendarModel = GetComponent<ZCalendarModel>();
            if (_zCalendarModel.awake2Init)
            {
                Init();
            }
        }

        public void Init()
        {
            if (_zCalendarModel == null)
            {
                _zCalendarModel = GetComponent<ZCalendarModel>();
            }

            _zCalendarController = new ZCalendarController
            {
                ZCalendar = this,
                ZCalendarModel = _zCalendarModel,
                Pos = transform.localPosition
            };
            _zCalendarController.Init();
            RefreshDate();
        }

        public void RefreshDate()
        {
            _zCalendarController.RefreshDate(DateTime.Today);
        }

        public void RefreshDate(DateTime dateTime)
        {
            // 保持原判断条件，避免改变既有行为
            if (dateTime.Hour != 0 || dateTime.Month != 0 || dateTime.Second != 0)
            {
                dateTime = StripTime(dateTime);
            }

            _zCalendarController.RefreshDate(dateTime);
        }

        public void RefreshDate(string dateTime)
        {
            if (!TryParseDate(dateTime, out DateTime parsed))
            {
                Debug.LogError("wrong format: Time divided by '-' and do not contains hour minute or second");
                return;
            }

            _zCalendarController.RefreshDate(parsed);
        }

        public void RefreshDate(DateTime startDateTime, DateTime endDateTime)
        {
            if (!_zCalendarModel.rangeCalendar)
            {
                Debug.LogError("ZCalendar Init Error：The config is not RangeCalendar!!!");
                return;
            }

            if (startDateTime.Hour != 0 || startDateTime.Minute != 0 || startDateTime.Second != 0)
            {
                startDateTime = StripTime(startDateTime);
            }

            if (endDateTime.Hour != 0 || endDateTime.Minute != 0 || endDateTime.Second != 0)
            {
                endDateTime = StripTime(endDateTime);
            }

            _zCalendarController.RefreshDate(startDateTime, endDateTime);
        }

        public void RefreshDate(string startDateTime, string endDateTime)
        {
            // 保持与原实现相同的校验条件
            if (!startDateTime.Contains("-") || !endDateTime.Contains("-"))
            {
                Debug.LogError("wrong format: Time divided by '-' and do not contains hour minute or second");
                return;
            }

            string[] startParts = startDateTime.Split('-');
            string[] endParts = endDateTime.Split('-');
            _zCalendarController.RefreshDate(
                new DateTime(int.Parse(startParts[0]), int.Parse(startParts[1]), int.Parse(startParts[2])),
                new DateTime(int.Parse(endParts[0]), int.Parse(endParts[1]), int.Parse(endParts[2])));
        }

        public void Show()
        {
            _zCalendarController?.Show();
        }

        public void Hide()
        {
            _zCalendarController?.Hide();
            OnHideEvent?.Invoke();
        }

        public void UpdateDate(DateTime obj)
        {
            m_DayRefresh.Invoke(obj);
        }

        public void DayClick(DateTime dayItem)
        {
            int hour = _zCalendarModel.hour.choiceTime;
            int minute = _zCalendarModel.min.choiceTime;
            int second = _zCalendarModel.second.choiceTime;

            DayValueChangedDayItemListenerEvent?.Invoke(dayItem);
            m_DayValueChanged.Invoke(dayItem.AddHours(hour).AddMinutes(minute).AddSeconds(second));
            CrtTime = dayItem;

            if (isFirstSelect)
            {
                _rangeStartTime = dayItem;
                _rangeEndTime = default;
                CusClear?.Invoke();
            }
        }

        public void TimeChoice()
        {
            if (_zCalendarModel.rangeCalendar)
            {
                RangeCalendar(_rangeStartTime, _rangeEndTime);
            }
            else
            {
                DayClick(CrtTime);
            }
        }

        public void DateComplete()
        {
            onComplete?.Invoke();
        }

        public void RangeCalendar(DateTime firstDay, DateTime secondDay)
        {
            RangeTimeChangedDayItemListenerEvent?.Invoke(firstDay, secondDay);
            if (isFirstSelect)
            {
                _firstTime.Hour = _zCalendarModel.hour.choiceTime;
                _firstTime.Minute = _zCalendarModel.min.choiceTime;
                _firstTime.Second = _zCalendarModel.second.choiceTime;
            }

            m_RangeTimeEvent?.Invoke(
                firstDay.AddHours(_firstTime.Hour).AddMinutes(_firstTime.Minute).AddSeconds(_firstTime.Second),
                secondDay.AddHours(_zCalendarModel.hour.choiceTime)
                    .AddMinutes(_zCalendarModel.min.choiceTime)
                    .AddSeconds(_zCalendarModel.second.choiceTime));
            _rangeStartTime = firstDay;
            _rangeEndTime = secondDay;
        }

        private static DateTime StripTime(DateTime dateTime)
        {
            return new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, 0, 0, 0);
        }

        private static bool TryParseDate(string dateTime, out DateTime result)
        {
            result = default;
            if (string.IsNullOrEmpty(dateTime) || !dateTime.Contains("-") || dateTime.Contains(":"))
            {
                return false;
            }

            string[] parts = dateTime.Split('-');
            if (parts.Length < 3) return false;
            if (!int.TryParse(parts[0], out int year)
                || !int.TryParse(parts[1], out int month)
                || !int.TryParse(parts[2], out int day))
            {
                return false;
            }

            result = new DateTime(year, month, day);
            return true;
        }

        private void OnDestroy()
        {
            _zCalendarController = null;
        }
    }
}
