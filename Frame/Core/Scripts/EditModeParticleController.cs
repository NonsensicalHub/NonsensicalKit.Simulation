    using System.Collections.Generic;
using NaughtyAttributes;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 挂在父节点上，在编辑模式下统一控制所有子节点中的 ParticleSystem 播放。
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class EditModeParticleController : MonoBehaviour
{
    [Header("收集")]
    [SerializeField] private bool m_includeInactive = true;

    [SerializeField, ReadOnly] private int m_particleCount;

    [Header("编辑预览")]
    [Tooltip("勾选后在编辑模式下持续模拟子节点粒子。取消勾选即停止。")]
    [SerializeField] private bool m_playInEditMode;

    private ParticleSystem[] _rootParticles = System.Array.Empty<ParticleSystem>();

#if UNITY_EDITOR
    private double _lastEditorTime;
    private bool _subscribedEditorUpdate;
    private bool _wasPlaying;
#endif

    private void OnEnable()
    {
        RefreshParticles();

#if UNITY_EDITOR
        SubscribeEditorUpdate();
        _wasPlaying = m_playInEditMode;
        if (m_playInEditMode)
        {
            RestartSimulation();
        }
#endif
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        // 注意：Inspector 点 Button / 改字段时，ExecuteAlways 常会短暂 OnDisable。
        // 这里只取消订阅，绝不 Stop/Clear，否则会表现为“点了播放没反应”。
        UnsubscribeEditorUpdate();
#endif
    }

    private void OnDestroy()
    {
#if UNITY_EDITOR
        UnsubscribeEditorUpdate();
        if (!Application.isPlaying)
        {
            StopSimulation(true);
        }
#endif
    }

    private void OnValidate()
    {
        RefreshParticles();

#if UNITY_EDITOR
        if (Application.isPlaying || !isActiveAndEnabled)
        {
            return;
        }

        // 用序列化开关驱动，避免 Button 回调被 OnDisable 打断后状态丢失。
        if (m_playInEditMode && !_wasPlaying)
        {
            RestartSimulation();
        }
        else if (!m_playInEditMode && _wasPlaying)
        {
            StopSimulation(true);
        }

        _wasPlaying = m_playInEditMode;
#endif
    }

    [Button("刷新粒子列表")]
    private void RefreshParticles()
    {
        var all = GetComponentsInChildren<ParticleSystem>(m_includeInactive);
        m_particleCount = all != null ? all.Length : 0;

        if (all == null || all.Length == 0)
        {
            _rootParticles = System.Array.Empty<ParticleSystem>();
            return;
        }

        // 只驱动“根”粒子，并用 withChildren=true，避免父子被重复 Simulate。
        var roots = new List<ParticleSystem>(all.Length);
        for (var i = 0; i < all.Length; i++)
        {
            var ps = all[i];
            if (ps == null)
            {
                continue;
            }

            if (IsRootParticle(ps))
            {
                roots.Add(ps);
            }
        }

        _rootParticles = roots.ToArray();
    }

    [Button("播放")]
    private void PlayButton()
    {
        m_playInEditMode = true;
#if UNITY_EDITOR
        _wasPlaying = true;
        if (!Application.isPlaying)
        {
            SubscribeEditorUpdate();
            RestartSimulation();
            return;
        }
#endif
        PlayRuntime();
    }

    [Button("停止")]
    private void StopButton()
    {
        m_playInEditMode = false;
#if UNITY_EDITOR
        _wasPlaying = false;
        if (!Application.isPlaying)
        {
            StopSimulation(true);
            return;
        }
#endif
        StopRuntime(true);
    }
    private bool IsRootParticle(ParticleSystem ps)
    {
        for (var t = ps.transform.parent; t != null; t = t.parent)
        {
            if (t.GetComponent<ParticleSystem>() != null)
            {
                return false;
            }

            if (t == transform)
            {
                break;
            }
        }

        return true;
    }

#if UNITY_EDITOR
    private void SubscribeEditorUpdate()
    {
        if (Application.isPlaying || _subscribedEditorUpdate)
        {
            return;
        }

        EditorApplication.update += EditorUpdate;
        _subscribedEditorUpdate = true;
        _lastEditorTime = EditorApplication.timeSinceStartup;
    }

    private void UnsubscribeEditorUpdate()
    {
        if (!_subscribedEditorUpdate)
        {
            return;
        }

        EditorApplication.update -= EditorUpdate;
        _subscribedEditorUpdate = false;
    }

    private void EditorUpdate()
    {
        if (this == null || Application.isPlaying)
        {
            return;
        }

        // 组件被临时 disable 时不模拟，但保留 m_playInEditMode，OnEnable 后继续。
        if (!isActiveAndEnabled || !m_playInEditMode)
        {
            return;
        }

        if (_rootParticles == null || _rootParticles.Length == 0)
        {
            RefreshParticles();
            if (_rootParticles.Length == 0)
            {
                return;
            }
        }

        var now = EditorApplication.timeSinceStartup;
        var dt = (float)(now - _lastEditorTime);
        _lastEditorTime = now;

        if (dt <= 0f || dt > 0.25f)
        {
            dt = 1f / 60f;
        }

        for (var i = 0; i < _rootParticles.Length; i++)
        {
            var ps = _rootParticles[i];
            if (ps == null)
            {
                continue;
            }

            // restart=false 连续推进；fixedTimeStep=false 避免编辑态步进卡顿。
            // Simulate 之后系统会处于 Pause，必须每帧继续 Simulate。
            ps.Simulate(dt, true, false, false);
        }

        SceneView.RepaintAll();
    }

    private void RestartSimulation()
    {
        RefreshParticles();
        _lastEditorTime = EditorApplication.timeSinceStartup;

        for (var i = 0; i < _rootParticles.Length; i++)
        {
            var ps = _rootParticles[i];
            if (ps == null)
            {
                continue;
            }

            // 先清场，再从 0 模拟一小步，立刻可见粒子。
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Clear(true);
            ps.Play(true);
            ps.Simulate(Mathf.Epsilon, true, true, false);
        }

        SceneView.RepaintAll();
    }

    private void StopSimulation(bool clear)
    {
        if (_rootParticles == null || _rootParticles.Length == 0)
        {
            RefreshParticles();
        }

        for (var i = 0; i < _rootParticles.Length; i++)
        {
            var ps = _rootParticles[i];
            if (ps == null)
            {
                continue;
            }

            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (clear)
            {
                ps.Clear(true);
            }
        }

        SceneView.RepaintAll();
    }
#endif

    private void PlayRuntime()
    {
        RefreshParticles();
        for (var i = 0; i < _rootParticles.Length; i++)
        {
            var ps = _rootParticles[i];
            if (ps == null)
            {
                continue;
            }

            ps.Clear(true);
            ps.Play(true);
        }
    }

    private void StopRuntime(bool clear)
    {
        if (_rootParticles == null || _rootParticles.Length == 0)
        {
            RefreshParticles();
        }

        for (var i = 0; i < _rootParticles.Length; i++)
        {
            var ps = _rootParticles[i];
            if (ps == null)
            {
                continue;
            }

            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (clear)
            {
                ps.Clear(true);
            }
        }
    }
}
