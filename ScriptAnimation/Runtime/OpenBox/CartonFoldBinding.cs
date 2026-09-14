using System;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    public enum CartonFoldChannel
    {
        /// <summary>侧壁锐角折（Right / Left）：压扁 0° → 成型 -90°</summary>
        WallAcute = 0,
        /// <summary>侧壁钝角折（Back）：压扁 -180° → 成型 -90°</summary>
        WallObtuse = 1,
        BottomShort = 2,
        BottomLong = 3,
        TopShort = 4,
        TopLong = 5
    }

    [Serializable]
    public class CartonFoldBinding
    {
        public string name;
        public Transform pivot;
        public Vector3 localAxis = Vector3.up;
        [Tooltip("fold=0 的角度。顶/底盖一般为 0°（竖直）。")]
        public float flatAngle;
        [Tooltip("fold=1 的角度。顶/底盖一般为 ±90°（闭合）；fold 每±1 再偏这么多。")]
        public float closedAngle = -90f;
        public CartonFoldChannel channel;
    }
}
