using System.Collections.Generic;
using NonsensicalKit.Core;
using NonsensicalKit.Core.DagLogicNode;
using NonsensicalKit.Tools;
using TMPro;
using UnityEngine;

/// <summary>
///设备状态信息管理类
/// 用于接收网络部分初步处理号的设备数据,并转发给UI作为显示
/// 包括信息显示与报警显示 消息订阅 
/// </summary>
namespace Frame.BizKit.WMS.Equipment
{
    public class EquipmentInfoManager : NonsensicalMono
    {
        [SerializeField] private TMP_Text _Title;
        [SerializeField] private RectTransform _rectTransform;
        [SerializeField] private GameObject m_prefab_equipmentDetailsElement;
        [SerializeField] private List<EquipmentDetailsElement> _eElements = new();


        Dictionary<string, List<EquipmentDetailsElement>> _elements = new();
        Dictionary<string, string> _equipmentTypeToName = new();


        private void Awake()
        {
            Subscribe<string>("showEquipment", ShowEquipment);
            Subscribe((int)DagLogicNodeEnum.NodeExit, "设备管理", OnExit);
        }

        public void Init(List<IconConfig> list)
        {
            foreach (var config in list)
            {
                var iconCount = config.CustomID ? config.CustomIDStrings.Length : config.Count;
                for (var i = 0; i <iconCount; i++)
                {
                    var obj = Instantiate(m_prefab_equipmentDetailsElement, m_prefab_equipmentDetailsElement.transform.parent);
                    obj.SetActive(true);
                    var element = obj.GetComponent<EquipmentDetailsElement>();
                    _eElements.Add(element);

                    element.Init(config.GetSprite(), config.CustomID ? config.CustomIDStrings[i] : config.PrefixID + i, config.Type, config.Name);
                    _elements.ListAdd(config.Type, element);
                    obj.SetActive(false);
                }

                _equipmentTypeToName.Add(config.Type, config.Name);
            }
        }

        private void OnEnable()
        {
            _Title.text = "";
            SetElementActive(string.Empty);
        }

        private void OnExit()
        {
        }

        private void ShowEquipment(string type)
        {
            _Title.text = _equipmentTypeToName[type];

            SetElementActive(type);
        }

        private void SetElementActive(string etype)
        {
            foreach (var item in _eElements)
            {
                item.gameObject.SetActive(item.m_EquipmentType == etype);
            }

            if (!string.IsNullOrEmpty(etype))
                RefreshVisible(etype);

            _rectTransform.offsetMax += new Vector2(0.001f, 0f);
            NonsensicalInstance.Instance.DelayDoIt(0f, () => { _rectTransform.offsetMax += new Vector2(-0.001f, 0f); });
        }

        public void Dispose()
        {
            foreach (var element in _eElements)
            {
                Destroy(element.gameObject);
            }
            _eElements.Clear();
            _elements.Clear();
            _equipmentTypeToName.Clear();
        }

        /// <summary>
        /// 刷新指定类型当前已显示的设备卡片（不改显隐/布局）。
        /// 供 <c>EquipmentInfoLiveRefresher</c> 调用；查询字典复用，降低 GC。
        /// </summary>
        public void RefreshVisible(string type)
        {
            if (string.IsNullOrEmpty(type) || !_elements.TryGetValue(type, out var list))
                return;

            var dic = new Dictionary<string, string>(2);
            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i];
                if (item == null || !item.gameObject.activeInHierarchy)
                    continue;

                dic.Clear();
                dic["DeviceCode"] = item.DeviceCode;
                dic["DeviceName"] = item.DeviceName;

                var info = Execute<Dictionary<string, string>, (SimpleInfo, Dictionary<string, string>)>(
                    "GetEquipmentInfo", dic);

                if (info.Item2 == null || !info.Item2.ContainsKey("DeviceCode"))
                    continue;

                item.RefreshStatus(info);
            }
        }
    }

    public class SimpleInfo
    {
        public KeyValuePair<string, string> Quest;
        public KeyValuePair<string, string> CompletionStatus;
        public Color Color;
        public int Status;
    }
}
