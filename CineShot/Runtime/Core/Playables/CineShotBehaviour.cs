using UnityEngine;
using UnityEngine.Playables;

namespace CineShot.Core
{
    public class CineShotBehaviour : PlayableBehaviour
    {
        CineShotClip m_Clip;

        public CineShotClip Clip { get { return m_Clip; } }

        public void Setup(CineShotClip clip)
        {
            m_Clip = clip;
        }
    }
}
