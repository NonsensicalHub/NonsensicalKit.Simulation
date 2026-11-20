using System;
using NonsensicalKit.ScriptAnimation;
using UnityEngine;
using NaughtyAttributes;

namespace HikvisionAgv
{
    /// <summary>Status=6（举升货架）时货叉动作方向（目标高度由挡位下标决定）。</summary>
    public enum ForkLiftCommand
    {
        [InspectorName("抬升")]
        Raise = 0,

        [InspectorName("下降")]
        Lower = 1
    }

    /// <summary>
    /// 路网点位行为：可显式配置原地旋转、货叉升降等。
    /// 与点位映射独立，挂在模块袋里，避免被地图重导入覆盖。
    /// </summary>
    [Serializable]
    [PathNodeFeatureMenu("海康/点位行为")]
    public sealed class HikvisionNodeBehaviorFeature : IPathNodeFeature
    {
        [ReadOnly] public string FeatureName = "点位行为";
        [Tooltip("勾选后该点可作为原地转向停靠点；AGV「先到可旋转点再转」只会匹配勾选了此项的点")]
        public bool AllowInPlaceRotate;

        [Tooltip(
            "旋转钳制距离（米）。AGV 锁定到本旋转点后，若 Status 目标距车体超过此值，则解除锁定并位移/旋转并行跟随。" +
            "≤0 表示不按距离解锁。仅「可原地旋转」点有意义")]
        public float RotateClampDistance = 0.4f;

        [Tooltip("勾选后，AGV 进入 Status=6 时才会按下方空载/载货配置切换货叉挡位；未勾选则忽略本点的升降配置")]
        public bool EnableForkLift;

        [Tooltip("空载（PodCode=0）且 Status=6 时：抬升或下降（需启用货叉升降）")]
        public ForkLiftCommand EmptyForkCommand = ForkLiftCommand.Raise;

        [Tooltip("空载 Status=6 时目标挡位下标，对应 AGV「货叉挡位高度」数组（需启用货叉升降）")]
        public int EmptyForkGear = 1;

        [Tooltip("载货（PodCode≠0，含空值）且 Status=6 时：抬升或下降（需启用货叉升降）")]
        public ForkLiftCommand LoadedForkCommand = ForkLiftCommand.Lower;

        [Tooltip("载货 Status=6 时目标挡位下标，对应 AGV「货叉挡位高度」数组（需启用货叉升降）")]
        public int LoadedForkGear;

        public ForkLiftCommand ResolveForkCommand(bool loaded) =>
            loaded ? LoadedForkCommand : EmptyForkCommand;

        public int ResolveForkGear(bool loaded) =>
            loaded ? LoadedForkGear : EmptyForkGear;
    }
}
