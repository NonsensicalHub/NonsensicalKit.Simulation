using System.Linq;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 移动 / 设备轨：PathMove 族与取放货 Clip 可在同轨混排（按前序位姿链衔接）。
    /// 专用组件类 Clip 请用 <see cref="ScriptDedicatedTrack"/>。
    /// </summary>
    [TrackColor(0.35f, 0.8f, 0.55f)]
    [TrackClipType(typeof(PathMoveClip), false)]
    [TrackClipType(typeof(DirectMoveClip), false)]
    [TrackClipType(typeof(TeleportClip), false)]
    [TrackClipType(typeof(RotateClip), false)]
    [TrackClipType(typeof(ThreePointTurnClip), false)]
    [TrackClipType(typeof(BezierCornerClip), false)]
    [TrackClipType(typeof(BezierDualCornerClip), false)]
    [TrackClipType(typeof(ReverseUTurnClip), false)]
    [TrackClipType(typeof(ForkliftClip), false)]
    [TrackClipType(typeof(LatentAgvClip), false)]
    [TrackClipType(typeof(CtuClip), false)]
    [TrackClipType(typeof(ShuttleClip), false)]
    [TrackClipType(typeof(StackerClip), false)]
    [TrackClipType(typeof(StackerForkClip), false)]
    [TrackClipType(typeof(CommentClip), false)]
    [TrackBindingType(typeof(ScriptAnimActor))]
    public class ScriptMovementTrack : ScriptAnimTrackBase
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            var playable = ScriptPlayable<ScriptMovementMixerBehaviour>.Create(graph, inputCount);
            ScriptMovementMixerBehaviour mixer = playable.GetBehaviour();
            mixer.Director = go != null ? go.GetComponent<PlayableDirector>() : null;
            mixer.PostPlaybackState = m_PostPlaybackState;
            mixer.Clips = GetClips().ToArray();
            return playable;
        }
    }
}
