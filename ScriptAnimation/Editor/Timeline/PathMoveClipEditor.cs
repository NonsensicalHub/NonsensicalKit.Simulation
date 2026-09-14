using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    [CustomEditor(typeof(PathMoveClip))]
    public class PathMoveClipEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Network"), new GUIContent("路网"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("StartNode"), new GUIContent("起点节点"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("EndNode"), new GUIContent("终点节点"), true);
            ClipDataInspectorGui.DrawChildren(serializedObject.FindProperty("Data"));
            serializedObject.ApplyModifiedProperties();

            var clipAsset = (PathMoveClip)target;
            Object binding = null;
            TimelineClip timelineClip = FindTimelineClip(clipAsset, out binding);
            var director = TimelineEditor.inspectedDirector;

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "时长请在 ScriptMovementTrack 上使用「按速度刷新全轨时长」。\n" +
                "需锁定目标世界旋转时，另建 ScriptDedicatedTrack 绑定任意物体上的 WorldRotationLockAnim（Target 指向要锁的节点），并铺 Lock Clip 与本 Clip 时间重叠。",
                MessageType.None);

            if (binding is PathMoveActor actor && clipAsset.Data != null && director != null)
            {
                var network = clipAsset.Network.Resolve(director);
                var start = clipAsset.StartNode.Resolve(director);
                var end = clipAsset.EndNode.Resolve(director);
                var points = new List<Vector3>(32);
                var pauses = new List<float>(32);
                if (PathMoveSampler.TryResolveWorldPoints(
                        network, start, end, clipAsset.Data, points, actor.PathOffset, pauses))
                {
                    float len = PathQuery.GetPolylineLength(points);
                    float pauseSum = PathMoveSampler.SumPauseDurations(pauses);
                    Quaternion incoming = ScriptAnimHomeResolver.ResolvePathMoveIncomingRotation(
                        timelineClip, actor, director, points,
                        clipAsset.Data.ReverseFacing);
                    float est = PathMoveSampler.EstimateDuration(
                        clipAsset.Data, actor, points, incoming, pauses);
                    PathMoveMode mode = PathMoveModeUtility.ResolveMode(clipAsset.Data, actor);
                    PathMoveSampler.TryGetPathEndPose(
                        points, out Vector3 endPos, out Quaternion endRot, actor,
                        clipAsset.Data.ReverseFacing, clipAsset.Data, incoming, pauses);
                    Quaternion pathStart = PathMoveSampler.GetPathStartRotation(
                        points, actor, clipAsset.Data.ReverseFacing);
                    float openAngle = Quaternion.Angle(incoming, pathStart);
                    string lockNote = "";
                    if (timelineClip != null &&
                        WorldRotationLockUtility.IsActiveDuringClip(timelineClip, director))
                        lockNote = "\nWorldRotationLock 重叠: 开";

                    string pauseNote = pauseSum > 1e-4f
                        ? $"停顿合计: {pauseSum:F2} s\n"
                        : "";

                    EditorGUILayout.HelpBox(
                        $"直线距离: {len:F2} m\n估算时长: {(est > 0f ? est.ToString("F3") : "-")} s\n" +
                        pauseNote +
                        $"Clip 速度: 移动 {clipAsset.Data.MoveSpeed:F2} m/s  旋转 {clipAsset.Data.RotateSpeed:F0} °/s\n" +
                        $"移动类型: {mode}\n" +
                        $"反向行驶: {(clipAsset.Data.ReverseFacing ? "开" : "关")}\n" +
                        $"开场转角: {openAngle:F1}°（前序结束→第一段）\n" +
                        $"终点: {endPos}  yaw≈{endRot.eulerAngles.y:F1}°{lockNote}\n" +
                        "t=0 固定在 StartNode；时长刷新请用 Track。",
                        MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "无法解析路径。请绑定 Network + StartNode + EndNode（Exposed Reference）。",
                        MessageType.Warning);
                }
            }
            else if (timelineClip != null && binding == null)
            {
                EditorGUILayout.HelpBox("Track 未绑定 PathMoveActor（或 ForkliftAnim）。", MessageType.Warning);
            }
        }

        private static TimelineClip FindTimelineClip(PathMoveClip asset, out Object binding)
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

    [CustomTimelineEditor(typeof(PathMoveClip))]
    public class PathMoveClipTimelineEditor : ClipEditor
    {
        // 路径移动 Clip 外观色
        private static readonly Color s_pathMoveColor = new Color(0.22f, 0.62f, 0.88f, 1f);

        public override ClipDrawOptions GetClipOptions(TimelineClip clip)
        {
            ClipDrawOptions options = base.GetClipOptions(clip);
            options.highlightColor = s_pathMoveColor;
            return options;
        }

        public override void OnCreate(TimelineClip clip, TrackAsset track, TimelineClip clonedFrom)
        {
            // 新建 Clip：若未指定起终点，则从前序 PathMove 继承（见 AddPathMoveClipAction）
            // 已有绑定则不覆盖 start，避免冲掉手动配置
            if (clip?.asset is not PathMoveClip pathClip || clonedFrom != null)
                return;

            var director = TimelineEditor.inspectedDirector;
            if (director == null)
                return;

            if (director.GetGenericBinding(track) is PathMoveActor actor && pathClip.Data != null)
            {
                Undo.RecordObject(pathClip, "Seed PathMoveClip Defaults");
                actor.ApplyClipDefaults(pathClip.Data);
                EditorUtility.SetDirty(pathClip);
            }

            // Network/Start 皆空时才 Seed 默认值
            if (pathClip.Network.Resolve(director) != null ||
                pathClip.StartNode.Resolve(director) != null)
                return;

            AddPathMoveClipAction.SeedFromPreviousPathMove(director, clip, pathClip);
        }

        public override void OnClipChanged(TimelineClip clip)
        {
            if (clip?.asset is not PathMoveClip pathClip || pathClip.Data == null)
                return;
            if (!pathClip.Data.AutoSyncDuration)
                return;

            ClipDurationSync.TryAutoSyncTrack(clip);
        }
    }
}
