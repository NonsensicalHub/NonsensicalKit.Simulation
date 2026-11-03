using System.Linq;
using NonsensicalKit.Core;
using NonsensicalKit.Core.DagLogicNode;
using NonsensicalKit.Core.Log;
using NonsensicalKit.Core.Service;
using NonsensicalKit.Tools;
using NonsensicalKit.UGUI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Frame.Core.DigitalTwinUI;


namespace Frame.BizKit.WMS.DigitalTwinUI
{
[RequireComponent(typeof(FollowGameObject))]
public class ReservoirEntranceUIPoint : NonsensicalUI, IDigitalTwinUI, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private GameObject m_controlPart;
    [SerializeField] private Button m_btn;
    [SerializeField] private TMP_Text m_showTipText;

    private FollowGameObject _follow;
    private string[] _showNodes;
    private string _dependencyNode;
    private string _id;
    private string _targetNode;
    private UIPoint _uiPoints;

    private DagLogicManager _logicM;
    private bool _registerUiPointListening;

    protected override void Awake()
    {
        base.Awake();

        _follow = GetComponent<FollowGameObject>();

        Subscribe<DagSwitchContext>((int)DagLogicNodeEnum.SwitchNode, OnChangedNode);
        if (m_btn != null)
        {
            m_btn.onClick.AddListener(OnButtonClick);
        }
    }


    private void Update()
    {
        m_controlPart.gameObject.SetActive(!_follow.Back);
    }

    public void Init(GameObject point, DigitalTwinUIInfo info)
    {
        _follow.SetTarget(point);
        _id = info.ID;
        _showNodes = info.DagNode.ShowNodes;
        _dependencyNode = info.DagNode.DependencyNode;
        m_showTipText.text = info.ID;
        if (!_registerUiPointListening)
        {
            IOCC.AddListener<UIPoint>("registerUIPoint", RegisterUIPoint);
            _registerUiPointListening = true;
        }

        // 若 UIPoint 已先注册到 Manager，立即绑定，不再只依赖后续 Set
        if (ServiceCore.TryGet<DigitalTwinUIManager>(out var manager) &&
            manager.TryGetUIPoint(_id, out var uiPoint))
        {
            RegisterUIPoint(uiPoint);
        }
    }

    private void RegisterUIPoint(UIPoint uiPoints)
    {
        if (uiPoints.m_IconID != _id) return;
        _uiPoints = uiPoints;
        //获取配置信息
        if (uiPoints != null)
        {
            ServiceCore.SafeGet<DagLogicManager>(OnGetService);
        }
        else
        {
            LogCore.Warning($"未找到id为{_id}的UI配置");
        }
    }

    private void OnButtonClick()
    {
        _uiPoints?.OnClick();
    }

    public void OnPointerEnter(PointerEventData eventData) => _uiPoints?.OnHoverEnter();

    public void OnPointerExit(PointerEventData eventData) => _uiPoints?.OnHoverExit();

    private void OnGetService(DagLogicManager logic)
    {
        _logicM = logic;
        CheckNodeID(logic.CrtSelectNode.NodeID);
    }

    private void OnChangedNode(DagSwitchContext node)
    {
        if (!string.IsNullOrEmpty(_dependencyNode) && !_logicM.ActivationChain.Contains(_dependencyNode)) return;
        if (_showNodes == null) return;
        CheckNodeID(node.ToNodeId);
    }

    private void CheckNodeID(string node)
    {
        if (_showNodes.Contains(node))
        {
            gameObject.SetActive(true);
            OpenSelf(true);
        }
        else
        {
            gameObject.SetActive(false);
            CloseSelf(false);
        }
    }

    protected override void OnDestroy()
    {
        if (_registerUiPointListening)
        {
            IOCC.RemoveListener<UIPoint>("registerUIPoint", RegisterUIPoint);
            _registerUiPointListening = false;
        }

        base.OnDestroy();
    }

    protected override void OnOpen()
    {
        CanvasGroup.blocksRaycasts = true;
        CanvasGroup.DoFade(1f, 0.2f);
    }
}
}
