using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    public enum ScriptAnimHomeSource
    {
        PreviousPathMove = 0,
        PreviousForklift = 1,
        /// <summary>已弃用语义：曾表示读 Body；现改为组件 Home。</summary>
        CurrentPose = 2,
        Failed = 3,
        PreviousTeleport = 4,
        PreviousCtu = 5,
        PreviousDirectMove = 6,
        PreviousShuttle = 7,
        PreviousRotate = 8,
        PreviousThreePointTurn = 9,
        PreviousLatentAgv = 10,
        PreviousBezierCorner = 11,
        PreviousReverseUTurn = 12,
        PreviousBezierDualCorner = 14,
        /// <summary>无前序 / 未知前序：组件上配置的默认 Home。</summary>
        ComponentHome = 13,
    }

    /// <summary>
    /// 取放货开场旋转策略（由同轨紧邻前序 Clip 决定）。
    /// </summary>
    public enum ForkliftRotateMode
    {
        /// <summary>无前序 / 未知前序：从组件 Home 瞬间转到面朝货点，不计旋转时长。</summary>
        Instant = 0,

        /// <summary>前序为 PathMove / DirectMove / Rotate / 机动 / Teleport：按角度/角速度计入旋转时长。</summary>
        Timed = 1,

        /// <summary>前序为取/放货（或 CTU/穿梭车车体）：不旋转，保持上一动作结束朝向直接开场。</summary>
        Skip = 2
    }

    /// <summary>
    /// Forklift 开场位姿与旋转：看同轨「开场更早且 start 最大」的前一个 Clip（允许时间重叠）。
    /// 无前序 / 未知前序时用组件 Home（调用方须缓存）。
    /// </summary>
    public static class ScriptAnimHomeResolver
    {
        /// <summary>用组件 Home 写开场位姿（未配置则确定性回退并 Warning）。</summary>
        public static ScriptAnimHomeSource ApplyComponentHome(
            PathMoveActor anim,
            out Vector3 homePos,
            out Quaternion homeRot,
            out string sourceLabel,
            string reason)
        {
            homePos = Vector3.zero;
            homeRot = Quaternion.identity;
            if (anim == null)
            {
                sourceLabel = "Failed";
                return ScriptAnimHomeSource.Failed;
            }

            anim.ResolveHomePoseOrFallback(out homePos, out homeRot, reason);
            sourceLabel = anim.HasHome
                ? $"{reason}：组件 Home"
                : $"{reason}：未配置 Home，已回退原点朝前";
            return ScriptAnimHomeSource.ComponentHome;
        }
        public static ScriptAnimHomeSource Resolve(
            TimelineClip forkliftTimelineClip,
            ForkliftClip forkliftAsset,
            ForkliftAnim anim,
            IExposedPropertyTable resolver,
            out Vector3 homePos,
            out Quaternion homeRot,
            out ForkliftRotateMode rotateMode,
            out string sourceLabel)
        {
            homePos = Vector3.zero;
            homeRot = Quaternion.identity;
            rotateMode = ForkliftRotateMode.Instant;
            sourceLabel = "Failed";

            if (!TryFindPreviousClip(forkliftTimelineClip, out TimelineClip previous))
            {
                rotateMode = ForkliftRotateMode.Instant;
                var src = ApplyComponentHome(
                    anim, out homePos, out homeRot, out sourceLabel, "无前序 Clip");
                if (src == ScriptAnimHomeSource.ComponentHome)
                    sourceLabel += "，瞬间转向";
                return src;
            }

            if (previous.asset is PathMoveClip pathAsset)
            {
                if (!TryResolvePathMoveExitPose(
                        previous, pathAsset, resolver, out homePos, out homeRot,
                        out bool facingFromEarlier, anim))
                    return ScriptAnimHomeSource.Failed;

                rotateMode = ForkliftRotateMode.Timed;
                sourceLabel = facingFromEarlier
                    ? $"前序 PathMove「{previous.displayName}」终点（未改朝向前进，沿用更早确立朝向），计入旋转"
                    : $"前序 PathMove「{previous.displayName}」终点，计入旋转";
                return ScriptAnimHomeSource.PreviousPathMove;
            }

            if (previous.asset is DirectMoveClip directAsset)
            {
                if (!TryResolveDirectMoveExitPose(
                        previous, directAsset, resolver, out homePos, out homeRot, anim))
                    return ScriptAnimHomeSource.Failed;

                rotateMode = ForkliftRotateMode.Timed;
                sourceLabel = $"前序 DirectMove「{previous.displayName}」终点（不改朝向，沿用进入朝向），计入旋转";
                return ScriptAnimHomeSource.PreviousDirectMove;
            }

            if (previous.asset is RotateClip rotateAsset)
            {
                if (!TryResolveRotateExitPose(
                        previous, rotateAsset, resolver, out homePos, out homeRot, anim))
                    return ScriptAnimHomeSource.Failed;

                rotateMode = ForkliftRotateMode.Timed;
                sourceLabel = $"前序原地旋转「{previous.displayName}」结束朝向，计入旋转";
                return ScriptAnimHomeSource.PreviousRotate;
            }

            if (ManeuverHomeUtility.TryResolvePathManeuverExitPose(
                    previous, anim, resolver, out homePos, out homeRot))
            {
                rotateMode = ForkliftRotateMode.Timed;
                sourceLabel = $"前序三点转向「{previous.displayName}」结束朝向，计入旋转";
                if (previous.asset is ThreePointTurnClip)
                    return ScriptAnimHomeSource.PreviousThreePointTurn;
                if (previous.asset is BezierCornerClip)
                    return ScriptAnimHomeSource.PreviousBezierCorner;
                if (previous.asset is BezierDualCornerClip)
                    return ScriptAnimHomeSource.PreviousBezierDualCorner;
                return ScriptAnimHomeSource.PreviousReverseUTurn;
            }

            if (previous.asset is TeleportClip teleportAsset)
            {
                if (!TryEvaluateTeleportEndPose(
                        previous, teleportAsset, anim, resolver, out homePos, out homeRot))
                    return ScriptAnimHomeSource.Failed;

                rotateMode = ForkliftRotateMode.Timed;
                sourceLabel = $"前序 Teleport「{previous.displayName}」落点，计入旋转";
                return ScriptAnimHomeSource.PreviousTeleport;
            }

            if (previous.asset is ForkliftClip prevFork)
            {
                if (!TryGetForkliftExitPose(
                        previous, prevFork, anim, resolver, out homePos, out homeRot))
                    return ScriptAnimHomeSource.Failed;

                rotateMode = ForkliftRotateMode.Skip;
                sourceLabel = $"前序取放货「{previous.displayName}」结束位姿，不旋转";
                return ScriptAnimHomeSource.PreviousForklift;
            }

            if (previous.asset is LatentAgvClip)
            {
                rotateMode = ForkliftRotateMode.Skip;
                var src = ApplyComponentHome(
                    anim, out homePos, out homeRot, out sourceLabel,
                    $"前序「{previous.displayName}」为潜伏车");
                if (src == ScriptAnimHomeSource.ComponentHome)
                    sourceLabel += "，叉车用 Home 不旋转";
                return src;
            }

            if (previous.asset is CtuClip)
            {
                // CTU 不改车体：开场位姿 = 进入该 CTU 时的车体位姿（再向前追溯）
                if (!TryGetCtuExitPose(previous, anim, resolver, out homePos, out homeRot))
                    return ScriptAnimHomeSource.Failed;

                rotateMode = ForkliftRotateMode.Skip;
                sourceLabel = $"前序 CTU「{previous.displayName}」车体结束位姿，不旋转";
                return ScriptAnimHomeSource.PreviousCtu;
            }

            if (previous.asset is ShuttleClip)
            {
                if (!TryGetShuttleExitPose(previous, anim, resolver, out homePos, out homeRot))
                    return ScriptAnimHomeSource.Failed;

                rotateMode = ForkliftRotateMode.Skip;
                sourceLabel = $"前序穿梭车「{previous.displayName}」车体结束位姿，不旋转";
                return ScriptAnimHomeSource.PreviousShuttle;
            }

            rotateMode = ForkliftRotateMode.Instant;
            var unknownSrc = ApplyComponentHome(
                anim, out homePos, out homeRot, out sourceLabel,
                $"前序「{previous.displayName}」类型未知");
            if (unknownSrc == ScriptAnimHomeSource.ComponentHome)
                sourceLabel += "，瞬间转向";
            return unknownSrc;
        }

        /// <summary>
        /// 潜伏车开场位姿：看同轨「开场更早且 start 最大」的前一个 Clip（允许时间重叠）。
        /// 无前序时用组件 Home（调用方须缓存）。
        /// 放货开场可按 <paramref name="rotateMode"/> 转向移动点；取货不转向。
        /// </summary>
        public static ScriptAnimHomeSource Resolve(
            TimelineClip latentTimelineClip,
            LatentAgvClip latentAsset,
            LatentAgvAnim anim,
            IExposedPropertyTable resolver,
            out Vector3 homePos,
            out Quaternion homeRot,
            out ForkliftRotateMode rotateMode,
            out string sourceLabel)
        {
            homePos = Vector3.zero;
            homeRot = Quaternion.identity;
            rotateMode = ForkliftRotateMode.Instant;
            sourceLabel = "Failed";

            if (!TryFindPreviousClip(latentTimelineClip, out TimelineClip previous))
            {
                rotateMode = ForkliftRotateMode.Instant;
                var src = ApplyComponentHome(
                    anim, out homePos, out homeRot, out sourceLabel, "无前序 Clip");
                if (src == ScriptAnimHomeSource.ComponentHome)
                    sourceLabel += "，瞬间转向（放货）/ 不转向（取货）";
                return src;
            }

            if (previous.asset is PathMoveClip pathAsset)
            {
                if (!TryResolvePathMoveExitPose(
                        previous, pathAsset, resolver, out homePos, out homeRot,
                        out bool facingFromEarlier, anim))
                    return ScriptAnimHomeSource.Failed;

                rotateMode = ForkliftRotateMode.Timed;
                sourceLabel = facingFromEarlier
                    ? $"前序 PathMove「{previous.displayName}」终点（未改朝向前进，沿用更早确立朝向），计入旋转"
                    : $"前序 PathMove「{previous.displayName}」终点，计入旋转";
                return ScriptAnimHomeSource.PreviousPathMove;
            }

            if (previous.asset is DirectMoveClip directAsset)
            {
                if (!TryResolveDirectMoveExitPose(
                        previous, directAsset, resolver, out homePos, out homeRot, anim))
                    return ScriptAnimHomeSource.Failed;

                rotateMode = ForkliftRotateMode.Timed;
                sourceLabel = $"前序 DirectMove「{previous.displayName}」终点，计入旋转";
                return ScriptAnimHomeSource.PreviousDirectMove;
            }

            if (previous.asset is RotateClip rotateAsset)
            {
                if (!TryResolveRotateExitPose(
                        previous, rotateAsset, resolver, out homePos, out homeRot, anim))
                    return ScriptAnimHomeSource.Failed;

                rotateMode = ForkliftRotateMode.Timed;
                sourceLabel = $"前序原地旋转「{previous.displayName}」结束朝向，计入旋转";
                return ScriptAnimHomeSource.PreviousRotate;
            }

            if (ManeuverHomeUtility.TryResolvePathManeuverExitPose(
                    previous, anim, resolver, out homePos, out homeRot))
            {
                rotateMode = ForkliftRotateMode.Timed;
                sourceLabel = $"前序机动「{previous.displayName}」结束朝向，计入旋转";
                if (previous.asset is ThreePointTurnClip)
                    return ScriptAnimHomeSource.PreviousThreePointTurn;
                if (previous.asset is BezierCornerClip)
                    return ScriptAnimHomeSource.PreviousBezierCorner;
                if (previous.asset is BezierDualCornerClip)
                    return ScriptAnimHomeSource.PreviousBezierDualCorner;
                return ScriptAnimHomeSource.PreviousReverseUTurn;
            }

            if (previous.asset is TeleportClip teleportAsset)
            {
                if (!TryEvaluateTeleportEndPose(
                        previous, teleportAsset, anim, resolver, out homePos, out homeRot))
                    return ScriptAnimHomeSource.Failed;

                rotateMode = ForkliftRotateMode.Timed;
                sourceLabel = $"前序 Teleport「{previous.displayName}」落点，计入旋转";
                return ScriptAnimHomeSource.PreviousTeleport;
            }

            if (previous.asset is LatentAgvClip prevLatent)
            {
                if (!TryGetLatentAgvExitPose(
                        previous, prevLatent, anim, resolver, out homePos, out homeRot))
                    return ScriptAnimHomeSource.Failed;

                rotateMode = ForkliftRotateMode.Skip;
                sourceLabel = $"前序取放货「{previous.displayName}」结束位姿，不旋转";
                return ScriptAnimHomeSource.PreviousLatentAgv;
            }

            if (previous.asset is ForkliftClip)
            {
                rotateMode = ForkliftRotateMode.Skip;
                var src = ApplyComponentHome(
                    anim, out homePos, out homeRot, out sourceLabel,
                    $"前序「{previous.displayName}」为叉车");
                if (src == ScriptAnimHomeSource.ComponentHome)
                    sourceLabel += "，潜伏车用 Home 不旋转";
                return src;
            }

            if (previous.asset is CtuClip)
            {
                if (!TryGetCtuExitPose(previous, anim, resolver, out homePos, out homeRot))
                    return ScriptAnimHomeSource.Failed;

                rotateMode = ForkliftRotateMode.Skip;
                sourceLabel = $"前序 CTU「{previous.displayName}」车体结束位姿，不旋转";
                return ScriptAnimHomeSource.PreviousCtu;
            }

            if (previous.asset is ShuttleClip)
            {
                if (!TryGetShuttleExitPose(previous, anim, resolver, out homePos, out homeRot))
                    return ScriptAnimHomeSource.Failed;

                rotateMode = ForkliftRotateMode.Skip;
                sourceLabel = $"前序穿梭车「{previous.displayName}」车体结束位姿，不旋转";
                return ScriptAnimHomeSource.PreviousShuttle;
            }

            rotateMode = ForkliftRotateMode.Instant;
            var latentUnknown = ApplyComponentHome(
                anim, out homePos, out homeRot, out sourceLabel,
                $"前序「{previous.displayName}」类型未知");
            if (latentUnknown == ScriptAnimHomeSource.ComponentHome)
                sourceLabel += "，瞬间转向";
            return latentUnknown;
        }

        /// <summary>
        /// CTU 开场车体位姿：同轨紧邻前序结束位姿；CTU 自身不改车体。
    /// 无前序时用组件 Home（调用方须缓存）。
    /// </summary>
        public static ScriptAnimHomeSource ResolveCtuBodyHome(
            TimelineClip ctuTimelineClip,
            CtuClip ctuAsset,
            CtuAnim anim,
            IExposedPropertyTable resolver,
            out Vector3 homePos,
            out Quaternion homeRot,
            out string sourceLabel)
        {
            homePos = Vector3.zero;
            homeRot = Quaternion.identity;
            sourceLabel = "Failed";
            _ = ctuAsset;

            if (!TryFindPreviousClip(ctuTimelineClip, out TimelineClip previous))
            {
                return ApplyComponentHome(
                    anim, out homePos, out homeRot, out sourceLabel, "无前序 Clip");
            }

            if (previous.asset is PathMoveClip pathAsset)
            {
                if (!TryResolvePathMoveExitPose(
                        previous, pathAsset, resolver, out homePos, out homeRot,
                        out bool facingFromEarlier, anim))
                    return ScriptAnimHomeSource.Failed;

                sourceLabel = facingFromEarlier
                    ? $"前序 PathMove「{previous.displayName}」终点（未改朝向前进，沿用更早确立朝向）"
                    : $"前序 PathMove「{previous.displayName}」终点";
                return ScriptAnimHomeSource.PreviousPathMove;
            }

            if (previous.asset is DirectMoveClip directAsset)
            {
                if (!TryResolveDirectMoveExitPose(
                        previous, directAsset, resolver, out homePos, out homeRot, anim))
                    return ScriptAnimHomeSource.Failed;

                sourceLabel = $"前序 DirectMove「{previous.displayName}」终点（不改朝向）";
                return ScriptAnimHomeSource.PreviousDirectMove;
            }

            if (previous.asset is RotateClip rotateAsset)
            {
                if (!TryResolveRotateExitPose(
                        previous, rotateAsset, resolver, out homePos, out homeRot, anim))
                    return ScriptAnimHomeSource.Failed;

                sourceLabel = $"前序原地旋转「{previous.displayName}」结束朝向";
                return ScriptAnimHomeSource.PreviousRotate;
            }

            if (ManeuverHomeUtility.TryResolvePathManeuverExitPose(
                    previous, anim, resolver, out homePos, out homeRot))
            {
                sourceLabel = $"前序机动「{previous.displayName}」结束朝向";
                if (previous.asset is ThreePointTurnClip)
                    return ScriptAnimHomeSource.PreviousThreePointTurn;
                if (previous.asset is BezierCornerClip)
                    return ScriptAnimHomeSource.PreviousBezierCorner;
                if (previous.asset is BezierDualCornerClip)
                    return ScriptAnimHomeSource.PreviousBezierDualCorner;
                return ScriptAnimHomeSource.PreviousReverseUTurn;
            }

            if (previous.asset is TeleportClip teleportAsset)
            {
                if (!TryEvaluateTeleportEndPose(
                        previous, teleportAsset, anim, resolver, out homePos, out homeRot))
                    return ScriptAnimHomeSource.Failed;

                sourceLabel = $"前序 Teleport「{previous.displayName}」落点";
                return ScriptAnimHomeSource.PreviousTeleport;
            }

            if (previous.asset is ForkliftClip)
            {
                return ApplyComponentHome(
                    anim, out homePos, out homeRot, out sourceLabel,
                    $"前序「{previous.displayName}」为 Forklift");
            }

            if (previous.asset is LatentAgvClip)
            {
                return ApplyComponentHome(
                    anim, out homePos, out homeRot, out sourceLabel,
                    $"前序「{previous.displayName}」为潜伏车");
            }

            if (previous.asset is CtuClip)
            {
                if (!TryGetCtuExitPose(previous, anim, resolver, out homePos, out homeRot))
                    return ScriptAnimHomeSource.Failed;

                sourceLabel = $"前序 CTU「{previous.displayName}」车体结束位姿（不改车体）";
                return ScriptAnimHomeSource.PreviousCtu;
            }

            if (previous.asset is ShuttleClip)
            {
                if (!TryGetShuttleExitPose(previous, anim, resolver, out homePos, out homeRot))
                    return ScriptAnimHomeSource.Failed;

                sourceLabel = $"前序穿梭车「{previous.displayName}」车体结束位姿（不改车体）";
                return ScriptAnimHomeSource.PreviousShuttle;
            }

            return ApplyComponentHome(
                anim, out homePos, out homeRot, out sourceLabel,
                $"前序「{previous.displayName}」类型未知");
        }

        /// <summary>重载：不需要 sourceLabel 时使用。</summary>
        public static ScriptAnimHomeSource ResolveCtuBodyHome(
            TimelineClip ctuTimelineClip,
            CtuClip ctuAsset,
            CtuAnim anim,
            IExposedPropertyTable resolver,
            out Vector3 homePos,
            out Quaternion homeRot)
        {
            return ResolveCtuBodyHome(
                ctuTimelineClip, ctuAsset, anim, resolver,
                out homePos, out homeRot, out _);
        }

        /// <summary>
        /// 穿梭车开场车体位姿：同轨紧邻前序结束位姿；穿梭车自身不改车体。
    /// 无前序时用组件 Home（调用方须缓存）。
    /// </summary>
        public static ScriptAnimHomeSource ResolveShuttleBodyHome(
            TimelineClip shuttleTimelineClip,
            ShuttleClip shuttleAsset,
            ShuttleAnim anim,
            IExposedPropertyTable resolver,
            out Vector3 homePos,
            out Quaternion homeRot,
            out string sourceLabel)
        {
            homePos = Vector3.zero;
            homeRot = Quaternion.identity;
            sourceLabel = "Failed";
            _ = shuttleAsset;

            if (!TryFindPreviousClip(shuttleTimelineClip, out TimelineClip previous))
            {
                return ApplyComponentHome(
                    anim, out homePos, out homeRot, out sourceLabel, "无前序 Clip");
            }

            if (previous.asset is PathMoveClip pathAsset)
            {
                if (!TryResolvePathMoveExitPose(
                        previous, pathAsset, resolver, out homePos, out homeRot,
                        out bool facingFromEarlier, anim))
                    return ScriptAnimHomeSource.Failed;

                sourceLabel = facingFromEarlier
                    ? $"前序 PathMove「{previous.displayName}」终点（未改朝向前进，沿用更早确立朝向）"
                    : $"前序 PathMove「{previous.displayName}」终点";
                return ScriptAnimHomeSource.PreviousPathMove;
            }

            if (previous.asset is DirectMoveClip directAsset)
            {
                if (!TryResolveDirectMoveExitPose(
                        previous, directAsset, resolver, out homePos, out homeRot, anim))
                    return ScriptAnimHomeSource.Failed;

                sourceLabel = $"前序 DirectMove「{previous.displayName}」终点（不改朝向）";
                return ScriptAnimHomeSource.PreviousDirectMove;
            }

            if (previous.asset is RotateClip rotateAsset)
            {
                if (!TryResolveRotateExitPose(
                        previous, rotateAsset, resolver, out homePos, out homeRot, anim))
                    return ScriptAnimHomeSource.Failed;

                sourceLabel = $"前序原地旋转「{previous.displayName}」结束朝向";
                return ScriptAnimHomeSource.PreviousRotate;
            }

            if (ManeuverHomeUtility.TryResolvePathManeuverExitPose(
                    previous, anim, resolver, out homePos, out homeRot))
            {
                sourceLabel = $"前序机动「{previous.displayName}」结束朝向";
                if (previous.asset is ThreePointTurnClip)
                    return ScriptAnimHomeSource.PreviousThreePointTurn;
                if (previous.asset is BezierCornerClip)
                    return ScriptAnimHomeSource.PreviousBezierCorner;
                if (previous.asset is BezierDualCornerClip)
                    return ScriptAnimHomeSource.PreviousBezierDualCorner;
                return ScriptAnimHomeSource.PreviousReverseUTurn;
            }

            if (previous.asset is TeleportClip teleportAsset)
            {
                if (!TryEvaluateTeleportEndPose(
                        previous, teleportAsset, anim, resolver, out homePos, out homeRot))
                    return ScriptAnimHomeSource.Failed;

                sourceLabel = $"前序 Teleport「{previous.displayName}」落点";
                return ScriptAnimHomeSource.PreviousTeleport;
            }

            if (previous.asset is ForkliftClip)
            {
                return ApplyComponentHome(
                    anim, out homePos, out homeRot, out sourceLabel,
                    $"前序「{previous.displayName}」为 Forklift");
            }

            if (previous.asset is LatentAgvClip)
            {
                return ApplyComponentHome(
                    anim, out homePos, out homeRot, out sourceLabel,
                    $"前序「{previous.displayName}」为潜伏车");
            }

            if (previous.asset is CtuClip)
            {
                if (!TryGetCtuExitPose(previous, anim, resolver, out homePos, out homeRot))
                    return ScriptAnimHomeSource.Failed;

                sourceLabel = $"前序 CTU「{previous.displayName}」车体结束位姿（不改车体）";
                return ScriptAnimHomeSource.PreviousCtu;
            }

            if (previous.asset is ShuttleClip)
            {
                if (!TryGetShuttleExitPose(previous, anim, resolver, out homePos, out homeRot))
                    return ScriptAnimHomeSource.Failed;

                sourceLabel = $"前序穿梭车「{previous.displayName}」车体结束位姿（不改车体）";
                return ScriptAnimHomeSource.PreviousShuttle;
            }

            return ApplyComponentHome(
                anim, out homePos, out homeRot, out sourceLabel,
                $"前序「{previous.displayName}」类型未知");
        }

        /// <summary>重载：不需要 sourceLabel 时使用。</summary>
        public static ScriptAnimHomeSource ResolveShuttleBodyHome(
            TimelineClip shuttleTimelineClip,
            ShuttleClip shuttleAsset,
            ShuttleAnim anim,
            IExposedPropertyTable resolver,
            out Vector3 homePos,
            out Quaternion homeRot)
        {
            return ResolveShuttleBodyHome(
                shuttleTimelineClip, shuttleAsset, anim, resolver,
                out homePos, out homeRot, out _);
        }

        /// <summary>
        /// CTU 机构开场：同轨向前追溯最近一个 CtuClip 的结束机构状态；
        /// 无前序 CTU 时用本 Clip 行驶态（转台行驶角、LiftTravelHeight、夹爪收回、拨爪按模式）。
    /// PathMove/Teleport 等不改机构，故可跨过它们继承更早 CTU 的结束态。
    /// </summary>
        public static void ResolveCtuMechanismStart(
            TimelineClip ctuTimelineClip,
            CtuClip ctuAsset,
            CtuAnim anim,
            IExposedPropertyTable resolver,
            out float startRotate,
            out float startLift,
            out float startClaw,
            out float startPaddle,
            out string sourceLabel)
        {
            ApplyCtuTravelMechanismStart(
                ctuAsset, anim,
                out startRotate, out startLift, out startClaw, out startPaddle);
            sourceLabel = "无前序 CTU：行驶态开场";

            if (ctuTimelineClip == null || anim == null || ctuAsset?.Data == null)
                return;

            TimelineClip cursor = ctuTimelineClip;
            while (TryFindPreviousClip(cursor, out TimelineClip previous))
            {
                if (previous.asset is CtuClip prevCtu)
                {
                    if (TryGetCtuMechanismExit(
                            previous, prevCtu, anim, resolver,
                            out startRotate, out startLift, out startClaw, out float endPaddle))
                    {
                        // 拨爪开场仍按本 Clip 模式（取=开、放=锁），与采样阶段 paddleFrom 一致
                        _ = endPaddle;
                        sourceLabel = DescribeCtuMechanismInherit(previous, prevCtu.Data);
                    }
                    else
                    {
                        ApplyCtuTravelMechanismStart(
                            ctuAsset, anim,
                            out startRotate, out startLift, out startClaw, out startPaddle);
                        sourceLabel = $"前序 CTU「{previous.displayName}」结束机构解析失败，行驶态开场";
                    }

                    return;
                }

                cursor = previous;
            }
        }

        /// <summary>重载：不需要 sourceLabel 时使用。</summary>
        public static void ResolveCtuMechanismStart(
            TimelineClip ctuTimelineClip,
            CtuClip ctuAsset,
            CtuAnim anim,
            IExposedPropertyTable resolver,
            out float startRotate,
            out float startLift,
            out float startClaw,
            out float startPaddle)
        {
            ResolveCtuMechanismStart(
                ctuTimelineClip, ctuAsset, anim, resolver,
                out startRotate, out startLift, out startClaw, out startPaddle, out _);
        }

        /// <summary>
        /// 穿梭车机构开场：夹爪收回；夹紧拨爪按本 Clip 模式（取=松开/开，放=夹持/锁）。
    /// 可跨过 PathMove 等继承更早穿梭车的夹爪结束态（始终收回）。
    /// </summary>
        public static void ResolveShuttleMechanismStart(
            TimelineClip shuttleTimelineClip,
            ShuttleClip shuttleAsset,
            ShuttleAnim anim,
            out float startClaw,
            out float startClamp,
            out float startPaddle,
            out string sourceLabel)
        {
            ApplyShuttleTravelMechanismStart(
                shuttleAsset, anim,
                out startClaw, out startClamp, out startPaddle);
            sourceLabel = "无前序穿梭车：按模式开场（夹爪收回）";

            if (shuttleTimelineClip == null || anim == null || shuttleAsset?.Data == null)
                return;

            TimelineClip cursor = shuttleTimelineClip;
            while (TryFindPreviousClip(cursor, out TimelineClip previous))
            {
                if (previous.asset is ShuttleClip prevShuttle)
                {
                    if (TryGetShuttleMechanismExit(
                            prevShuttle, anim,
                            out startClaw, out float endClamp, out float endPaddle))
                    {
                        _ = endClamp;
                        _ = endPaddle;
                        sourceLabel = $"前序穿梭车「{previous.displayName}」结束机构（夹爪已收回；夹紧/拨爪按本 Clip 模式）";
                    }
                    else
                    {
                        ApplyShuttleTravelMechanismStart(
                            shuttleAsset, anim,
                            out startClaw, out startClamp, out startPaddle);
                        sourceLabel = $"前序穿梭车「{previous.displayName}」结束机构解析失败，按模式开场";
                    }

                    return;
                }

                cursor = previous;
            }
        }

        /// <summary>重载：不需要 sourceLabel 时使用。</summary>
        public static void ResolveShuttleMechanismStart(
            TimelineClip shuttleTimelineClip,
            ShuttleClip shuttleAsset,
            ShuttleAnim anim,
            out float startClaw,
            out float startClamp,
            out float startPaddle)
        {
            ResolveShuttleMechanismStart(
                shuttleTimelineClip, shuttleAsset, anim,
                out startClaw, out startClamp, out startPaddle, out _);
        }

        private static void ApplyCtuTravelMechanismStart(
            CtuClip ctuAsset,
            CtuAnim anim,
            out float startRotate,
            out float startLift,
            out float startClaw,
            out float startPaddle)
        {
            var data = ctuAsset != null ? ctuAsset.Data : null;
            startRotate = anim != null ? anim.RotateTravelAngle : 0f;
            startLift = data != null ? data.LiftTravelHeight : 0f;
            startClaw = data != null ? data.ClawRetracted : 0f;
            startPaddle = data != null && data.Mode == ForkliftMode.PickUp
                ? (anim != null ? anim.PaddleOpenAngle : 0f)
                : (anim != null ? anim.PaddleLockedAngle : 0f);
        }

        private static void ApplyShuttleTravelMechanismStart(
            ShuttleClip shuttleAsset,
            ShuttleAnim anim,
            out float startClaw,
            out float startClamp,
            out float startPaddle)
        {
            var data = shuttleAsset != null ? shuttleAsset.Data : null;
            startClaw = data != null ? data.ClawRetracted : 0f;
            bool pickUp = data == null || data.Mode == ForkliftMode.PickUp;
            startClamp = data != null
                ? (pickUp ? data.ClampReleased : data.ClampClosed)
                : 0f;
            startPaddle = pickUp
                ? (anim != null ? anim.PaddleOpenAngle : 0f)
                : (anim != null ? anim.PaddleLockedAngle : 0f);
        }

        /// <summary>穿梭车结束机构（夹爪已收回；取货夹持+拨爪锁，放货松开+拨爪开）。</summary>
        public static bool TryGetShuttleMechanismExit(
            ShuttleClip shuttleAsset,
            ShuttleAnim anim,
            out float endClaw,
            out float endClamp,
            out float endPaddle)
        {
            endClaw = 0f;
            endClamp = 0f;
            endPaddle = 0f;
            if (shuttleAsset?.Data == null || anim == null)
                return false;

            ShuttleClipData data = shuttleAsset.Data;
            endClaw = data.ClawRetracted;
            bool pickUp = data.Mode == ForkliftMode.PickUp;
            endClamp = pickUp ? data.ClampClosed : data.ClampReleased;
            endPaddle = pickUp ? anim.PaddleLockedAngle : anim.PaddleOpenAngle;
            return true;
        }

        /// <summary>CTU 结束机构状态（转台/升降看回正开关；夹爪已收回；拨爪取货锁、放货开）。</summary>
        public static bool TryGetCtuMechanismExit(
            TimelineClip ctuTimelineClip,
            CtuClip ctuAsset,
            CtuAnim anim,
            IExposedPropertyTable resolver,
            out float endRotate,
            out float endLift,
            out float endClaw,
            out float endPaddle)
        {
            endRotate = 0f;
            endLift = 0f;
            endClaw = 0f;
            endPaddle = 0f;
            if (ctuAsset?.Data == null || anim == null)
                return false;

            CtuClipData data = ctuAsset.Data;
            ResolveCtuBodyHome(
                ctuTimelineClip, ctuAsset, anim, resolver,
                out Vector3 homePos, out Quaternion homeRot);

            Transform station = null;
            if (resolver != null)
                station = ScriptAnimPointUtility.AsTransform(
                    ScriptAnimPointUtility.Resolve(ctuAsset.Station, resolver));

            float faceAngle = anim.RotateTravelAngle;
            if (station != null)
            {
                Vector3 stationPos = station.position + data.DestinationOffset;
                faceAngle = anim.ResolveRotateAngleTo(stationPos, homePos, homeRot);
            }

            endRotate = data.ReturnRotateToTravel ? anim.RotateTravelAngle : faceAngle;
            endLift = data.ReturnLiftToTravel ? data.LiftTravelHeight : data.LiftPlaceHeight;
            endClaw = data.ClawRetracted;
            endPaddle = data.Mode == ForkliftMode.PickUp
                ? anim.PaddleLockedAngle
                : anim.PaddleOpenAngle;
            return true;
        }

        private static string DescribeCtuMechanismInherit(TimelineClip previous, CtuClipData data)
        {
            if (data == null)
                return $"前序 CTU「{previous.displayName}」结束机构状态";

            bool rotateTravel = data.ReturnRotateToTravel;
            bool liftTravel = data.ReturnLiftToTravel;
            if (rotateTravel && liftTravel)
                return $"前序 CTU「{previous.displayName}」结束机构状态";

            if (!rotateTravel && !liftTravel)
                return $"前序 CTU「{previous.displayName}」已回正（行驶态）";

            if (!rotateTravel)
                return $"前序 CTU「{previous.displayName}」未回正：沿用其对准角与取放高度";

            return $"前序 CTU「{previous.displayName}」升降未回正：沿用取放高度";
        }

        /// <summary>CTU 不改车体：结束位姿 = 进入该 Clip 时的车体位姿。</summary>
        private static bool TryGetCtuExitPose(
            TimelineClip ctuTimelineClip,
            PathMoveActor anim,
            IExposedPropertyTable resolver,
            out Vector3 position,
            out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (anim == null)
                return false;

            if (ctuTimelineClip?.asset is not CtuClip ctuAsset)
                return false;

            if (anim is CtuAnim ctu)
            {
                ResolveCtuBodyHome(
                    ctuTimelineClip, ctuAsset, ctu, resolver,
                    out position, out rotation);
                return true;
            }

            // Forklift 等追溯前序 CTU 时：再向前一个 Clip
            if (!TryFindPreviousClip(ctuTimelineClip, out TimelineClip before))
            {
                anim.ResolveHomePoseOrFallback(
                    out position, out rotation, "追溯 CTU 无更早前序");
                return true;
            }

            rotation = ResolveClipEffectiveExitRotation(
                before, anim, resolver,
                anim.HasHome ? anim.HomeRotation : Quaternion.identity);
            // 位置：PathMove/Teleport/Forklift 才有明确终点；CTU 链上取 Body 或再解析
            if (before.asset is PathMoveClip pathAsset)
            {
                return TryResolvePathMoveExitPose(
                    before, pathAsset, resolver, out position, out rotation, out _, anim);
            }

            if (before.asset is DirectMoveClip directAsset)
            {
                return TryResolveDirectMoveExitPose(
                    before, directAsset, resolver, out position, out rotation, anim);
            }

            if (before.asset is RotateClip rotateAsset)
            {
                return TryResolveRotateExitPose(
                    before, rotateAsset, resolver, out position, out rotation, anim);
            }

            if (ManeuverHomeUtility.TryResolvePathManeuverExitPose(
                    before, anim, resolver, out position, out rotation))
                return true;

            if (before.asset is TeleportClip teleportAsset)
            {
                return TryEvaluateTeleportEndPose(
                    before, teleportAsset, anim, resolver, out position, out rotation);
            }

            if (before.asset is ForkliftClip prevFork && anim is ForkliftAnim forklift)
            {
                return TryGetForkliftExitPose(
                    before, prevFork, forklift, resolver, out position, out rotation);
            }

            if (before.asset is LatentAgvClip prevLatent && anim is LatentAgvAnim latent)
            {
                return TryGetLatentAgvExitPose(
                    before, prevLatent, latent, resolver, out position, out rotation);
            }

            if (before.asset is CtuClip)
                return TryGetCtuExitPose(before, anim, resolver, out position, out rotation);

            if (before.asset is ShuttleClip)
                return TryGetShuttleExitPose(before, anim, resolver, out position, out rotation);

            anim.ResolveHomePoseOrFallback(
                out position, out rotation, $"追溯 CTU 前序「{before.displayName}」类型未知");
            return true;
        }

        /// <summary>穿梭车不改车体：结束位姿 = 进入该 Clip 时的车体位姿。</summary>
        private static bool TryGetShuttleExitPose(
            TimelineClip shuttleTimelineClip,
            PathMoveActor anim,
            IExposedPropertyTable resolver,
            out Vector3 position,
            out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (anim == null)
                return false;

            if (shuttleTimelineClip?.asset is not ShuttleClip shuttleAsset)
                return false;

            if (anim is ShuttleAnim shuttle)
            {
                ResolveShuttleBodyHome(
                    shuttleTimelineClip, shuttleAsset, shuttle, resolver,
                    out position, out rotation);
                return true;
            }

            if (!TryFindPreviousClip(shuttleTimelineClip, out TimelineClip before))
            {
                anim.ResolveHomePoseOrFallback(
                    out position, out rotation, "追溯穿梭车无更早前序");
                return true;
            }

            rotation = ResolveClipEffectiveExitRotation(
                before, anim, resolver,
                anim.HasHome ? anim.HomeRotation : Quaternion.identity);
            if (before.asset is PathMoveClip pathAsset)
            {
                return TryResolvePathMoveExitPose(
                    before, pathAsset, resolver, out position, out rotation, out _, anim);
            }

            if (before.asset is DirectMoveClip directAsset)
            {
                return TryResolveDirectMoveExitPose(
                    before, directAsset, resolver, out position, out rotation, anim);
            }

            if (before.asset is RotateClip rotateAsset)
            {
                return TryResolveRotateExitPose(
                    before, rotateAsset, resolver, out position, out rotation, anim);
            }

            if (ManeuverHomeUtility.TryResolvePathManeuverExitPose(
                    before, anim, resolver, out position, out rotation))
                return true;

            if (before.asset is TeleportClip teleportAsset)
            {
                return TryEvaluateTeleportEndPose(
                    before, teleportAsset, anim, resolver, out position, out rotation);
            }

            if (before.asset is ForkliftClip prevFork && anim is ForkliftAnim forklift)
            {
                return TryGetForkliftExitPose(
                    before, prevFork, forklift, resolver, out position, out rotation);
            }

            if (before.asset is LatentAgvClip prevLatent && anim is LatentAgvAnim latent)
            {
                return TryGetLatentAgvExitPose(
                    before, prevLatent, latent, resolver, out position, out rotation);
            }

            if (before.asset is CtuClip)
                return TryGetCtuExitPose(before, anim, resolver, out position, out rotation);

            if (before.asset is ShuttleClip)
                return TryGetShuttleExitPose(before, anim, resolver, out position, out rotation);

            anim.ResolveHomePoseOrFallback(
                out position, out rotation, $"追溯穿梭车前序「{before.displayName}」类型未知");
            return true;
        }

        /// <summary>
        /// 同轨、start 严格早于 current 且 start 最大的前一个 Clip（跳过注释 / 世界旋转锁定等标记 Clip）。
    /// 允许为current 时间重叠：Ease 混合时前序尚朝end≤current.start）
        /// 若仍要求「已结束」则取放货会误用组件 Home、旋转角/时长算错。
    /// </summary>
        public static bool TryFindPreviousClip(TimelineClip current, out TimelineClip previous)
        {
            previous = null;
            if (current == null)
                return false;

            var track = current.GetParentTrack();
            if (track == null)
                return false;

            foreach (var clip in track.GetClips())
            {
                if (clip == null || clip == current)
                    continue;
                if (clip.asset is CommentClip || clip.asset is WorldRotationLockClip)
                    continue;
                // 只要求开场更早；重叠（含 Timeline Ease 交叠）仍视为前序
                if (clip.start >= current.start - 1e-6)
                    continue;
                if (previous == null || clip.start > previous.start)
                    previous = clip;
            }

            return previous != null;
        }

        /// <summary>上一取放货结束位姿：回到其开场位姿，朝向按其 ReverseFacing 对齐货点。</summary>
        private static bool TryGetForkliftExitPose(
            TimelineClip prevTimelineClip,
            ForkliftClip prevAsset,
            ForkliftAnim anim,
            IExposedPropertyTable resolver,
            out Vector3 position,
            out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (prevAsset?.Data == null || anim == null)
                return false;

            Transform prevStation = null;
            if (resolver != null)
                prevStation = ScriptAnimPointUtility.AsTransform(
                    ScriptAnimPointUtility.Resolve(prevAsset.Station, resolver));

            Resolve(
                prevTimelineClip,
                prevAsset,
                anim,
                resolver,
                out position,
                out Quaternion prevHomeRot,
                out _,
                out _);

            if (prevStation == null)
            {
                rotation = prevHomeRot;
                return true;
            }

            Vector3 stationPos = prevStation.position + prevAsset.Data.DestinationOffset;
            rotation = GetFlatFacing(
                anim, position, stationPos, prevHomeRot, prevAsset.Data.ReverseFacing);
            return true;
        }

        /// <summary>
        /// 上一潜伏车取放货结束位姿：停在移动点。
        /// 取货保持开场朝向；放货在 Instant/Timed 下朝向移动点，Skip 保持开场朝向。
        /// </summary>
        private static bool TryGetLatentAgvExitPose(
            TimelineClip prevTimelineClip,
            LatentAgvClip prevAsset,
            LatentAgvAnim anim,
            IExposedPropertyTable resolver,
            out Vector3 position,
            out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (prevAsset?.Data == null || anim == null)
                return false;

            Transform movePoint = null;
            if (resolver != null)
                movePoint = ScriptAnimPointUtility.AsTransform(
                    ScriptAnimPointUtility.Resolve(prevAsset.MovePoint, resolver));

            Resolve(
                prevTimelineClip,
                prevAsset,
                anim,
                resolver,
                out Vector3 homePos,
                out Quaternion homeRot,
                out ForkliftRotateMode rotateMode,
                out _);

            if (movePoint == null)
            {
                position = homePos;
                rotation = homeRot;
                return true;
            }

            position = anim.WithUpHeight(movePoint.position, homePos);

            bool pickUp = prevAsset.Data.Mode == ForkliftMode.PickUp;
            if (pickUp || rotateMode == ForkliftRotateMode.Skip)
            {
                rotation = homeRot;
                return true;
            }

            rotation = GetFlatFacing(anim, homePos, position, homeRot);
            return true;
        }

        public static bool TryEvaluatePathMoveEndPose(
            PathMoveClip pathAsset,
            IExposedPropertyTable resolver,
            out Vector3 position,
            out Quaternion rotation,
            PathMoveActor actor = null,
            TimelineClip timelineClip = null)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (pathAsset?.Data == null)
                return false;

            PathNetwork network = null;
            PathNode start = null;
            PathNode end = null;
            if (resolver != null)
            {
                network = pathAsset.Network.Resolve(resolver);
                start = pathAsset.StartNode.Resolve(resolver);
                end = pathAsset.EndNode.Resolve(resolver);
            }

            // 局部列表：解析开场朝向会递归进前序 PathMove，不能共用静态缓冲。
            var points = new List<Vector3>(32);
            var pauses = new List<float>(32);
            Vector3 pathOffset = actor != null ? actor.PathOffset : default;
            if (!PathMoveSampler.TryResolveWorldPoints(
                    network, start, end, pathAsset.Data, points, pathOffset, pauses))
                return false;

            // 受限转弯 / 边走边转的结束朝向都依赖开场朝向（后者用 RotateTowards 追切线，
            // 可能未对齐末段）；先转后移结束朝向为几何末段，可不传 incoming。
            Quaternion incoming = default;
            if (actor != null)
            {
                PathMoveMode mode = PathMoveModeUtility.ResolveMode(pathAsset.Data, actor);
                if (mode == PathMoveMode.FaceWhileMove)
                {
                    incoming = timelineClip != null
                        ? ResolvePathMoveIncomingRotation(
                            timelineClip, actor, resolver, points, pathAsset.Data.ReverseFacing)
                        : PathMoveSampler.GetPathStartRotation(
                            points, actor, pathAsset.Data.ReverseFacing);
                }
                else if (mode == PathMoveMode.MoveOnly)
                {
                    Quaternion silentHome = actor.TryGetHomePose(out _, out Quaternion hr)
                        ? hr
                        : Quaternion.identity;
                    incoming = timelineClip != null
                        ? ResolveEffectiveExitRotationBefore(
                            timelineClip, actor, resolver, silentHome)
                        : actor.ResolveHomeRotationOrFallback(
                            silentHome, "PathMove MoveOnly");
                }
            }

            return PathMoveSampler.TryGetPathEndPose(
                points, out position, out rotation, actor,
                pathAsset.Data.ReverseFacing, pathAsset.Data, incoming, pauses);
        }

        public static bool TryEvaluateDirectMoveEndPose(
            DirectMoveClip directAsset,
            IExposedPropertyTable resolver,
            out Vector3 position,
            PathMoveActor actor = null)
        {
            position = Vector3.zero;
            if (directAsset?.Data == null)
                return false;

            Transform start = null;
            Transform end = null;
            if (resolver != null)
            {
                start = ScriptAnimPointUtility.AsTransform(
                    ScriptAnimPointUtility.Resolve(directAsset.StartNode, resolver));
                end = ScriptAnimPointUtility.AsTransform(
                    ScriptAnimPointUtility.Resolve(directAsset.EndNode, resolver));
            }

            Vector3 pathOffset = actor != null ? actor.PathOffset : default;
            if (!DirectMoveSampler.TryResolveEndpoints(
                    start, end, directAsset.Data, out _, out position, pathOffset))
                return false;

            return true;
        }

        /// <summary>
        /// DirectMove 结束位姿：位置取直线终点；朝向始终沿用进入该 Clip 时的朝向（本 Clip 不改朝向）。
    /// </summary>
        public static bool TryResolveDirectMoveExitPose(
            TimelineClip directMoveTimelineClip,
            DirectMoveClip directAsset,
            IExposedPropertyTable resolver,
            out Vector3 position,
            out Quaternion rotation,
            PathMoveActor actor = null)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (!TryEvaluateDirectMoveEndPose(directAsset, resolver, out position, actor))
                return false;

            rotation = ResolveEffectiveExitRotationBefore(
                directMoveTimelineClip, actor, resolver, Quaternion.identity);
            return true;
        }

        /// <summary>
        /// PathMove 实际结束位姿：位置取路径终点；
        /// 若该 Clip（或组件默认）未「朝向前进方向」，朝向沿用进入该 Clip 时的朝向
        /// （再向前序追溯，例如上上个 Move 的结束朝向），而非路径几何末段朝向。
    /// </summary>
        public static bool TryResolvePathMoveExitPose(TimelineClip pathMoveTimelineClip,
            PathMoveClip pathAsset,
            IExposedPropertyTable resolver,
            out Vector3 position,
            out Quaternion rotation,
            out bool facingFromEarlier,
            PathMoveActor actor = null)
        {
            facingFromEarlier = false;
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (!TryEvaluatePathMoveEndPose(
                    pathAsset, resolver, out position, out Quaternion geometricEnd, actor, pathMoveTimelineClip))
                return false;

            if (IsPathMoveUpdatingFacing(pathAsset.Data, actor))
            {
                rotation = geometricEnd;
                return true;
            }

            facingFromEarlier = true;
            rotation = ResolveEffectiveExitRotationBefore(
                pathMoveTimelineClip, actor, resolver, geometricEnd);
            return true;
        }

        /// <summary>
        /// 该 PathMove 采样时是否会改写结束朝向。
    /// 朝向前进 / 先转后移 / 特种机动 Clip 都会改写；只移不转沿用进入朝向。
    /// 边走边转结束朝向为 RotateTowards 模拟结果（未必对齐几何末段）。
    /// </summary>
        public static bool IsPathMoveUpdatingFacing(PathMoveClipData data, PathMoveActor actor)
        {
            PathMoveMode mode = PathMoveModeUtility.ResolveMode(data, actor);
            return PathMoveModeUtility.UpdatesFacing(mode);
        }

        /// <summary>
        /// 从 <paramref name="clip"/> 的紧邻前序起，解析实际结束朝向（跳过未改写朝向的 PathMove / DirectMove）。
    /// </summary>
        public static Quaternion ResolveEffectiveExitRotationBefore(
            TimelineClip clip,
            ScriptAnimActor actor,
            IExposedPropertyTable resolver,
            Quaternion fallback)
        {
            if (!TryFindPreviousClip(clip, out TimelineClip previous))
            {
                if (actor is PathMoveActor rail)
                    return rail.ResolveHomeRotationOrFallback(fallback, "无前序结束朝向");
                return fallback;
            }

            return ResolveClipEffectiveExitRotation(previous, actor, resolver, fallback);
        }

        private static Quaternion ResolveClipEffectiveExitRotation(
            TimelineClip clip,
            ScriptAnimActor actor,
            IExposedPropertyTable resolver,
            Quaternion fallback)
        {
            if (clip == null)
                return fallback;

            var rail = actor as PathMoveActor;

            if (clip.asset is PathMoveClip pathAsset)
            {
                if (TryResolvePathMoveExitPose(
                        clip, pathAsset, resolver, out _, out Quaternion pathRot, out _, rail))
                    return pathRot;
                return fallback;
            }

            if (clip.asset is DirectMoveClip directAsset)
            {
                if (TryResolveDirectMoveExitPose(
                        clip, directAsset, resolver, out _, out Quaternion directRot, rail))
                    return directRot;
                return fallback;
            }

            if (clip.asset is RotateClip rotateAsset)
            {
                if (TryResolveRotateExitPose(
                        clip, rotateAsset, resolver, out _, out Quaternion rotateRot, rail))
                    return rotateRot;
                return fallback;
            }

            if (ManeuverHomeUtility.TryResolvePathManeuverExitPose(
                    clip, rail, resolver, out _, out Quaternion maneuverRot))
                return maneuverRot;

            if (clip.asset is TeleportClip teleportAsset)
            {
                if (TryEvaluateTeleportEndPose(
                        clip, teleportAsset, actor, resolver, out _, out Quaternion teleportRot))
                    return teleportRot;
                return fallback;
            }

            if (clip.asset is ForkliftClip prevFork)
            {
                var forklift = actor as ForkliftAnim;
                if (forklift != null &&
                    TryGetForkliftExitPose(
                        clip, prevFork, forklift, resolver, out _, out Quaternion exitRot))
                    return exitRot;
            }

            if (clip.asset is LatentAgvClip prevLatent)
            {
                var latent = actor as LatentAgvAnim;
                if (latent != null &&
                    TryGetLatentAgvExitPose(
                        clip, prevLatent, latent, resolver, out _, out Quaternion exitRot))
                    return exitRot;
            }

            if (clip.asset is CtuClip && rail != null)
            {
                // CTU 不改车体朝向：沿用进入该 Clip 时的朝向
                if (TryGetCtuExitPose(clip, rail, resolver, out _, out Quaternion ctuRot))
                    return ctuRot;
            }

            if (clip.asset is ShuttleClip && rail != null)
            {
                if (TryGetShuttleExitPose(clip, rail, resolver, out _, out Quaternion shuttleRot))
                    return shuttleRot;
            }

            // 堆垛机 / 货叉不改车体 yaw：沿用进入该 Clip 时的朝向
            if (clip.asset is StackerClip or StackerForkClip)
                return ResolveClipEffectiveExitRotation(
                    TryFindPreviousClip(clip, out TimelineClip beforeStacker) ? beforeStacker : null,
                    actor,
                    resolver,
                    fallback);

            return fallback;
        }

        public static bool TryEvaluateTeleportEndPose(
            TimelineClip teleportTimelineClip,
            TeleportClip teleportAsset,
            ScriptAnimActor actor,
            IExposedPropertyTable resolver,
            out Vector3 position,
            out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (teleportAsset?.Data == null)
                return false;

            PathNode target = null;
            if (resolver != null)
                target = teleportAsset.TargetNode.Resolve(resolver);

            Quaternion fallback = ResolveTeleportFallbackRotation(
                teleportTimelineClip, actor, resolver);

            return TeleportSampler.TryResolvePose(
                target, teleportAsset.Data, fallback, out position, out rotation,
                actor as PathMoveActor);
        }

        /// <summary>
        /// Teleport KeepPrevious：取前序结束朝向，再不行用组件 Home。
    /// </summary>
        public static Quaternion ResolveTeleportFallbackRotation(
            TimelineClip teleportTimelineClip,
            ScriptAnimActor actor,
            IExposedPropertyTable resolver)
        {
            Quaternion silentHome = Quaternion.identity;
            if (actor is PathMoveActor rail && rail.TryGetHomePose(out _, out Quaternion hr))
                silentHome = hr;

            if (TryFindPreviousClip(teleportTimelineClip, out TimelineClip previous))
                return ResolveClipEffectiveExitRotation(
                    previous, actor, resolver, silentHome);

            if (actor is PathMoveActor noPrev)
                return noPrev.ResolveHomeRotationOrFallback(
                    Quaternion.identity, "Teleport KeepPrevious");
            return Quaternion.identity;
        }

        /// <summary>
        /// PathMove 开场朝向：同轨紧邻前序 Clip 的结束朝向；
        /// 无前序时用组件 Home，再不行用路径第一段朝向。
    /// 调用方须缓存（见 <see cref="PathMoveBehaviour.EnsureIncomingResolved"/>），
    /// </summary>
        public static Quaternion ResolvePathMoveIncomingRotation(
            TimelineClip pathMoveTimelineClip,
            ScriptAnimActor actor,
            IExposedPropertyTable resolver,
            IList<Vector3> worldPoints,
            bool reverseFacing = false)
        {
            var rail = actor as PathMoveActor;
            Quaternion pathStart = PathMoveSampler.GetPathStartRotation(
                worldPoints, rail, reverseFacing);

            if (!TryFindPreviousClip(pathMoveTimelineClip, out TimelineClip previous))
            {
                if (rail != null)
                    return rail.ResolveHomeRotationOrFallback(pathStart, "PathMove 开场朝向");
                return pathStart;
            }

            return ResolveClipEffectiveExitRotation(previous, actor, resolver, pathStart);
        }

        /// <summary>
        /// DirectMove 开场朝向：同轨紧邻前序结束朝向；无前序时用组件 Home。
    /// 本 Clip 全程不改朝向，须缓存（见 <see cref="DirectMoveBehaviour.EnsureIncomingResolved"/>）。
    /// </summary>
        public static Quaternion ResolveDirectMoveIncomingRotation(
            TimelineClip directMoveTimelineClip,
            ScriptAnimActor actor,
            IExposedPropertyTable resolver)
        {
            Quaternion silentHome = Quaternion.identity;
            if (actor is PathMoveActor rail && rail.TryGetHomePose(out _, out Quaternion hr))
                silentHome = hr;

            if (!TryFindPreviousClip(directMoveTimelineClip, out TimelineClip previous))
            {
                if (actor is PathMoveActor noPrev)
                    return noPrev.ResolveHomeRotationOrFallback(
                        Quaternion.identity, "DirectMove 开场朝向");
                return Quaternion.identity;
            }

            return ResolveClipEffectiveExitRotation(
                previous, actor, resolver, silentHome);
        }

        /// <summary>
        /// 原地旋转开场位姿：位置取同轨紧邻前序结束落点（无前序用组件 Home 位置）；
        /// 朝向可 Clip 上的 <see cref="RotateClipData.StartYawDegrees"/>。
    /// 位置须缓存（见 <see cref="RotateBehaviour.EnsurePositionResolved"/>）。
    /// </summary>
        public static bool TryResolveRotateIncomingPose(
            TimelineClip rotateTimelineClip,
            PathMoveActor actor,
            IExposedPropertyTable resolver,
            out Vector3 position,
            out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            RotateClipData data = null;
            if (rotateTimelineClip?.asset is RotateClip rotateClip)
                data = rotateClip.Data;

            if (!TryFindPreviousClip(rotateTimelineClip, out TimelineClip previous))
            {
                if (actor == null)
                    return false;
                position = actor.ResolveHomePositionOrFallback("Rotate 开场位置");
                rotation = RotateSampler.EvaluateStartRotation(actor, data);
                return true;
            }

            if (!TryResolveClipExitPose(previous, actor, resolver, out position, out _))
                return false;

            rotation = RotateSampler.EvaluateStartRotation(actor, data);
            return true;
        }

        /// <summary>
        /// 原地旋转结束位姿：位置与进入时相同；
        /// 朝向 = StartYawDegrees + 方向×AngleDegrees（由进度 t=1 直接算出）。
    /// </summary>
        public static bool TryResolveRotateExitPose(
            TimelineClip rotateTimelineClip,
            RotateClip rotateAsset,
            IExposedPropertyTable resolver,
            out Vector3 position,
            out Quaternion rotation,
            PathMoveActor actor = null)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (rotateAsset?.Data == null)
                return false;

            if (!TryResolveRotateIncomingPose(
                    rotateTimelineClip, actor, resolver, out position, out _))
                return false;

            rotation = RotateSampler.EvaluateEndRotation(actor, rotateAsset.Data);
            return true;
        }

        /// <summary>
        /// 三点转向开场位姿：同轨紧邻前序结束位姿；无前序时用组件 Home。
    /// 须缓存（见 <see cref="ThreePointTurnBehaviour.EnsurePoseResolved"/>）。
    /// </summary>
        public static bool TryResolveThreePointTurnIncomingPose(
            TimelineClip turnTimelineClip,
            PathMoveActor actor,
            IExposedPropertyTable resolver,
            out Vector3 position,
            out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            if (!TryFindPreviousClip(turnTimelineClip, out TimelineClip previous))
            {
                if (actor == null)
                    return false;
                actor.ResolveHomePoseOrFallback(out position, out rotation, "ThreePointTurn 开场");
                return true;
            }

            return TryResolveClipExitPose(previous, actor, resolver, out position, out rotation);
        }

        /// <summary>
        /// 三点转向结束位姿：位置回到开场点；朝向相对开场车头左转或右转 90°。
    /// </summary>
        public static bool TryResolveThreePointTurnExitPose(
            TimelineClip turnTimelineClip,
            ThreePointTurnClip turnAsset,
            IExposedPropertyTable resolver,
            out Vector3 position,
            out Quaternion rotation,
            PathMoveActor actor = null)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (turnAsset?.Data == null)
                return false;

            if (!TryResolveThreePointTurnIncomingPose(
                    turnTimelineClip, actor, resolver, out Vector3 incomingPos, out Quaternion incomingRot))
                return false;

            return ThreePointTurnSampler.TryGetEndPose(
                actor, turnAsset.Data, incomingPos, incomingRot, out position, out rotation);
        }

        /// <summary>解析任意已支持 Clip 的结束车体位姿；未知类型回退组件 Home，再不行失败。</summary>
        public static bool TryResolveClipExitPose(
            TimelineClip clip,
            ScriptAnimActor actor,
            IExposedPropertyTable resolver,
            out Vector3 position,
            out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (clip == null)
                return false;

            var rail = actor as PathMoveActor;

            if (clip.asset is PathMoveClip pathAsset)
                return TryResolvePathMoveExitPose(
                    clip, pathAsset, resolver, out position, out rotation, out _, rail);

            if (clip.asset is DirectMoveClip directAsset)
                return TryResolveDirectMoveExitPose(
                    clip, directAsset, resolver, out position, out rotation, rail);

            if (clip.asset is TeleportClip teleportAsset)
                return TryEvaluateTeleportEndPose(
                    clip, teleportAsset, actor, resolver, out position, out rotation);

            if (clip.asset is RotateClip rotateAsset)
                return TryResolveRotateExitPose(
                    clip, rotateAsset, resolver, out position, out rotation, rail);

            if (ManeuverHomeUtility.TryResolvePathManeuverExitPose(
                    clip, rail, resolver, out position, out rotation))
                return true;

            if (clip.asset is ForkliftClip prevFork && actor is ForkliftAnim forklift)
                return TryGetForkliftExitPose(
                    clip, prevFork, forklift, resolver, out position, out rotation);

            if (clip.asset is LatentAgvClip prevLatent && actor is LatentAgvAnim latent)
                return TryGetLatentAgvExitPose(
                    clip, prevLatent, latent, resolver, out position, out rotation);

            if (clip.asset is CtuClip && rail != null)
                return TryGetCtuExitPose(clip, rail, resolver, out position, out rotation);

            if (clip.asset is ShuttleClip && rail != null)
                return TryGetShuttleExitPose(clip, rail, resolver, out position, out rotation);

            if (clip.asset is StackerClip stackerAsset && actor is StackerAnim stacker)
                return TryResolveStackerExitPose(
                    clip, stackerAsset, stacker, resolver, out position, out rotation);

            if (clip.asset is StackerForkClip)
            {
                // 货叉不动车体：结束位姿 = 前序结束 / Home
                if (TryFindPreviousClip(clip, out TimelineClip beforeFork))
                    return TryResolveClipExitPose(
                        beforeFork, actor, resolver, out position, out rotation);
                if (actor is StackerAnim forkStacker && forkStacker.HasHome)
                {
                    position = forkStacker.HomePosition;
                    rotation = Quaternion.identity;
                    return true;
                }

                return false;
            }

            if (rail != null && rail.TryGetHomePose(out position, out rotation))
                return true;

            return false;
        }

        /// <summary>堆垛机结束位姿：终点货位世界坐标；朝向沿用前序（堆垛机不改 yaw）。</summary>
        static bool TryResolveStackerExitPose(
            TimelineClip clip,
            StackerClip stackerAsset,
            StackerAnim stacker,
            IExposedPropertyTable resolver,
            out Vector3 position,
            out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (stackerAsset?.Data == null || stacker == null)
                return false;

            if (!StackerSampler.TryResolveEndWorld(stacker, stackerAsset.Data, out position))
                return false;

            if (TryFindPreviousClip(clip, out TimelineClip previous) &&
                TryResolveClipExitPose(previous, stacker, resolver, out _, out rotation))
                return true;

            rotation = Quaternion.identity;
            return true;
        }

        private static Quaternion GetFlatFacing(
            PathMoveActor actor, Vector3 from, Vector3 to, Quaternion fallback,
            bool reverseFacing = false)
        {
            Vector3 moveDir = to - from;
            Vector3 faceDir = PathMoveSampler.FacingDirection(moveDir, reverseFacing);
            if (actor != null)
                return actor.LookRotation(faceDir, fallback);

            Vector3 dir = faceDir;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f)
                return fallback;
            return Quaternion.LookRotation(dir.normalized, Vector3.up);
        }
    }
}
