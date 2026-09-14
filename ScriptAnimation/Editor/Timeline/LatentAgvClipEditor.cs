using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    [CustomEditor(typeof(LatentAgvClip))]
    public class LatentAgvClipEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            ClipDataInspectorGui.DrawChildren(serializedObject.FindProperty("Data"));
            serializedObject.ApplyModifiedProperties();

            var clipAsset = (LatentAgvClip)target;
            Object binding = null;
            TimelineClip timelineClip = FindTimelineClip(clipAsset, out binding);
            var director = TimelineEditor.inspectedDirector;

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "时长请在 ScriptMovementTrack 上使用「按速度刷新全轨时长」。\n" +
                "车体请先用 PathMove 移到货位下方；本 Clip 只做平台升降（无货点 / 接近距离）。\n" +
                "取货：Place→Lift；放货：Lift→Place。\n" +
                "CargoSwapHoldFrames：到达 Place 时停顿，供货物显隐切换（默认 5 帧@60fps）。\n" +
                "锁定目标世界旋转：另建 ScriptDedicatedTrack，绑定任意物体上的 WorldRotationLockAnim（Target 指向要锁的节点），铺 Lock Clip 与移动 Clip 重叠。",
                MessageType.None);

            if (binding is LatentAgvAnim anim && clipAsset.Data != null && director != null)
            {
                var source = ScriptAnimHomeResolver.Resolve(
                    timelineClip,
                    clipAsset,
                    anim,
                    director,
                    out Vector3 homePos,
                    out Quaternion homeRot,
                    out ForkliftRotateMode rotateMode,
                    out string sourceLabel);

                float est = LatentAgvSampler.EstimateDuration(anim, clipAsset.Data);

                MessageType msgType = source == ScriptAnimHomeSource.Failed
                    ? MessageType.Warning
                    : MessageType.Info;

                string lockNote = timelineClip != null &&
                    WorldRotationLockUtility.IsActiveDuringClip(
                        timelineClip, TimelineEditor.inspectedDirector)
                    ? "开（锁定轨重叠）"
                    : "关";

                EditorGUILayout.HelpBox(
                    $"开场: {sourceLabel}\n模式: {DescribeRotate(rotateMode)}\n" +
                    $"动作: 潜伏车 / {(clipAsset.Data.Mode == ForkliftMode.PickUp ? "取货" : "放货")}\n" +
                    $"起点: {homePos}  yaw≈{homeRot.eulerAngles.y:F1}°\n" +
                    $"世界旋转锁定: {lockNote}\n" +
                    $"估算时长: {(est > 0f ? est.ToString("F3") : "-")} s\n" +
                    $"平台 {clipAsset.Data.PlatformSpeed:F2} m/s",
                    msgType);
            }
            else if (timelineClip != null && binding is ScriptAnimActor)
            {
                EditorGUILayout.HelpBox("LatentAgvClip 需要绑定 LatentAgvAnim（继承 PathMoveActor）。", MessageType.Warning);
            }
        }

        private static string DescribeRotate(ForkliftRotateMode mode)
        {
            return mode switch
            {
                ForkliftRotateMode.Instant => "瞬间转向（不计时）",
                ForkliftRotateMode.Timed => "按角度计旋转时长",
                ForkliftRotateMode.Skip => "不旋转",
                _ => mode.ToString()
            };
        }

        private static TimelineClip FindTimelineClip(LatentAgvClip asset, out Object binding)
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

    [CustomTimelineEditor(typeof(LatentAgvClip))]
    public class LatentAgvClipTimelineEditor : ClipEditor
    {
        private static readonly Color s_pickUpColor = new Color(0.92f, 0.58f, 0.18f, 1f);
        private static readonly Color s_putDownColor = new Color(0.32f, 0.72f, 0.38f, 1f);

        public override ClipDrawOptions GetClipOptions(TimelineClip clip)
        {
            ClipDrawOptions options = base.GetClipOptions(clip);
            if (clip?.asset is LatentAgvClip latent && latent.Data != null)
            {
                options.highlightColor = latent.Data.Mode == ForkliftMode.PickUp
                    ? s_pickUpColor
                    : s_putDownColor;
            }

            return options;
        }

        public override void OnClipChanged(TimelineClip clip)
        {
            if (clip?.asset is not LatentAgvClip latentClip || latentClip.Data == null)
                return;
            if (!latentClip.Data.AutoSyncDuration)
                return;

            ClipDurationSync.TryAutoSyncTrack(clip);
        }
    }
}
