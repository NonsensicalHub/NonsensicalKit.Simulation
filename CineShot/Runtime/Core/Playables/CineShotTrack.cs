using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace CineShot.Core
{
    [TrackClipType(typeof(CineShotClip))]
    [TrackBindingType(typeof(CineShotRig))]
    [TrackColor(0.15f, 0.45f, 0.75f)]
    public class CineShotTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            return ScriptPlayable<CineMixerBehaviour>.Create(graph, inputCount);
        }

        public override void GatherProperties(PlayableDirector director, IPropertyCollector driver)
        {
            if (director == null)
                return;
            var rig = director.GetGenericBinding(this) as CineShotRig;
            if (rig == null || rig.OutputVcam == null)
                return;
            var tr = rig.OutputVcam.transform;
            driver.AddFromName<Transform>(tr.gameObject, "m_LocalPosition");
            driver.AddFromName<Transform>(tr.gameObject, "m_LocalRotation");
        }
    }
}
