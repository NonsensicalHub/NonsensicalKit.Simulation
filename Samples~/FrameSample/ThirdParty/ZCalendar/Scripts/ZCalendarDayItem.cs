using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ZTools
{
    public class ZCalendarDayItem : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField, Tooltip("自定义时间段标记")] private bool cus;
        public GameObject imgBk;
        public GameObject rangeBk;
        public TMP_Text txt;
        public Button btn;
        public TMP_Text lunarTxt;

        [SerializeField] private GameObject _left;
        [SerializeField] private GameObject _right;

        [HideInInspector]
        public ZCalendarController zCalendarController;

        private bool _isCanClick = true;
        public int Year { get; set; }
        public int Month { get; set; }
        public int Day { get; set; }
        public DateTime DateTime;

        private bool _isOn;
        private bool _isRange;
        private Color _crtDayColor;
        private Color _crtLunarColor;
        private Color _greyColor;
        private bool _eventsBound;
        private ZCalendarModel _model;
        private ZCalendar _calendar;

        public bool IsOn
        {
            get => _isOn;
            set
            {
                // 保持原逻辑：已选中时再次设为 true 仍会触发选中流程
                if (_isOn != value || _isOn)
                {
                    _isOn = value;
                    imgBk?.SetActive(value);
                    if (!value) return;

                    if (!zCalendarController.IsInRange)
                    {
                        _calendar.DayClick(DateTime);
                    }

                    if (_model.rangeCalendar)
                    {
                        zCalendarController.ChangeRangeType(DateTime);
                    }

                    if (_model.isPopupCalendar
                        && zCalendarController.IsInit
                        && !_model.rangeCalendar
                        && _model.isHideOnSelect)
                    {
                        zCalendarController.Hide();
                    }
                }
            }
        }

        public bool IsOnWithOutEvent
        {
            set
            {
                if (_isOn == value) return;
                _isOn = value;
                imgBk?.SetActive(value);
            }
        }

        public bool IsRange
        {
            get => _isRange;
            set
            {
                if (_isRange == value) return;
                _isRange = value;
                rangeBk?.SetActive(value);
            }
        }

        private void Awake()
        {
            _crtDayColor = txt.color;
            _crtLunarColor = lunarTxt.color;
        }

        /// <summary>
        /// 初始化日期
        /// </summary>
        public void Init(DateTime dateTime, DateTime crtDay)
        {
            enabled = true;
            _model = zCalendarController.ZCalendarModel;
            _calendar = zCalendarController.ZCalendar;

            IsOnWithOutEvent = false;
            IsRange = false;
            DateTime = dateTime;
            Year = dateTime.Year;
            Month = dateTime.Month;
            Day = dateTime.Day;
            btn.interactable = true;
            txt.color = _crtDayColor;
            lunarTxt.color = _crtLunarColor;
            txt.text = Day.ToString("00");

            BindEvents();

            if (!_model.rangeCalendar)
            {
                IsOn = DateTime.Compare(dateTime, crtDay) == 0;
            }

            _isCanClick = !_model.isStaticCalendar;
            _greyColor = zCalendarController.GreyColor.a == 0
                ? new Color(txt.color.r, txt.color.g, txt.color.b, 0.1f)
                : zCalendarController.GreyColor;

            if (!_model.isUnexpiredTimeCanClick)
            {
                ApplyUnexpiredTime(zCalendarController.NowTime, dateTime);
            }

            if (_model.autoFillDate)
            {
                ApplyCurrentMonth(zCalendarController.Month);
            }

            if (_model.lunar)
            {
                lunarTxt.gameObject.SetActive(true);
                SolarToLunar(dateTime);
            }
        }

        private void BindEvents()
        {
            // 仅在已绑定过时解绑，避免首次 Init 清掉 Prefab 上原有监听
            if (_eventsBound)
            {
                UnbindEvents();
            }

            if (_model.rangeCalendar)
            {
                _calendar.RangeTimeChangedDayItemListenerEvent += RangeTimeEvent;
            }

            if (!_model.isStaticCalendar)
            {
                btn.onClick.AddListener(OnDayButtonClick);
                _calendar.DayValueChangedDayItemListenerEvent += ChangeState;
            }

            if (cus)
            {
                _calendar.CusClear += ClearCusMark;
            }

            _eventsBound = true;
        }

        private void UnbindEvents()
        {
            if (_calendar != null)
            {
                _calendar.RangeTimeChangedDayItemListenerEvent -= RangeTimeEvent;
                _calendar.DayValueChangedDayItemListenerEvent -= ChangeState;
                _calendar.CusClear -= ClearCusMark;
            }

            // 与原逻辑一致：静态日历不清理 Button 监听
            if (btn != null && (_model == null || !_model.isStaticCalendar))
            {
                btn.onClick.RemoveAllListeners();
            }

            _eventsBound = false;
        }

        private void OnDayButtonClick()
        {
            IsOn = true;
        }

        private void ClearCusMark()
        {
            lunarTxt.text = string.Empty;
            if (_left != null) _left.SetActive(false);
            if (_right != null) _right.SetActive(false);
        }

        /// <summary>
        /// 关闭可点击权限
        /// </summary>
        public void CloseClickAble()
        {
            _isRange = rangeBk.activeInHierarchy;
            _isOn = imgBk.activeInHierarchy;
            IsOn = false;
            txt.text = string.Empty;
            enabled = false;
            IsOnWithOutEvent = false;
            IsRange = false;
        }

        /// <summary>
        /// 判断是否在选择区间内的时间
        /// </summary>
        public void IsRangeDayItem(DateTime d1, DateTime d2)
        {
            if (DateTime.Compare(d1, DateTime) == 0 || DateTime.Compare(d2, DateTime) == 0)
            {
                IsOnWithOutEvent = true;
            }
            else
            {
                RangeTimeEvent(d1, d2);
            }
        }

        /// <summary>
        /// 自定义时间段落标记
        /// </summary>
        private void Cus(DateTime d1, DateTime d2)
        {
            int compare = DateTime.Compare(d1, d2);
            if (compare == 0) return;

            bool isStart = DateTime.Compare(DateTime, d1) == 0;
            bool showLeft = isStart ? compare < 0 : compare > 0;

            if (showLeft)
            {
                lunarTxt.text = "开始";
                _left.SetActive(true);
                _right.SetActive(false);
            }
            else
            {
                lunarTxt.text = "结束";
                _left.SetActive(false);
                _right.SetActive(true);
            }
        }

        /// <summary>
        /// 判断当前是否在区域选择时间内
        /// </summary>
        void RangeTimeEvent(DateTime d1, DateTime d2)
        {
            if (DateTime.Compare(d1, DateTime) < 0 && DateTime.Compare(d2, DateTime) > 0)
            {
                IsRange = true;
            }

            if (DateTime.Compare(d1, DateTime) == 0 || DateTime.Compare(d2, DateTime) == 0)
            {
                if (zCalendarController.IsInRange) return;
                if (cus)
                {
                    Cus(d1, d2);
                }
            }
        }

        /// <summary>
        /// 改变当前状态
        /// </summary>
        void ChangeState(DateTime dayItem)
        {
            if (dayItem != DateTime)
            {
                IsOn = false;
                IsRange = false;
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!_isOn && _isCanClick)
            {
                imgBk.SetActive(true);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!_isOn && _isCanClick)
            {
                imgBk.SetActive(false);
            }
        }

        void ApplyUnexpiredTime(DateTime time, DateTime crtTime)
        {
            if (DateTime.Compare(time, crtTime) >= 0) return;
            DisableInteractive();
        }

        void ApplyCurrentMonth(int currentMonth)
        {
            if (currentMonth == Month) return;
            DisableInteractive();
        }

        void DisableInteractive()
        {
            btn.interactable = false;
            _isCanClick = false;
            txt.color = _greyColor;
            lunarTxt.color = _greyColor;
        }

        void SolarToLunar(DateTime dt)
        {
            int year = ZCalendarController.Cncld.GetYear(dt);
            int leapMonth = ZCalendarController.Cncld.GetLeapMonth(year);
            int month = ZCalendarController.Cncld.GetMonth(dt);
            if (leapMonth > 0 && month >= leapMonth)
            {
                month--;
            }

            int day = ZCalendarController.Cncld.GetDayOfMonth(dt);
            lunarTxt.text = day == 1 ? GetLunarMonth(month) : GetLunarDay(day);
        }

        string GetLunarMonth(int month)
        {
            if (month < 1 || month > 12)
            {
                throw new ArgumentOutOfRangeException(nameof(month), "无效的月份!");
            }

            return $"{ZCalendarController.LunarMonths[month - 1]}月";
        }

        string GetLunarDay(int day)
        {
            if (day < 1 || day > 31)
            {
                throw new ArgumentOutOfRangeException(nameof(day), "无效的日!");
            }

            if (day != 20 && day != 30)
            {
                return string.Concat(
                    ZCalendarController.LunarDaysT[(day - 1) / 10],
                    ZCalendarController.LunarDays[(day - 1) % 10]);
            }

            return string.Concat(
                ZCalendarController.LunarDays[(day - 1) / 10],
                ZCalendarController.LunarDaysT[1]);
        }

        /// <summary>
        /// 翻页/回收前清理监听（兼容旧调用名 OnDestroy）
        /// </summary>
        public void Cleanup()
        {
            UnbindEvents();
        }

        /// <summary>
        /// 兼容旧调用；Unity 销毁对象时也会走到这里
        /// </summary>
        public void OnDestroy()
        {
            Cleanup();
        }
    }
}
