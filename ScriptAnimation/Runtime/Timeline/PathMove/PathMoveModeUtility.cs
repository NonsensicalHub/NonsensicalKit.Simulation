using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    public static class PathMoveModeUtility
    {
        public static PathMoveMode ResolveMode(PathMoveClipData data, PathMoveActor actor)
        {
            _ = actor;
            return data != null ? data.MoveMode : PathMoveMode.FaceWhileMove;
        }

        public static float ResolveEarlyTurnDistance(PathMoveActor actor)
            => actor != null ? Mathf.Max(0.01f, actor.EarlyTurnDistance) : 1f;

        public static float ResolveBackDistance(PathMoveActor actor)
            => actor != null ? Mathf.Max(0.01f, actor.BackDistance) : 0.8f;

        /// <summary>车体旋转速度（度/秒）：取自 Clip 数据；data 为空时用硬编码回退。</summary>
        public static float ResolveRotateSpeed(PathMoveClipData data, PathMoveActor actor)
        {
            _ = actor;
            return data != null
                ? DurationUtility.SafeSpeed(data.RotateSpeed)
                : DurationUtility.SafeSpeed(90f);
        }

        /// <summary>请 PathMove 是否会改写结束朝向。</summary>
        public static bool UpdatesFacing(PathMoveMode mode)
        {
            return mode != PathMoveMode.MoveOnly;
        }

        public static bool IsRotateThenMove(PathMoveMode mode)
            => mode == PathMoveMode.RotateThenMove;
    }
}
