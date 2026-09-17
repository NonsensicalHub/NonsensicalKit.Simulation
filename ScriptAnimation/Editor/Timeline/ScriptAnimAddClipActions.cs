using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEditor.Timeline.Actions;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    /// <summary>
    /// ScriptAnimation Timeline 的 Add Clip 菜单项：按 Track / Actor 类型校验后创建。
    /// </summary>
    static class ScriptAnimAddClipUtility
    {
        public static ActionValidity ValidateEffectAny(
            IEnumerable<TrackAsset> tracks,
            params Type[] requiredActorTypes) =>
            ValidateDedicatedAny(tracks, requiredActorTypes);

        static ActionValidity ValidateDedicatedAny(
            IEnumerable<TrackAsset> tracks,
            params Type[] requiredActorTypes)
        {
            if (requiredActorTypes == null || requiredActorTypes.Length == 0 || tracks == null || !tracks.Any())
                return ActionValidity.NotApplicable;

            if (tracks.Any(t => t == null || t is not ScriptDedicatedTrack))
                return ActionValidity.NotApplicable;

            PlayableDirector director = TimelineEditor.inspectedDirector;
            if (director == null)
                return ActionValidity.NotApplicable;

            foreach (TrackAsset track in tracks)
            {
                Object binding = director.GetGenericBinding(track);
                if (binding == null)
                    return ActionValidity.NotApplicable;

                bool matched = false;
                for (int i = 0; i < requiredActorTypes.Length; i++)
                {
                    Type actorType = requiredActorTypes[i];
                    if (actorType != null && actorType.IsInstanceOfType(binding))
                    {
                        matched = true;
                        break;
                    }
                }

                if (!matched)
                    return ActionValidity.NotApplicable;
            }

            if (tracks.Any(t => t.lockedInHierarchy))
                return ActionValidity.Invalid;

            return ActionValidity.Valid;
        }

        public static ActionValidity ValidateMovement(IEnumerable<TrackAsset> tracks, Type requiredActorType) =>
            ValidateTrackType(tracks, typeof(ScriptMovementTrack), requiredActorType);

        public static ActionValidity ValidateDedicated(IEnumerable<TrackAsset> tracks, Type requiredActorType) =>
            ValidateTrackType(tracks, typeof(ScriptDedicatedTrack), requiredActorType);

        public static ActionValidity ValidateEffect(IEnumerable<TrackAsset> tracks, Type requiredActorType) =>
            ValidateDedicated(tracks, requiredActorType);

        public static ActionValidity ValidateRobotArm(IEnumerable<TrackAsset> tracks, Type requiredActorType) =>
            ValidateDedicated(tracks, requiredActorType);

        public static ActionValidity ValidateWorldRotationLock(IEnumerable<TrackAsset> tracks) =>
            ValidateDedicated(tracks, typeof(WorldRotationLockAnim));

        public static ActionValidity ValidateComment(IEnumerable<TrackAsset> tracks) =>
            ValidateAnyScriptAnimTrack(tracks, typeof(ScriptAnimActor));

        static ActionValidity ValidateTrackType(
            IEnumerable<TrackAsset> tracks,
            Type requiredTrackType,
            Type requiredActorType)
        {
            if (requiredTrackType == null || requiredActorType == null || tracks == null || !tracks.Any())
                return ActionValidity.NotApplicable;

            if (tracks.Any(t => t == null || !requiredTrackType.IsInstanceOfType(t)))
                return ActionValidity.NotApplicable;

            PlayableDirector director = TimelineEditor.inspectedDirector;
            if (director == null)
                return ActionValidity.NotApplicable;

            foreach (TrackAsset track in tracks)
            {
                Object binding = director.GetGenericBinding(track);
                if (binding == null || !requiredActorType.IsInstanceOfType(binding))
                    return ActionValidity.NotApplicable;
            }

            if (tracks.Any(t => t.lockedInHierarchy))
                return ActionValidity.Invalid;

            return ActionValidity.Valid;
        }

        static ActionValidity ValidateAnyScriptAnimTrack(
            IEnumerable<TrackAsset> tracks,
            Type requiredActorType)
        {
            if (requiredActorType == null || tracks == null || !tracks.Any())
                return ActionValidity.NotApplicable;

            if (tracks.Any(t => t is not ScriptAnimTrackBase))
                return ActionValidity.NotApplicable;

            PlayableDirector director = TimelineEditor.inspectedDirector;
            if (director == null)
                return ActionValidity.NotApplicable;

            foreach (TrackAsset track in tracks)
            {
                Object binding = director.GetGenericBinding(track);
                if (binding == null || !requiredActorType.IsInstanceOfType(binding))
                    return ActionValidity.NotApplicable;
            }

            if (tracks.Any(t => t.lockedInHierarchy))
                return ActionValidity.Invalid;

            return ActionValidity.Valid;
        }

        public static bool ExecuteMovement<TClip>(IEnumerable<TrackAsset> tracks)
            where TClip : ScriptableObject, IPlayableAsset
        {
            return Execute<TClip, ScriptAnimActor>(tracks, typeof(ScriptMovementTrack), null);
        }

        public static bool ExecuteDedicated<TClip>(IEnumerable<TrackAsset> tracks)
            where TClip : ScriptableObject, IPlayableAsset
        {
            return Execute<TClip, ScriptAnimActor>(tracks, typeof(ScriptDedicatedTrack), null);
        }

        public static bool ExecuteEffect<TClip>(IEnumerable<TrackAsset> tracks)
            where TClip : ScriptableObject, IPlayableAsset
        {
            return ExecuteDedicated<TClip>(tracks);
        }

        public static bool ExecuteMovement<TClip, TAnim>(
            IEnumerable<TrackAsset> tracks,
            Action<TAnim, TClip> seedFromAnim)
            where TClip : ScriptableObject, IPlayableAsset
            where TAnim : class
        {
            return Execute(tracks, typeof(ScriptMovementTrack), seedFromAnim);
        }

        public static bool ExecuteDedicated<TClip, TAnim>(
            IEnumerable<TrackAsset> tracks,
            Action<TAnim, TClip> seedFromAnim)
            where TClip : ScriptableObject, IPlayableAsset
            where TAnim : class
        {
            return Execute(tracks, typeof(ScriptDedicatedTrack), seedFromAnim);
        }

        public static bool ExecuteEffect<TClip, TAnim>(
            IEnumerable<TrackAsset> tracks,
            Action<TAnim, TClip> seedFromAnim)
            where TClip : ScriptableObject, IPlayableAsset
            where TAnim : class
        {
            return ExecuteDedicated(tracks, seedFromAnim);
        }

        static bool Execute<TClip, TAnim>(
            IEnumerable<TrackAsset> tracks,
            Type requiredTrackType,
            Action<TAnim, TClip> seedFromAnim)
            where TClip : ScriptableObject, IPlayableAsset
            where TAnim : class
        {
            PlayableDirector director = TimelineEditor.inspectedDirector;
            double time = director != null ? director.time : 0.0;

            bool any = false;
            foreach (TrackAsset track in tracks)
            {
                if (track == null || !requiredTrackType.IsInstanceOfType(track))
                    continue;

                TimelineClip clip = track.CreateClip<TClip>();
                if (clip == null)
                    continue;

                clip.start = time;
                if (seedFromAnim != null
                    && director != null
                    && clip.asset is TClip clipAsset
                    && director.GetGenericBinding(track) is TAnim anim)
                {
                    seedFromAnim(anim, clipAsset);
                }

                any = true;
            }

            if (any)
                TimelineEditor.Refresh(RefreshReason.ContentsAddedOrRemoved);

            return any;
        }
    }

    [MenuEntry("添加路径移动 Clip", MenuPriority.AddItem.addCustomClip)]
    class AddPathMoveClipAction : TrackAction
    {
        public override ActionValidity Validate(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ValidateMovement(tracks, typeof(PathMoveActor));

        public override bool Execute(IEnumerable<TrackAsset> tracks)
        {
            PlayableDirector director = TimelineEditor.inspectedDirector;
            double time = director != null ? director.time : 0.0;

            bool any = false;
            foreach (TrackAsset track in tracks)
            {
                if (track is not ScriptMovementTrack movementTrack)
                    continue;

                TimelineClip clip = movementTrack.CreateClip<PathMoveClip>();
                if (clip == null)
                    continue;

                clip.start = time;
                if (clip.asset is PathMoveClip pathAsset && director != null)
                {
                    if (director.GetGenericBinding(track) is PathMoveActor actor)
                    {
                        Undo.RecordObject(pathAsset, "Seed PathMoveClip From Actor");
                        actor.ApplyClipDefaults(pathAsset.Data);
                        EditorUtility.SetDirty(pathAsset);
                    }
                    SeedFromPreviousPathMove(director, clip, pathAsset);
                }

                any = true;
            }

            if (any)
                TimelineEditor.Refresh(RefreshReason.ContentsAddedOrRemoved);

            return any;
        }

        /// <summary>
        /// 从前序 PathMove Clip 继承路网与起终点，便于连续铺轨。
        /// </summary>
        internal static void SeedFromPreviousPathMove(
            PlayableDirector director,
            TimelineClip timelineClip,
            PathMoveClip pathClip)
        {
            if (director == null || timelineClip == null || pathClip == null)
                return;

            if (!TryFindPreviousPathMoveClip(timelineClip, out PathMoveClip previous))
                return;

            PathNetwork network = previous.Network.Resolve(director);
            PathNode endNode = previous.EndNode.Resolve(director);
            if (network == null && endNode == null)
                return;

            Undo.RecordObject(pathClip, "Seed PathMove From Previous");
            Undo.RecordObject(director, "Seed PathMove From Previous");

            if (network != null)
                pathClip.Network = BindExposed(director, network);
            if (endNode != null)
                pathClip.StartNode = BindExposed(director, endNode);

            EditorUtility.SetDirty(pathClip);
            EditorUtility.SetDirty(director);
        }

        static bool TryFindPreviousPathMoveClip(TimelineClip current, out PathMoveClip previousAsset)
        {
            previousAsset = null;
            if (current == null)
                return false;

            TrackAsset track = current.GetParentTrack();
            if (track == null)
                return false;

            TimelineClip best = null;
            foreach (TimelineClip other in track.GetClips())
            {
                if (other == null || other == current)
                    continue;
                if (other.asset is not PathMoveClip)
                    continue;
                if (other.start >= current.start - 1e-6)
                    continue;
                if (best == null || other.start > best.start)
                    best = other;
            }

            if (best?.asset is not PathMoveClip asset)
                return false;

            previousAsset = asset;
            return true;
        }

        static ExposedReference<T> BindExposed<T>(PlayableDirector director, T value)
            where T : Object
        {
            var exposed = new ExposedReference<T>
            {
                exposedName = Guid.NewGuid().ToString("N")
            };
            director.SetReferenceValue(exposed.exposedName, value);
            return exposed;
        }
    }

    [MenuEntry("添加直线移动 Clip", MenuPriority.AddItem.addCustomClip + 1)]
    class AddDirectMoveClipAction : TrackAction
    {
        public override ActionValidity Validate(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ValidateMovement(tracks, typeof(PathMoveActor));

        public override bool Execute(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ExecuteMovement<DirectMoveClip, PathMoveActor>(
                tracks,
                (anim, clip) =>
                {
                    if (clip?.Data == null || anim == null)
                        return;
                    Undo.RecordObject(clip, "Seed DirectMoveClip From Actor");
                    anim.ApplyClipDefaults(clip.Data);
                    EditorUtility.SetDirty(clip);
                });
    }

    [MenuEntry("添加瞬移 Clip", MenuPriority.AddItem.addCustomClip + 2)]
    class AddTeleportClipAction : TrackAction
    {
        public override ActionValidity Validate(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ValidateMovement(tracks, typeof(PathMoveActor));

        public override bool Execute(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ExecuteMovement<TeleportClip>(tracks);
    }

    [MenuEntry("添加原地旋转 Clip", MenuPriority.AddItem.addCustomClip + 3)]
    class AddRotateClipAction : TrackAction
    {
        public override ActionValidity Validate(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ValidateMovement(tracks, typeof(PathMoveActor));

        public override bool Execute(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ExecuteMovement<RotateClip, PathMoveActor>(
                tracks,
                (anim, clip) =>
                {
                    if (clip?.Data == null || anim == null)
                        return;
                    Undo.RecordObject(clip, "Seed RotateClip From Actor");
                    anim.ApplyClipDefaults(clip.Data);
                    EditorUtility.SetDirty(clip);
                });
    }

    [MenuEntry("添加三点转向 Clip", MenuPriority.AddItem.addCustomClip + 4)]
    class AddThreePointTurnClipAction : TrackAction
    {
        public override ActionValidity Validate(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ValidateMovement(tracks, typeof(PathMoveActor));

        public override bool Execute(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ExecuteMovement<ThreePointTurnClip, PathMoveActor>(
                tracks,
                (anim, clip) =>
                {
                    if (clip?.Data == null || anim == null)
                        return;
                    Undo.RecordObject(clip, "Seed ThreePointTurnClip From Actor");
                    anim.ApplyClipDefaults(clip.Data);
                    EditorUtility.SetDirty(clip);
                });
    }

    [MenuEntry("添加贝塞尔拐弯 Clip", MenuPriority.AddItem.addCustomClip + 5)]
    class AddBezierCornerClipAction : TrackAction
    {
        public override ActionValidity Validate(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ValidateMovement(tracks, typeof(PathMoveActor));

        public override bool Execute(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ExecuteMovement<BezierCornerClip, PathMoveActor>(
                tracks,
                (anim, clip) =>
                {
                    if (clip?.Data == null || anim == null)
                        return;
                    Undo.RecordObject(clip, "Seed BezierCornerClip From Actor");
                    anim.ApplyClipDefaults(clip.Data);
                    EditorUtility.SetDirty(clip);
                });
    }

    [MenuEntry("添加双拐点贝塞尔弯 Clip", MenuPriority.AddItem.addCustomClip + 5)]
    class AddBezierDualCornerClipAction : TrackAction
    {
        public override ActionValidity Validate(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ValidateMovement(tracks, typeof(PathMoveActor));

        public override bool Execute(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ExecuteMovement<BezierDualCornerClip, PathMoveActor>(
                tracks,
                (anim, clip) =>
                {
                    if (clip?.Data == null || anim == null)
                        return;
                    Undo.RecordObject(clip, "Seed BezierDualCornerClip From Actor");
                    anim.ApplyClipDefaults(clip.Data);
                    EditorUtility.SetDirty(clip);
                });
    }

    [MenuEntry("添加倒车掉头 Clip", MenuPriority.AddItem.addCustomClip + 6)]
    class AddReverseUTurnClipAction : TrackAction
    {
        public override ActionValidity Validate(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ValidateMovement(tracks, typeof(PathMoveActor));

        public override bool Execute(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ExecuteMovement<ReverseUTurnClip, PathMoveActor>(
                tracks,
                (anim, clip) =>
                {
                    if (clip?.Data == null || anim == null)
                        return;
                    Undo.RecordObject(clip, "Seed ReverseUTurnClip From Actor");
                    anim.ApplyClipDefaults(clip.Data);
                    EditorUtility.SetDirty(clip);
                });
    }

    [MenuEntry("添加叉车 Clip", MenuPriority.AddItem.addCustomClip + 7)]
    class AddForkliftClipAction : TrackAction
    {
        public override ActionValidity Validate(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ValidateMovement(tracks, typeof(ForkliftAnim));

        public override bool Execute(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ExecuteMovement<ForkliftClip, ForkliftAnim>(
                tracks,
                (anim, clip) =>
                {
                    if (clip?.Data == null || anim == null)
                        return;
                    Undo.RecordObject(clip, "Seed ForkliftClip From Anim");
                    anim.ApplyClipDefaults(clip.Data);
                    EditorUtility.SetDirty(clip);
                });
    }

    [MenuEntry("添加潜伏车 Clip", MenuPriority.AddItem.addCustomClip + 8)]
    class AddLatentAgvClipAction : TrackAction
    {
        public override ActionValidity Validate(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ValidateMovement(tracks, typeof(LatentAgvAnim));

        public override bool Execute(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ExecuteMovement<LatentAgvClip, LatentAgvAnim>(
                tracks,
                (anim, clip) =>
                {
                    if (clip?.Data == null || anim == null)
                        return;
                    Undo.RecordObject(clip, "Seed LatentAgvClip From Anim");
                    anim.ApplyClipDefaults(clip.Data);
                    EditorUtility.SetDirty(clip);
                });
    }

    [MenuEntry("添加 CTU Clip", MenuPriority.AddItem.addCustomClip + 9)]
    class AddCtuClipAction : TrackAction
    {
        public override ActionValidity Validate(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ValidateMovement(tracks, typeof(CtuAnim));

        public override bool Execute(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ExecuteMovement<CtuClip, CtuAnim>(
                tracks,
                (anim, clip) =>
                {
                    if (clip?.Data == null || anim == null)
                        return;
                    Undo.RecordObject(clip, "Seed CtuClip From Anim");
                    anim.ApplyClipDefaults(clip.Data);
                    EditorUtility.SetDirty(clip);
                });
    }

    [MenuEntry("添加穿梭车 Clip", MenuPriority.AddItem.addCustomClip + 10)]
    class AddShuttleClipAction : TrackAction
    {
        public override ActionValidity Validate(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ValidateMovement(tracks, typeof(ShuttleAnim));

        public override bool Execute(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ExecuteMovement<ShuttleClip, ShuttleAnim>(
                tracks,
                (anim, clip) =>
                {
                    if (clip?.Data == null || anim == null)
                        return;
                    Undo.RecordObject(clip, "Seed ShuttleClip From Anim");
                    anim.ApplyClipDefaults(clip.Data);
                    EditorUtility.SetDirty(clip);
                });
    }

    [MenuEntry("添加堆垛机 Clip", MenuPriority.AddItem.addCustomClip + 11)]
    class AddStackerClipAction : TrackAction
    {
        public override ActionValidity Validate(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ValidateMovement(tracks, typeof(StackerAnim));

        public override bool Execute(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ExecuteMovement<StackerClip, StackerAnim>(
                tracks,
                (anim, clip) =>
                {
                    if (clip?.Data == null || anim == null)
                        return;
                    Undo.RecordObject(clip, "Seed StackerClip From Anim");
                    anim.ApplyClipDefaults(clip.Data);
                    EditorUtility.SetDirty(clip);
                });
    }

    [MenuEntry("添加堆垛机货叉 Clip", MenuPriority.AddItem.addCustomClip + 12)]
    class AddStackerForkClipAction : TrackAction
    {
        public override ActionValidity Validate(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ValidateMovement(tracks, typeof(StackerAnim));

        public override bool Execute(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ExecuteMovement<StackerForkClip, StackerAnim>(
                tracks,
                (anim, clip) =>
                {
                    if (clip?.Data == null || anim == null)
                        return;
                    Undo.RecordObject(clip, "Seed StackerForkClip From Anim");
                    anim.ApplyClipDefaults(clip.Data);
                    EditorUtility.SetDirty(clip);
                });
    }

    [MenuEntry("添加机械臂 Clip", MenuPriority.AddItem.addCustomClip + 13)]
    class AddRobotArmClipAction : TrackAction
    {
        public override ActionValidity Validate(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ValidateRobotArm(tracks, typeof(RobotArmAnim));

        public override bool Execute(IEnumerable<TrackAsset> tracks)
        {
            PlayableDirector director = TimelineEditor.inspectedDirector;
            if (director == null)
                return false;

            double time = director.time;
            bool any = false;

            foreach (TrackAsset track in tracks)
            {
                if (track is not ScriptDedicatedTrack dedicatedTrack)
                    continue;

                var anim = director.GetGenericBinding(track) as RobotArmAnim;
                TimelineClip clip = dedicatedTrack.CreateClip<RobotArmClip>();
                if (clip == null)
                    continue;

                clip.start = time;
                if (clip.asset is RobotArmClip asset && anim != null)
                    SyncRobotArmClipFromAnim(director, asset, anim);

                any = true;
            }

            if (any)
                TimelineEditor.Refresh(RefreshReason.ContentsAddedOrRemoved);

            return any;
        }

        internal static void SyncRobotArmClipFromAnim(
            PlayableDirector director,
            RobotArmClip clip,
            RobotArmAnim anim)
        {
            if (clip?.Data == null || anim == null)
                return;

            Undo.RecordObject(clip, "Sync RobotArmClip From Anim");
            anim.ApplyClipDefaults(clip.Data);
            clip.EnsureWaypointTargetCount();
            clip.InvalidatePlanCache();

            if (director != null && anim.IkTarget != null)
            {
                Transform existingEnd = ScriptAnimPointUtility.AsTransform(
                    ScriptAnimPointUtility.Resolve(clip.EndTarget, director));
                if (existingEnd == null)
                {
                    ScriptAnimPoint point = EnsurePoint(anim.IkTarget);
                    clip.EndTarget = BindExposed(director, point);
                }
            }

            EditorUtility.SetDirty(clip);
        }

        internal static ExposedReference<ScriptAnimPoint> BindExposed(
            PlayableDirector director, ScriptAnimPoint value)
        {
            var exposed = new ExposedReference<ScriptAnimPoint>
            {
                exposedName = Guid.NewGuid().ToString("N")
            };
            director.SetReferenceValue(exposed.exposedName, value);
            return exposed;
        }

        internal static ScriptAnimPoint EnsurePoint(Transform transform)
        {
            if (transform == null)
                return null;
            var point = transform.GetComponent<ScriptAnimPoint>();
            if (point == null)
                point = Undo.AddComponent<ScriptAnimPoint>(transform.gameObject);
            return point;
        }
    }

    [MenuEntry("添加五轴机械臂 Clip", MenuPriority.AddItem.addCustomClip + 14)]
    class AddRobotArm5ClipAction : TrackAction
    {
        public override ActionValidity Validate(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ValidateRobotArm(tracks, typeof(RobotArm5Anim));

        public override bool Execute(IEnumerable<TrackAsset> tracks)
        {
            PlayableDirector director = TimelineEditor.inspectedDirector;
            if (director == null)
                return false;

            double time = director.time;
            bool any = false;

            foreach (TrackAsset track in tracks)
            {
                if (track is not ScriptDedicatedTrack dedicatedTrack)
                    continue;

                var anim = director.GetGenericBinding(track) as RobotArm5Anim;
                TimelineClip clip = dedicatedTrack.CreateClip<RobotArm5Clip>();
                if (clip == null)
                    continue;

                clip.start = time;
                if (clip.asset is RobotArm5Clip asset && anim != null)
                    SyncRobotArm5ClipFromAnim(director, asset, anim);

                any = true;
            }

            if (any)
                TimelineEditor.Refresh(RefreshReason.ContentsAddedOrRemoved);

            return any;
        }

        internal static void SyncRobotArm5ClipFromAnim(
            PlayableDirector director,
            RobotArm5Clip clip,
            RobotArm5Anim anim)
        {
            if (clip?.Data == null || anim == null)
                return;

            Undo.RecordObject(clip, "Sync RobotArm5Clip From Anim");
            anim.ApplyClipDefaults(clip.Data);
            clip.EnsureWaypointTargetCount();
            clip.InvalidatePlanCache();

            if (director != null && anim.IkTarget != null)
            {
                Transform existingEnd = ScriptAnimPointUtility.AsTransform(
                    ScriptAnimPointUtility.Resolve(clip.EndTarget, director));
                if (existingEnd == null)
                {
                    ScriptAnimPoint point = AddRobotArmClipAction.EnsurePoint(anim.IkTarget);
                    clip.EndTarget = AddRobotArmClipAction.BindExposed(director, point);
                }
            }

            EditorUtility.SetDirty(clip);
        }
    }

    [MenuEntry("添加透明度 Clip", MenuPriority.AddItem.addCustomClip + 16)]
    class AddFadeClipAction : TrackAction
    {
        public override ActionValidity Validate(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ValidateEffect(tracks, typeof(FadeAnim));

        public override bool Execute(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ExecuteEffect<FadeClip>(tracks);
    }

    [MenuEntry("添加间隔显隐 Clip", MenuPriority.AddItem.addCustomClip + 17)]
    class AddBlinkClipAction : TrackAction
    {
        public override ActionValidity Validate(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ValidateEffect(tracks, typeof(BlinkAnim));

        public override bool Execute(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ExecuteEffect<BlinkClip>(tracks);
    }

    [MenuEntry("添加对象切换 Clip", MenuPriority.AddItem.addCustomClip + 25)]
    class AddObjectSwitchClipAction : TrackAction
    {
        public override ActionValidity Validate(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ValidateEffect(tracks, typeof(ObjectSwitchAnim));

        public override bool Execute(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ExecuteEffect<ObjectSwitchClip>(tracks);
    }

    [MenuEntry("添加摇摆翻转 Clip", MenuPriority.AddItem.addCustomClip + 18)]
    class AddSwingFlipClipAction : TrackAction
    {
        public override ActionValidity Validate(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ValidateEffect(tracks, typeof(SwingFlipAnim));

        public override bool Execute(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ExecuteEffect<SwingFlipClip, SwingFlipAnim>(
                tracks,
                (anim, clip) =>
                {
                    if (clip?.Data == null || anim == null)
                        return;
                    Undo.RecordObject(clip, "Seed SwingFlipClip From Anim");
                    anim.ApplyClipDefaults(clip.Data);
                    EditorUtility.SetDirty(clip);
                });
    }

    [MenuEntry("添加依次换位 Clip", MenuPriority.AddItem.addCustomClip + 19)]
    class AddSequentialPositionClipAction : TrackAction
    {
        public override ActionValidity Validate(IEnumerable<TrackAsset> tracks)
        {
            ActionValidity baseValidity =
                ScriptAnimAddClipUtility.ValidateEffect(tracks, typeof(SequentialPositionAnim));
            if (baseValidity != ActionValidity.Valid)
                return baseValidity;

            PlayableDirector director = TimelineEditor.inspectedDirector;
            if (director == null)
                return ActionValidity.NotApplicable;

            foreach (TrackAsset track in tracks)
            {
                var anim = director.GetGenericBinding(track) as SequentialPositionAnim;
                if (!SequentialPositionSampler.HasValidDuration(anim))
                    return ActionValidity.Invalid;
            }

            return ActionValidity.Valid;
        }

        public override bool Execute(IEnumerable<TrackAsset> tracks)
        {
            PlayableDirector director = TimelineEditor.inspectedDirector;
            if (director == null)
                return false;

            double time = director.time;
            bool any = false;

            foreach (TrackAsset track in tracks)
            {
                if (track is not ScriptDedicatedTrack dedicatedTrack)
                    continue;

                var anim = director.GetGenericBinding(track) as SequentialPositionAnim;
                float duration = SequentialPositionSampler.EstimateDuration(anim);
                if (duration <= 1e-6f)
                {
                    EditorUtility.DisplayDialog(
                        "Cannot add clip",
                        "SequentialPositionAnim effective duration is 0.\nConfigure waypoint intervals first.",
                        "OK");
                    return false;
                }

                TimelineClip clip = dedicatedTrack.CreateClip<SequentialPositionClip>();
                if (clip == null)
                    continue;

                clip.start = time;
                ClipDurationSync.SetDuration(clip, duration);
                any = true;
            }

            if (any)
                TimelineEditor.Refresh(RefreshReason.ContentsAddedOrRemoved);

            return any;
        }
    }

    [MenuEntry("添加姿态改变 Clip", MenuPriority.AddItem.addCustomClip + 20)]
    class AddPoseChangeClipAction : TrackAction
    {
        public override ActionValidity Validate(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ValidateEffectAny(
                tracks,
                typeof(PoseChangeAnim),
                typeof(PoseChangeAnimMax));

        public override bool Execute(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ExecuteEffect<PoseChangeClip>(tracks);
    }

    [MenuEntry("添加开箱 Clip", MenuPriority.AddItem.addCustomClip + 22)]
    class AddOpenBoxClipAction : TrackAction
    {
        public override ActionValidity Validate(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ValidateEffect(tracks, typeof(OpenBoxAnim));

        public override bool Execute(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ExecuteEffect<OpenBoxClip>(tracks);
    }

    [MenuEntry("添加缠膜 Clip", MenuPriority.AddItem.addCustomClip + 21)]
    class AddFilmWrapClipAction : TrackAction
    {
        public override ActionValidity Validate(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ValidateEffect(tracks, typeof(FilmWrapAnim));

        public override bool Execute(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ExecuteEffect<FilmWrapClip>(tracks);
    }

    [MenuEntry("添加世界旋转锁定 Clip", MenuPriority.AddItem.addCustomClip + 23)]
    class AddWorldRotationLockClipAction : TrackAction
    {
        public override ActionValidity Validate(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ValidateWorldRotationLock(tracks);

        public override bool Execute(IEnumerable<TrackAsset> tracks)
        {
            double time = TimelineEditor.inspectedDirector != null
                ? TimelineEditor.inspectedDirector.time
                : 0.0;

            bool any = false;
            foreach (TrackAsset track in tracks)
            {
                if (track is not ScriptDedicatedTrack dedicatedTrack)
                    continue;

                TimelineClip clip = dedicatedTrack.CreateClip<WorldRotationLockClip>();
                if (clip == null)
                    continue;

                clip.start = time;
                if (clip.asset is WorldRotationLockClip lockClip)
                {
                    if (lockClip.Data == null)
                        lockClip.Data = new WorldRotationLockClipData();

                    if (TimelineEditor.inspectedDirector != null)
                    {
                        var anim = TimelineEditor.inspectedDirector.GetGenericBinding(dedicatedTrack)
                            as WorldRotationLockAnim;
                        if (anim != null && anim.TryGetCurrentWorldEuler(out Vector3 euler))
                            lockClip.Data.LockEulerAngles = euler;
                    }

                    Vector3 e = lockClip.Data.LockEulerAngles;
                    clip.displayName = $"锁定世界旋转 ({e.x:0.#},{e.y:0.#},{e.z:0.#})";
                }
                else
                {
                    clip.displayName = "锁定世界旋转";
                }

                any = true;
            }

            if (any)
                TimelineEditor.Refresh(RefreshReason.ContentsAddedOrRemoved);

            return any;
        }
    }

    [MenuEntry("添加注释 Clip", MenuPriority.AddItem.addCustomClip + 24)]
    class AddCommentClipAction : TrackAction
    {
        public override ActionValidity Validate(IEnumerable<TrackAsset> tracks) =>
            ScriptAnimAddClipUtility.ValidateComment(tracks);

        public override bool Execute(IEnumerable<TrackAsset> tracks)
        {
            PlayableDirector director = TimelineEditor.inspectedDirector;
            double time = director != null ? director.time : 0.0;
            bool any = false;

            foreach (TrackAsset track in tracks)
            {
                if (track is not ScriptAnimTrackBase scriptTrack)
                    continue;

                TimelineClip clip = scriptTrack.CreateClip<CommentClip>();
                if (clip == null)
                    continue;

                clip.start = time;
                any = true;
            }

            if (any)
                TimelineEditor.Refresh(RefreshReason.ContentsAddedOrRemoved);

            return any;
        }
    }
}
