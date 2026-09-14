using System;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 播放时间落在本 Clip 且开启时，进入瞬间捕获并锁定 <see cref="WorldRotationLockAnim.Target"/> 的世界旋转。
    /// 设为配置的锁定欧拉角（确定性，不依赖进入瞬间的运行时姿态）。
    /// 关闭时不进行任何控制，也不还原。
    /// </summary>
    [Serializable]
    public class WorldRotationLockClipData
    {
        [Tooltip("关闭后本 Clip 不生效：不写入目标旋转，也不还原。")]
        [InspectorLabel("锁定世界旋转")]
        public bool Enabled = true;

        [Tooltip("锁定期间写入 Target 的世界欧拉角（度）。跳转到任意帧结果一致。")]
        [InspectorLabel("锁定欧拉角")]
        public Vector3 LockEulerAngles;
    }
}
