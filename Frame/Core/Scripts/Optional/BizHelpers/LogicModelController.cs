using System.Collections.Generic;
using NonsensicalKit.Core;
using NonsensicalKit.Core.DagLogicNode;
using NonsensicalKit.Core.Service;
using UnityEngine;

namespace Frame.Core
{
public class LogicModelController : NonsensicalMono
{
    [SerializeField] private DagNodeCheckType m_checkType;
    [SerializeField] private string m_nodeID;
    [SerializeField] private string m_virtualHideNodeID;//当节点路径为该值是隐藏物体

    [SerializeField] private List<string> m_spOn;
    [SerializeField] private List<string> m_spOff;
    private GameObject _controlTarget;
    private DagLogicManager _manager;

    private void Awake()
    {
        ServiceCore.SafeGet<DagLogicManager>(OnGetService);
    }

    private void OnGetService(DagLogicManager service)
    {
        _controlTarget = gameObject;
        _manager = service;
        Init();
    }

    private void Init()
    {
        Subscribe<DagRuntimeNode>((int)DagLogicNodeEnum.SwitchNode, OnSwitchNode);
        if (_manager.CrtSelectNode != null)
        {
            OnSwitchNode(_manager.CrtSelectNode);
        }
    }

    private void OnSwitchNode(DagRuntimeNode node)
    {
        if (m_spOn.Contains(node.NodeID))
        {
            _controlTarget.SetActive(true);
            return;
        }

        if (m_spOff.Contains(node.NodeID))
        {
            _controlTarget.SetActive(false);
            return;
        }
        
        if (node.NodeID is "全景" or "设备管理" or "数据统计")
        {
            IOCC.Set<string>("crtNodePath", string.Empty);
            IOCC.Publish("crtNodePath", "null");
        }

        var target = _manager.CheckState(m_nodeID, m_checkType);
        if (target)
        {
            if (IOCC.Get<string>("crtNodePath") != string.Empty && IOCC.Get<string>("crtNodePath") == m_virtualHideNodeID)
            {
                target = false;
            }
        }

        _controlTarget.SetActive(target);
    }
}
}
