using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace CineShot.Samples
{
    /// <summary>走动章节只沿前向位移，并播放跑步动画。不改旋转，也不改镜头。</summary>
    public sealed class DemoHeroMotion : MonoBehaviour
    {
        static readonly int k_Moving = Animator.StringToHash("Moving");

        [SerializeField] PlayableDirector m_Director;
        [SerializeField] Animator m_Animator;
        [SerializeField] RuntimeAnimatorController m_Controller;
        [SerializeField] float m_Speed = 1.25f;

        Vector3 m_Origin;
        double[] m_WalkStart = new double[0];
        double[] m_WalkEnd = new double[0];

        void Awake()
        {
            if (m_Director == null)
                m_Director = FindObjectOfType<PlayableDirector>();
            if (m_Animator == null)
                m_Animator = GetComponent<Animator>();
            m_Origin = transform.position;
            CacheWalkClips();
            if (m_Animator == null)
                return;
            m_Animator.applyRootMotion = false;
            if (m_Controller != null)
                m_Animator.runtimeAnimatorController = m_Controller;
            m_Animator.enabled = false;
        }

        void Update()
        {
            if (m_Director == null)
                return;
            double clipStart;
            bool moving = TryWalkClip(m_Director.time, out clipStart);
            var pos = m_Origin;
            if (moving)
                pos.z += (float)(m_Director.time - clipStart) * m_Speed;
            transform.position = pos;

            if (m_Animator == null || m_Animator.runtimeAnimatorController == null)
                return;
            if (m_Animator.enabled != moving)
                m_Animator.enabled = moving;
            if (moving)
                m_Animator.SetBool(k_Moving, true);
        }

        void CacheWalkClips()
        {
            var timeline = m_Director != null ? m_Director.playableAsset as TimelineAsset : null;
            if (timeline == null)
                return;
            var starts = new System.Collections.Generic.List<double>();
            var ends = new System.Collections.Generic.List<double>();
            foreach (var track in timeline.GetOutputTracks())
            {
                if (!(track is CineShot.Core.CineShotTrack))
                    continue;
                foreach (var clip in track.GetClips())
                {
                    if (clip.displayName == null || !clip.displayName.StartsWith("走动", System.StringComparison.Ordinal))
                        continue;
                    starts.Add(clip.start);
                    ends.Add(clip.end);
                }
            }
            m_WalkStart = starts.ToArray();
            m_WalkEnd = ends.ToArray();
        }

        bool TryWalkClip(double time, out double clipStart)
        {
            clipStart = 0d;
            bool found = false;
            for (int i = 0; i < m_WalkStart.Length; i++)
            {
                if (time < m_WalkStart[i] || time >= m_WalkEnd[i])
                    continue;
                if (!found || m_WalkStart[i] > clipStart)
                    clipStart = m_WalkStart[i];
                found = true;
            }
            return found;
        }
    }
}
