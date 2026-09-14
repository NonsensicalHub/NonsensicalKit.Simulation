using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    [CustomEditor(typeof(ThreePointTurnClip))]
    public class ThreePointTurnClipEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            ClipDataInspectorGui.DrawChildren(serializedObject.FindProperty("Data"));
            serializedObject.ApplyModifiedProperties();

            var clipAsset = (ThreePointTurnClip)target;
            Object binding = null;
            TimelineClip timelineClip = FindTimelineClip(clipAsset, out binding);
            var director = TimelineEditor.inspectedDirector;

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "窄道三点转向：只配左转右转。开场朝向取前序路径结束朝向。\n" +
                "右转：先沿车头前进，再向左侧倒车转 90° 对准右侧目标，然后前进回到开场位置。\n" +
                "用于路口宽度不够、无法原地掉头的场景。后续 PathMove / 取放货从转正后的开场点继续。\n" +
                "时长请在 ScriptAnim Track 上使用「按速度刷新全轨时长」。",
                MessageType.None);

            if (binding is PathMoveActor actor && clipAsset.Data != null)
            {
                DrawPreview(clipAsset, timelineClip, actor, director);
            }
            else if (timelineClip != null && binding == null)
            {
                EditorGUILayout.HelpBox("Track 未绑定 PathMoveActor（或 ForkliftAnim）。", MessageType.Warning);
            }
            else if (binding is ScriptAnimActor)
            {
                EditorGUILayout.HelpBox("ThreePointTurnClip 需要绑定 PathMoveActor（或 ForkliftAnim）。", MessageType.Warning);
            }
        }

        static void DrawPreview(
            ThreePointTurnClip clipAsset,
            TimelineClip timelineClip,
            PathMoveActor actor,
            PlayableDirector director)
        {
            float d = ThreePointTurnSampler.ResolveDistance(clipAsset.Data, actor);
            float moveSpeed = ThreePointTurnSampler.ResolveMoveSpeed(clipAsset.Data, actor);
            float rotSpeed = ThreePointTurnSampler.ResolveRotateSpeed(clipAsset.Data, actor);
            float est = ThreePointTurnSampler.EstimateDuration(clipAsset.Data, actor);
            string dirLabel = clipAsset.Data.Direction == ThreePointTurnDirection.Left ? "左转" : "右转";

            string poseLabel = "当前 Body";
            string endLabel = "-";
            if (director != null &&
                ScriptAnimHomeResolver.TryResolveThreePointTurnIncomingPose(
                    timelineClip, actor, director,
                    out Vector3 pos, out Quaternion rot) &&
                ThreePointTurnSampler.TryBuildPlan(
                    actor, clipAsset.Data, pos, rot, out var plan))
            {
                poseLabel = $"{pos}  yaw≈{YawFromRotation(actor, rot):F1}°";
                endLabel =
                    $"{plan.P3}  yaw≈{YawFromRotation(actor, plan.RotEnd):F1}°";
            }

            string lockNote = "";
            if (timelineClip != null &&
                WorldRotationLockUtility.IsActiveDuringClip(
                    timelineClip, TimelineEditor.inspectedDirector))
                lockNote = "\nWorldRotationLock 重叠: 开";

            EditorGUILayout.HelpBox(
                $"方向: {dirLabel}（0°）\n" +
                $"机动距离: {d:F2} m\n" +
                $"移动速度: {moveSpeed:F2} m/s\n" +
                $"倒车转向: {rotSpeed:F1} °/s\n" +
                $"估算时长: {(est > 0f ? est.ToString("F3") : "-")} s\n" +
                $"开场 {poseLabel}\n" +
                $"结束: {endLabel}（回到开场点，朝向已转正）{lockNote}\n" +
                "时长刷新请用 Track。",
                MessageType.Info);
        }

        static float YawFromRotation(PathMoveActor actor, Quaternion rotation)
        {
            Vector3 flatFwd = actor.Flatten(rotation * actor.Forward);
            if (flatFwd.sqrMagnitude < 1e-8f)
                return rotation.eulerAngles.y;
            return Mathf.Atan2(flatFwd.x, flatFwd.z) * Mathf.Rad2Deg;
        }

        static TimelineClip FindTimelineClip(ThreePointTurnClip asset, out Object binding)
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

    [CustomTimelineEditor(typeof(ThreePointTurnClip))]
    public class ThreePointTurnClipTimelineEditor : ClipEditor
    {
        // 三点转向：暖橙，区别于 PathMove 青蓝 / DirectMove 青绿 / Rotate 紫红 / Teleport 琥珀
        static readonly Color s_turnColor = new Color(0.95f, 0.55f, 0.22f, 1f);

        public override ClipDrawOptions GetClipOptions(TimelineClip clip)
        {
            ClipDrawOptions options = base.GetClipOptions(clip);
            options.highlightColor = s_turnColor;
            return options;
        }

        public override void OnCreate(TimelineClip clip, TrackAsset track, TimelineClip clonedFrom)
        {
            if (clip == null || clonedFrom != null)
                return;

            if (clip.asset is ThreePointTurnClip turnClip && turnClip.Data != null)
            {
                var director = TimelineEditor.inspectedDirector;
                if (director != null && director.GetGenericBinding(track) is PathMoveActor actor)
                {
                    Undo.RecordObject(turnClip, "Seed ThreePointTurnClip Defaults");
                    actor.ApplyClipDefaults(turnClip.Data);
                    EditorUtility.SetDirty(turnClip);
                }
            }

            UpdateDisplayName(clip);
        }

        public override void OnClipChanged(TimelineClip clip)
        {
            if (clip?.asset is not ThreePointTurnClip turnClip || turnClip.Data == null)
                return;

            UpdateDisplayName(clip);

            if (!turnClip.Data.AutoSyncDuration)
                return;

            ClipDurationSync.TryAutoSyncTrack(clip);
        }

        static void UpdateDisplayName(TimelineClip clip)
        {
            if (clip?.asset is not ThreePointTurnClip turnClip || turnClip.Data == null)
                return;

            clip.displayName = turnClip.Data.Direction == ThreePointTurnDirection.Left
                ? "三点转向 · 左转"
                : "三点转向 · 右转";
        }
    }
}
