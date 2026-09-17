using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation
{
    /// <summary>
    /// 专用轨 Mixer：Fade / RobotArm / WorldRotationLock 等非位移类 Clip。
    /// 业务上一条轨实例通常只使用一种 Anim/Clip。
    /// </summary>
    public class ScriptDedicatedMixerBehaviour : ScriptAnimMixerBase
    {
        SequentialPositionAnim m_sequentialAnim;
        int m_robotArmMixerDebugTick;

        protected override void OnMixerDestroyAfterPoseRestore(Playable playable)
        {
            if (!HasInitialPose || BoundActor == null)
            {
                if (m_sequentialAnim != null)
                    m_sequentialAnim.Hide();
                return;
            }

            if (PostPlaybackState == ScriptAnimTrackBase.PostPlaybackState.Revert)
            {
                if (BoundActor is OpenBoxAnim openBox)
                {
                    openBox.RevertToRest();
                    openBox.EndTimelineDrive();
                }

                if (BoundActor is FilmWrapAnim filmWrap)
                {
                    filmWrap.RevertToRest();
                    filmWrap.EndTimelineDrive();
                }

                if (BoundActor is FadeAnim fade)
                    fade.RevertToRest();

                if (BoundActor is BlinkAnim blink)
                    blink.RevertToRest();

                if (BoundActor is ObjectSwitchAnim objectSwitch)
                    objectSwitch.RevertToRest();

                if (BoundActor is SequentialPositionAnim sequential)
                    sequential.RevertToRest();
            }
            else
            {
                if (BoundActor is OpenBoxAnim leaveBox)
                {
                    leaveBox.CommitSampledPose();
                    leaveBox.ClearRest();
                    leaveBox.EndTimelineDrive();
                }

                if (BoundActor is FilmWrapAnim leaveWrap)
                    leaveWrap.EndTimelineDrive();

                if (BoundActor is FadeAnim leaveFade)
                    leaveFade.ClearRest();

                if (BoundActor is BlinkAnim leaveBlink)
                    leaveBlink.ClearRest();

                if (BoundActor is ObjectSwitchAnim leaveSwitch)
                    leaveSwitch.ClearRest();

                if (BoundActor is SequentialPositionAnim leaveSeq)
                    leaveSeq.ClearRest();
            }
        }

        protected override void BeforeProcessInputs(Playable playable, ScriptAnimActor actor, FrameData info)
        {
            if (actor is SequentialPositionAnim sequential)
            {
                m_sequentialAnim = sequential;
                sequential.CaptureRestIfNeeded();
            }

            if (actor is OpenBoxAnim openBox)
            {
                openBox.BeginTimelineDrive();
                openBox.CaptureRestIfNeeded();
            }

            if (actor is FilmWrapAnim filmWrap)
            {
                filmWrap.BeginTimelineDrive();
                filmWrap.CaptureRestIfNeeded();
            }

            if (actor is FadeAnim fade)
                fade.CaptureRestIfNeeded();

            if (actor is BlinkAnim blink)
                blink.CaptureRestIfNeeded();

            if (actor is ObjectSwitchAnim objectSwitch)
                objectSwitch.CaptureRestIfNeeded();

            if (actor is RobotArmAnim debugArm && debugArm.DebugForcePlayback)
                LogRobotArmMixerInputs(playable, debugArm);

            if (actor is RobotArm5Anim debugArm5 && debugArm5.DebugForcePlayback)
                LogRobotArm5MixerInputs(playable, debugArm5);
        }

        protected override void AfterNoActiveClip(Playable playable, ScriptAnimActor actor, FrameData info)
        {
            if (actor is SequentialPositionAnim seq)
                seq.Hide();

            if (actor is ObjectSwitchAnim objectSwitch)
                objectSwitch.SetVisibleIndex(-1);

            // 时间轴在第一个相关 Clip 之前：无已结束 clip 可 hold，回到开场默认态
            if (actor is PoseChangeAnimMax poseMax)
                poseMax.ApplyPoses(poseMax.ResolveDefaultStartPoses());
            else if (actor is IPoseChangeActor poseChange)
                poseChange.ApplyPose(poseChange.ResolveDefaultStartPose());
            else if (actor is OpenBoxAnim openBox)
                OpenBoxSampler.Sample(openBox, openBox.ResolveDefaultStartPose());
            else if (actor is FadeAnim fade)
                fade.ApplyCapturedRest();
            else if (actor is BlinkAnim blink)
                blink.ApplyCapturedRest();
            else if (actor is FilmWrapAnim filmWrap)
                filmWrap.ApplyCapturedRest();
            else if (actor is SwingFlipAnim swingFlip)
                swingFlip.ApplyPose(0f, 0f);
            else if (actor is RobotArmAnim robotArm)
                robotArm.ApplyHomePose();
            else if (actor is RobotArm5Anim robotArm5)
                robotArm5.ApplyHomePose();
            else if (actor is WorldRotationLockAnim && HasInitialPose)
            {
                // 仅首个 Lock Clip 之前：还原开场旋转。
                // Clip 结束后由 holdEnd 认领且不写入，保留离开瞬间的局部旋转，随后随父节点自然转动。
                InitialPose.Restore();
            }
        }

        protected override bool TryProcessInput(
            Playable input, ScriptAnimActor actor, FrameData info, bool holdEnd, TimelineClip timelineClip)
        {
            Type type = input.GetPlayableType();
            if (type == typeof(FadeBehaviour))
                return ProcessFade(input, actor, holdEnd);
            if (type == typeof(BlinkBehaviour))
                return ProcessBlink(input, actor, holdEnd);
            if (type == typeof(ObjectSwitchBehaviour))
                return ProcessObjectSwitch(input, actor, holdEnd);
            if (type == typeof(SwingFlipBehaviour))
                return ProcessSwingFlip(input, actor, holdEnd);
            if (type == typeof(SequentialPositionBehaviour))
                return ProcessSequentialPosition(input, actor, holdEnd);
            if (type == typeof(PoseChangeBehaviour))
                return ProcessPoseChange(input, actor, info, holdEnd, timelineClip);
            if (type == typeof(OpenBoxBehaviour))
                return ProcessOpenBox(input, actor, info, holdEnd, timelineClip);
            if (type == typeof(FilmWrapBehaviour))
                return ProcessFilmWrap(input, actor, holdEnd, timelineClip);
            if (type == typeof(RobotArmBehaviour))
                return ProcessRobotArm(input, actor, info, holdEnd, timelineClip);
            if (type == typeof(RobotArm5Behaviour))
                return ProcessRobotArm5(input, actor, info, holdEnd, timelineClip);
            if (type == typeof(WorldRotationLockBehaviour))
                return ProcessWorldRotationLock(input, actor, holdEnd);
            if (type == typeof(CommentBehaviour))
                return false;
            return false;
        }

        private bool ProcessFade(Playable input, ScriptAnimActor actor, bool holdEnd)
        {
            var anim = actor as FadeAnim;
            if (anim == null)
            {
                Debug.LogWarning("[ScriptAnim] FadeClip 需要绑定 FadeAnim", actor);
                return true;
            }

            var inputPlayable = (ScriptPlayable<FadeBehaviour>)input;
            FadeBehaviour behaviour = inputPlayable.GetBehaviour();
            if (behaviour?.Data == null)
                return true;

            FadeSampler.Sample(anim, behaviour.Data, NormalizedTime(inputPlayable, holdEnd));
            return true;
        }

        private bool ProcessBlink(Playable input, ScriptAnimActor actor, bool holdEnd)
        {
            var anim = actor as BlinkAnim;
            if (anim == null)
            {
                Debug.LogWarning("[ScriptAnim] BlinkClip 需要绑定 BlinkAnim", actor);
                return true;
            }

            var inputPlayable = (ScriptPlayable<BlinkBehaviour>)input;
            BlinkBehaviour behaviour = inputPlayable.GetBehaviour();
            if (behaviour?.Data == null)
                return true;

            BlinkSampler.Sample(anim, behaviour.Data, NormalizedTime(inputPlayable, holdEnd), holdEnd);
            return true;
        }

        /// <summary>
        /// Clip 覆盖时显示指定索引；不 hold 结束态（无覆盖时全部隐藏）。
        /// 刻意不 GatherProperties(m_IsActive)，见 <see cref="ScriptAnimTrackBase.GatherProperties"/>。
        /// </summary>
        private bool ProcessObjectSwitch(Playable input, ScriptAnimActor actor, bool holdEnd)
        {
            var anim = actor as ObjectSwitchAnim;
            if (anim == null)
            {
                Debug.LogWarning("[ScriptAnim] ObjectSwitchClip 需要绑定 ObjectSwitchAnim", actor);
                return true;
            }

            // 不 hold：Clip 结束后 / 空隙交给 AfterNoActiveClip 全部隐藏
            if (holdEnd)
                return false;

            var inputPlayable = (ScriptPlayable<ObjectSwitchBehaviour>)input;
            ObjectSwitchBehaviour behaviour = inputPlayable.GetBehaviour();
            if (behaviour?.Data == null)
                return true;

            anim.SetVisibleIndex(behaviour.Data.VisibleIndex);
            return true;
        }

        private bool ProcessSwingFlip(Playable input, ScriptAnimActor actor, bool holdEnd)
        {
            var anim = actor as SwingFlipAnim;
            if (anim == null)
            {
                Debug.LogWarning("[ScriptAnim] SwingFlipClip 需要绑定 SwingFlipAnim", actor);
                return true;
            }

            var inputPlayable = (ScriptPlayable<SwingFlipBehaviour>)input;
            SwingFlipBehaviour behaviour = inputPlayable.GetBehaviour();
            if (behaviour?.Data == null)
                return true;

            SwingFlipSampler.Sample(anim, behaviour.Data, NormalizedTime(inputPlayable, holdEnd));
            return true;
        }

        private bool ProcessSequentialPosition(Playable input, ScriptAnimActor actor, bool holdEnd)
        {
            var anim = actor as SequentialPositionAnim;
            if (anim == null)
            {
                Debug.LogWarning("[ScriptAnim] SequentialPositionClip 需要绑定 SequentialPositionAnim", actor);
                return true;
            }

            // 不 hold：Clip 外交给 AfterNoActiveClip 隐藏（与组件文档「播放前/结束后隐藏」一致）
            if (holdEnd)
                return false;

            var inputPlayable = (ScriptPlayable<SequentialPositionBehaviour>)input;
            SequentialPositionBehaviour behaviour = inputPlayable.GetBehaviour();
            if (behaviour?.Data == null)
                return true;

            SequentialPositionSampler.Sample(anim, (float)inputPlayable.GetTime());
            return true;
        }

        private bool ProcessPoseChange(
            Playable input, ScriptAnimActor actor, FrameData info, bool holdEnd, TimelineClip timelineClip)
        {
            if (actor is not IPoseChangeActor and not PoseChangeAnimMax)
            {
                Debug.LogWarning("[ScriptAnim] PoseChangeClip 需要绑定 PoseChangeAnim 或 PoseChangeAnimMax", actor);
                return true;
            }

            var inputPlayable = (ScriptPlayable<PoseChangeBehaviour>)input;
            PoseChangeBehaviour behaviour = inputPlayable.GetBehaviour();
            if (behaviour?.Data == null)
                return true;

            if (info.seekOccurred)
            {
                behaviour.IncomingResolved = false;
                behaviour.ResolveFailed = false;
            }

            if (!behaviour.EnsureIncomingResolved(timelineClip, actor))
            {
                // 目标姿态无效时仍写入确定态，避免 seek 后残留跳转前画面
                ApplyPoseChangeResolvedOrDefault(actor, timelineClip);
                return true;
            }

            float normalized = PoseChangeSampler.IsInstant(behaviour.Data)
                ? 1f
                : NormalizedTime(inputPlayable, holdEnd);

            if (behaviour.IsMultiTarget && actor is PoseChangeAnimMax maxAnim)
            {
                PoseChangeSampler.Sample(
                    maxAnim,
                    behaviour.Data,
                    behaviour.CachedStartMulti,
                    behaviour.CachedEndMulti,
                    normalized);
            }
            else if (actor is IPoseChangeActor anim)
            {
                PoseChangeSampler.Sample(
                    anim,
                    behaviour.Data,
                    behaviour.CachedStart,
                    behaviour.CachedEnd,
                    normalized);
            }

            return true;
        }

        /// <summary>
        /// 写入前序 PoseChange 终点；无前序则开场默认姿态。供解析失败或首 Clip 之前使用。
        /// </summary>
        static void ApplyPoseChangeResolvedOrDefault(ScriptAnimActor actor, TimelineClip timelineClip)
        {
            if (actor is PoseChangeAnimMax maxAnim)
            {
                if (PoseChangeSampler.TryResolvePreviousEndPose(maxAnim, timelineClip, out var poses))
                    maxAnim.ApplyPoses(poses);
                else
                    maxAnim.ApplyPoses(maxAnim.ResolveDefaultStartPoses());
                return;
            }

            if (actor is IPoseChangeActor anim)
            {
                if (PoseChangeSampler.TryResolvePreviousEndPose(anim, timelineClip, out var pose))
                    anim.ApplyPose(pose);
                else
                    anim.ApplyPose(anim.ResolveDefaultStartPose());
            }
        }

        private bool ProcessOpenBox(
            Playable input, ScriptAnimActor actor, FrameData info, bool holdEnd, TimelineClip timelineClip)
        {
            var anim = actor as OpenBoxAnim;
            if (anim == null)
            {
                Debug.LogWarning("[ScriptAnim] OpenBoxClip 需要绑定 OpenBoxAnim", actor);
                return true;
            }

            var inputPlayable = (ScriptPlayable<OpenBoxBehaviour>)input;
            OpenBoxBehaviour behaviour = inputPlayable.GetBehaviour();
            if (behaviour?.Data == null)
                return true;

            if (info.seekOccurred)
            {
                behaviour.IncomingResolved = false;
                behaviour.ResolveFailed = false;
            }

            if (!behaviour.EnsureIncomingResolved(timelineClip, anim))
            {
                OpenBoxSampler.Sample(anim, OpenBoxSampler.ResolveStartPose(anim, timelineClip));
                return true;
            }

            float normalized = OpenBoxSampler.IsInstant(behaviour.Data)
                ? 1f
                : NormalizedTime(inputPlayable, holdEnd);
            OpenBoxPose pose = OpenBoxSampler.Evaluate(
                behaviour.CachedStart, behaviour.Data, normalized);
            OpenBoxSampler.Sample(anim, pose);
            return true;
        }

        private bool ProcessFilmWrap(
            Playable input, ScriptAnimActor actor, bool holdEnd, TimelineClip timelineClip)
        {
            var anim = actor as FilmWrapAnim;
            if (anim == null)
            {
                Debug.LogWarning("[ScriptAnim] FilmWrapClip 需要绑定 FilmWrapAnim", actor);
                return true;
            }

            var inputPlayable = (ScriptPlayable<FilmWrapBehaviour>)input;
            FilmWrapBehaviour behaviour = inputPlayable.GetBehaviour();
            if (behaviour?.Data == null)
                return true;

            FilmWrapSampler.Sample(
                anim, behaviour.Data, NormalizedClipTime(timelineClip, inputPlayable, holdEnd));
            return true;
        }

        private bool ProcessWorldRotationLock(Playable input, ScriptAnimActor actor, bool holdEnd)
        {
            var behaviour = ((ScriptPlayable<WorldRotationLockBehaviour>)input).GetBehaviour();
            if (behaviour?.Data == null || !behaviour.Data.Enabled)
                return false;

            // 仅覆盖区间内每帧写入世界旋转。
            // 结束后 hold：认领该帧但不写入——保留离开时的局部旋转，避免继续锁世界角，
            // 也不要落到 AfterNoActiveClip 把姿态瞬间还原成开场态。
            if (holdEnd)
                return true;

            if (actor is WorldRotationLockAnim lockAnim && lockAnim.Target != null)
                lockAnim.ApplyWorldRotation(Quaternion.Euler(behaviour.Data.LockEulerAngles));

            return true;
        }

        private void LogRobotArmMixerInputs(Playable playable, RobotArmAnim anim)
        {
            m_robotArmMixerDebugTick++;
            if (m_robotArmMixerDebugTick % 60 != 1)
                return;

            int inputCount = playable.GetInputCount();
            var sb = new System.Text.StringBuilder(256);
            for (int i = 0; i < inputCount; i++)
            {
                Playable child = playable.GetInput(i);
                sb.Append($"[{i}] w={playable.GetInputWeight(i):F3} ");
                sb.Append(child.IsValid() ? child.GetPlayableType().Name : "invalid");
                sb.Append(' ');
            }

            double directorTime = Director != null ? Director.time : -1d;
            Debug.Log(
                $"[RobotArm DBG] Mixer 输入：{sb}Director.t={directorTime:F2}",
                anim);
        }

        private void LogRobotArm5MixerInputs(Playable playable, RobotArm5Anim anim)
        {
            m_robotArmMixerDebugTick++;
            if (m_robotArmMixerDebugTick % 60 != 1)
                return;

            int inputCount = playable.GetInputCount();
            var sb = new System.Text.StringBuilder(256);
            for (int i = 0; i < inputCount; i++)
            {
                Playable child = playable.GetInput(i);
                sb.Append($"[{i}] w={playable.GetInputWeight(i):F3} ");
                sb.Append(child.IsValid() ? child.GetPlayableType().Name : "invalid");
                sb.Append(' ');
            }

            double directorTime = Director != null ? Director.time : -1d;
            Debug.Log(
                $"[RobotArm5 DBG] Mixer 输入：{sb}Director.t={directorTime:F2}",
                anim);
        }

        private bool ProcessRobotArm(
            Playable input, ScriptAnimActor actor, FrameData info, bool holdEnd, TimelineClip timelineClip)
        {
            var anim = actor as RobotArmAnim;
            if (anim == null)
            {
                Debug.LogWarning("[ScriptAnim] RobotArmClip 需要绑定 RobotArmAnim", actor);
                return true;
            }

            var inputPlayable = (ScriptPlayable<RobotArmBehaviour>)input;
            RobotArmBehaviour behaviour = inputPlayable.GetBehaviour();
            if (behaviour?.Data == null)
                return true;

            if (!anim.AreJointsAssigned())
            {
                Debug.LogWarning("[RobotArm] 关键 Transform 未正确绑定", anim);
                return true;
            }

            anim.EnsureRestPose();

            IExposedPropertyTable resolver = Director != null
                ? Director
                : (IExposedPropertyTable)null;
            TimelineClip ownerClip = FindTimelineClip(behaviour.ClipAsset) ?? timelineClip;
            bool forceDebug = RobotArmSampler.ShouldForceDebugPlayback(anim, behaviour.Data);
            if (forceDebug)
                behaviour.ClipAsset?.CachedPlans.Invalidate();

            RobotArmSampler.TimelineStartFallback fallback;
            if (forceDebug)
            {
                var resolved = RobotArmSampler.ResolvePreviousClipEnd(
                    anim, ownerClip, resolver);
                if (resolved.HasValue && resolved.HasJointAngles)
                    fallback = new RobotArmSampler.TimelineStartFallback(
                        resolved.Position, resolved.Rotation, resolved.JointAngles);
                else
                    fallback = resolved;
            }
            else
            {
                fallback = behaviour.EnsureFallbackResolved(
                    ownerClip, anim, resolver, info.seekOccurred);
            }

            Transform[] targets = behaviour.WaypointTargets;
            if (resolver != null && behaviour.ClipAsset != null)
                targets = behaviour.ClipAsset.ResolveWaypointTargets(resolver);

            Transform firstTarget = RobotArmSampler.GetTarget(targets, 0);
            float normalized = NormalizedClipTime(ownerClip, inputPlayable, holdEnd);
            float[] continuousStart = RobotArmSampler.GetContinuousStartAngles(
                behaviour.Data, fallback, firstTarget);
            float[] ikSeed = RobotArmSampler.GetIkSeedAngles(continuousStart, fallback);

            if (!RobotArmSampler.TryBuildPathPlans(
                    behaviour.ClipAsset, anim, behaviour.Data, targets, fallback,
                    out RobotArmSampler.PathMotionPlans path,
                    continuousStart, ikSeed,
                    useCache: !forceDebug))
            {
                Debug.LogWarning(
                    "[RobotArm] 无法解析点位链表（至少 1 个点位；路径为 Home → 点位… → Home）",
                    anim);
                return true;
            }

            if (forceDebug)
            {
                behaviour.DebugProcessFrameCount++;
                if (behaviour.DebugProcessFrameCount == 1 ||
                    behaviour.DebugProcessFrameCount % 30 == 0)
                {
                    float maxDelta = 0f;
                    int jointCount = anim.JointCount;
                    if (path.StartAngles != null && path.EndAngles != null)
                    {
                        for (int j = 0; j < jointCount; j++)
                        {
                            maxDelta = Mathf.Max(
                                maxDelta,
                                Mathf.Abs(Mathf.DeltaAngle(
                                    path.StartAngles[j], path.EndAngles[j])));
                        }
                    }

                    int segCount = path.Segments != null ? path.Segments.Length : 0;
                    Debug.Log(
                        $"[RobotArm DBG] ProcessRobotArm " +
                        $"n={normalized:F3} clip={ownerClip?.displayName ?? "?"} " +
                        $"segs={segCount} max?={maxDelta:F1}° dur={path.Duration:F3}s " +
                        $"holdEnd={holdEnd} seek={info.seekOccurred}",
                        anim);

                    if (maxDelta < 0.01f && behaviour.DebugProcessFrameCount == 1)
                    {
                        Debug.LogWarning(
                            "[RobotArm] 路径起/终点 IK 关节角相同（maxΔ≈0°），请检查目标是否可达或连杆参数",
                            anim);
                    }
                }
            }

            if (!RobotArmSampler.TrySamplePath(anim, path, normalized))
            {
                Debug.LogWarning(
                    $"[RobotArm] 采样失败：JointCount={anim.JointCount}，path 无效",
                    anim);
            }
            return true;
        }

        private bool ProcessRobotArm5(
            Playable input, ScriptAnimActor actor, FrameData info, bool holdEnd, TimelineClip timelineClip)
        {
            var anim = actor as RobotArm5Anim;
            if (anim == null)
            {
                Debug.LogWarning("[ScriptAnim] RobotArm5Clip 需要绑定 RobotArm5Anim", actor);
                return true;
            }

            var inputPlayable = (ScriptPlayable<RobotArm5Behaviour>)input;
            RobotArm5Behaviour behaviour = inputPlayable.GetBehaviour();
            if (behaviour?.Data == null)
                return true;

            if (!anim.AreJointsAssigned())
            {
                Debug.LogWarning("[RobotArm5] 关键 Transform 未正确绑定", anim);
                return true;
            }

            anim.EnsureRestPose();

            IExposedPropertyTable resolver = Director != null
                ? Director
                : (IExposedPropertyTable)null;
            TimelineClip ownerClip = FindTimelineClip(behaviour.ClipAsset) ?? timelineClip;
            bool forceDebug = RobotArm5Sampler.ShouldForceDebugPlayback(anim, behaviour.Data);
            if (forceDebug)
                behaviour.ClipAsset?.CachedPlans.Invalidate();

            RobotArm5Sampler.TimelineStartFallback fallback;
            if (forceDebug)
            {
                var resolved = RobotArm5Sampler.ResolvePreviousClipEnd(
                    anim, ownerClip, resolver);
                if (resolved.HasValue && resolved.HasJointAngles)
                    fallback = new RobotArm5Sampler.TimelineStartFallback(
                        resolved.Position, resolved.Rotation, resolved.JointAngles);
                else
                    fallback = resolved;
            }
            else
            {
                fallback = behaviour.EnsureFallbackResolved(
                    ownerClip, anim, resolver, info.seekOccurred);
            }

            Transform[] targets = behaviour.WaypointTargets;
            if (resolver != null && behaviour.ClipAsset != null)
                targets = behaviour.ClipAsset.ResolveWaypointTargets(resolver);

            Transform firstTarget = RobotArm5Sampler.GetTarget(targets, 0);
            float normalized = NormalizedClipTime(ownerClip, inputPlayable, holdEnd);
            float[] continuousStart = RobotArm5Sampler.GetContinuousStartAngles(
                behaviour.Data, fallback, firstTarget);
            float[] ikSeed = RobotArm5Sampler.GetIkSeedAngles(continuousStart, fallback);

            if (!RobotArm5Sampler.TryBuildPathPlans(
                    behaviour.ClipAsset, anim, behaviour.Data, targets, fallback,
                    out RobotArm5Sampler.PathMotionPlans path,
                    continuousStart, ikSeed,
                    useCache: !forceDebug))
            {
                Debug.LogWarning(
                    "[RobotArm5] 无法解析点位链表（至少 1 个点位；路径为 Home → 点位… → Home）",
                    anim);
                return true;
            }

            if (forceDebug)
            {
                behaviour.DebugProcessFrameCount++;
                if (behaviour.DebugProcessFrameCount == 1 ||
                    behaviour.DebugProcessFrameCount % 30 == 0)
                {
                    float maxDelta = 0f;
                    int jointCount = anim.JointCount;
                    if (path.StartAngles != null && path.EndAngles != null)
                    {
                        for (int j = 0; j < jointCount; j++)
                        {
                            maxDelta = Mathf.Max(
                                maxDelta,
                                Mathf.Abs(Mathf.DeltaAngle(
                                    path.StartAngles[j], path.EndAngles[j])));
                        }
                    }

                    int segCount = path.Segments != null ? path.Segments.Length : 0;
                    Debug.Log(
                        $"[RobotArm5 DBG] ProcessRobotArm5 " +
                        $"n={normalized:F3} clip={ownerClip?.displayName ?? "?"} " +
                        $"segs={segCount} max?={maxDelta:F1}° dur={path.Duration:F3}s " +
                        $"holdEnd={holdEnd} seek={info.seekOccurred}",
                        anim);

                    if (maxDelta < 0.01f && behaviour.DebugProcessFrameCount == 1)
                    {
                        Debug.LogWarning(
                            "[RobotArm5] 路径起/终点 IK 关节角相同（maxΔ≈0°），请检查目标是否可达或连杆参数",
                            anim);
                    }
                }
            }

            if (!RobotArm5Sampler.TrySamplePath(anim, path, normalized))
            {
                Debug.LogWarning(
                    $"[RobotArm5] 采样失败：JointCount={anim.JointCount}，path 无效",
                    anim);
            }
            return true;
        }
    }
}
