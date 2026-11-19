using System;
using UnityEngine;
using UnityEngine.Playables;

namespace CineShot.Core
{
    public enum CineState
    {
        Idle = 0,
        Playing = 1,
        BlendingOut = 2,
        PausedResumable = 3
    }

    public sealed class CinePlayer
    {
        const float DefaultBlend = 0.35f;

        readonly ICameraOutput m_FixedOutput;
        readonly PlayableDirector m_Director;
        readonly CineShotRig m_Rig;
        CineState m_State = CineState.Idle;
        float m_BlendRemaining;
        float m_BlendTotal;
        double m_ResumePoint;
        bool m_HasResume;
        ShotAsset m_CurrentShot;

        public event Action<ShotAsset, ShotAsset> OnShotChanged;
        public event Action<float> OnInterrupted;
        public event Action<ShotAsset> OnFinished;

        public CineState State { get { return m_State; } }
        public ShotAsset CurrentShot { get { return m_CurrentShot; } }
        public bool HasResume { get { return m_HasResume; } }
        public double ResumePoint { get { return m_ResumePoint; } }

        /// <summary>输出随 Rig 走（Rig 重建适配器后仍有效）。</summary>
        public CinePlayer(PlayableDirector director, CineShotRig rig)
        {
            m_Director = director;
            m_Rig = rig;
        }

        /// <summary>不依赖 Rig 的输出，测试或自定义输出用。</summary>
        public CinePlayer(ICameraOutput output, PlayableDirector director, CineShotRig rig)
            : this(director, rig)
        {
            m_FixedOutput = output;
        }

        ICameraOutput Output
        {
            get
            {
                if (m_FixedOutput != null)
                    return m_FixedOutput;
                return m_Rig != null ? m_Rig.Output : NullCameraOutput.Instance;
            }
        }

        /// <summary>从 startTime 开播。任何状态都可调用，会丢弃续播点；要接着打断处播用 <see cref="Resume"/>。</summary>
        public bool Play(ShotAsset shot, double startTime)
        {
            m_HasResume = false;
            EnterPlaying(shot, startTime);
            return true;
        }

        public void Stop(bool immediate)
        {
            if (m_State == CineState.Idle)
                return;
            if (!immediate)
            {
                BeginBlend(m_State == CineState.Playing ? DefaultBlend : 0f, false);
                return;
            }
            Output.SetControlling(false);
            if (m_Director != null)
                m_Director.Stop();
            m_HasResume = false;
            RaiseFinished();
            m_State = CineState.Idle;
        }

        public bool Interrupt(float blendSeconds)
        {
            if (m_State == CineState.Idle)
                return false;
            if (m_State == CineState.Playing)
            {
                if (m_Director != null)
                {
                    SaveResumePoint(m_Director.time);
                    m_Director.Pause();
                }
                RaiseInterrupted(blendSeconds);
            }
            BeginBlend(blendSeconds, true);
            return true;
        }

        public bool Resume()
        {
            if (m_State != CineState.PausedResumable || !m_HasResume)
                return false;
            EnterPlaying(m_CurrentShot, m_ResumePoint);
            return true;
        }

        public void SaveResumePoint(double directorTime)
        {
            m_ResumePoint = directorTime;
            m_HasResume = true;
        }

        public void Tick(float dt)
        {
            if (m_State == CineState.Playing && m_Rig != null)
            {
                var active = m_Rig.ActiveShot;
                if (active != m_CurrentShot)
                {
                    var old = m_CurrentShot;
                    m_CurrentShot = active;
                    RaiseShotChanged(old, m_CurrentShot);
                }
            }

            if (m_State != CineState.BlendingOut)
                return;
            m_BlendRemaining -= dt;
            float k = m_BlendTotal > 0f ? 1f - Mathf.Clamp01(m_BlendRemaining / m_BlendTotal) : 1f;
            if (m_Rig != null)
                m_Rig.BlendToGameCamera(k);
            if (m_BlendRemaining > 0f)
                return;

            Output.SetControlling(false);
            if (m_HasResume)
            {
                m_State = CineState.PausedResumable;
            }
            else
            {
                if (m_Director != null)
                    m_Director.Stop();
                RaiseFinished();
                m_State = CineState.Idle;
            }
        }

        void EnterPlaying(ShotAsset shot, double time)
        {
            var old = m_CurrentShot;
            if (shot != null)
                m_CurrentShot = shot;
            Output.SetControlling(true);
            if (m_Director != null)
            {
                m_Director.time = time;
                m_Director.Evaluate();
                m_Director.Play();
            }
            m_State = CineState.Playing;
            RaiseShotChanged(old, m_CurrentShot);
        }

        void BeginBlend(float seconds, bool keepResume)
        {
            m_BlendTotal = Mathf.Max(0f, seconds);
            m_BlendRemaining = m_BlendTotal;
            if (!keepResume)
                m_HasResume = false;
            if (m_Director != null && m_State == CineState.Playing)
                m_Director.Pause();
            m_State = CineState.BlendingOut;
            if (m_BlendTotal <= 0f)
                Tick(0f);
        }

        void RaiseShotChanged(ShotAsset oldShot, ShotAsset newShot)
        {
            var handlers = OnShotChanged;
            if (handlers == null) return;
            foreach (Action<ShotAsset, ShotAsset> h in handlers.GetInvocationList())
            {
                try { h(oldShot, newShot); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
        }

        void RaiseInterrupted(float sec)
        {
            var handlers = OnInterrupted;
            if (handlers == null) return;
            foreach (Action<float> h in handlers.GetInvocationList())
            {
                try { h(sec); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
        }

        void RaiseFinished()
        {
            var handlers = OnFinished;
            if (handlers == null) return;
            foreach (Action<ShotAsset> h in handlers.GetInvocationList())
            {
                try { h(m_CurrentShot); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
        }
    }
}
