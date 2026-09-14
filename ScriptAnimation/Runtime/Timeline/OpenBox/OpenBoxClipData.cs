using System;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 开箱 Clip 参数：勾选要插值的阶段，按 From→To 驱动；未勾选的保持前序 Clip 终点。
    /// DurationSeconds 为 0 时瞬间到位。
    /// </summary>
    [Serializable]
    public class OpenBoxClipData
    {
        [Tooltip("开启后忽略 From，从前序终点（无前序则用组件默认状态）插到 To。关闭则按 From→To 插值。")]
        [InspectorLabel("从当前/前序开姿")]
        public bool FromCurrent;

        [Header("侧壁")]
        [InspectorLabel("插值侧壁")]
        public bool AnimateWall = true;

        [Range(0f, 1f)]
        [InspectorLabel("侧壁起点")]
        public float FromWall;

        [Range(0f, 1f)]
        [InspectorLabel("侧壁终点")]
        public float Wall = 1f;

        [Header("底盖")]
        [InspectorLabel("插值底盖短边")]
        public bool AnimateBottomShort;

        [Range(OpenBoxPose.FlapFoldMin, OpenBoxPose.FlapFoldMax)]
        [InspectorLabel("底盖短边起点")]
        public float FromBottomShort;

        [Range(OpenBoxPose.FlapFoldMin, OpenBoxPose.FlapFoldMax)]
        [InspectorLabel("底盖短边终点")]
        public float BottomShort = 1f;

        [InspectorLabel("插值底盖长边")]
        public bool AnimateBottomLong;

        [Range(OpenBoxPose.FlapFoldMin, OpenBoxPose.FlapFoldMax)]
        [InspectorLabel("底盖长边起点")]
        public float FromBottomLong;

        [Range(OpenBoxPose.FlapFoldMin, OpenBoxPose.FlapFoldMax)]
        [InspectorLabel("底盖长边终点")]
        public float BottomLong = 1f;

        [Header("顶盖")]
        [InspectorLabel("插值顶盖短边")]
        public bool AnimateTopShort;

        [Range(OpenBoxPose.FlapFoldMin, OpenBoxPose.FlapFoldMax)]
        [InspectorLabel("顶盖短边起点")]
        public float FromTopShort;

        [Range(OpenBoxPose.FlapFoldMin, OpenBoxPose.FlapFoldMax)]
        [InspectorLabel("顶盖短边终点")]
        public float TopShort = 1f;

        [InspectorLabel("插值顶盖长边")]
        public bool AnimateTopLong;

        [Range(OpenBoxPose.FlapFoldMin, OpenBoxPose.FlapFoldMax)]
        [InspectorLabel("顶盖长边起点")]
        public float FromTopLong;

        [Range(OpenBoxPose.FlapFoldMin, OpenBoxPose.FlapFoldMax)]
        [InspectorLabel("顶盖长边终点")]
        public float TopLong = 1f;

        [Header("插值")]
        [Tooltip("归一化时间 → 插值权重；默认线性")]
        [InspectorLabel("缓动曲线")]
        public AnimationCurve Ease = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Min(0f)]
        [Tooltip("自动同步开启时的目标用时（秒）；0 表示瞬间改变。关闭自动同步时由 Clip 长度决定插值时长。")]
        [InspectorLabel("目标用时（秒）")]
        public float DurationSeconds = 1f;

        [Tooltip("开启后「按速度刷新全轨时长」会按目标用时回写 Clip 长度（用时为 0 时写为占位帧数）；关闭则可拖拽 Clip 控制时长")]
        [InspectorLabel("自动同步时长")]
        public bool AutoSyncDuration;

        public bool AnimatesAny =>
            AnimateWall || AnimateBottomShort || AnimateBottomLong || AnimateTopShort || AnimateTopLong;

        public int AnimatedCount
        {
            get
            {
                int n = 0;
                if (AnimateWall) n++;
                if (AnimateBottomShort) n++;
                if (AnimateBottomLong) n++;
                if (AnimateTopShort) n++;
                if (AnimateTopLong) n++;
                return n;
            }
        }
    }
}
