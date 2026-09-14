using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    [CustomEditor(typeof(CtuClip))]
    public class CtuClipEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Station"), new GUIContent("取放货点"), true);
            ClipDataInspectorGui.DrawChildren(serializedObject.FindProperty("Data"));
            serializedObject.ApplyModifiedProperties();

            var clipAsset = (CtuClip)target;
            Object binding = null;
            TimelineClip timelineClip = FindTimelineClip(clipAsset, out binding);
            var director = TimelineEditor.inspectedDirector;

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "时长请在 ScriptAnim Track 上使用「按速度刷新全轨时长」。\n" +
                "CTU 机构：车 → 举升 → 旋转 → 夹爪 → 拨爪。\n" +
                "取货：升降到指定高度 → 旋转对准 → 侧伸 → 拨爪(开→锁) → 收回 → 旋转回正 →（可选）恢复默认高度。\n" +
                "放货：同上，拨爪改为抬起(锁→开)；显隐切换叠在拨爪旋转时间内，无额外停顿。\n" +
                "ReturnRotateToTravel / ReturnLiftToTravel：结束后是否回正；关闭时后续 CtuClip 从其结束转台/升降开场。",
                MessageType.None);

            if (binding is CtuAnim anim && clipAsset.Data != null && director != null)
            {
                var station = ScriptAnimPointUtility.AsTransform(
                    ScriptAnimPointUtility.Resolve(clipAsset.Station, director));
                var source = ScriptAnimHomeResolver.ResolveCtuBodyHome(
                    timelineClip,
                    clipAsset,
                    anim,
                    director,
                    out Vector3 homePos,
                    out Quaternion homeRot,
                    out string sourceLabel);

                ScriptAnimHomeResolver.ResolveCtuMechanismStart(
                    timelineClip,
                    clipAsset,
                    anim,
                    director,
                    out float startRotate,
                    out float startLift,
                    out float startClaw,
                    out float startPaddle,
                    out string mechanismLabel);

                float est = CtuSampler.EstimateDuration(
                    anim,
                    clipAsset.Data,
                    station,
                    homePos,
                    homeRot,
                    startRotate,
                    startLift,
                    startClaw,
                    startPaddle);

                MessageType msgType = source == ScriptAnimHomeSource.Failed
                    ? MessageType.Warning
                    : MessageType.Info;

                EditorGUILayout.HelpBox(
                    $"开场车体 {sourceLabel}\n" +
                    $"开场机构 {mechanismLabel}\n" +
                    $"转台 {startRotate:F1}°  升降 {startLift:F3}  夹爪 {startClaw:F3}\n" +
                    $"起点: {homePos}  yaw≈{homeRot.eulerAngles.y:F1}°\n" +
                    $"估算时长: {(est > 0f ? est.ToString("F3") : "-")} s\n" +
                    $"旋转 {clipAsset.Data.RotateSpeed:F0} °/s  升降 {clipAsset.Data.LiftSpeed:F2} m/s  " +
                    $"夹爪 {clipAsset.Data.ClawSpeed:F2} m/s  拨爪 {clipAsset.Data.PaddleSpeed:F0} °/s",
                    msgType);
            }
            else if (timelineClip != null && binding is ScriptAnimActor)
            {
                EditorGUILayout.HelpBox("CtuClip 需要绑定 CtuAnim（继承 PathMoveActor）。", MessageType.Warning);
            }
        }

        private static TimelineClip FindTimelineClip(CtuClip asset, out Object binding)
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

    [CustomTimelineEditor(typeof(CtuClip))]
    public class CtuClipTimelineEditor : ClipEditor
    {
        // 取货：琥珀；放货：草绿（与 Forklift 一致）
        private static readonly Color s_pickUpColor = new Color(0.92f, 0.58f, 0.18f, 1f);
        private static readonly Color s_putDownColor = new Color(0.32f, 0.72f, 0.38f, 1f);

        public override ClipDrawOptions GetClipOptions(TimelineClip clip)
        {
            ClipDrawOptions options = base.GetClipOptions(clip);
            if (clip?.asset is CtuClip ctu && ctu.Data != null)
            {
                options.highlightColor = ctu.Data.Mode == ForkliftMode.PickUp
                    ? s_pickUpColor
                    : s_putDownColor;
            }

            return options;
        }

        public override void OnClipChanged(TimelineClip clip)
        {
            if (clip?.asset is not CtuClip ctuClip || ctuClip.Data == null)
                return;
            if (!ctuClip.Data.AutoSyncDuration)
                return;

            ClipDurationSync.TryAutoSyncTrack(clip);
        }
    }
}
