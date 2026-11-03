using System.Collections.Generic;
using Newtonsoft.Json;
using NonsensicalKit.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Random = UnityEngine.Random;
namespace Frame.BizKit.WMS.Equipment
{
    /// <summary>
    ///设备详情处理器
    /// 订阅对应设备消息,然后响应显示 
    /// </summary>
    public class EquipmentDetailsElement : NonsensicalMono, IPointerClickHandler
    {
        [SerializeField] private string selfID; //用于从配置文件中获取设备ID
        [SerializeField] public string m_EquipmentType;
        [SerializeField] private TMP_Text _littleTitle;
        [SerializeField] private TMP_Text _questInfo;
        [SerializeField] private TMP_Text _questInfoHead;
        [SerializeField] private TMP_Text _completionstatusHead;
        [SerializeField] private TMP_Text _completionstatus;

        [SerializeField] private Button _btn_orientation;
        [SerializeField] private Button _btn_details;
        [SerializeField] private Button _btn_fold;

        [SerializeField] private EquipmentStatusImage _statusImg;
        [SerializeField] private Image m_detailEquipmentImg;
        [SerializeField] private TMP_Text _equipmentDetailsTitle;

        [SerializeField] private GameObject _detailsObj;
        [SerializeField] private GameObject _normalBG;
        [SerializeField] private GameObject _selectBG;


        [SerializeField] private RectTransform m_scrollRect;
        [SerializeField] private GlossariesTableManager _GManaget;
        [SerializeField] private CanvasGroup _canvasGroup;

        public string SelfID => selfID;
        public string DeviceCode => _littleTitle.text;
        public string DeviceName => _equipmentDetailsTitle.text;

        private bool _warningBuffer;


        private void Awake()
        {
            _btn_orientation.onClick.AddListener(FocusTo);
            _btn_details.onClick.AddListener(ShowDetails);
            _btn_fold.onClick.AddListener(FoldDetails);

            Subscribe<EquipmentDetailsElement>("selectEquipmentDetailsElement", OnSelect);
            Subscribe<EquipmentDetailsElement>("selectEquipmentDetails", OnUnFoldDetails);
        }


        public void Init(Sprite sprite, string deviceID, string etype, string deviceName)
        {
            m_detailEquipmentImg.sprite = sprite;
            m_EquipmentType = etype;
            _littleTitle.text = deviceID;
            _equipmentDetailsTitle.text = $"{deviceName}--{deviceID}";
            _statusImg.EquipmentID = _littleTitle.text;
        }

        private void Start()
        {
            ResetCardUi();
            _detailsObj.SetActive(false);
        }

        private void OnEnable()
        {
            // 列表按类型切换会批量 Enable/Disable 卡片，这里只复位本卡 UI，
            // 不能发 UnselectEquipment，否则会误清场景高亮。
            ResetCardUi();
        }

        private void OnDisable()
        {
            ResetCardUi();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            _btn_orientation.onClick.RemoveAllListeners();
            _btn_details.onClick.RemoveAllListeners();
            _btn_fold.onClick.RemoveAllListeners();
        }


        public void RefreshStatus(( SimpleInfo, Dictionary<string, string>) info)
        {
            SetInfo(info);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            Publish("selectEquipmentDetailsElement", this);
            Publish("UnselectEquipment"); //取消上一次选择对象的高光
            PublishWithID("SelectEquipment", _littleTitle.text);
        }

        /// <summary>仅复位本卡片 UI，不碰场景选中。</summary>
        private void ResetCardUi()
        {
            Select(false);
            if (_detailsObj != null)
                _detailsObj.SetActive(false);
        }

        /// <summary>主动取消选中（含场景高亮）。点击其它设备时走 OnPointerClick/FocusTo。</summary>
        public void OnReset()
        {
            ResetCardUi();
            Publish("UnselectEquipment");
        }

        #region 交互事件

        private void OnSelect(EquipmentDetailsElement element)
        {
            if (element == this)
            {
                Select();
            }
            else
            {
                Select(false);
            }
        }

        private void OnUnFoldDetails(EquipmentDetailsElement element)
        {
            if (element == this)
            {
                UnFoldDetails();
                // Select();
                Publish("selectEquipmentDetailsElement", this);
            }
            else
            {
                FoldDetails();
            }
        }

        #endregion

        #region 实现

        private void Select(bool tar = true)
        {
            _selectBG.SetActive(tar);
            _normalBG.SetActive(!tar);
        }

        private void UnFoldDetails()
        {
            _detailsObj.SetActive(true);
            RefreshRect();
        }

        private void FoldDetails()
        {
            _detailsObj.SetActive(false);
            RefreshRect();
        }

        #endregion


        private void ShowDetails()
        {
            Publish("selectEquipmentDetails", this);
        }

        private void FocusTo()
        {
            Debug.LogWarning("注视设备: " + _littleTitle.text);
            PublishWithID("focusEquipment", _littleTitle.text); //聚焦设备
            Publish("focusEquipment", _littleTitle.text); //聚焦设备
            Publish("selectEquipmentDetailsElement", this); //高亮UI标签

            Publish("UnselectEquipment"); //取消上一次选择对象的高光
            PublishWithID("SelectEquipment", _littleTitle.text); //高亮设备
            // Publish("showEnvironmentObj", false); //隐藏货架等无关模型
            Publish("HideEnvironmentObj", m_EquipmentType, _littleTitle.text); //隐藏货架等无关模型
        }


        private void SetInfo(( SimpleInfo, Dictionary<string, string>) info)
        {
            _questInfoHead.text = info.Item1.Quest.Key;
            _questInfo.text = info.Item1.Quest.Value;
            _completionstatusHead.text = info.Item1.CompletionStatus.Key;
            _completionstatus.text = info.Item1.CompletionStatus.Value;

            _completionstatus.color = info.Item1.Color;


            _GManaget.UpDateInfo(info.Item2);

            PublishWithID("setEquipmentStatus", "1002", _littleTitle.text, info.Item1.Status);
        }

        private void RefreshRect()
        {
            _canvasGroup.alpha = 0;
            m_scrollRect.offsetMax += new Vector2(1, 1);
            NonsensicalInstance.Instance.DelayDoIt(0f, () => { m_scrollRect.offsetMax += new Vector2(-1, -1); });
            NonsensicalInstance.Instance.DelayDoIt(0f, () => _canvasGroup.alpha = 1);
        }
    }
}
