using UnityEngine;

namespace CineShot.Core
{
    public enum RigKind
    {
        [InspectorName("静态 Static")] Static = 0,
        [InspectorName("轨道跟拍 DollyTrack")] DollyTrack = 1,
        [InspectorName("环绕 Orbit")] Orbit = 2,
        [InspectorName("推拉 PushPull")] PushPull = 3,
        [InspectorName("摇臂 Crane")] Crane = 4,
        [InspectorName("过肩 Shoulder")] Shoulder = 5,
        [InspectorName("手持 Handheld")] Handheld = 6,
        [InspectorName("样条 Dolly SplineDolly")] SplineDolly = 7,
        [InspectorName("摇镜 Pan")] Pan = 8
    }

    public enum FocusMode
    {
        [InspectorName("无")] None = 0,
        [InspectorName("跟随目标")] Target = 1,
        [InspectorName("固定距离")] FixedDistance = 2,
        [InspectorName("超焦距")] Hyperfocal = 3
    }

    public enum ShotBlendMode
    {
        [InspectorName("硬切 Cut")] Cut = 0,
        [InspectorName("混合 Blend")] Blend = 1,
        [InspectorName("匹配帧 MatchFrame")] MatchFrame = 2
    }

    [System.Flags]
    public enum ShotOverrideMask
    {
        [InspectorName("无")] None = 0,
        [InspectorName("机位参数")] RigParams = 1 << 0,
        [InspectorName("构图")] Composition = 1 << 1,
        [InspectorName("光学（FOV/裁剪）")] Optics = 1 << 2,
        [InspectorName("焦点")] Focus = 1 << 3,
        [InspectorName("缓动")] Easing = 1 << 4,
        [InspectorName("优先级")] PriorityTags = 1 << 5,
        [InspectorName("目标偏移")] TargetOffset = 1 << 6,
        [InspectorName("手持层")] Handheld = 1 << 7,
        [InspectorName("匹配帧锚点")] MatchPoint = 1 << 8
    }
}
