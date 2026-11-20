using UnityEngine;
using UnityEngine.Playables;

namespace CineShot.Core
{
    /// <summary>场景里的播放入口：播放 / 打断 / 续播 / 停止，内部是 <see cref="CinePlayer"/> 状态机。</summary>
    [DisallowMultipleComponent]
    public sealed class CineShotPlayer : MonoBehaviour
    {
        [SerializeField] PlayableDirector m_Director;
        [SerializeField] CineShotRig m_Rig;
        [SerializeField] float m_DefaultInterruptBlend = 0.35f;

        CinePlayer m_Player;

        public CinePlayer Player { get { return EnsurePlayer(); } }
        public PlayableDirector Director { get { return m_Director; } }
        public CineShotRig Rig { get { return m_Rig; } }

        void Awake()
        {
            if (m_Director == null)
                m_Director = GetComponent<PlayableDirector>();
            if (m_Rig == null)
                m_Rig = GetComponent<CineShotRig>();
            EnsurePlayer();
        }

        CinePlayer EnsurePlayer()
        {
            if (m_Player == null)
                m_Player = new CinePlayer(m_Director, m_Rig);
            return m_Player;
        }

        void Update()
        {
            if (m_Player != null)
                m_Player.Tick(Time.deltaTime);
        }

        public bool Play(double startTime = 0.0)
        {
            if (m_Rig != null)
                m_Rig.RebuildOutputs();
            return EnsurePlayer().Play(null, startTime);
        }

        public void Stop(bool immediate = true)
        {
            EnsurePlayer().Stop(immediate);
        }

        public bool Interrupt(float blendSeconds = -1f)
        {
            if (blendSeconds < 0f)
                blendSeconds = m_DefaultInterruptBlend;
            return EnsurePlayer().Interrupt(blendSeconds);
        }

        public bool Resume()
        {
            return EnsurePlayer().Resume();
        }
    }
}
