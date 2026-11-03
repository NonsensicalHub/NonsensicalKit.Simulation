using System;
using System.Collections;
using System.Collections.Generic;
using NonsensicalKit.Core;
using NonsensicalKit.Tools;
using NonsensicalKit.UGUI.Table;
using UnityEngine;
namespace Frame.BizKit.WMS.RealTimeAlarm
{
    public class RealTimeAlarmTable : NonsensicalMono
    {
        private struct PushItem
        {
            public RectTransform Item;
            public Vector3 From;
            public Vector3 To;
        }

        [SerializeField] private ScrollView m_scrollView;
        [SerializeField] private RectTransform m_scrollRect;

        [SerializeField, Tooltip("在线报警有新卡出现在最上方时，可见卡片一起往下挤。关掉则立刻换位。")]
        private bool m_pushInNewest = true;

        private Coroutine _rollCoroutine;
        private Coroutine _pushCoroutine;

        private bool _initialized;
        private bool _realtimeList;
        private bool _seenSnapshot;
        private bool _sawHead;
        private RectTransform _headItem;
        private readonly List<RealTimeAlarmInfo> _datas = new();
        private readonly Dictionary<RectTransform, RealTimeAlarmElement> _elements = new();
        private readonly HashSet<string> _knownEpisodeIds = new();
        private readonly List<PushItem> _pushItems = new(8);

        private readonly WaitForSeconds _sleep8 = new(8);
        private readonly WaitForSeconds _sleep5 = new(5);
        private int _rollDir = 1;

        private void Awake()
        {
            Subscribe<List<RealTimeAlarmInfo>>("refreshRealTimeAlarmTable", OnRefresh);
        }

        private void OnEnable()
        {
            if (TryPull(out List<RealTimeAlarmInfo> list))
                Replace(list);

            if (!gameObject.activeInHierarchy)
                return;
            if (_datas.Count == 0 && !_initialized)
                return;

            Refresh();
        }

        private void OnDisable()
        {
            StopPush();
        }

        private void OnRefresh(List<RealTimeAlarmInfo> source)
        {
            Replace(source);
            if (isActiveAndEnabled)
                Refresh();
        }

        private void Replace(List<RealTimeAlarmInfo> source)
        {
            _datas.Clear();
            _realtimeList = false;
            if (source == null)
                return;

            for (int i = 0; i < source.Count; i++)
            {
                RealTimeAlarmInfo info = source[i];
                if (info == null)
                    continue;

                _datas.Add(info);
                if (!_realtimeList && !string.IsNullOrEmpty(info.EpisodeId))
                    _realtimeList = true;
            }
        }

        private bool TryPull(out List<RealTimeAlarmInfo> list)
        {
            try
            {
                list = Execute<List<RealTimeAlarmInfo>>(RealTimeAlarmInfo.SnapshotHandler);
                return list != null;
            }
            catch (Exception)
            {
                list = null;
                return false;
            }
        }

        private void Init()
        {
            RectTransform template = m_scrollRect != null && m_scrollRect.childCount > 0
                ? m_scrollRect.GetChild(0) as RectTransform
                : null;
            if (template == null)
            {
                Debug.LogWarning("模板对象不存在");
                _initialized = false;
                return;
            }

            m_scrollView.SetTemplate(template);
            m_scrollView.SetUpdateFunc(BindItem);
            m_scrollView.SetItemCountFunc(() => _datas.Count);
            _initialized = true;
        }

        private void BindItem(int index, RectTransform rectTransform)
        {
            if (index < 0 || index >= _datas.Count)
                return;

            if (!_elements.TryGetValue(rectTransform, out RealTimeAlarmElement element) || element == null)
                _elements[rectTransform] = element = rectTransform.GetComponent<RealTimeAlarmElement>();

            if (index == 0)
            {
                _headItem = rectTransform;
                _sawHead = true;
            }

            if (element != null)
                element.SetValue(_datas[index]);
        }

        private void Refresh()
        {
            if (!_initialized)
                Init();
            if (!_initialized)
                return;

            StopRoll();
            StopPush();

            _sawHead = false;
            m_scrollView.UpdateData(true);
            bool inserted = TakeNewHeadInsert();

            if (_realtimeList)
            {
                if (inserted && m_pushInNewest && CanPushIn())
                    _pushCoroutine = StartCoroutine(PushInNewest());
                return;
            }

            if (_datas.Count < 2)
                return;

            if (!m_scrollView.IsDragging && m_scrollView.velocity.sqrMagnitude == 0)
            {
                float v = m_scrollView.GetScrollValue(_datas.Count - 1, 1);
                m_scrollView.DoScrollTo(new Vector2(m_scrollView.horizontalNormalizedPosition, v), 0.5f);
            }

            _rollCoroutine = StartCoroutine(RollView());
        }

        private bool TakeNewHeadInsert()
        {
            string head = _datas.Count > 0 ? _datas[0].EpisodeId : null;
            bool inserted = _seenSnapshot && !string.IsNullOrEmpty(head) && !_knownEpisodeIds.Contains(head);

            _knownEpisodeIds.Clear();
            for (int i = 0; i < _datas.Count; i++)
            {
                if (!string.IsNullOrEmpty(_datas[i].EpisodeId))
                    _knownEpisodeIds.Add(_datas[i].EpisodeId);
            }

            if (_datas.Count > 0)
                _seenSnapshot = true;
            return inserted;
        }

        private bool CanPushIn()
        {
            if (!_sawHead || _headItem == null || m_scrollView.IsDragging || m_scrollView.velocity.sqrMagnitude >= 1f)
                return false;

            RectTransform content = m_scrollView.content;
            RectTransform viewport = m_scrollView.viewport;
            bool fits = content != null && viewport != null && content.rect.height <= viewport.rect.height + 1f;
            return fits || m_scrollView.verticalNormalizedPosition > 0.98f;
        }

        private IEnumerator PushInNewest()
        {
            yield return null;
            if (_headItem == null)
            {
                _pushCoroutine = null;
                yield break;
            }

            float height = _headItem.rect.height;
            if (height < 1f)
                height = 108f;

            Transform parent = _headItem.parent;
            _pushItems.Clear();
            foreach (var pair in _elements)
            {
                RectTransform item = pair.Key;
                if (item == null || !item.gameObject.activeInHierarchy || item.parent != parent)
                    continue;

                Vector3 to = item.localPosition;
                Vector3 from = to;
                from.y += height;
                item.localPosition = from;
                _pushItems.Add(new PushItem { Item = item, From = from, To = to });
            }

            const float duration = 0.35f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (m_scrollView.IsDragging)
                    break;

                elapsed += Time.deltaTime;
                float k = Mathf.Clamp01(elapsed / duration);
                k = k * k * (3f - 2f * k);
                for (int i = 0; i < _pushItems.Count; i++)
                {
                    PushItem push = _pushItems[i];
                    if (push.Item != null)
                        push.Item.localPosition = Vector3.Lerp(push.From, push.To, k);
                }

                yield return null;
            }

            for (int i = 0; i < _pushItems.Count; i++)
            {
                PushItem push = _pushItems[i];
                if (push.Item != null)
                    push.Item.localPosition = push.To;
            }

            _pushCoroutine = null;
        }

        /// <summary>
        /// 离线模拟把新记录加在列表末尾，所以从最底下往上滚。在线最新在最上，不进这里。
        /// </summary>
        private IEnumerator RollView()
        {
            yield return null;
            yield return _sleep8;
            if (_datas.Count <= 4)
                yield break;

            int index = _datas.Count - 2;
            _rollDir = 1;
            while (true)
            {
                m_scrollView.DoScrollTo(
                    new Vector2(m_scrollView.horizontalNormalizedPosition, m_scrollView.GetScrollValue(index, 1)),
                    1f);
                index -= _rollDir;
                yield return _sleep5;
                if (index <= 0)
                    _rollDir = -1;
                else if (index >= _datas.Count - 1)
                    _rollDir = 1;
            }
        }

        private void StopRoll()
        {
            if (_rollCoroutine == null)
                return;
            StopCoroutine(_rollCoroutine);
            _rollCoroutine = null;
        }

        private void StopPush()
        {
            if (_pushCoroutine == null)
                return;
            StopCoroutine(_pushCoroutine);
            _pushCoroutine = null;
        }
    }
}
