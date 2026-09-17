using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 移动轨 Mixer：位移类 Clip 推进位姿后，若存在 Target 属于本 Actor 层级的
    /// WorldRotationLock，再写一次锁定旋转（Lock 本身由专用轨驱动，此处仅防父节点转动带偏）。
    /// </summary>
    public class ScriptMovementMixerBehaviour : ScriptAnimMixerBase
    {
        protected override void AfterNoActiveClip(Playable playable, ScriptAnimActor actor, FrameData info)
        {
            // 首个 Clip 之前：车体/机构回到组件 Home 与行驶默认态，避免任意 seek 残留
            if (actor is ForkliftAnim forklift)
                forklift.ApplyDefaultTravelPose();
            else if (actor is LatentAgvAnim latent)
                latent.ApplyDefaultTravelPose();
            else if (actor is CtuAnim ctu)
                ctu.ApplyDefaultTravelPose();
            else if (actor is ShuttleAnim shuttle)
                shuttle.ApplyDefaultTravelPose();
            else if (actor is StackerAnim stacker)
                stacker.ApplyDefaultTravelPose();
            else if (actor is PathMoveActor rail)
                rail.ApplyHomePose("首 Clip 之前");
        }

        /// <summary>
        /// 位移后补写：专用轨上的 Lock 可能先于本轨评估；父节点一转，子节点世界旋转会被带偏。
        /// 仅处理 Target 在本 Actor 层级下的 Lock，与 WorldRotationLockAnim 挂在哪无关。
        /// </summary>
        private void TryApplyWorldRotationLock(ScriptAnimActor actor)
        {
            if (Director == null || actor == null)
                return;
            var timeline = Director.playableAsset as TimelineAsset;
            if (!WorldRotationLockUtility.TryGetActiveLockForActor(
                    timeline, Director.time, Director, actor,
                    out WorldRotationLockAnim lockAnim, out WorldRotationLockClipData data))
                return;
            if (lockAnim == null || data == null || !data.Enabled)
                return;
            lockAnim.ApplyWorldRotation(Quaternion.Euler(data.LockEulerAngles));
        }

        protected override bool TryProcessInput(
            Playable input, ScriptAnimActor actor, FrameData info, bool holdEnd, TimelineClip timelineClip)
        {
            Type type = input.GetPlayableType();
            if (type == typeof(PathMoveBehaviour))
                return ProcessPathMove(input, actor, info, holdEnd, timelineClip);
            if (type == typeof(DirectMoveBehaviour))
                return ProcessDirectMove(input, actor, info, holdEnd, timelineClip);
            if (type == typeof(RotateBehaviour))
                return ProcessRotate(input, actor, info, holdEnd, timelineClip);
            if (type == typeof(ThreePointTurnBehaviour))
                return ProcessThreePointTurn(input, actor, info, holdEnd, timelineClip);
            if (type == typeof(BezierCornerBehaviour))
                return ProcessBezierCorner(input, actor, info, holdEnd, timelineClip);
            if (type == typeof(BezierDualCornerBehaviour))
                return ProcessBezierDualCorner(input, actor, info, holdEnd, timelineClip);
            if (type == typeof(ReverseUTurnBehaviour))
                return ProcessReverseUTurn(input, actor, info, holdEnd, timelineClip);
            if (type == typeof(TeleportBehaviour))
                return ProcessTeleport(input, actor, info, holdEnd, timelineClip);
            if (type == typeof(ForkliftBehaviour))
                return ProcessForklift(input, actor, info, holdEnd, timelineClip);
            if (type == typeof(LatentAgvBehaviour))
                return ProcessLatentAgv(input, actor, info, holdEnd, timelineClip);
            if (type == typeof(CtuBehaviour))
                return ProcessCtu(input, actor, info, holdEnd, timelineClip);
            if (type == typeof(ShuttleBehaviour))
                return ProcessShuttle(input, actor, info, holdEnd, timelineClip);
            if (type == typeof(StackerBehaviour))
                return ProcessStacker(input, actor, info, holdEnd, timelineClip);
            if (type == typeof(StackerForkBehaviour))
                return ProcessStackerFork(input, actor, holdEnd, timelineClip);
            if (type == typeof(CommentBehaviour))
                return false;
            return false;
        }

        private bool ProcessPathMove(
            Playable input, ScriptAnimActor actor, FrameData info, bool holdEnd, TimelineClip timelineClip)
        {
            var rail = actor as PathMoveActor;
            if (rail == null)
            {
                Debug.LogWarning("[ScriptAnim] PathMoveClip 需要绑定 PathMoveActor（或 ForkliftAnim）", actor);
                return true;
            }
            var inputPlayable = (ScriptPlayable<PathMoveBehaviour>)input;
            PathMoveBehaviour behaviour = inputPlayable.GetBehaviour();
            if (behaviour?.Data == null)
                return true;
            IExposedPropertyTable resolver = Director != null
                ? Director
                : (IExposedPropertyTable)null;
            // scrub / seek 时清除 IncomingResolved，强制重解路径开场（含 RobotArm fallback）
            if (info.seekOccurred)
                behaviour.IncomingResolved = false;
            if (!behaviour.EnsureIncomingResolved(timelineClip, rail, resolver))
                return true;
            PathMoveSampler.Sample(
                rail,
                behaviour.Data,
                behaviour.CachedPoints,
                behaviour.CachedAccum,
                behaviour.CachedTotalLen,
                NormalizedTime(inputPlayable, holdEnd),
                behaviour.IncomingRotation,
                behaviour.CachedPauses);
            TryApplyWorldRotationLock(rail);
            return true;
        }

        private bool ProcessDirectMove(
            Playable input, ScriptAnimActor actor, FrameData info, bool holdEnd, TimelineClip timelineClip)
        {
            var rail = actor as PathMoveActor;
            if (rail == null)
            {
                Debug.LogWarning("[ScriptAnim] DirectMoveClip 需要绑定 PathMoveActor（或 ForkliftAnim）", actor);
                return true;
            }
            var inputPlayable = (ScriptPlayable<DirectMoveBehaviour>)input;
            DirectMoveBehaviour behaviour = inputPlayable.GetBehaviour();
            if (behaviour?.Data == null)
                return true;
            IExposedPropertyTable resolver = Director != null
                ? Director
                : (IExposedPropertyTable)null;
            if (info.seekOccurred)
                behaviour.IncomingResolved = false;
            if (!behaviour.EnsureIncomingResolved(timelineClip, rail, resolver))
                return true;
            DirectMoveSampler.Sample(
                rail,
                behaviour.Data,
                behaviour.CachedStart,
                behaviour.CachedEnd,
                NormalizedTime(inputPlayable, holdEnd),
                behaviour.IncomingRotation);
            TryApplyWorldRotationLock(rail);
            return true;
        }

        private bool ProcessRotate(
            Playable input, ScriptAnimActor actor, FrameData info, bool holdEnd, TimelineClip timelineClip)
        {
            var rail = actor as PathMoveActor;
            if (rail == null)
            {
                Debug.LogWarning("[ScriptAnim] RotateClip 需要绑定 PathMoveActor（或 ForkliftAnim）", actor);
                return true;
            }
            var inputPlayable = (ScriptPlayable<RotateBehaviour>)input;
            RotateBehaviour behaviour = inputPlayable.GetBehaviour();
            if (behaviour?.Data == null)
                return true;
            IExposedPropertyTable resolver = Director != null
                ? Director
                : (IExposedPropertyTable)null;
            if (info.seekOccurred)
                behaviour.PositionResolved = false;

            float normalized = NormalizedTime(inputPlayable, holdEnd);
            // 任意 seek 须同时钉住落点；RotationOnly 也不再依赖场景当前位置
            if (!behaviour.EnsurePositionResolved(timelineClip, rail, resolver))
                return true;
            RotateSampler.Sample(
                rail,
                behaviour.Data,
                behaviour.CachedPosition,
                normalized);

            TryApplyWorldRotationLock(rail);
            return true;
        }

        private bool ProcessThreePointTurn(
            Playable input, ScriptAnimActor actor, FrameData info, bool holdEnd, TimelineClip timelineClip)
        {
            var rail = actor as PathMoveActor;
            if (rail == null)
            {
                Debug.LogWarning("[ScriptAnim] ThreePointTurnClip 需要绑定 PathMoveActor（或 ForkliftAnim）", actor);
                return true;
            }
            var inputPlayable = (ScriptPlayable<ThreePointTurnBehaviour>)input;
            ThreePointTurnBehaviour behaviour = inputPlayable.GetBehaviour();
            if (behaviour?.Data == null)
                return true;
            IExposedPropertyTable resolver = Director != null
                ? Director
                : (IExposedPropertyTable)null;
            if (info.seekOccurred)
                behaviour.PoseResolved = false;
            if (!behaviour.EnsurePoseResolved(timelineClip, rail, resolver))
                return true;
            ThreePointTurnSampler.Sample(
                rail,
                behaviour.Data,
                behaviour.CachedPlan,
                NormalizedTime(inputPlayable, holdEnd));
            TryApplyWorldRotationLock(rail);
            return true;
        }

        private bool ProcessBezierCorner(
            Playable input, ScriptAnimActor actor, FrameData info, bool holdEnd, TimelineClip timelineClip)
        {
            var rail = actor as PathMoveActor;
            if (rail == null)
            {
                Debug.LogWarning("[ScriptAnim] BezierCornerClip 需要绑定 PathMoveActor（或 ForkliftAnim）", actor);
                return true;
            }
            var inputPlayable = (ScriptPlayable<BezierCornerBehaviour>)input;
            BezierCornerBehaviour behaviour = inputPlayable.GetBehaviour();
            if (behaviour?.Data == null)
                return true;
            IExposedPropertyTable resolver = Director != null ? Director : null;
            if (info.seekOccurred)
                behaviour.PoseResolved = false;
            if (!behaviour.EnsurePoseResolved(timelineClip, rail, resolver))
                return true;
            BezierCornerSampler.Sample(
                rail,
                behaviour.Data,
                behaviour.CornerNode,
                behaviour.PrevNode,
                behaviour.NextNode,
                behaviour.IncomingPosition,
                behaviour.IncomingRotation,
                NormalizedTime(inputPlayable, holdEnd));
            TryApplyWorldRotationLock(rail);
            return true;
        }

        private bool ProcessBezierDualCorner(
            Playable input, ScriptAnimActor actor, FrameData info, bool holdEnd, TimelineClip timelineClip)
        {
            var rail = actor as PathMoveActor;
            if (rail == null)
            {
                Debug.LogWarning("[ScriptAnim] BezierDualCornerClip 需要绑定 PathMoveActor（或 ForkliftAnim）", actor);
                return true;
            }
            var inputPlayable = (ScriptPlayable<BezierDualCornerBehaviour>)input;
            BezierDualCornerBehaviour behaviour = inputPlayable.GetBehaviour();
            if (behaviour?.Data == null)
                return true;
            IExposedPropertyTable resolver = Director != null ? Director : null;
            if (info.seekOccurred)
                behaviour.PoseResolved = false;
            if (!behaviour.EnsurePoseResolved(timelineClip, rail, resolver))
                return true;
            BezierDualCornerSampler.Sample(
                rail,
                behaviour.Data,
                behaviour.CornerNodeA,
                behaviour.CornerNodeB,
                behaviour.PrevNode,
                behaviour.NextNode,
                behaviour.IncomingPosition,
                behaviour.IncomingRotation,
                NormalizedTime(inputPlayable, holdEnd));
            TryApplyWorldRotationLock(rail);
            return true;
        }

        private bool ProcessReverseUTurn(
            Playable input, ScriptAnimActor actor, FrameData info, bool holdEnd, TimelineClip timelineClip)
        {
            var rail = actor as PathMoveActor;
            if (rail == null)
            {
                Debug.LogWarning("[ScriptAnim] ReverseUTurnClip 需要绑定 PathMoveActor（或 ForkliftAnim）", actor);
                return true;
            }
            var inputPlayable = (ScriptPlayable<ReverseUTurnBehaviour>)input;
            ReverseUTurnBehaviour behaviour = inputPlayable.GetBehaviour();
            if (behaviour?.Data == null)
                return true;
            IExposedPropertyTable resolver = Director != null ? Director : null;
            if (info.seekOccurred)
                behaviour.PoseResolved = false;
            if (!behaviour.EnsurePoseResolved(timelineClip, rail, resolver))
                return true;
            ReverseUTurnSampler.Sample(
                rail,
                behaviour.Data,
                behaviour.CornerNode,
                behaviour.PrevNode,
                behaviour.NextNode,
                behaviour.IncomingPosition,
                behaviour.IncomingRotation,
                NormalizedTime(inputPlayable, holdEnd));
            TryApplyWorldRotationLock(rail);
            return true;
        }

        private bool ProcessTeleport(
            Playable input, ScriptAnimActor actor, FrameData info, bool holdEnd, TimelineClip timelineClip)
        {
            var rail = actor as PathMoveActor;
            if (rail == null)
            {
                Debug.LogWarning("[ScriptAnim] TeleportClip 需要绑定 PathMoveActor（或 ForkliftAnim）", actor);
                return true;
            }
            var inputPlayable = (ScriptPlayable<TeleportBehaviour>)input;
            TeleportBehaviour behaviour = inputPlayable.GetBehaviour();
            if (behaviour?.Data == null)
                return true;
            // Clip 覆盖区间与 hold 终点均每帧写入落点，保证任意 seek 状态唯一
            IExposedPropertyTable resolver = Director != null
                ? Director
                : (IExposedPropertyTable)null;
            Quaternion fallback = ScriptAnimHomeResolver.ResolveTeleportFallbackRotation(
                timelineClip, rail, resolver);
            if (!TeleportSampler.TryResolvePose(
                    behaviour.TargetNode,
                    behaviour.Data,
                    fallback,
                    out Vector3 position,
                    out Quaternion rotation,
                    rail))
            {
                Debug.LogWarning(
                    $"[Teleport] 未解析目标 target={behaviour.TargetNode} useWorld={behaviour.Data.UseWorldPosition}",
                    rail);
                return true;
            }
            TeleportSampler.Sample(rail, behaviour.Data, position, rotation);
            TryApplyWorldRotationLock(rail);
            return true;
        }

        private bool ProcessForklift(
            Playable input, ScriptAnimActor actor, FrameData info, bool holdEnd, TimelineClip timelineClip)
        {
            var anim = actor as ForkliftAnim;
            if (anim == null)
            {
                Debug.LogWarning("[ScriptAnim] ForkliftClip 需要绑定 ForkliftAnim", actor);
                return true;
            }
            var inputPlayable = (ScriptPlayable<ForkliftBehaviour>)input;
            ForkliftBehaviour behaviour = inputPlayable.GetBehaviour();
            if (behaviour?.Data == null)
                return true;
            if (behaviour.Station == null)
            {
                Debug.LogWarning("[Forklift] Station ExposedReference 未解析到 ScriptAnimPoint", anim);
                return true;
            }
            IExposedPropertyTable resolver = Director != null
                ? Director
                : (IExposedPropertyTable)null;
            if (info.seekOccurred)
                behaviour.HomeResolved = false;
            if (!behaviour.EnsureHomeResolved(timelineClip, anim, resolver))
                return true;
            ForkliftSampler.Sample(
                anim,
                behaviour.Data,
                ScriptAnimPointUtility.AsTransform(behaviour.Station),
                behaviour.CachedHomePos,
                behaviour.CachedHomeRot,
                behaviour.CachedRotateMode,
                NormalizedTime(inputPlayable, holdEnd));
            TryApplyWorldRotationLock(anim);
            return true;
        }

        private bool ProcessLatentAgv(
            Playable input, ScriptAnimActor actor, FrameData info, bool holdEnd, TimelineClip timelineClip)
        {
            var anim = actor as LatentAgvAnim;
            if (anim == null)
            {
                Debug.LogWarning("[ScriptAnim] LatentAgvClip 需要绑定 LatentAgvAnim", actor);
                return true;
            }
            var inputPlayable = (ScriptPlayable<LatentAgvBehaviour>)input;
            LatentAgvBehaviour behaviour = inputPlayable.GetBehaviour();
            if (behaviour?.Data == null)
                return true;
            if (behaviour.MovePoint == null)
            {
                Debug.LogWarning("[LatentAgv] MovePoint ExposedReference 未解析到 ScriptAnimPoint", anim);
                return true;
            }
            IExposedPropertyTable resolver = Director != null
                ? Director
                : (IExposedPropertyTable)null;
            if (info.seekOccurred)
                behaviour.HomeResolved = false;
            if (!behaviour.EnsureHomeResolved(timelineClip, anim, resolver))
                return true;
            LatentAgvSampler.Sample(
                anim,
                behaviour.Data,
                ScriptAnimPointUtility.AsTransform(behaviour.MovePoint),
                behaviour.CachedHomePos,
                behaviour.CachedHomeRot,
                behaviour.CachedRotateMode,
                NormalizedTime(inputPlayable, holdEnd));
            TryApplyWorldRotationLock(anim);
            return true;
        }

        private bool ProcessCtu(
            Playable input, ScriptAnimActor actor, FrameData info, bool holdEnd, TimelineClip timelineClip)
        {
            var anim = actor as CtuAnim;
            if (anim == null)
            {
                Debug.LogWarning("[ScriptAnim] CtuClip 需要绑定 CtuAnim", actor);
                return true;
            }
            var inputPlayable = (ScriptPlayable<CtuBehaviour>)input;
            CtuBehaviour behaviour = inputPlayable.GetBehaviour();
            if (behaviour?.Data == null)
                return true;
            if (behaviour.Station == null)
            {
                Debug.LogWarning("[CTU] Station ExposedReference 未解析到 ScriptAnimPoint", anim);
                return true;
            }
            IExposedPropertyTable resolver = Director != null
                ? Director
                : (IExposedPropertyTable)null;
            if (info.seekOccurred)
                behaviour.HomeResolved = false;
            if (!behaviour.EnsureHomeResolved(timelineClip, anim, resolver))
                return true;
            CtuSampler.Sample(
                anim,
                behaviour.Data,
                ScriptAnimPointUtility.AsTransform(behaviour.Station),
                behaviour.CachedHomePos,
                behaviour.CachedHomeRot,
                behaviour.CachedStartRotateAngle,
                behaviour.CachedStartLiftHeight,
                behaviour.CachedStartClawExtend,
                behaviour.CachedStartPaddleAngle,
                NormalizedTime(inputPlayable, holdEnd));
            return true;
        }

        private bool ProcessShuttle(
            Playable input, ScriptAnimActor actor, FrameData info, bool holdEnd, TimelineClip timelineClip)
        {
            var anim = actor as ShuttleAnim;
            if (anim == null)
            {
                Debug.LogWarning("[ScriptAnim] ShuttleClip 需要绑定 ShuttleAnim", actor);
                return true;
            }
            var inputPlayable = (ScriptPlayable<ShuttleBehaviour>)input;
            ShuttleBehaviour behaviour = inputPlayable.GetBehaviour();
            if (behaviour?.Data == null)
                return true;
            IExposedPropertyTable resolver = Director != null
                ? Director
                : (IExposedPropertyTable)null;
            if (info.seekOccurred)
                behaviour.HomeResolved = false;
            if (!behaviour.EnsureHomeResolved(timelineClip, anim, resolver))
                return true;
            ShuttleSampler.Sample(
                anim,
                behaviour.Data,
                behaviour.CachedHomePos,
                behaviour.CachedHomeRot,
                behaviour.CachedStartClawExtend,
                behaviour.CachedStartClampOffset,
                behaviour.CachedStartPaddleAngle,
                NormalizedTime(inputPlayable, holdEnd));
            return true;
        }

        private bool ProcessStacker(
            Playable input, ScriptAnimActor actor, FrameData info, bool holdEnd, TimelineClip timelineClip)
        {
            var anim = actor as StackerAnim;
            if (anim == null)
            {
                Debug.LogWarning("[ScriptAnim] StackerClip 需要绑定 StackerAnim", actor);
                return true;
            }
            var inputPlayable = (ScriptPlayable<StackerBehaviour>)input;
            StackerBehaviour behaviour = inputPlayable.GetBehaviour();
            if (behaviour?.Data == null)
                return true;
            if (info.seekOccurred)
            {
                behaviour.EndpointsResolved = false;
                behaviour.ResolveFailed = false;
            }
            if (!behaviour.EnsureEndpointsResolved(anim, timelineClip))
                return true;
            StackerSampler.Sample(
                anim,
                behaviour.Data,
                behaviour.CachedStart,
                behaviour.CachedEnd,
                NormalizedTime(inputPlayable, holdEnd));
            return true;
        }

        private bool ProcessStackerFork(
            Playable input, ScriptAnimActor actor, bool holdEnd, TimelineClip timelineClip)
        {
            var anim = actor as StackerAnim;
            if (anim == null)
            {
                Debug.LogWarning("[ScriptAnim] StackerForkClip 需要绑定 StackerAnim", actor);
                return true;
            }
            var inputPlayable = (ScriptPlayable<StackerForkBehaviour>)input;
            StackerForkBehaviour behaviour = inputPlayable.GetBehaviour();
            if (behaviour?.Data == null)
                return true;
            if (anim.PrimaryFork == null && anim.SecondaryFork == null)
            {
                Debug.LogWarning("[StackerFork] StackerAnim 未绑定一级或二级货叉", anim);
                return true;
            }

            // 货叉 Clip 不改车体；seek 时仍须钉住前序 Stacker 终点或 Home，避免槽位粘滞
            var fallback = StackerSampler.ResolvePreviousClipEnd(anim, timelineClip);
            if (fallback.HasValue)
                anim.ApplySlotPosition(fallback.Value);
            else if (anim.HasHome)
                anim.ApplyHomePose();

            StackerForkSampler.Sample(anim, behaviour.Data, NormalizedTime(inputPlayable, holdEnd));
            return true;
        }
    }
}
