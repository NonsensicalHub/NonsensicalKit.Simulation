using System;
using HighlightPlus;
using UnityEngine;

/// <summary>
/// 向 <see cref="FocusPointManager"/> 注入 HighlightPlus 高亮实现（Func），不实现 Core 接口。
/// 挂在 FocusPointManager 同物体即可。
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-50)]
[AddComponentMenu("Frame Sample/HighlightPlus Focus Highlight")]
public class HighlightPlusFocusHighlight : MonoBehaviour
{
    [SerializeField] private FocusPointManager m_focusPointManager;

    private Func<GameObject, Action> _boundHandler;

    private void Awake()
    {
        if (m_focusPointManager == null)
            m_focusPointManager = GetComponent<FocusPointManager>();
    }

    private void OnEnable()
    {
        if (m_focusPointManager == null)
            m_focusPointManager = GetComponent<FocusPointManager>();
        if (m_focusPointManager == null)
        {
            Debug.LogWarning("[HighlightPlusFocusHighlight] 未找到 FocusPointManager。", this);
            return;
        }

        _boundHandler = CreateHandler;
        m_focusPointManager.HighlightHandler = _boundHandler;
    }

    private void OnDisable()
    {
        if (m_focusPointManager != null &&
            ReferenceEquals(m_focusPointManager.HighlightHandler, _boundHandler))
        {
            m_focusPointManager.HighlightHandler = null;
        }

        _boundHandler = null;
    }

    private static Action CreateHandler(GameObject go)
    {
        if (go == null)
            return null;

        var fx = go.GetComponent<HighlightEffect>();
        if (fx == null)
            return null;

        fx.highlighted = true;
        return () =>
        {
            if (fx != null)
                fx.highlighted = false;
        };
    }
}
