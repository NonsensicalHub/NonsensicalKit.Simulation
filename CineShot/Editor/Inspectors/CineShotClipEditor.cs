using CineShot.Core;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Timeline;

namespace CineShot.Editor
{
    [CustomTimelineEditor(typeof(CineShotClip))]
    public sealed class CineShotClipEditor : ClipEditor
    {
        public override void DrawBackground(TimelineClip clip, ClipBackgroundRegion region)
        {
            var asset = clip.asset as CineShotClip;
            if (asset == null || asset.shot == null)
                return;
            var rect = region.position;
            rect.width = 48f;
            string mark = RigMark(asset.shot.rigKind);
            GUI.Label(rect, mark);
        }

        static string RigMark(RigKind kind)
        {
            switch (kind)
            {
                case RigKind.Static: return "静";
                case RigKind.DollyTrack: return "轨";
                case RigKind.Orbit: return "环";
                case RigKind.PushPull: return "推";
                case RigKind.Crane: return "吊";
                case RigKind.Shoulder: return "肩";
                case RigKind.Handheld: return "手";
                case RigKind.SplineDolly: return "线";
                case RigKind.Pan: return "摇";
                default: return "?";
            }
        }
    }
}
