using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NonsensicalKit.Core;
using NonsensicalKit.Tools.ObjectPool;
using UnityEngine;

namespace Frame.Equipment
{
    public class GlossariesTable : NonsensicalMono
    {
        [SerializeField] private Transform m_btn_fold;
        [SerializeField] private GlossariesTableElement m_elementPrefab;
        [SerializeField] private GlossariesTableElement[] elements;

        private ComponentPoolMk2<GlossariesTableElement> _pool;

        // 料箱库:任务ID请求任务信息 
        private void Reset()
        {
            elements = GetComponentsInChildren<GlossariesTableElement>();
        }

        private void Awake()
        {
            _pool = new ComponentPoolMk2<GlossariesTableElement>(
                prefab: m_elementPrefab,
                resetAction: null,
                initAction: (element) =>
                {
                    element.gameObject.SetActive(true);
                    element.transform.SetParent(this.transform);
                    element.transform.localScale = Vector3.one;
                },
                createAction: null
            );
        }


        private void SetElements(List<GlossariesTableElementData> elementDatas)
        {
            _pool.Clear();
            foreach (var item in elementDatas)
            {
                _pool.New().SetValue(item);
            }

            m_btn_fold.SetAsLastSibling();
        }

        public void SetElement(Dictionary<string, string> info)
        {
            List<GlossariesTableElementData> temps = new();
            if (info.TryGetValue("DeviceCode", out string deviceCode) == false) return;

            foreach (var kp in info)
            {
                if (kp.Key == "DeviceCode") continue;
                temps.Add(new GlossariesTableElementData(GlossariesInfoType.info, kp.Key, kp.Value, deviceCode));
            }

            SetElements(temps);
        }
    }
}
