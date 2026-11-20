using System.Collections;
using NonsensicalKit.Core;
using NonsensicalKit.Core.DagLogicNode;
using NonsensicalKit.DigitalTwin.Render;
using UnityEngine;

namespace Frame.Core.UI
{
public class DagControlActiveWithAlpha : DagSwitchEventBase
{
    [SerializeField] private GameObject m_controlTarget;
    [SerializeField] private MultiRender m_multiRender;
    [SerializeField, Min(0f)] private float m_fadeDuration = 1f;

    private Coroutine _fadeCoroutine;
    private int _fadeVersion;

    private void Reset()
    {
        m_controlTarget = gameObject;
        m_multiRender = GetComponent<MultiRender>();
    }
    
    protected override void OnStateChanged(bool newState)
    {
        if (m_multiRender == null)
        {
            if (m_controlTarget != null)
            {
                m_controlTarget.SetActive(newState);
            }

            return;
        }

        int version = ++_fadeVersion;

        var runner = NonsensicalInstance.Instance;
        if (_fadeCoroutine != null)
        {
            runner.StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = null;
        }

        _fadeCoroutine = runner.StartCoroutine(FadeDitherVisibility(newState, version));
    }

    private IEnumerator FadeDitherVisibility(bool visible, int version)
    {
        if (visible && m_controlTarget != null && !m_controlTarget.activeSelf)
        {
            m_controlTarget.SetActive(true);
        }

        float start = m_multiRender.GlobalDitherVisibility;
        float end = visible ? 1f : 0f;

        if (m_fadeDuration <= 0f)
        {
            if (version != _fadeVersion)
            {
                yield break;
            }

            m_multiRender.SetGlobalDitherVisibility(end);
            ApplyControlTargetActive(visible, version);
            ClearFadeCoroutineIfCurrent(version);
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < m_fadeDuration)
        {
            if (version != _fadeVersion)
            {
                yield break;
            }

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / m_fadeDuration);
            m_multiRender.SetGlobalDitherVisibility(Mathf.Lerp(start, end, t));
            yield return null;
        }

        if (version != _fadeVersion)
        {
            yield break;
        }

        m_multiRender.SetGlobalDitherVisibility(end);
        ApplyControlTargetActive(visible, version);
        ClearFadeCoroutineIfCurrent(version);
    }

    private void ApplyControlTargetActive(bool visible, int version)
    {
        if (version != _fadeVersion)
        {
            return;
        }

        if (!visible && m_controlTarget != null)
        {
            m_controlTarget.SetActive(false);
        }
    }

    private void ClearFadeCoroutineIfCurrent(int version)
    {
        if (version == _fadeVersion)
        {
            _fadeCoroutine = null;
        }
    }
}
}
