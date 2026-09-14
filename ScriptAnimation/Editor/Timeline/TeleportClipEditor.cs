using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    [CustomEditor(typeof(TeleportClip))]
    public class TeleportClipEditor : UnityEditor.Editor
    {
        private static readonly HashSet<string> s_destinationDataFields = new HashSet<string>
        {
            "UseWorldPosition",
            "WorldPosition"
        };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDestination(
                serializedObject.FindProperty("TargetNode"),
                serializedObject.FindProperty("Data.UseWorldPosition"),
                serializedObject.FindProperty("Data.WorldPosition"));
            ClipDataInspectorGui.DrawChildren(
                serializedObject.FindProperty("Data"), s_destinationDataFields);
            serializedObject.ApplyModifiedProperties();

            var clipAsset = (TeleportClip)target;
            Object binding = null;
            TimelineClip timelineClip = FindTimelineClip(clipAsset, out binding);
            var director = TimelineEditor.inspectedDirector;

            EditorGUILayout.Space();
            int holdFrames = ScriptAnimTrackBase.ResolveInstantHoldFrames(timelineClip);
            EditorGUILayout.HelpBox(
                "瞬间落到目标路径节点或世界坐标，用于同一对象循环复用动画。\n" +
                "朝向模式可配：对齐节点 / 保持前序朝向 / 自定义偏航角。\n" +
                "使用世界坐标时，「对齐目标节点」会回退为保持前序朝向。\n" +
                $"占位 {holdFrames} 帧（ScriptAnimTrack「瞬间占位帧数」），可在 Timeline 上自由拖拽；仅首帧写入位姿。\n" +
                "勾选「自动同步时长」后，「按速度刷新全轨时长」会重置为轨配置的占位帧数。",
                MessageType.Info);

            if (binding is PathMoveActor rail && clipAsset.Data != null && director != null)
            {
                var targetNode = clipAsset.TargetNode.Resolve(director);
                Quaternion fallback = ScriptAnimHomeResolver.ResolveTeleportFallbackRotation(
                    timelineClip, rail, director);
                if (TeleportSampler.TryResolvePose(
                        targetNode, clipAsset.Data, fallback, out Vector3 pos, out Quaternion rot, rail))
                {
                    bool useWorld = clipAsset.Data.UseWorldPosition;
                    string facingLabel = clipAsset.Data.FacingMode switch
                    {
                        TeleportFacingMode.FaceNode when useWorld =>
                            $"世界坐标无节点朝向，回退前序/当前 yaw≈{rot.eulerAngles.y:F1}°",
                        TeleportFacingMode.FaceNode =>
                            $"对齐节点 yaw≈{rot.eulerAngles.y:F1}°",
                        TeleportFacingMode.KeepPrevious =>
                            $"保持前序/当前 yaw≈{rot.eulerAngles.y:F1}°",
                        TeleportFacingMode.CustomYaw =>
                            $"自定义 yaw={clipAsset.Data.CustomYawDegrees:F1}°",
                        _ => clipAsset.Data.FacingMode.ToString()
                    };
                    float fps = TeleportSampler.ResolveFrameRate(timelineClip);
                    int frames = TeleportSampler.ResolveHoldFrames(timelineClip);
                    double clipDur = timelineClip != null ? timelineClip.duration : 0;
                    EditorGUILayout.HelpBox(
                        $"落点: {FormatDestination(useWorld, targetNode, pos)}\n" +
                        $"朝向: {facingLabel}\n" +
                        $"当前时长: {clipDur:F3} s（约 {clipDur * fps:F0} 帧 @ {fps:F0} fps）\n" +
                        $"轨占位 {TeleportSampler.EstimateDuration(timelineClip):F3} s" +
                        $"（{frames} 帧，仅首帧生效）",
                        MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "无法解析目标。请绑定目标节点，或勾选「使用世界坐标」并填写坐标。",
                        MessageType.Warning);
                }
            }
            else if (binding is ScriptAnimActor)
            {
                EditorGUILayout.HelpBox("TeleportClip 需要绑定 PathMoveActor（或 ForkliftAnim）。", MessageType.Warning);
            }
            else if (binding == null)
            {
                EditorGUILayout.HelpBox("Track 未绑定 PathMoveActor（或 ForkliftAnim）。", MessageType.Warning);
            }
        }

        private static void DrawDestination(
            SerializedProperty nodeProp,
            SerializedProperty useWorldProp,
            SerializedProperty worldProp)
        {
            EditorGUILayout.LabelField("目标", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(useWorldProp, new GUIContent("使用世界坐标"));
            if (useWorldProp.boolValue)
                EditorGUILayout.PropertyField(worldProp, new GUIContent("世界坐标"));
            else
                EditorGUILayout.PropertyField(nodeProp, new GUIContent("目标节点"), true);
            EditorGUILayout.Space(2f);
        }

        private static string FormatDestination(bool useWorld, PathNode target, Vector3 resolved)
        {
            if (useWorld)
                return $"{resolved}（世界坐标）";
            if (target != null)
                return $"{resolved}（{target.name}）";
            return resolved.ToString();
        }

        private static TimelineClip FindTimelineClip(TeleportClip asset, out Object binding)
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

    [CustomTimelineEditor(typeof(TeleportClip))]
    public class TeleportClipTimelineEditor : ClipEditor
    {
        // 瞬移：琥珀橙，区别于 PathMove 青蓝
        private static readonly Color s_teleportColor = new Color(0.95f, 0.55f, 0.18f, 1f);

        public override ClipDrawOptions GetClipOptions(TimelineClip clip)
        {
            ClipDrawOptions options = base.GetClipOptions(clip);
            options.highlightColor = s_teleportColor;
            return options;
        }

        public override void OnCreate(TimelineClip clip, TrackAsset track, TimelineClip clonedFrom)
        {
            if (clip == null)
                return;

            // 克隆保留原时长；新建用轨配置的占位帧数
            if (clonedFrom != null)
                return;

            ClipDurationSync.SetDuration(clip, TeleportSampler.EstimateDuration(clip));
        }

        public override void OnClipChanged(TimelineClip clip)
        {
            if (clip?.asset is not TeleportClip teleportClip || teleportClip.Data == null)
                return;
            if (!teleportClip.Data.AutoSyncDuration)
                return;

            ClipDurationSync.TryAutoSyncTrack(clip);
        }
    }
}
