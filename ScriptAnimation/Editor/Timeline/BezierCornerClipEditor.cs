using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    [CustomEditor(typeof(BezierCornerClip))]
    public class BezierCornerClipEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("PrevNode"), new GUIContent("上一节点"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("CornerNode"), new GUIContent("拐点节点"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("NextNode"), new GUIContent("下一节点"), true);
            ClipDataInspectorGui.DrawChildren(serializedObject.FindProperty("Data"));
            serializedObject.ApplyModifiedProperties();

            var clipAsset = (BezierCornerClip)target;
            Object binding = null;
            TimelineClip timelineClip = FindTimelineClip(clipAsset, out binding);
            var director = TimelineEditor.inspectedDirector;

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "路网拐点贝塞尔直角弯：Prev →Corner →Next。\n" +
                "开场位姿取前序 Clip 结束；直角弯用二次贝塞尔进 Corner，结束于出弯提前点。\n" +
                "长车体转弯请在本 Clip 前后用 PathMove 分段，勿再用 PathMove 受限转弯模式。",
                MessageType.None);

            if (binding is PathMoveActor actor && clipAsset.Data != null && director != null)
                DrawPreview(clipAsset, timelineClip, actor, director);
            else if (timelineClip != null && binding == null)
                EditorGUILayout.HelpBox("Track 未绑定 PathMoveActor（或 ForkliftAnim）。", MessageType.Warning);
        }

        static void DrawPreview(
            BezierCornerClip clipAsset,
            TimelineClip timelineClip,
            PathMoveActor actor,
            PlayableDirector director)
        {
            var prev = clipAsset.PrevNode.Resolve(director);
            var corner = clipAsset.CornerNode.Resolve(director);
            var next = clipAsset.NextNode.Resolve(director);
            float early = BezierCornerSampler.ResolveEarlyDistance(clipAsset.Data, actor);
            float est = -1f;
            string endLabel = "-";

            if (ManeuverHomeUtility.TryResolveIncomingPose(
                    timelineClip, actor, director, out Vector3 pos, out Quaternion rot) &&
                BezierCornerSampler.TryBuildPlan(
                    actor, clipAsset.Data, corner, prev, next, pos, rot, out var plan))
            {
                endLabel = $"{plan.EndPosition}  yaw≈{YawFromRotation(actor, plan.EndRotation):F1}°";
                est = BezierCornerSampler.EstimateDuration(
                    clipAsset.Data, actor, corner, prev, next, pos, rot);
            }

            EditorGUILayout.HelpBox(
                $"提前转弯: {early:F2} m\n" +
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

        static TimelineClip FindTimelineClip(BezierCornerClip asset, out Object binding)
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

    [CustomTimelineEditor(typeof(BezierCornerClip))]
    public class BezierCornerClipTimelineEditor : ClipEditor
    {
        static readonly Color s_color = new Color(0.35f, 0.75f, 0.45f, 1f);

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

            if (clip.asset is BezierCornerClip cornerClip && cornerClip.Data != null)
            {
                var director = TimelineEditor.inspectedDirector;
                if (director != null && director.GetGenericBinding(track) is PathMoveActor actor)
                {
                    Undo.RecordObject(cornerClip, "Seed BezierCornerClip Defaults");
                    actor.ApplyClipDefaults(cornerClip.Data);
                    EditorUtility.SetDirty(cornerClip);
                }
            }

            clip.displayName = "贝塞尔直角弯";
        }

        public override void OnClipChanged(TimelineClip clip)
        {
            if (clip?.asset is not BezierCornerClip asset || asset.Data == null)
                return;
            if (!asset.Data.AutoSyncDuration)
                return;
            ClipDurationSync.TryAutoSyncTrack(clip);
        }
    }
}
