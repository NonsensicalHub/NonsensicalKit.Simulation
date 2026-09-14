using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    [CustomEditor(typeof(StackerForkClip))]
    public class StackerForkClipEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            ClipDataInspectorGui.DrawChildren(serializedObject.FindProperty("Data"));
            serializedObject.ApplyModifiedProperties();

            var clipAsset = (StackerForkClip)target;
            Object binding = null;
            FindTimelineClip(clipAsset, out binding);

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "机身停在当前货位，一级/二级货叉沿 StackerAnim 各自本地运动轴运动。\n" +
                "二级货叉未绑定时，Clip 中的二级参数会被忽略。\n" +
                "通常接在 StackerClip 到位之后；时长请用 Track「按速度刷新全轨时长」。",
                MessageType.Info);

            if (binding is StackerAnim anim && clipAsset.Data != null)
            {
                if (anim.PrimaryFork == null && anim.SecondaryFork == null)
                {
                    EditorGUILayout.HelpBox("StackerAnim 未指定一级或二级货叉。", MessageType.Warning);
                    return;
                }

                float est = StackerForkSampler.EstimateDuration(anim, clipAsset.Data);
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"模式: {(clipAsset.Data.Mode == ForkliftMode.PickUp ? "取货" : "放货")}");
                sb.AppendLine($"估算时长: {(est > 0f ? est.ToString("F3") : "-")} s");

                if (anim.PrimaryFork != null)
                {
                    sb.AppendLine(
                        $"一级货叉轴 {anim.PrimaryForkAxisLocal}  速度 {clipAsset.Data.ForkSpeed:F2} m/s");
                    sb.AppendLine(
                        $"  Travel={clipAsset.Data.ForkTravelOffset:F2}  " +
                        $"Place={clipAsset.Data.ForkPlaceOffset:F2}  " +
                        $"Lift={clipAsset.Data.ForkLiftOffset:F2}");
                }
                else
                {
                    sb.AppendLine("一级货叉: 未绑定");
                }

                if (anim.SecondaryFork != null)
                {
                    sb.AppendLine(
                        $"二级货叉轴 {anim.SecondaryForkAxisLocal}  速度 {clipAsset.Data.SecondaryForkSpeed:F2} m/s");
                    sb.AppendLine(
                        $"  Travel={clipAsset.Data.SecondaryForkTravelOffset:F2}  " +
                        $"Place={clipAsset.Data.SecondaryForkPlaceOffset:F2}  " +
                        $"Lift={clipAsset.Data.SecondaryForkLiftOffset:F2}");
                }
                else
                {
                    sb.AppendLine("二级货叉: 未绑定（Clip 二级参数忽略）");
                }

                EditorGUILayout.HelpBox(sb.ToString().TrimEnd(), MessageType.Info);
            }
            else if (binding is ScriptAnimActor)
            {
                EditorGUILayout.HelpBox("StackerForkClip 需要绑定 StackerAnim。", MessageType.Warning);
            }
        }

        private static TimelineClip FindTimelineClip(StackerForkClip asset, out Object binding)
        {
            binding = null;
            var director = TimelineEditor.inspectedDirector;
            var timeline = TimelineEditor.inspectedAsset;
            if (director == null || timeline == null || asset == null)
                return null;

            foreach (var track in timeline.GetOutputTracks())
            {
                if (track is not ScriptAnimTrackBase)
                    continue;

                foreach (var clip in track.GetClips())
                {
                    if (clip.asset != asset)
                        continue;
                    binding = director.GetGenericBinding(track);
                    return clip;
                }
            }

            return null;
        }
    }

    [CustomTimelineEditor(typeof(StackerForkClip))]
    public class StackerForkClipTimelineEditor : ClipEditor
    {
        private static readonly Color s_pickUpColor = new Color(0.55f, 0.72f, 0.95f, 1f);
        private static readonly Color s_putDownColor = new Color(0.45f, 0.55f, 0.88f, 1f);

        public override ClipDrawOptions GetClipOptions(TimelineClip clip)
        {
            ClipDrawOptions options = base.GetClipOptions(clip);
            if (clip?.asset is StackerForkClip fork && fork.Data != null)
            {
                options.highlightColor = fork.Data.Mode == ForkliftMode.PickUp
                    ? s_pickUpColor
                    : s_putDownColor;
            }

            return options;
        }

        public override void OnClipChanged(TimelineClip clip)
        {
            if (clip?.asset is not StackerForkClip forkClip || forkClip.Data == null)
                return;
            if (!forkClip.Data.AutoSyncDuration)
                return;

            ClipDurationSync.TryAutoSyncTrack(clip);
        }
    }
}
