using Demo;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ZTools
{
    public class ZCalendarModel : MonoBehaviour
    {
        [Header("根据当前时间自动初始化")]
        public bool awake2Init = true;

        [Header("自动补充前后月份的日期")]
        public bool autoFillDate = true;

        [Header("超过当前时间是否可以点击")]
        public bool isUnexpiredTimeCanClick = true;

        [Header("显示农历日期")]
        public bool lunar = true;

        [Header("当前是否为弹窗日历")]
        [Header("如果为true，本对象显示状态不能关闭，可通过子集bak的显示状态控制默认状态")]
        public bool isPopupCalendar = false;

        [Header("弹窗日历时,选择日期后隐藏面板")]
        public bool isHideOnSelect = false;

        [Header("当前是否为静态日历,不可选择其他月份日期")]
        public bool isStaticCalendar = false;

        [Header("自动修改日期尺寸")]
        public bool autoSetItemSize = true;

        [Header("是否可以选择日期范围")]
        public bool rangeCalendar = false;

        [Header("是否需要选择时间")]
        public bool timeChoice = false;

        [Header("--------------------------------------------------------------------")]
        public GameObject bak;

        public Button btnLastYear;
        public Button btnNextYear;
        public Button btnLastMonth;
        public Button btnNextMonth;
        public TMP_Text txtYear;
        public TMP_Text txtMonth;
        public Transform dayContent;
        public ZCalendarDayItem dayItem;
        public GameObject timeBox;
        public CanvasGroup selectTiemObj;

        [HideInInspector]
        public Button btnClose;

        [HideInInspector]
        public ZCalendarController zCalendarController;

        private ZCalendarDayItem _crtItem;
        private RectTransform _rectTransform;
        private GridLayoutGroup _dayLayoutGroup;
        private RectTransform _dayContentRect;

        [HideInInspector]
        public List<ZCalendarDayItem> dayItemList = new List<ZCalendarDayItem>();

        [HideInInspector]
        public InitOnStart hour;

        [HideInInspector]
        public InitOnStart min;

        [HideInInspector]
        public InitOnStart second;

        /// <summary>
        /// 初始化
        /// </summary>
        public void Init()
        {
            CacheRefs();
            ResolveTimePickers();

            if (isPopupCalendar)
            {
                AddCloseBtn();
            }

            if (!bak.activeInHierarchy)
            {
                bak.SetActive(true);
                GetComponent<ZCalendar>().Hide();
            }

            if (autoSetItemSize)
            {
                SetItemSize();
            }

            if (isStaticCalendar)
            {
                btnLastYear.gameObject.SetActive(false);
                btnNextYear.gameObject.SetActive(false);
                btnLastMonth.gameObject.SetActive(false);
                btnNextMonth.gameObject.SetActive(false);
            }

            timeBox.SetActive(timeChoice);
            selectTiemObj.alpha = rangeCalendar ? 1 : 0;
        }

        private void CacheRefs()
        {
            if (_rectTransform == null) _rectTransform = GetComponent<RectTransform>();
            if (_dayContentRect == null) _dayContentRect = dayContent as RectTransform
                                                           ?? dayContent.GetComponent<RectTransform>();
            if (_dayLayoutGroup == null) _dayLayoutGroup = dayContent.GetComponent<GridLayoutGroup>();
        }

        private void ResolveTimePickers()
        {
            var dateList = timeBox.GetComponentsInChildren<InitOnStart>(true);
            for (int i = 0; i < dateList.Length; i++)
            {
                var item = dateList[i];
                string nameLower = item.gameObject.name.ToLowerInvariant();
                if (nameLower.Contains("hour"))
                {
                    hour = item;
                }
                else if (nameLower.Contains("min"))
                {
                    min = item;
                }
                else if (nameLower.Contains("second"))
                {
                    second = item;
                }
            }
        }

        /// <summary>
        /// 生成一个日期对象
        /// </summary>
        public ZCalendarDayItem Instantiate()
        {
            int index = zCalendarController.CrtDayIndex;
            if (!zCalendarController.IsInit || index >= dayItemList.Count)
            {
                _crtItem = Instantiate(dayItem, dayContent);
                dayItemList.Add(_crtItem);
            }
            else
            {
                _crtItem = dayItemList[index];
            }

            zCalendarController.CrtDayIndex++;
            return _crtItem;
        }

        public void RecycleItem(int index)
        {
            if (index < dayItemList.Count)
            {
                dayItemList[index].CloseClickAble();
            }
        }

        /// <summary>
        /// 根据日历尺寸，设置宽高
        /// </summary>
        public void SetItemSize()
        {
            CacheRefs();
            Vector2 bakSize = _rectTransform.sizeDelta;
            Vector2 dayContentSize = _dayContentRect.sizeDelta;
            var layoutGroup = _dayLayoutGroup;
            float itemSizeWidth = (bakSize.x
                                   - layoutGroup.spacing.x * layoutGroup.constraintCount
                                   - layoutGroup.padding.left
                                   - layoutGroup.padding.right)
                                  / layoutGroup.constraintCount;
            float itemSizeHeight = (bakSize.y
                                    - Mathf.Abs(dayContentSize.y)
                                    - layoutGroup.padding.top
                                    - layoutGroup.padding.bottom
                                    - layoutGroup.spacing.y * 6)
                                   / 6;
            layoutGroup.cellSize = new Vector2(itemSizeWidth, itemSizeHeight);
        }

        /// <summary>
        /// 添加空白处可关闭功能
        /// </summary>
        public void AddCloseBtn()
        {
            var btnCloseObj = new GameObject("CalendarCloseBtn");
            var btnCloseRect = btnCloseObj.AddComponent<RectTransform>();
            btnCloseObj.transform.SetParent(transform, false);
            btnCloseObj.transform.SetAsFirstSibling();
            btnCloseRect.sizeDelta = new Vector2(Screen.width, Screen.height);
            btnCloseObj.transform.position = new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);
            btnCloseObj.AddComponent<Image>().color = Color.clear;
            btnClose = btnCloseObj.AddComponent<Button>();
        }

        /// <summary>
        /// 设置年月文字
        /// </summary>
        public void SetTimeTxt(int year, int month)
        {
            txtYear.text = year + "年";
            txtMonth.text = month + "月";
        }
    }
}
