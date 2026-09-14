using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    [CustomEditor(typeof(StackerClip))]
    public class StackerClipEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            ClipDataInspectorGui.DrawChildren(serializedObject.FindProperty("Data"));
            serializedObject.ApplyModifiedProperties();

            var clipAsset = (StackerClip)target;
            Object binding = null;
            TimelineClip timelineClip = FindTimelineClip(clipAsset, out binding);

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "起终点为货位坐标（层/列/排/深），经 WarehouseManager 或 CellPositionTable 解析世界位置。\n" +
                "双轴各沿 StackerAnim 配置的单一世界方向同时加减速；改参后自动同步时长（也可 Track「按速度刷新」）。\n" +
                "HasStartCell=false 时：前序 StackerClip 终点 / Home。",
                MessageType.Info);

            if (binding is StackerAnim anim && clipAsset.Data != null)
            {
                if (anim.Warehouse == null && anim.CellTable == null)
                {
                    EditorGUILayout.HelpBox(
                        "StackerAnim 未指定 WarehouseManager 或 CellPositionTable，无法解析坐标。",
                        MessageType.Warning);
                    return;
                }

                var fallback = StackerSampler.TimelineStartFallback.None;
                if (!clipAsset.Data.HasStartCell && timelineClip != null)
                    fallback = ResolvePreviousEnd(anim, timelineClip);

                bool endOk = StackerSampler.TryResolveEndWorld(
                    anim, clipAsset.Data, out Vector3 endPos);
                bool startOk = StackerSampler.TryResolveStartWorld(
                    anim, clipAsset.Data, fallback, out Vector3 startPos);

                if (!endOk)
                {
                    EditorGUILayout.HelpBox(
                        $"无法解析终点 {StackerSampler.FormatCell(clipAsset.Data.EndCell)}",
                        MessageType.Warning);
                    return;
                }

                if (!startOk)
                {
                    EditorGUILayout.HelpBox("无法解析起点货位。", MessageType.Warning);
                    return;
                }

                var plans = StackerSampler.BuildPlans(anim, clipAsset.Data, startPos, endPos);
                Vector3 delta = endPos - startPos;
                float travelDist = Mathf.Abs(
                    SignedAxisUtil.GetComponent(delta, anim.TravelDirection));
                float liftDist = Mathf.Abs(
                    SignedAxisUtil.GetComponent(delta, anim.LiftDirection));
                string startLabel = clipAsset.Data.HasStartCell
                    ? StackerSampler.FormatCell(clipAsset.Data.StartCell)
                    : "（前序/Home）";

                EditorGUILayout.HelpBox(
                    $"起点 {startLabel} → {startPos}\n" +
                    $"终点 {StackerSampler.FormatCell(clipAsset.Data.EndCell)} → {endPos}\n" +
                    $"行走 [{anim.TravelDirection}] {travelDist:F2} m → {plans.Travel.Duration:F3} s（峰值 {plans.Travel.PeakSpeed:F2} m/s）\n" +
                    $"升降 [{anim.LiftDirection}] {liftDist:F2} m → {plans.Lift.Duration:F3} s（峰值 {plans.Lift.PeakSpeed:F2} m/s）\n" +
                    $"总时长 max = {plans.Duration:F3} s\n" +
                    $"仓库: {(anim.Warehouse != null ? anim.Warehouse.name : anim.CellTable != null ? anim.CellTable.name : "-")}",
                    MessageType.Info);
            }
            else if (timelineClip != null && binding is ScriptAnimActor)
            {
                EditorGUILayout.HelpBox("StackerClip 需要绑定 StackerAnim。", MessageType.Warning);
            }
        }

        private static StackerSampler.TimelineStartFallback ResolvePreviousEnd(
            StackerAnim anim, TimelineClip clip)
        {
            TrackAsset track = clip.GetParentTrack();
            if (track == null)
                return StackerSampler.TimelineStartFallback.None;

            double bestStart = double.NegativeInfinity;
            TimelineClip previous = null;
            foreach (TimelineClip other in track.GetClips())
            {
                if (other == null || other == clip || other.asset is not StackerClip)
                    continue;
                if (other.start >= clip.start)
                    continue;
                if (other.start > bestStart)
                {
                    bestStart = other.start;
                    previous = other;
                }
            }

            if (previous?.asset is not StackerClip prevAsset || prevAsset.Data == null)
                return StackerSampler.TimelineStartFallback.None;

            if (!StackerSampler.TryResolveEndWorld(anim, prevAsset.Data, out Vector3 endPos))
                return StackerSampler.TimelineStartFallback.None;

            return new StackerSampler.TimelineStartFallback(endPos);
        }

        private static TimelineClip FindTimelineClip(StackerClip asset, out Object binding)
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

    [CustomTimelineEditor(typeof(StackerClip))]
    public class StackerClipTimelineEditor : ClipEditor
    {
        public override void OnClipChanged(TimelineClip clip)
        {
            if (clip?.asset is not StackerClip stackerClip || stackerClip.Data == null)
                return;
            if (!stackerClip.Data.AutoSyncDuration)
                return;

            ClipDurationSync.TryAutoSyncTrack(clip);
        }
    }
}
