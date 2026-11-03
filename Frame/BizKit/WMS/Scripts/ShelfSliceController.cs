using NonsensicalKit.Core;
using NonsensicalKit.DigitalTwin.Warehouse;
using System.Collections;
using NaughtyAttributes;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

public class ShelfSliceController : NonsensicalMono
{
    // 外部可监听当前是否存在列动画（例如用于禁用交互）。
    [SerializeField] private UnityEvent<bool> m_animationStateChanged;

    // 与滑条事件总线绑定的信号 ID。
    [SerializeField] private string m_signalID;

    [FormerlySerializedAs("m_sliderWithInput")]
    [Header("绑定")]
    [SerializeField] private Transform[] m_columns;

    // 货物管理器：用于把列动画偏移同步给货物。
    [SerializeField] private WarehouseManager m_warehouseManager;

    [Header("显示动画")]
    [SerializeField] private float m_showFromAboveDistance = 1.2f;

    [SerializeField] private float m_showDuration = 0.25f;
    [SerializeField] private AnimationCurve m_showCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("隐藏动画")]
    [SerializeField] private float m_hideRiseDistance = 0.8f;

    [SerializeField] private float m_hideDuration = 0.2f;
    [SerializeField] private AnimationCurve m_hideCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("初始化")]
    [SerializeField] private bool m_playAnimationOnEnable;

    public bool IsAnimating => _isAnimating;

    // 每一列在初始状态下的 localPosition（作为动画目标和偏移基准）。
    private Vector3[] _originLocalPositions;

    // 每一列正在运行的协程，便于切换状态时打断旧动画。
    private Coroutine[] _runningCoroutines;
    private bool[] _columnVisibleStates;
    private int _runningCoroutineCount;
    private int _currentVisibleCount = -1;
    private bool _isAnimating;
    private Coroutine _deferredStopNotifyCoroutine;

    #region Single Column Display

    [Header("单列显示动画")]
    [SerializeField] private AnimationCurve m_showOnlyColumnCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private Coroutine _showOnlyColumnCoroutine;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        if (m_columns == null || m_columns.Length == 0)
        {
            Debug.LogWarning($"{nameof(ShelfSliceController)} 引用未设置完整。", this);
            return;
        }

        Subscribe<float>("SliderValueChanged", m_signalID, OnSliderValueChanged);
        Subscribe<int>("ShelfColumnChange", m_signalID, ShowOnlyColumn);

        _originLocalPositions = new Vector3[m_columns.Length];
        _runningCoroutines = new Coroutine[m_columns.Length];
        _columnVisibleStates = new bool[m_columns.Length];

        for (var i = 0; i < m_columns.Length; i++)
        {
            if (m_columns[i] == null)
            {
                continue;
            }

            _originLocalPositions[i] = m_columns[i].localPosition;
            _columnVisibleStates[i] = m_columns[i].gameObject.activeSelf;
        }

        ResetState();
        Subscribe("ResetShelfState", ResetState);
    }


    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (_runningCoroutines == null)
        {
            return;
        }

        for (var i = 0; i < _runningCoroutines.Length; i++)
        {
            if (_runningCoroutines[i] != null)
            {
                StopCoroutine(_runningCoroutines[i]);
                _runningCoroutines[i] = null;
                _runningCoroutineCount = Mathf.Max(0, _runningCoroutineCount - 1);
            }
        }

        if (_deferredStopNotifyCoroutine != null)
        {
            StopCoroutine(_deferredStopNotifyCoroutine);
            _deferredStopNotifyCoroutine = null;
        }

        #region Single Column Display Cleanup

        StopShowOnlyColumnRoutine();

        #endregion

        SetAnimatingState(false);
    }

    #endregion

    #region Public Method

    [Button]
    public void ResetState()
    {
        if (m_columns == null || m_columns.Length == 0)
        {
            return;
        }

        ApplyVisibleCount(m_columns.Length, !m_playAnimationOnEnable);
    }

    #endregion

    #region 单列显示

    public void ShowOnlyColumn(float column) => ShowOnlyColumn((int)column);

    public void ShowOnlyColumn(int column)
    {
        if (m_columns == null || m_columns.Length == 0)
        {
            return;
        }

        var columnIndex = column - 1;
        if (columnIndex < 0 || columnIndex >= m_columns.Length)
        {
            Debug.LogWarning($"{nameof(ShelfSliceController)} 列号越界：{column}。", this);
            return;
        }

        StopShowOnlyColumnRoutine();
        _currentVisibleCount = -1;
        _showOnlyColumnCoroutine = StartCoroutine(ShowOnlyColumnRoutine(columnIndex));
    }

    private IEnumerator ShowOnlyColumnRoutine(int columnIndex)
    {
        SetColumnVisible(columnIndex, true, false, m_showOnlyColumnCurve);

        var maxDistance = Mathf.Max(columnIndex, m_columns.Length - 1 - columnIndex);

        for (var distance = 1; distance <= maxDistance; distance++)
        {
            var leftIndex = columnIndex - distance;
            if (leftIndex >= 0)
            {
                SetColumnVisible(leftIndex, false, false, m_showOnlyColumnCurve);
            }

            var rightIndex = columnIndex + distance;
            if (rightIndex < m_columns.Length)
            {
                SetColumnVisible(rightIndex, false, false, m_showOnlyColumnCurve);
            }

            if (distance < maxDistance)
            {
                yield return null;
            }
        }

        _showOnlyColumnCoroutine = null;
    }

    private void StopShowOnlyColumnRoutine()
    {
        if (_showOnlyColumnCoroutine == null)
        {
            return;
        }

        StopCoroutine(_showOnlyColumnCoroutine);
        _showOnlyColumnCoroutine = null;
    }

    #endregion

    #region Slider Driven Visibility

    private void OnSliderValueChanged(float value)
    {
        ApplyVisibleCount(value, false);
    }

    private void ApplyVisibleCount(float sliderValue, bool immediate)
    {
        //停止单列显示
        StopShowOnlyColumnRoutine();

        // slider 值按向下取整得到“应显示列数”，并限制在合法范围内。
        var visibleCount = Mathf.Clamp(Mathf.FloorToInt(sliderValue), 0, m_columns.Length);
        if (_currentVisibleCount == visibleCount)
        {
            return;
        }

        if (_currentVisibleCount < 0)
        {
            for (var i = 0; i < m_columns.Length; i++)
            {
                var shouldShow = i < visibleCount;
                SetColumnVisible(i, shouldShow, immediate);
            }

            _currentVisibleCount = visibleCount;
            return;
        }

        if (visibleCount > _currentVisibleCount)
        {
            for (var i = _currentVisibleCount; i < visibleCount; i++)
            {
                SetColumnVisible(i, true, immediate);
            }
        }
        else
        {
            for (var i = visibleCount; i < _currentVisibleCount; i++)
            {
                SetColumnVisible(i, false, immediate);
            }
        }

        _currentVisibleCount = visibleCount;
    }

    private void SetColumnVisible(int index, bool shouldShow, bool immediate, AnimationCurve animationCurve = null)
    {
        var column = m_columns[index];
        if (column == null)
        {
            return;
        }

        var hasRunningCoroutine = _runningCoroutines[index] != null;
        if (!immediate && !hasRunningCoroutine && _columnVisibleStates[index] == shouldShow)
        {
            return;
        }

        if (hasRunningCoroutine)
        {
            StopColumnRoutine(index);
        }

        var targetPos = _originLocalPositions[index];
        if (immediate)
        {
            SetAnimatingState(true);
            column.localPosition = targetPos;
            column.gameObject.SetActive(shouldShow);
            _columnVisibleStates[index] = shouldShow;
            // 即时模式也要同步货物偏移（通常会回到零偏移）。
            SyncColumnOffset(index, column.localPosition);
            SyncColumnState(index, shouldShow);
            RequestStopAnimatingEvent();
            return;
        }

        if (shouldShow)
        {
            ShowColumn(column, index, targetPos, animationCurve ?? m_showCurve);
            return;
        }

        HideColumn(column, index, targetPos, animationCurve ?? m_hideCurve);
    }

    #endregion

    #region Column Animation

    private void ShowColumn(Transform column, int index, Vector3 targetPos, AnimationCurve curve)
    {
        if (!column.gameObject.activeSelf)
        {
            column.localPosition = targetPos + Vector3.up * m_showFromAboveDistance;
            column.gameObject.SetActive(true);
            SyncColumnState(index, true);
        }

        _columnVisibleStates[index] = true;
        StartColumnRoutine(index, MoveColumnRoutine(column, index, column.localPosition, targetPos,
            m_showDuration, curve, true));
    }

    private void HideColumn(Transform column, int index, Vector3 originPos, AnimationCurve curve)
    {
        if (!column.gameObject.activeSelf)
        {
            // 已隐藏则确保位置归位，避免累积误差。
            column.localPosition = originPos;
            SyncColumnOffset(index, column.localPosition);
            SyncColumnState(index, false);
            _columnVisibleStates[index] = false;
            return;
        }

        var hideTarget = originPos + Vector3.up * m_hideRiseDistance;
        _columnVisibleStates[index] = false;
        StartColumnRoutine(index, MoveColumnRoutine(column, index, column.localPosition, hideTarget,
            m_hideDuration, curve, false));
    }

    private IEnumerator MoveColumnRoutine(Transform column, int index, Vector3 startPos, Vector3 endPos, float duration,
        AnimationCurve curve, bool keepActiveAtEnd)
    {
        var safeDuration = Mathf.Max(0.0001f, duration);
        var elapsed = 0f;

        while (elapsed < safeDuration)
        {
            elapsed += Time.deltaTime;
            var t = Mathf.Clamp01(elapsed / safeDuration);
            var curveT = curve?.Evaluate(t) ?? t;
            column.localPosition = Vector3.LerpUnclamped(startPos, endPos, curveT);
            // 货架与货物在同一协程同一帧内更新，避免不同驱动源导致的错位。
            SyncColumnOffset(index, column.localPosition);
            yield return null;
        }

        if (keepActiveAtEnd)
        {
            column.localPosition = endPos;
            SyncColumnOffset(index, column.localPosition);
        }
        else
        {
            var originPos = _originLocalPositions[index];
            column.localPosition = originPos;
            column.gameObject.SetActive(false);
            SyncColumnOffset(index, column.localPosition);
            SyncColumnState(index, false);
        }

        ClearColumnRoutine(index);
    }

    #endregion

    #region Warehouse Sync

    private void SyncColumnOffset(int index, Vector3 currentColumnLocalPosition)
    {
        if (m_warehouseManager == null || index < 0 || index >= m_columns.Length)
        {
            return;
        }

        var currentOffset = currentColumnLocalPosition - _originLocalPositions[index];
        m_warehouseManager.SetColumnOffset(index, currentOffset);
        Publish("ShelfSliceOffsetChanged", index, currentOffset);
    }

    private void SyncColumnState(int index, bool state)
    {
        if (m_warehouseManager == null || index < 0 || index >= m_columns.Length)
        {
            return;
        }

        m_warehouseManager.SetColumnState(index, state);
        Publish("ShelfSliceStateChanged", index, state);
    }

    #endregion

    #region Coroutine Tracking

    private void StartColumnRoutine(int index, IEnumerator routine)
    {
        if (_runningCoroutines[index] != null)
        {
            StopColumnRoutine(index);
        }

        _runningCoroutines[index] = StartCoroutine(routine);
        _runningCoroutineCount++;
        SetAnimatingState(true);
    }

    private void StopColumnRoutine(int index)
    {
        if (_runningCoroutines[index] == null)
        {
            return;
        }

        StopCoroutine(_runningCoroutines[index]);
        _runningCoroutines[index] = null;
        _runningCoroutineCount = Mathf.Max(0, _runningCoroutineCount - 1);
        OnCoroutineCountChanged();
    }

    private void ClearColumnRoutine(int index)
    {
        if (_runningCoroutines[index] == null)
        {
            return;
        }

        _runningCoroutines[index] = null;
        _runningCoroutineCount = Mathf.Max(0, _runningCoroutineCount - 1);
        OnCoroutineCountChanged();
    }

    #endregion

    #region Animation State

    private void OnCoroutineCountChanged()
    {
        if (_runningCoroutineCount > 0)
        {
            if (_deferredStopNotifyCoroutine != null)
            {
                StopCoroutine(_deferredStopNotifyCoroutine);
                _deferredStopNotifyCoroutine = null;
            }

            SetAnimatingState(true);
            return;
        }

        RequestStopAnimatingEvent();
    }

    private void RequestStopAnimatingEvent()
    {
        if (_deferredStopNotifyCoroutine == null)
        {
            _deferredStopNotifyCoroutine = StartCoroutine(DelayStopAnimatingEvent());
        }
    }

    private IEnumerator DelayStopAnimatingEvent()
    {
        // 延迟一帧保证状态同步。
        yield return null;
        _deferredStopNotifyCoroutine = null;
        if (_runningCoroutineCount > 0)
        {
            yield break;
        }

        SetAnimatingState(false);
    }

    private void SetAnimatingState(bool isAnimating)
    {
        if (_isAnimating == isAnimating)
        {
            return;
        }

        _isAnimating = isAnimating;
        m_animationStateChanged?.Invoke(_isAnimating);
    }

    #endregion
}
