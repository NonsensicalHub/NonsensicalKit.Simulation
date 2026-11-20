using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using Frame.BizKit.WMS;
using NonsensicalKit.Core;
using NonsensicalKit.Core.DagLogicNode;
using NonsensicalKit.UGUI;
using NonsensicalKit.UGUI.Table;
using UnityEngine;
using UnityEngine.UI;

namespace Frame.BizKit.WMS
{
public class CargoInfoPaging : NonsensicalUI
{

    [SerializeField] private ScrollViewEx m_scrollView;

    [SerializeField] private RectTransform m_rectTransform_viewport;
    [SerializeField] private RectTransform m_rectTransform_content;

    [SerializeField] private Button m_button_L;
    [SerializeField] private Button m_button_R;

    [SerializeField] private bool _canUpdate = true;

    private int _selectTemp;
    private List<CargoInfo> datas = new();
    
    protected override void Awake() 
    {
        base.Awake();
        Subscribe<List<CargoInfo>>("updateCargoInfoPaging", UpData);
        Subscribe<int>("selectCargoInfoDetails", (e) => _selectTemp = e);

        Subscribe((int)DagLogicNodeEnum.NodeEnter, "库存管理", OnEnter);
        Subscribe((int)DagLogicNodeEnum.NodeExit, "库存管理", OnExit);

        m_button_L.onClick.AddListener(() => m_scrollView.normalizedPosition += new Vector2(0.2f, 0));
        m_button_R.onClick.AddListener(() => m_scrollView.normalizedPosition -= new Vector2(0.2f, 0));
    }
    
    protected override void Start()
    {
        base.Start();
        Init();
    }
    protected override void OnDestroy()
    {
        base.OnDestroy();
        m_button_L.onClick.RemoveAllListeners();
        m_button_R.onClick.RemoveAllListeners();
    }

    private void Init()
    {
        m_scrollView.SetItemCountFunc(() =>
        {
            return datas.Count;
        });

        m_scrollView.SetUpdateFunc((index, rectTransform) =>
        {
            try
            {
                if (datas.Count == 0) return;
                if (datas.Count <= index) return;
                //Debug.Log(datas.Count + " dsds  " + index);
                //rectTransform.GetComponent<CargoDetailsElement>().SetValue(datas[index], index + 1, _selectTemp);
            }
            catch (Exception e) { Debug.LogError(e); Debug.Log(datas.Count + " dsds  " + index); }
        });
    }
    
    private void OnEnter()
    {
        NonsensicalInstance.Instance.DelayDoIt(0.01f, () => _canUpdate = true);
    }
    private void OnExit()
    {
        NonsensicalInstance.Instance.DelayDoIt(0.01f, () => _canUpdate = false);
    }

    private void UpData(List<CargoInfo> list)
    {
        datas = list;
        m_scrollView.UpdateData(true);
        _selectTemp = -1;
        if (datas.Count < 1) return;
       
        Publish("selectFirst");
        // 使用 DOTween.dll 核心 API，不依赖 DOTween.Modules（DOHorizontalNormalizedPos 在 ModuleUI 中）
        DOTween.To(
                () => m_scrollView.horizontalNormalizedPosition,
                x => m_scrollView.horizontalNormalizedPosition = x,
                0f,
                0.2f)
            .SetTarget(m_scrollView);
    }

    private void Update()
    {
        if (_canUpdate)
        {
            if (m_rectTransform_content.rect.width > m_rectTransform_viewport.rect.width)
            {
                if (m_scrollView.normalizedPosition.x < 0.1f)
                {
                    m_button_L.gameObject.SetActive(true);
                    m_button_R.gameObject.SetActive(false);
                }
                else if (m_scrollView.normalizedPosition.x > 0.9f)
                {
                    m_button_L.gameObject.SetActive(false);
                    m_button_R.gameObject.SetActive(true);
                }
                else
                {
                    m_button_L.gameObject.SetActive(true);
                    m_button_R.gameObject.SetActive(true);
                }
            }
            else
            {
                m_button_L.gameObject.SetActive(false);
                m_button_R.gameObject.SetActive(false);
            }
        }
    }
}
}
