using System.Collections;
using NaughtyAttributes;
using UnityEngine;
using UnityEngine.Events;

namespace Frame.Core.UI
{
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class UIFlexibleSizeTransition : MonoBehaviour
{
    [Header("目标引用")]
    [SerializeField] private RectTransform m_rectTransform;

    [SerializeField] private UITransitionGroup m_group;

    [Header("默认参数")]
    [Min(0f)]
    [SerializeField] private float m_defaultDuration = 0.25f;

    [SerializeField] private bool m_ignoreTimeScale = true;

    [Header("展开配置")]
    [SerializeField] private Vector2 m_expandedSize = new Vector2(600f, 300f);

    [Header("收起配置")]
    [SerializeField] private bool m_useCustomCollapsedSize;

    [ShowIf(nameof(m_useCustomCollapsedSize))]
    [SerializeField] private Vector2 m_collapsedSize = new Vector2(300f, 150f);

    [Header("事件")]
    [SerializeField] private UnityEvent m_onBeforeExpand;
    [SerializeField] private UnityEvent m_onExpanded;
    [SerializeField] private UnityEvent m_onCollapsed;

    private Coroutine _transitionCoroutine;
    private State _originalState;
    private bool _hasOriginalState;
    private bool _isExpanded;

    public bool IsExpanded => _isExpanded;

    public UITransitionGroup Group
    {
        get => m_group;
        set
        {
            if (m_group == value) return;
            if (m_group != null) m_group.Unregister(this);
            m_group = value;
            if (isActiveAndEnabled && m_group != null) m_group.Register(this);
        }
    }

    public UnityEvent OnExpanded => m_onExpanded;
    public UnityEvent OnCollapsed => m_onCollapsed;

    private struct State
    {
        public Vector2 Size;
        public Vector2 OffsetMin;
        public Vector2 OffsetMax;
    }

    private void Reset()
    {
        m_rectTransform = GetComponent<RectTransform>();
    }

    private void Awake() => EnsureInitialized(true);

    private void OnEnable()
    {
        EnsureInitialized(false);
        if (m_group != null) m_group.Register(this);
    }

    private void OnDisable()
    {
        StopTransition();
        if (m_group != null) m_group.Unregister(this);
    }

    [ContextMenu("Cache Current As Original")]
    public void CacheCurrentAsOriginal() => CacheOriginalState();

    public void SetSizeInstant(Vector2 targetSize)
    {
        if (!EnsureInitialized(false)) return;
        StopTransition();
        ApplyState(BuildStateFromSize(targetSize, _originalState));
        Finish(Approximately(targetSize, m_expandedSize), true);
    }

    public void RestoreInstant() => CollapseInstant();

    public void SetSize(Vector2 targetSize) => SetSize(targetSize, m_defaultDuration);

    public void SetSize(Vector2 targetSize, float duration)
    {
        if (!EnsureInitialized(false)) return;
        bool expand = Approximately(targetSize, m_expandedSize);
        PlayTo(BuildStateFromSize(targetSize, _originalState), duration, expand);
    }

    [ContextMenu("Expand"), Button]
    public void Expand() => Expand(m_defaultDuration);

    public void Expand(float duration) => Expand(duration, true);

    public void Expand(bool sendCallback) => Expand(m_defaultDuration, sendCallback);

    public void Expand(float duration, bool sendCallback)
    {
        if (!EnsureInitialized(false)||_isExpanded) return;

        _isExpanded = true;
        if (m_group != null) m_group.NotifyItemExpanded(this, sendCallback);

        m_onBeforeExpand?.Invoke();
        m_rectTransform.gameObject.SetActive(true);
        PlayTo(BuildStateFromSize(m_expandedSize, _originalState), duration, true, sendCallback, true);
    }

    [ContextMenu("Collapse"), Button]
    public void Collapse() => Collapse(m_defaultDuration);

    public void Collapse(float duration) => Collapse(duration, true);

    public void Collapse(bool sendCallback) => Collapse(m_defaultDuration, sendCallback);

    public void Collapse(float duration, bool sendCallback)
    {
        CollapseInternal(duration, sendCallback, true);
    }

    public void CollapseInstant(bool sendCallback = true)
    {
        if (!EnsureInitialized(false)) return;
        if (!CanCollapse(true)) return;

        StopTransition();
        ApplyState(GetCollapsedState());
        Finish(false, sendCallback, true);
    }

    public void Restore() => Collapse();
    public void Restore(float duration) => Collapse(duration);

    [ContextMenu("Toggle"), Button]
    public void Toggle() => Toggle(m_defaultDuration);

    public void Toggle(float duration)
    {
        if (_isExpanded) Collapse(duration);
        else Expand(duration);
    }

    public Vector2 GetCurrentSize() => m_rectTransform == null ? Vector2.zero : m_rectTransform.rect.size;

    public Vector2 GetOriginalSize()
    {
        if (!EnsureInitialized(false)) return Vector2.zero;
        return _originalState.Size;
    }

    public void SetExpandedSize(Vector2 expandedSize) => m_expandedSize = expandedSize;

    public void SetCollapsedSize(Vector2 collapsedSize)
    {
        m_collapsedSize = collapsedSize;
        m_useCustomCollapsedSize = true;
    }

    public void ClearCollapsedSize() => m_useCustomCollapsedSize = false;

    [ContextMenu("Expand Instant")]
    private void ExpandInstantContext() => SetSizeInstant(m_expandedSize);

    [ContextMenu("Collapse Instant")]
    private void CollapseInstantContext() => CollapseInstant();

    private bool EnsureInitialized(bool forceCache)
    {
        m_rectTransform ??= GetComponent<RectTransform>();
        if (m_rectTransform == null) return false;
        if (forceCache || !_hasOriginalState) CacheOriginalState();
        return true;
    }

    private void CacheOriginalState()
    {
        _originalState = CaptureState();
        _hasOriginalState = true;
    }

    private static bool Approximately(Vector2 a, Vector2 b)
    {
        return Mathf.Approximately(a.x, b.x) && Mathf.Approximately(a.y, b.y);
    }

    private State CaptureState()
    {
        return new State
        {
            Size = m_rectTransform.rect.size,
            OffsetMin = m_rectTransform.offsetMin,
            OffsetMax = m_rectTransform.offsetMax
        };
    }

    private State BuildStateFromSize(Vector2 targetSize, State referenceState)
    {
        Vector2 delta = targetSize - referenceState.Size;
        Vector2 pivot = m_rectTransform.pivot;

        return new State
        {
            Size = targetSize,
            OffsetMin = referenceState.OffsetMin - Vector2.Scale(delta, pivot),
            OffsetMax = referenceState.OffsetMax + Vector2.Scale(delta, Vector2.one - pivot)
        };
    }

    private void ApplyState(State state)
    {
        m_rectTransform.offsetMin = state.OffsetMin;
        m_rectTransform.offsetMax = state.OffsetMax;
    }

    private State GetCollapsedState()
    {
        return m_useCustomCollapsedSize
            ? BuildStateFromSize(m_collapsedSize, _originalState)
            : _originalState;
    }

    private void PlayTo(State endState, float duration, bool expand, bool sendCallback = true, bool notifyGroup = true)
    {
        StopTransition();
        _isExpanded = expand;

        if (duration <= 0f)
        {
            ApplyState(endState);
            Finish(expand, sendCallback, notifyGroup);
            return;
        }

        _transitionCoroutine = StartCoroutine(TransitionCoroutine(CaptureState(), endState, duration, expand, sendCallback, notifyGroup));
    }

    private IEnumerator TransitionCoroutine(State startState, State endState, float duration, bool expand, bool sendCallback, bool notifyGroup)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += m_ignoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            m_rectTransform.offsetMin = Vector2.LerpUnclamped(startState.OffsetMin, endState.OffsetMin, t);
            m_rectTransform.offsetMax = Vector2.LerpUnclamped(startState.OffsetMax, endState.OffsetMax, t);
            yield return null;
        }

        ApplyState(endState);
        _transitionCoroutine = null;
        Finish(expand, sendCallback, notifyGroup);
    }

    private void Finish(bool expand, bool sendCallback, bool notifyGroup = true)
    {
        _isExpanded = expand;

        if (expand)
        {
            if (sendCallback) m_onExpanded?.Invoke();
            return;
        }

        if (notifyGroup && m_group != null) m_group.NotifyItemCollapsed(this);
        if (sendCallback) m_onCollapsed?.Invoke();
    }

    private void StopTransition()
    {
        if (_transitionCoroutine == null) return;
        StopCoroutine(_transitionCoroutine);
        _transitionCoroutine = null;
    }

    private bool CanCollapse(bool notifyGroup)
    {
        return !(notifyGroup && m_group != null && !m_group.AllowSwitchOff && _isExpanded && !m_group.AnyExpandedExcept(this));
    }

    private void CollapseInternal(float duration, bool sendCallback, bool notifyGroup)
    {
        if (!EnsureInitialized(false)) return;
        if (!CanCollapse(notifyGroup)) return;
        PlayTo(GetCollapsedState(), duration, false, sendCallback, notifyGroup);
    }

    internal void CollapseFromGroup(bool sendCallback)
    {
        CollapseInternal(m_defaultDuration, sendCallback, false);
    }
}
}
