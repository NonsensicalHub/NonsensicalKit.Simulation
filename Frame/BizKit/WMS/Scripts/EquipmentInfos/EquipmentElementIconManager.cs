using System;
using System.Collections.Generic;
using Frame.Equipment;
using NonsensicalKit.Core;
using NonsensicalKit.Core.DagLogicNode;
using NonsensicalKit.Core.Service;
using UnityEngine;

public class EquipmentElementIconManager : NonsensicalMono
{
    [SerializeField] private GameObject _iconPrefab;
    [SerializeField] private EquipmentElementIconConfig _config;
    [SerializeField] private EquipmentInfoManager _equipmentInfoManager;

    private IconConfigType _iconConfigType;
    private readonly List<GameObject> _equipmentTypeIcons = new();

    private bool _inited;

    private void Awake()
    {
        if (_equipmentInfoManager == null)
        {
            _equipmentInfoManager = FindFirstObjectByType<EquipmentInfoManager>();
        }

        Subscribe<string>("UnloadSceneCompleted", ClearScene);
    }


    private void OnEnable()
    {
        if (_inited) return;
        _inited = true;
        ServiceCore.SafeGet<DagLogicManager>(LoadIconConfig);
    }

    private void LoadIconConfig(DagLogicManager obj)
    {
        IconConfigType temp = new();
        foreach (var node in _config.data.Types)
        {
            if (obj.IsInActivationChain(node.m_BelongNode))
            {
                temp = node;
                break;
            }

            if (node.m_IsDefault) temp = node;
        }

        _iconConfigType = temp;
        InitIconConfig();
    }

    private void InitIconConfig()
    {
        foreach (var icon in _iconConfigType.m_Config)
        {
            var go = Instantiate(_iconPrefab.gameObject, _iconPrefab.transform.parent);
            go.SetActive(true);
            _equipmentTypeIcons.Add(go);
            var iconUI = go.GetComponent<EquipmentElement>();
            iconUI.Init(icon.Name, icon.Sprite, icon.Type);
        }

        if (_equipmentInfoManager == null)
        {
            Debug.LogError(" 未找到设备信息管理器,将无法生成设备详细信息Element ");
            return;
        }

        _equipmentInfoManager.Init(_iconConfigType.m_Config);
    }

    private void ClearScene(string obj)
    {
        _inited = false;
        foreach (var t in _equipmentTypeIcons)
        {
            Destroy(t);
        }

        if (_equipmentInfoManager == null) return;
        _equipmentInfoManager.Dispose();
    }
}
