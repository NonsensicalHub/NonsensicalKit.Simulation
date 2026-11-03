using System;
using System.Collections.Generic;
using NonsensicalKit.Core;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 监听 HideEnvironmentObj(设备类型, 设备ID)：仅按 Type 隐藏对应环境物体，忽略设备 ID。
/// 隐藏前缓存 activeSelf；切换类型时上一类型 OnExit、本类型 OnEnter；
/// 退出 Focus 时全部类型执行 OnExit，并恢复物体显示。
/// Type 与 EquipmentElementIconConfig.m_Config.Type 对齐。
/// </summary>
public class HideEnvironmentObjController : NonsensicalMono
{
    [Serializable]
    public class TypeHideGroup
    {
        [Tooltip("与 EquipmentElementIconConfig.IconConfig.Type 一致")]
        public string Type;

        [Tooltip("注视该类型设备时需要隐藏的物体")]
        public GameObject[] ObjectsToHide;

        [Tooltip("进入该类型注视时触发")]
        public UnityEvent OnEnter = new UnityEvent();

        [Tooltip("离开该类型注视时触发（切换到其他类型或退出 Focus）")]
        public UnityEvent OnExit = new UnityEvent();
    }

    [SerializeField] private TypeHideGroup[] m_typeGroups;

    private readonly Dictionary<string, TypeHideGroup> _lookup = new();
    private readonly List<(GameObject go, bool wasActive)> _cachedStates = new();
    private TypeHideGroup _activeGroup;
    private bool _hasCached;

    private void Awake()
    {
        BuildLookup();
        Subscribe<string, string>("HideEnvironmentObj", OnHideEnvironmentObj);
        Subscribe("ExitFocus", RestoreEnvironmentObj);
        Subscribe("RestoreEnvironmentObj", RestoreEnvironmentObj);
    }

    private void BuildLookup()
    {
        _lookup.Clear();
        if (m_typeGroups == null) return;

        foreach (var group in m_typeGroups)
        {
            if (group == null || string.IsNullOrEmpty(group.Type))
                continue;

            if (_lookup.ContainsKey(group.Type))
            {
                Debug.LogWarning(
                    $"[HideEnvironmentObj] 重复的 Type「{group.Type}」，后出现的将覆盖先前项。",
                    this);
            }

            _lookup[group.Type] = group;
        }
    }

    private void OnHideEnvironmentObj(string equipmentType, string _)
    {
        ExitActiveGroup();

        if (string.IsNullOrEmpty(equipmentType) ||
            !_lookup.TryGetValue(equipmentType, out TypeHideGroup group))
            return;

        group.OnEnter?.Invoke();
        _activeGroup = group;
        CacheAndHide(group.ObjectsToHide);
    }

    /// <summary>退出 Focus：全部类型执行 OnExit，并恢复物体。</summary>
    private void RestoreEnvironmentObj()
    {
        if (m_typeGroups != null)
        {
            foreach (var group in m_typeGroups)
                group?.OnExit?.Invoke();
        }

        _activeGroup = null;
        RestoreCachedObjects();
    }

    /// <summary>切换类型：仅上一类型 OnExit + 恢复其隐藏物体。</summary>
    private void ExitActiveGroup()
    {
        if (_activeGroup != null)
        {
            _activeGroup.OnExit?.Invoke();
            _activeGroup = null;
        }

        RestoreCachedObjects();
    }

    private void CacheAndHide(GameObject[] targets)
    {
        _cachedStates.Clear();
        _hasCached = false;
        if (targets == null || targets.Length == 0)
            return;

        foreach (var go in targets)
        {
            if (go == null) continue;

            _cachedStates.Add((go, go.activeSelf));
            if (go.activeSelf)
                go.SetActive(false);
        }

        _hasCached = _cachedStates.Count > 0;
    }

    private void RestoreCachedObjects()
    {
        if (!_hasCached)
        {
            _cachedStates.Clear();
            return;
        }

        foreach (var (go, wasActive) in _cachedStates)
        {
            if (go != null && go.activeSelf != wasActive)
                go.SetActive(wasActive);
        }

        _cachedStates.Clear();
        _hasCached = false;
    }
}
