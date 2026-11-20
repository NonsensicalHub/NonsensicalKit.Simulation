using System.Collections.Generic;
using UnityEngine;

namespace Frame.Core.UI
{
[DisallowMultipleComponent]
public class UITransitionGroup : MonoBehaviour
{
    [SerializeField] private bool m_allowSwitchOff;

    private readonly List<UIFlexibleSizeTransition> _items = new List<UIFlexibleSizeTransition>();
    private UIFlexibleSizeTransition _activeItem;

    public bool AllowSwitchOff
    {
        get => m_allowSwitchOff;
        set => m_allowSwitchOff = value;
    }

    public UIFlexibleSizeTransition ActiveItem => _activeItem;

    public void Register(UIFlexibleSizeTransition item)
    {
        if (item == null || _items.Contains(item)) return;
        _items.Add(item);

        if (item.IsExpanded)
        {
            NotifyItemExpanded(item, false);
        }
    }

    public void Unregister(UIFlexibleSizeTransition item)
    {
        if (item == null) return;

        _items.Remove(item);
        if (_activeItem == item) _activeItem = null;
    }

    public void NotifyItemExpanded(UIFlexibleSizeTransition item, bool sendCallback = true)
    {
        if (item == null || !_items.Contains(item)) return;

        UIFlexibleSizeTransition previous = _activeItem;
        _activeItem = item;

        foreach (var other in _items)
        {
            if (other == null || other == item) continue;
            if (!other.IsExpanded && other != previous) continue;
            other.CollapseFromGroup(sendCallback);
        }
    }

    public void NotifyItemCollapsed(UIFlexibleSizeTransition item)
    {
        if (item == null) return;
        if (_activeItem == item) _activeItem = null;
    }

    public bool AnyExpanded()
    {
        for (int i = 0; i < _items.Count; i++)
        {
            if (_items[i] != null && _items[i].IsExpanded) return true;
        }

        return false;
    }

    public bool AnyExpandedExcept(UIFlexibleSizeTransition item)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            UIFlexibleSizeTransition other = _items[i];
            if (other != null && other != item && other.IsExpanded) return true;
        }

        return false;
    }

    public void CollapseAll()
    {
        if (!m_allowSwitchOff) return;

        for (int i = 0; i < _items.Count; i++)
        {
            UIFlexibleSizeTransition item = _items[i];
            if (item == null || !item.IsExpanded) continue;
            item.CollapseFromGroup(true);
        }

        _activeItem = null;
    }

    private void OnDisable()
    {
        _activeItem = null;
    }
}
}
