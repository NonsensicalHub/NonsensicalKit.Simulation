using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    [CustomEditor(typeof(RotateClip))]
    public class RotateClipEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            ClipDataInspectorGui.DrawChildren(serializedObject.FindProperty("Data"));
            serializedObject.ApplyModifiedProperties();

            var clipAsset = (RotateClip)target;
            Object binding = null;
            TimelineClip timelineClip = FindTimelineClip(clipAsset, out binding);
            var director = TimelineEditor.inspectedDirector;

            EditorGUILayout.Space();
            string modeHint = clipAsset.Data != null && clipAsset.Data.PositionMode == RotatePositionMode.RotationOnly
                ? "仅旋转：不写位置，只按起始偏航角与进度更新朝向。\n"
                : "原地转圈：位置保持开场落点；朝向由「起始偏航角 + 方向×角度 × Clip 进度」直接算出。\n";
            EditorGUILayout.HelpBox(
                modeHint +
                "360° 为一圈，可填 720° 转两圈。顺时针/逆时针为俯视方向。\n" +
                "时长请在 ScriptAnim Track 上使用「按速度刷新全轨时长」。",
                MessageType.None);

            if (binding is PathMoveActor actor && clipAsset.Data != null)
            {
                DrawCaptureStartYaw(clipAsset, timelineClip, actor, director);

                float speed = RotateSampler.ResolveSpeed(clipAsset.Data, actor);
                float signed = RotateSampler.GetSignedAngle(clipAsset.Data);
                float est = RotateSampler.EstimateDuration(clipAsset.Data, actor);
                float startYaw = clipAsset.Data.StartYawDegrees;
                float endYaw = RotateSampler.EvaluateYaw(clipAsset.Data, 1f);
                bool holdsPosition = RotateSampler.HoldsPosition(clipAsset.Data);

                string posLabel = holdsPosition ? "当前 Body" : "不写入（保持运行时位置）";
                if (holdsPosition &&
                    director != null &&
                    ScriptAnimHomeResolver.TryResolveRotateIncomingPose(
                        timelineClip, actor, director,
                        out Vector3 pos, out _))
                {
                    posLabel = $"前序终点 {pos}（全程锁定）";
                }

                EditorGUILayout.HelpBox(
                    $"位置模式: {(holdsPosition ? "原地旋转（维持位置）" : "仅旋转（不写入位置）")}\n" +
                    $"旋转: {Mathf.Abs(signed):F1}°（{(signed >= 0f ? "顺时针" : "逆时针")}）\n" +
                    $"角速度: {speed:F1} °/s\n" +
                    $"估算时长: {(est > 0f ? est.ToString("F3") : "-")} s\n" +
                    $"位置: {posLabel}\n" +
                    $"朝向: yaw {startYaw:F1}° → {endYaw:F1}°（由起始偏航角 + 进度）\n" +
                    (Mathf.Abs(signed) >= 359f && Mathf.Abs(signed) % 360f < 1f
                        ? "整圈结束朝向与进入时相同。\n"
                        : "") +
                    "时长刷新请用 Track。",
                    MessageType.Info);
            }
            else if (timelineClip != null && binding == null)
            {
                EditorGUILayout.HelpBox("Track 未绑定 PathMoveActor（或 ForkliftAnim）。", MessageType.Warning);
            }
            else if (binding is ScriptAnimActor)
            {
                EditorGUILayout.HelpBox("RotateClip 需要绑定 PathMoveActor（或 ForkliftAnim）。", MessageType.Warning);
            }
        }

        static void DrawCaptureStartYaw(
            RotateClip clipAsset,
            TimelineClip timelineClip,
            PathMoveActor actor,
            PlayableDirector director)
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("从前序结束捕获起始偏航角"))
            {
                float yaw = ResolveCaptureYaw(timelineClip, actor, director, preferPrevious: true);
                Undo.RecordObject(clipAsset, "捕获起始偏航角");
                clipAsset.Data.StartYawDegrees = yaw;
                EditorUtility.SetDirty(clipAsset);
            }

            if (GUILayout.Button("从当前 Body 捕获"))
            {
                float yaw = YawFromRotation(actor, actor.Body.rotation);
                Undo.RecordObject(clipAsset, "捕获起始偏航角");
                clipAsset.Data.StartYawDegrees = yaw;
                EditorUtility.SetDirty(clipAsset);
            }

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 新建 Clip 时写入起始偏航：优先前序结束 yaw，否则当前 Body。
    /// </summary>
        public static void TrySeedStartYaw(
            RotateClip clipAsset,
            TimelineClip timelineClip,
            PathMoveActor actor,
            PlayableDirector director)
        {
            if (clipAsset?.Data == null || actor == null)
                return;

            clipAsset.Data.StartYawDegrees = ResolveCaptureYaw(
                timelineClip, actor, director, preferPrevious: true);
            EditorUtility.SetDirty(clipAsset);
        }

        static float ResolveCaptureYaw(
            TimelineClip timelineClip,
            PathMoveActor actor,
            PlayableDirector director,
            bool preferPrevious)
        {
            if (preferPrevious &&
                director != null &&
                ScriptAnimHomeResolver.TryFindPreviousClip(timelineClip, out TimelineClip previous) &&
                ScriptAnimHomeResolver.TryResolveClipExitPose(
                    previous, actor, director, out _, out Quaternion exitRot))
            {
                return YawFromRotation(actor, exitRot);
            }

            return YawFromRotation(actor, actor.Body.rotation);
        }

        static float YawFromRotation(PathMoveActor actor, Quaternion rotation)
        {
            Vector3 flatFwd = actor.Flatten(rotation * actor.Forward);
            if (flatFwd.sqrMagnitude < 1e-8f)
                return rotation.eulerAngles.y;
            return Mathf.Atan2(flatFwd.x, flatFwd.z) * Mathf.Rad2Deg;
        }

        static TimelineClip FindTimelineClip(RotateClip asset, out Object binding)
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

    [CustomTimelineEditor(typeof(RotateClip))]
    public class RotateClipTimelineEditor : ClipEditor
    {
        // 原地旋转：紫红，区别于 PathMove 青蓝 / DirectMove 青绿 / Teleport 琥珀
        static readonly Color s_rotateColor = new Color(0.78f, 0.38f, 0.82f, 1f);

        public override ClipDrawOptions GetClipOptions(TimelineClip clip)
        {
            ClipDrawOptions options = base.GetClipOptions(clip);
            options.highlightColor = s_rotateColor;
            return options;
        }

        public override void OnCreate(TimelineClip clip, TrackAsset track, TimelineClip clonedFrom)
        {
            if (clip?.asset is not RotateClip rotateClip || rotateClip.Data == null)
                return;
            if (clonedFrom != null)
                return;

            var director = TimelineEditor.inspectedDirector;
            Object binding = director != null ? director.GetGenericBinding(track) : null;
            if (binding is PathMoveActor actor)
            {
                Undo.RecordObject(rotateClip, "Seed RotateClip Defaults");
                actor.ApplyClipDefaults(rotateClip.Data);
                EditorUtility.SetDirty(rotateClip);
                RotateClipEditor.TrySeedStartYaw(rotateClip, clip, actor, director);
            }
        }

        public override void OnClipChanged(TimelineClip clip)
        {
            if (clip?.asset is not RotateClip rotateClip || rotateClip.Data == null)
                return;
            if (!rotateClip.Data.AutoSyncDuration)
                return;

            ClipDurationSync.TryAutoSyncTrack(clip);
        }
    }
}
