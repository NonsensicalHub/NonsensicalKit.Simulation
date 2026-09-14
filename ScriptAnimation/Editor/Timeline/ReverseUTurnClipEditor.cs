using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    [CustomEditor(typeof(ReverseUTurnClip))]
    public class ReverseUTurnClipEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("PrevNode"), new GUIContent("上一节点"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("CornerNode"), new GUIContent("拐点节点"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("NextNode"), new GUIContent("下一节点"), true);
            ClipDataInspectorGui.DrawChildren(serializedObject.FindProperty("Data"));
            serializedObject.ApplyModifiedProperties();

            var clipAsset = (ReverseUTurnClip)target;
            Object binding = null;
            TimelineClip timelineClip = FindTimelineClip(clipAsset, out binding);
            var director = TimelineEditor.inspectedDirector;

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "路网拐点倒车掉头：到 Corner 后倒车旋转，再贝塞尔出弯至 Next 方向提前点。\n" +
                "适用于夹角>135° 或需倒车对准下一段的场景。",
                MessageType.None);

            if (binding is PathMoveActor actor && clipAsset.Data != null && director != null)
                DrawPreview(clipAsset, timelineClip, actor, director);
            else if (timelineClip != null && binding == null)
                EditorGUILayout.HelpBox("Track 未绑定 PathMoveActor（或 ForkliftAnim）。", MessageType.Warning);
        }

        static void DrawPreview(
            ReverseUTurnClip clipAsset,
            TimelineClip timelineClip,
            PathMoveActor actor,
            PlayableDirector director)
        {
            var prev = clipAsset.PrevNode.Resolve(director);
            var corner = clipAsset.CornerNode.Resolve(director);
            var next = clipAsset.NextNode.Resolve(director);
            float back = ReverseUTurnSampler.ResolveBackDistance(clipAsset.Data, actor);
            float early = ReverseUTurnSampler.ResolveEarlyDistance(clipAsset.Data, actor);
            float est = -1f;
            string endLabel = "-";

            if (ManeuverHomeUtility.TryResolveIncomingPose(
                    timelineClip, actor, director, out Vector3 pos, out Quaternion rot) &&
                ReverseUTurnSampler.TryBuildPlan(
                    actor, clipAsset.Data, corner, prev, next, pos, rot, out var plan))
            {
                endLabel = $"{plan.EndPosition}  yaw≈{YawFromRotation(actor, plan.EndRotation):F1}°";
                est = ReverseUTurnSampler.EstimateDuration(
                    clipAsset.Data, actor, corner, prev, next, pos, rot);
            }

            EditorGUILayout.HelpBox(
                $"后退: {back:F2} m  提前转弯: {early:F2} m\n" +
                $"估算时长: {(est > 0f ? est.ToString("F3") : "-")} s\n" +
                $"结束: {endLabel}\n时长刷新请用 Track。",
                MessageType.Info);
        }

        static float YawFromRotation(PathMoveActor actor, Quaternion rotation)
        {
            Vector3 flatFwd = actor.Flatten(rotation * actor.Forward);
            if (flatFwd.sqrMagnitude < 1e-8f)
                return rotation.eulerAngles.y;
            return Mathf.Atan2(flatFwd.x, flatFwd.z) * Mathf.Rad2Deg;
        }

        static TimelineClip FindTimelineClip(ReverseUTurnClip asset, out Object binding)
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

    [CustomTimelineEditor(typeof(ReverseUTurnClip))]
    public class ReverseUTurnClipTimelineEditor : ClipEditor
    {
        static readonly Color s_color = new Color(0.55f, 0.35f, 0.82f, 1f);

        public override ClipDrawOptions GetClipOptions(TimelineClip clip)
        {
            ClipDrawOptions options = base.GetClipOptions(clip);
            options.highlightColor = s_color;
            return options;
        }

        public override void OnCreate(TimelineClip clip, TrackAsset track, TimelineClip clonedFrom)
        {
            if (clip == null || clonedFrom != null)
                return;

            if (clip.asset is ReverseUTurnClip uturnClip && uturnClip.Data != null)
            {
                var director = TimelineEditor.inspectedDirector;
                if (director != null && director.GetGenericBinding(track) is PathMoveActor actor)
                {
                    Undo.RecordObject(uturnClip, "Seed ReverseUTurnClip Defaults");
                    actor.ApplyClipDefaults(uturnClip.Data);
                    EditorUtility.SetDirty(uturnClip);
                }
            }

            clip.displayName = "倒车掉头";
        }

        public override void OnClipChanged(TimelineClip clip)
        {
            if (clip?.asset is not ReverseUTurnClip asset || asset.Data == null)
                return;
            if (!asset.Data.AutoSyncDuration)
                return;
            ClipDurationSync.TryAutoSyncTrack(clip);
        }
    }
}
