using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    [CustomEditor(typeof(DirectMoveClip))]
    public class DirectMoveClipEditor : UnityEditor.Editor
    {
        private static readonly HashSet<string> s_endpointDataFields = new HashSet<string>
        {
            "UseStartWorldPosition",
            "StartWorldPosition",
            "UseEndWorldPosition",
            "EndWorldPosition"
        };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawEndpoint(
                "起点",
                serializedObject.FindProperty("StartNode"),
                serializedObject.FindProperty("Data.UseStartWorldPosition"),
                serializedObject.FindProperty("Data.StartWorldPosition"));
            DrawEndpoint(
                "终点",
                serializedObject.FindProperty("EndNode"),
                serializedObject.FindProperty("Data.UseEndWorldPosition"),
                serializedObject.FindProperty("Data.EndWorldPosition"));

            ClipDataInspectorGui.DrawChildren(
                serializedObject.FindProperty("Data"), s_endpointDataFields);
            serializedObject.ApplyModifiedProperties();

            var clipAsset = (DirectMoveClip)target;
            Object binding = null;
            TimelineClip timelineClip = FindTimelineClip(clipAsset, out binding);
            var director = TimelineEditor.inspectedDirector;

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "A→B 直线移动：不寻路、不旋转。\n" +
                "端点请绑定 ScriptAnimPoint（也可直接填写世界坐标）。\n" +
                "时长请在 ScriptAnim Track 上使用「按速度刷新全轨时长」。",
                MessageType.None);

            if (binding is PathMoveActor actor && clipAsset.Data != null && director != null)
            {
                var start = ScriptAnimPointUtility.AsTransform(
                    ScriptAnimPointUtility.Resolve(clipAsset.StartNode, director));
                var end = ScriptAnimPointUtility.AsTransform(
                    ScriptAnimPointUtility.Resolve(clipAsset.EndNode, director));
                if (DirectMoveSampler.TryResolveEndpoints(
                        start, end, clipAsset.Data, out Vector3 startPos, out Vector3 endPos, actor.PathOffset))
                {
                    float len = Vector3.Distance(startPos, endPos);
                    float est = DirectMoveSampler.EstimateDuration(
                        clipAsset.Data, actor, startPos, endPos);
                    Quaternion incoming = ScriptAnimHomeResolver.ResolveDirectMoveIncomingRotation(
                        timelineClip, actor, director);
                    EditorGUILayout.HelpBox(
                        $"直线距离: {len:F2} m\n估算时长: {(est > 0f ? est.ToString("F3") : "-")} s\n" +
                        $"Clip 速度: {clipAsset.Data.MoveSpeed:F2} m/s\n" +
                        $"朝向保持: yaw≈{incoming.eulerAngles.y:F1}°（全程不旋转）\n" +
                        $"起点: {FormatEndpoint(clipAsset.Data.UseStartWorldPosition, start, startPos)}\n" +
                        $"终点: {FormatEndpoint(clipAsset.Data.UseEndWorldPosition, end, endPos)}\n" +
                        "t=0 固定在起点；时长刷新请用 Track。",
                        MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "无法解析端点。请为起/终点绑定 ScriptAnimPoint，或勾选「用世界坐标」并填写坐标。",
                        MessageType.Warning);
                }
            }
            else if (timelineClip != null && binding == null)
            {
                EditorGUILayout.HelpBox("Track 未绑定 PathMoveActor（或 ForkliftAnim）。", MessageType.Warning);
            }
            else if (binding is ScriptAnimActor)
            {
                EditorGUILayout.HelpBox("DirectMoveClip 需要绑定 PathMoveActor（或 ForkliftAnim）。", MessageType.Warning);
            }
        }

        private static void DrawEndpoint(
            string title,
            SerializedProperty transformProp,
            SerializedProperty useWorldProp,
            SerializedProperty worldProp)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(useWorldProp, new GUIContent("使用世界坐标"));
            if (useWorldProp.boolValue)
                EditorGUILayout.PropertyField(worldProp, new GUIContent("世界坐标"));
            else
                EditorGUILayout.PropertyField(transformProp, new GUIContent("目标点位"), true);
            EditorGUILayout.Space(2f);
        }

        private static string FormatEndpoint(bool useWorld, Transform target, Vector3 resolved)
        {
            if (useWorld)
                return $"{resolved}（世界坐标）";
            if (target != null)
                return $"{resolved}（{target.name}）";
            return resolved.ToString();
        }

        private static TimelineClip FindTimelineClip(DirectMoveClip asset, out Object binding)
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

    [CustomTimelineEditor(typeof(DirectMoveClip))]
    public class DirectMoveClipTimelineEditor : ClipEditor
    {
        // 直线移动：青绿，区别于 PathMove 青蓝 / Teleport 琥珀
        private static readonly Color s_directMoveColor = new Color(0.28f, 0.78f, 0.55f, 1f);

        public override ClipDrawOptions GetClipOptions(TimelineClip clip)
        {
            ClipDrawOptions options = base.GetClipOptions(clip);
            options.highlightColor = s_directMoveColor;
            return options;
        }

        public override void OnCreate(TimelineClip clip, TrackAsset track, TimelineClip clonedFrom)
        {
            if (clip?.asset is not DirectMoveClip directClip || directClip.Data == null)
                return;
            if (clonedFrom != null)
                return;

            var director = TimelineEditor.inspectedDirector;
            if (director != null && director.GetGenericBinding(track) is PathMoveActor actor)
            {
                Undo.RecordObject(directClip, "Seed DirectMoveClip Defaults");
                actor.ApplyClipDefaults(directClip.Data);
                EditorUtility.SetDirty(directClip);
            }
        }

        public override void OnClipChanged(TimelineClip clip)
        {
            if (clip?.asset is not DirectMoveClip directClip || directClip.Data == null)
                return;
            if (!directClip.Data.AutoSyncDuration)
                return;

            ClipDurationSync.TryAutoSyncTrack(clip);
        }
    }
}
