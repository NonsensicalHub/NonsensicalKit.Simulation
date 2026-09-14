using System;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 姿态改变 Clip 参数：按名称选目标姿态，在 Clip 时长内从进入时位姿插值到位。
    /// 默认可拖拽 Clip 控制时长；勾选 AutoSyncDuration 后按 DurationSeconds 回写 Clip。
    /// DurationSeconds 为0 时瞬间改变；Timeline 上仍占所层 <see cref="ScriptAnimTrackBase.InstantHoldFrames"/> 帧以便选中。
    /// </summary>
    [Serializable]
    public class PoseChangeClipData
    {
        [Tooltip("目标姿态名称，对应 PoseChangeAnim / PoseChangeAnimMax 姿态列表中的命名")]
        [InspectorLabel("目标姿态")]
        public string PoseName;

        [Min(0f)]
        [Tooltip("自动同步开启时的目标用时（秒）； 表示瞬间改变。关闭自动同步时由 Clip 长度决定插值时长。")]
        [InspectorLabel("目标用时（秒）")]
        public float DurationSeconds = 1f;

        [Tooltip("归一化时间 → 插值权重；默认线性")]
        [InspectorLabel("缓动曲线")]
        public AnimationCurve Ease = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Tooltip("开启后「按速度刷新全轨时长」会按目标用时回写 Clip 长度（用时为 0 时写为占位帧数）；关闭则可拖拽 Clip 控制时长")]
        [InspectorLabel("自动同步时长")]
        public bool AutoSyncDuration;
    }
}
