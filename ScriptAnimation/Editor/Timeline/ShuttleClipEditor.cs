using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    [CustomEditor(typeof(ShuttleClip))]
    public class ShuttleClipEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            ClipDataInspectorGui.DrawChildren(serializedObject.FindProperty("Data"));
            serializedObject.ApplyModifiedProperties();

            var clipAsset = (ShuttleClip)target;
            Object binding = null;
            TimelineClip timelineClip = FindTimelineClip(clipAsset, out binding);
            var director = TimelineEditor.inspectedDirector;

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "时长请在 ScriptAnim Track 上使用「按速度刷新全轨时长」。\n" +
                "伸出方向：只决定沿夹爪轴往左还是往右（右=正向，左=负向）。\n" +
                "左夹爪 / 右夹爪：决定动哪几只夹爪，可只开一侧或两侧同向伸出。\n" +
                "左夹紧 / 右夹紧：可只开一侧；未勾选或未绑定的一侧保持松开。\n" +
                "取货：夹紧(松→夹) → 侧伸 → 放拨爪(开→锁) → 收回。\n" +
                "放货：侧伸 → 抬拨爪(锁→开) → 收回 → 松开夹紧(夹→松)。",
                MessageType.None);

            if (binding is ShuttleAnim anim && clipAsset.Data != null && director != null)
            {
                var source = ScriptAnimHomeResolver.ResolveShuttleBodyHome(
                    timelineClip,
                    clipAsset,
                    anim,
                    director,
                    out Vector3 homePos,
                    out Quaternion homeRot,
                    out string sourceLabel);

                ScriptAnimHomeResolver.ResolveShuttleMechanismStart(
                    timelineClip,
                    clipAsset,
                    anim,
                    out float startClaw,
                    out float startClamp,
                    out float startPaddle,
                    out string mechanismLabel);

                float est = ShuttleSampler.EstimateDuration(
                    anim,
                    clipAsset.Data,
                    startClaw,
                    startClamp,
                    startPaddle);

                MessageType msgType = source == ScriptAnimHomeSource.Failed
                    ? MessageType.Warning
                    : MessageType.Info;

                string sideLabel = clipAsset.Data.Side == ShuttleSide.Left ? "左" : "右";
                string clawLabel =
                    (clipAsset.Data.UsesClawLeft(anim) ? "左" : "") +
                    (clipAsset.Data.UsesClawLeft(anim) && clipAsset.Data.UsesClawRight(anim) ? "+" : "") +
                    (clipAsset.Data.UsesClawRight(anim) ? "右" : "");
                if (string.IsNullOrEmpty(clawLabel))
                    clawLabel = "无";
                string clampLabel =
                    (clipAsset.Data.UsesClampLeft(anim) ? "左" : "") +
                    (clipAsset.Data.UsesClampLeft(anim) && clipAsset.Data.UsesClampRight(anim) ? "+" : "") +
                    (clipAsset.Data.UsesClampRight(anim) ? "右" : "");
                if (string.IsNullOrEmpty(clampLabel))
                    clampLabel = "无";
                EditorGUILayout.HelpBox(
                    $"开场车体 {sourceLabel}\n" +
                    $"开场机构 {mechanismLabel}\n" +
                    $"方向: {sideLabel}  伸出 {clipAsset.Data.SignedClawExtended:F3}\n" +
                    $"夹爪: {clawLabel}  夹紧: {clampLabel}\n" +
                    $"夹爪 {startClaw:F3}  夹紧 {startClamp:F3}  拨爪 {startPaddle:F1}°\n" +
                    $"起点: {homePos}  yaw≈{homeRot.eulerAngles.y:F1}°\n" +
                    $"估算时长: {(est > 0f ? est.ToString("F3") : "-")} s\n" +
                    $"夹爪 {clipAsset.Data.ClawSpeed:F2} m/s  夹紧 {clipAsset.Data.ClampSpeed:F2} m/s  " +
                    $"拨爪 {clipAsset.Data.PaddleSpeed:F0} °/s",
                    msgType);
            }
            else if (timelineClip != null && binding is ScriptAnimActor)
            {
                EditorGUILayout.HelpBox("ShuttleClip 需要绑定 ShuttleAnim（继承 PathMoveActor）。", MessageType.Warning);
            }
        }

        private static TimelineClip FindTimelineClip(ShuttleClip asset, out Object binding)
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

    [CustomTimelineEditor(typeof(ShuttleClip))]
    public class ShuttleClipTimelineEditor : ClipEditor
    {
        private static readonly Color s_pickUpColor = new Color(0.92f, 0.58f, 0.18f, 1f);
        private static readonly Color s_putDownColor = new Color(0.32f, 0.72f, 0.38f, 1f);

        public override ClipDrawOptions GetClipOptions(TimelineClip clip)
        {
            ClipDrawOptions options = base.GetClipOptions(clip);
            if (clip?.asset is ShuttleClip shuttle && shuttle.Data != null)
            {
                options.highlightColor = shuttle.Data.Mode == ForkliftMode.PickUp
                    ? s_pickUpColor
                    : s_putDownColor;
            }

            return options;
        }

        public override void OnClipChanged(TimelineClip clip)
        {
            if (clip?.asset is not ShuttleClip shuttleClip || shuttleClip.Data == null)
                return;
            if (!shuttleClip.Data.AutoSyncDuration)
                return;

            ClipDurationSync.TryAutoSyncTrack(clip);
        }
    }
}
