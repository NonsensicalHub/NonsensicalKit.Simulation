using NonsensicalKit.Core;
using NonsensicalKit.Tools;
using UnityEngine;
using UnityEngine.UI;

public class EasyButtonAnime : MonoBehaviour
{
    [SerializeField] private Transform m_controlTarget;
    [SerializeField] private float m_time = 0.5f;

    private Vector3 _endPos;
    private Vector3 _startPos;
    private bool _hasEndPos;

    private void OnEnable()
    {
        m_controlTarget.gameObject.SetActive(false);
        NonsensicalInstance.Instance.DelayDoIt(0, PlayAnime);
    }

    private void OnDisable()
    {
        if (_hasEndPos)
            m_controlTarget.position = _startPos;
    }

    private void PlayAnime()
    {
        if (!isActiveAndEnabled || m_controlTarget == null)
            return;

        // 先激活，再强制布局，最后才记终点。
        // Awake 时父 Rect / CanvasScaler 往往还没算完，stretch 锚点下的 localPosition 会偏。
        m_controlTarget.gameObject.SetActive(true);

        if (!_hasEndPos)
        {
            CaptureEndPos();
            _hasEndPos = true;
        }

        var rect = transform.parent as RectTransform;
        if (rect == null)
            return;

        _startPos = GetGeometricCenter(rect);
        m_controlTarget.position = _startPos;
        m_controlTarget.DoLocalMove(_endPos, m_time);
    }

    private void CaptureEndPos()
    {
        if (m_controlTarget.parent is RectTransform parentRect)
            LayoutRebuilder.ForceRebuildLayoutImmediate(parentRect);
        else
            Canvas.ForceUpdateCanvases();

        _endPos = m_controlTarget.localPosition;
    }

    public static Vector3 GetGeometricCenter(RectTransform rectTransform)
    {
        Vector3[] corners = new Vector3[4];
        rectTransform.GetWorldCorners(corners);
        return (corners[0] + corners[2]) * 0.5f;
    }
}
