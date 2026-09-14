using UnityEngine;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>从路网节点解析拐点几何（Prev →Corner →Next）。</summary>
    internal static class CornerManeuverUtility
    {
        internal static bool TryResolveAxes(
            PathMoveActor actor,
            PathNode corner,
            PathNode prev,
            PathNode next,
            Vector3 pathOffset,
            out Vector3 pivot,
            out Vector3 dirIn,
            out Vector3 dirOut,
            out float lenIn,
            out float lenOut)
        {
            pivot = Vector3.zero;
            dirIn = Vector3.forward;
            dirOut = Vector3.forward;
            lenIn = 0f;
            lenOut = 0f;

            if (corner == null || prev == null || next == null)
                return false;

            Vector3 prevPos = prev.transform.position + pathOffset;
            pivot = corner.transform.position + pathOffset;
            Vector3 nextPos = next.transform.position + pathOffset;

            if (actor != null)
            {
                prevPos = actor.WithUpHeight(prevPos, pivot);
                pivot = actor.WithUpHeight(pivot, pivot);
                nextPos = actor.WithUpHeight(nextPos, pivot);
            }

            Vector3 inVec = pivot - prevPos;
            Vector3 outVec = nextPos - pivot;
            if (actor != null)
            {
                inVec = actor.Flatten(inVec);
                outVec = actor.Flatten(outVec);
            }
            else
            {
                inVec.y = 0f;
                outVec.y = 0f;
            }

            if (inVec.sqrMagnitude < 1e-8f || outVec.sqrMagnitude < 1e-8f)
                return false;

            lenIn = inVec.magnitude;
            lenOut = outVec.magnitude;
            dirIn = inVec / lenIn;
            dirOut = outVec / lenOut;
            return true;
        }
    }
}
