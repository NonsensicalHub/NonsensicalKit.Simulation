using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    [CustomEditor(typeof(ForkliftClip))]
    public class ForkliftClipEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Station"), new GUIContent("取放货点"), true);
            ClipDataInspectorGui.DrawChildren(serializedObject.FindProperty("Data"));
            serializedObject.ApplyModifiedProperties();

            var clipAsset = (ForkliftClip)target;
            Object binding = null;
            TimelineClip timelineClip = FindTimelineClip(clipAsset, out binding);
            var director = TimelineEditor.inspectedDirector;

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "时长请在 ScriptMovementTrack 上使用「按速度刷新全轨时长」。\n" +
                "开场旋转：无前序→组件 Home，瞬间转向（不计旋转时长）；\n" +
                "前序 PathMove/DirectMove/Rotate/机动/Teleport→取其结束位姿，按转角÷角速度计入旋转；\n" +
                "前序 Move（只移不转）→位置取该 Move 终点，朝向沿用更早确立的朝向再计入旋转；\n" +
                "前序取放货→不旋转。\n" +
                "四高度：空载 / 载货行驶 / 放货插入 / 载货抬起。\n" +
                "取货：空载→插入(停顿)→抬起→后退→载货行驶高度。\n" +
                "放货：载货行驶→抬起→前进→放置(停顿)→后退→空载。\n" +
                "反向行驶：车头背对货点倒车取放（与 PathMove 反向行驶同语义）。\n" +
                "CargoSwapHoldFrames：到达 Place 时停顿，供货物显隐切换（默认 5 帧 @60fps）。\n" +
                "锁定目标世界旋转：另建 ScriptDedicatedTrack，绑定任意物体上的 WorldRotationLockAnim（Target 指向要锁的节点），铺 Lock Clip 与移动 Clip 重叠。",
                MessageType.None);

            if (binding is ForkliftAnim anim && clipAsset.Data != null && director != null)
            {
                var station = ScriptAnimPointUtility.AsTransform(
                    ScriptAnimPointUtility.Resolve(clipAsset.Station, director));
                var source = ScriptAnimHomeResolver.Resolve(
                    timelineClip,
                    clipAsset,
                    anim,
                    director,
                    out Vector3 homePos,
                    out Quaternion homeRot,
                    out ForkliftRotateMode rotateMode,
                    out string sourceLabel);

                float est = ForkliftSampler.EstimateDuration(
                    anim, clipAsset.Data, station, homePos, homeRot, rotateMode);

                MessageType msgType = source == ScriptAnimHomeSource.Failed
                    ? MessageType.Warning
                    : MessageType.Info;

                string lockNote = timelineClip != null &&
                    WorldRotationLockUtility.IsActiveDuringClip(
                        timelineClip, TimelineEditor.inspectedDirector)
                    ? "开（锁定轨重叠）"
                    : "关";

                EditorGUILayout.HelpBox(
                    $"开场旋转: {sourceLabel}\n模式: {DescribeRotate(rotateMode)}\n" +
                    $"运动: 叉车 / {(clipAsset.Data.Mode == ForkliftMode.PickUp ? "取货" : "放货")}\n" +
                    $"反向行驶: {(clipAsset.Data.ReverseFacing ? "开" : "关")}\n" +
                    $"起点: {homePos}  yaw≈{homeRot.eulerAngles.y:F1}°\n" +
                    $"世界旋转锁定: {lockNote}\n" +
                    $"估算时长: {(est > 0f ? est.ToString("F3") : "-")} s\n" +
                    $"移动 {clipAsset.Data.MoveSpeed:F2} m/s  货叉 {clipAsset.Data.ForkSpeed:F2} m/s  " +
                    $"旋转 {clipAsset.Data.RotateSpeed:F0} °/s",
                    msgType);
            }
            else if (timelineClip != null && binding is ScriptAnimActor)
            {
                EditorGUILayout.HelpBox("ForkliftClip 需要绑定 ForkliftAnim（继承 PathMoveActor）。", MessageType.Warning);
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

        private static TimelineClip FindTimelineClip(ForkliftClip asset, out Object binding)
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

    [CustomTimelineEditor(typeof(ForkliftClip))]
    public class ForkliftClipTimelineEditor : ClipEditor
    {
        private static readonly Color s_pickUpColor = new Color(0.92f, 0.58f, 0.18f, 1f);
        private static readonly Color s_putDownColor = new Color(0.32f, 0.72f, 0.38f, 1f);

        public override ClipDrawOptions GetClipOptions(TimelineClip clip)
        {
            ClipDrawOptions options = base.GetClipOptions(clip);
            if (clip?.asset is ForkliftClip forklift && forklift.Data != null)
            {
                options.highlightColor = forklift.Data.Mode == ForkliftMode.PickUp
                    ? s_pickUpColor
                    : s_putDownColor;
            }

            return options;
        }

        public override void OnClipChanged(TimelineClip clip)
        {
            if (clip?.asset is not ForkliftClip forkliftClip || forkliftClip.Data == null)
                return;
            if (!forkliftClip.Data.AutoSyncDuration)
                return;

            ClipDurationSync.TryAutoSyncTrack(clip);
        }
    }
}
