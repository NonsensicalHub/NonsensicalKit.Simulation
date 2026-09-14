using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>PathMove 移动类型。</summary>
    public enum PathMoveMode
    {
        [InspectorName("边走边转")]
        FaceWhileMove = 0,

        [InspectorName("先转后移")]
        RotateThenMove = 1,

        [InspectorName("仅移动")]
        MoveOnly = 3,
    }
}
