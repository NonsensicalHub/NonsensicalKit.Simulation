using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    [CustomEditor(typeof(BezierDualCornerClip))]
    public class BezierDualCornerClipEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("PrevNode"), new GUIContent("上一节点"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("CornerNodeA"), new GUIContent("拐点 A"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("CornerNodeB"), new GUIContent("拐点 B"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("NextNode"), new GUIContent("下一节点"), true);
            ClipDataInspectorGui.DrawChildren(serializedObject.FindProperty("Data"));
            serializedObject.ApplyModifiedProperties();

            var clipAsset = (BezierDualCornerClip)target;
            Object binding = null;
            TimelineClip timelineClip = FindTimelineClip(clipAsset, out binding);
            var director = TimelineEditor.inspectedDirector;

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "路网双拐点贝塞尔弯：Prev → CornerA → CornerB → Next。\n" +
                "开场位姿取前序 Clip 结束；三次贝塞尔以两拐点为控制点，出弯后直线走到 Next。\n" +
                "「提前转弯距离」控制入弯点离拐点 A、出弯点离拐点 B 多远，不影响最终到达 Next。",
                MessageType.None);

            if (binding is PathMoveActor actor && clipAsset.Data != null && director != null)
                DrawPreview(clipAsset, timelineClip, actor, director);
            else if (timelineClip != null && binding == null)
                EditorGUILayout.HelpBox("Track 未绑定 PathMoveActor（或 ForkliftAnim）。", MessageType.Warning);
        }

        static void DrawPreview(
            BezierDualCornerClip clipAsset,
            TimelineClip timelineClip,
            PathMoveActor actor,
            PlayableDirector director)
        {
            var prev = clipAsset.PrevNode.Resolve(director);
            var cornerA = clipAsset.CornerNodeA.Resolve(director);
            var cornerB = clipAsset.CornerNodeB.Resolve(director);
            var next = clipAsset.NextNode.Resolve(director);
            float early = BezierDualCornerSampler.ResolveEarlyDistance(clipAsset.Data, actor);
            float est = -1f;
            string endLabel = "-";

            if (ManeuverHomeUtility.TryResolveIncomingPose(
                    timelineClip, actor, director, out Vector3 pos, out Quaternion rot) &&
                BezierDualCornerSampler.TryBuildPlan(
                    actor, clipAsset.Data, cornerA, cornerB, prev, next, pos, rot, out var plan))
            {
                endLabel = $"{plan.EndPosition}  yaw≈{YawFromRotation(actor, plan.EndRotation):F1}°";
                est = BezierDualCornerSampler.EstimateDuration(
                    clipAsset.Data, actor, cornerA, cornerB, prev, next, pos, rot);
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

        static TimelineClip FindTimelineClip(BezierDualCornerClip asset, out Object binding)
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

    [CustomTimelineEditor(typeof(BezierDualCornerClip))]
    public class BezierDualCornerClipTimelineEditor : ClipEditor
    {
        static readonly Color s_color = new Color(0.25f, 0.7f, 0.55f, 1f);

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

            if (clip.asset is BezierDualCornerClip dualClip && dualClip.Data != null)
            {
                var director = TimelineEditor.inspectedDirector;
                if (director != null && director.GetGenericBinding(track) is PathMoveActor actor)
                {
                    Undo.RecordObject(dualClip, "Seed BezierDualCornerClip Defaults");
                    actor.ApplyClipDefaults(dualClip.Data);
                    EditorUtility.SetDirty(dualClip);
                }
            }

            clip.displayName = "双拐点贝塞尔弯";
        }

        public override void OnClipChanged(TimelineClip clip)
        {
            if (clip?.asset is not BezierDualCornerClip asset || asset.Data == null)
                return;
            if (!asset.Data.AutoSyncDuration)
                return;
            ClipDurationSync.TryAutoSyncTrack(clip);
        }
    }
}
