using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Frame.Core.UI
{
/// <summary>
/// 可自定义个数的按钮事件绑定：每个按钮对应一个 UnityEvent，点击后触发。
/// </summary>
public class CustomButtonEvents : MonoBehaviour
{
    [Serializable]
    public class ButtonEventItem
    {
        [Tooltip("可选备注，仅用于 Inspector 识别")]
        public string Name;

        public Button Button;

        public UnityEvent OnClick = new UnityEvent();
    }

    [SerializeField] private List<ButtonEventItem> m_items = new List<ButtonEventItem>();

    [Tooltip("启用时自动绑定；禁用时自动解绑")]
    [SerializeField] private bool m_bindOnEnable = true;

    private readonly List<(Button button, UnityAction action)> _bound =
        new List<(Button, UnityAction)>();

    public IReadOnlyList<ButtonEventItem> Items => m_items;

    private void OnEnable()
    {
        if (m_bindOnEnable)
            BindAll();
    }

    private void OnDisable()
    {
        if (m_bindOnEnable)
            UnbindAll();
    }

    private void OnDestroy()
    {
        UnbindAll();
    }

    /// <summary>手动绑定全部按钮事件。</summary>
    public void BindAll()
    {
        UnbindAll();

        if (m_items == null)
            return;

        for (var i = 0; i < m_items.Count; i++)
        {
            var item = m_items[i];
            if (item == null || item.Button == null)
                continue;

            var captured = item;
            UnityAction action = () => captured.OnClick?.Invoke();
            item.Button.onClick.AddListener(action);
            _bound.Add((item.Button, action));
        }
    }

    /// <summary>解除全部按钮事件绑定。</summary>
    public void UnbindAll()
    {
        for (var i = 0; i < _bound.Count; i++)
        {
            var (button, action) = _bound[i];
            if (button != null)
                button.onClick.RemoveListener(action);
        }

        _bound.Clear();
    }

    /// <summary>运行时追加一项并立即绑定。</summary>
    public void AddItem(Button button, UnityAction callback, string name = null)
    {
        if (button == null)
            return;

        var item = new ButtonEventItem
        {
            Name = name,
            Button = button
        };
        if (callback != null)
            item.OnClick.AddListener(callback);

        m_items ??= new List<ButtonEventItem>();
        m_items.Add(item);

        if (isActiveAndEnabled && m_bindOnEnable)
        {
            UnityAction action = () => item.OnClick?.Invoke();
            button.onClick.AddListener(action);
            _bound.Add((button, action));
        }
    }

    /// <summary>按索引触发对应事件（不依赖按钮点击）。</summary>
    public void InvokeAt(int index)
    {
        if (m_items == null || index < 0 || index >= m_items.Count)
            return;

        m_items[index]?.OnClick?.Invoke();
    }
}
}
