using System.Linq;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    public static class ClipDurationSync
    {
        /// <summary>写入 Clip 时长并应用所层 <see cref="ScriptAnimTrackBase"/> 的最小长度。</summary>
        public static void SetDuration(TimelineClip timelineClip, float duration)
        {
            if (timelineClip == null || duration < 0f)
                return;
            timelineClip.duration = ScriptAnimTrackBase.ClampDuration(timelineClip, duration);
        }

        public static bool TryApply(
            TimelineClip timelineClip,
            Object trackBinding,
            out float duration,
            bool recordUndo = true,
            bool refreshTimeline = true,
            IExposedPropertyTable resolver = null)
        {
            duration = -1f;
            if (timelineClip?.asset is not IDurationResolvable resolvable)
                return false;

            var ctx = new DurationResolveContext
            {
                TrackBinding = trackBinding,
                Resolver = resolver ?? ResolveDirector(timelineClip.GetParentTrack()),
                TimelineClip = timelineClip
            };
            duration = resolvable.ResolveDuration(ctx);
            if (duration <= 0f)
                return false;

            duration = ScriptAnimTrackBase.ClampDuration(timelineClip, duration);

            if (Mathf.Abs((float)timelineClip.duration - duration) < 0.0005f)
                return true;

            if (recordUndo && timelineClip.GetParentTrack() != null)
                Undo.RecordObject(timelineClip.GetParentTrack(), "Sync Clip Duration");

            timelineClip.duration = duration;

            var track = timelineClip.GetParentTrack();
            if (track != null)
                EditorUtility.SetDirty(track);

            if (refreshTimeline)
                TimelineEditor.Refresh(RefreshReason.ContentsModified);

            return true;
        }

        /// <summary>
        /// 按 start 升序从前到后刷新整轨所有可估算 Clip 的时长。
    /// </summary>
        public static int TryApplyTrack(
            TrackAsset track,
            Object trackBinding,
            out int failed,
            bool recordUndo = true,
            bool refreshTimeline = true)
        {
            failed = 0;
            if (track == null)
                return 0;

            TimelineClip[] ordered = track.GetClips().OrderBy(c => c.start).ToArray();
            if (ordered.Length == 0)
                return 0;

            if (recordUndo)
                Undo.RecordObject(track, "Sync Track Clip Durations");

            var resolver = ResolveDirector(track);
            if (trackBinding == null)
                trackBinding = GetTrackBinding(track);

            int updated = 0;
            foreach (var clip in ordered)
            {
                if (clip?.asset is not IDurationResolvable)
                    continue;

                if (TryApply(
                        clip,
                        trackBinding,
                        out _,
                        recordUndo: false,
                        refreshTimeline: false,
                        resolver: resolver))
                    updated++;
                else
                    failed++;
            }

            EditorUtility.SetDirty(track);
            if (refreshTimeline)
                TimelineEditor.Refresh(RefreshReason.ContentsModified);

            return updated;
        }

        /// <summary>
        /// 刷新 TimelineAsset 内所朝 <see cref="ScriptAnimTrackBase"/> 的可估算 Clip 时长。
    /// 各轨独立按 start 升序处理；轨与轨之间无先后依赖。
    /// </summary>
        public static int TryApplyTimeline(
            TimelineAsset asset,
            out int failed,
            out int trackCount,
            bool recordUndo = true,
            bool refreshTimeline = true)
        {
            failed = 0;
            trackCount = 0;
            if (asset == null)
                return 0;

            var director = ResolveDirector(null);
            if (director == null || director.playableAsset != asset)
            {
                var directors = Object.FindObjectsByType<PlayableDirector>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var d in directors)
                {
                    if (d != null && d.playableAsset == asset)
                    {
                        director = d;
                        break;
                    }
                }
            }

            int updated = 0;
            foreach (var track in asset.GetOutputTracks())
            {
                if (track is not ScriptAnimTrackBase scriptTrack)
                    continue;

                trackCount++;
                Object binding = director != null ? director.GetGenericBinding(scriptTrack) : null;
                updated += TryApplyTrack(
                    scriptTrack,
                    binding,
                    out int trackFailed,
                    recordUndo: recordUndo,
                    refreshTimeline: false);
                failed += trackFailed;
            }

            if (refreshTimeline && trackCount > 0)
                TimelineEditor.Refresh(RefreshReason.ContentsModified);

            return updated;
        }

        /// <summary>AutoSync：整轨顺序刷新（前序 PathMove 先定稿，后续 Forklift 才正确）。</summary>
        public static void TryAutoSyncTrack(TimelineClip anyClipOnTrack)
        {
            if (anyClipOnTrack == null)
                return;

            var track = anyClipOnTrack.GetParentTrack();
            if (track == null)
                return;

            if (!ShouldAutoSyncTrack(track))
                return;

            Object binding = GetTrackBinding(anyClipOnTrack);
            TryApplyTrack(track, binding, out _, recordUndo: false, refreshTimeline: false);
        }

        private static bool ShouldAutoSyncTrack(TrackAsset track)
        {
            foreach (var clip in track.GetClips())
            {
                if (clip?.asset is PathMoveClip path && path.Data != null && path.Data.AutoSyncDuration)
                    return true;
                if (clip?.asset is DirectMoveClip direct &&
                    direct.Data != null &&
                    direct.Data.AutoSyncDuration)
                    return true;
                if (clip?.asset is TeleportClip teleport &&
                    teleport.Data != null &&
                    teleport.Data.AutoSyncDuration)
                    return true;
                if (clip?.asset is RotateClip rotate &&
                    rotate.Data != null &&
                    rotate.Data.AutoSyncDuration)
                    return true;
                if (clip?.asset is ThreePointTurnClip threePoint &&
                    threePoint.Data != null &&
                    threePoint.Data.AutoSyncDuration)
                    return true;
                if (clip?.asset is BezierCornerClip bezierCorner &&
                    bezierCorner.Data != null &&
                    bezierCorner.Data.AutoSyncDuration)
                    return true;
                if (clip?.asset is BezierDualCornerClip bezierDual &&
                    bezierDual.Data != null &&
                    bezierDual.Data.AutoSyncDuration)
                    return true;
                if (clip?.asset is ReverseUTurnClip reverseUTurn &&
                    reverseUTurn.Data != null &&
                    reverseUTurn.Data.AutoSyncDuration)
                    return true;
                if (clip?.asset is ForkliftClip fork && fork.Data != null && fork.Data.AutoSyncDuration)
                    return true;
                if (clip?.asset is LatentAgvClip latent &&
                    latent.Data != null &&
                    latent.Data.AutoSyncDuration)
                    return true;
                if (clip?.asset is CtuClip ctu && ctu.Data != null && ctu.Data.AutoSyncDuration)
                    return true;
                if (clip?.asset is ShuttleClip shuttle &&
                    shuttle.Data != null &&
                    shuttle.Data.AutoSyncDuration)
                    return true;
                if (clip?.asset is StackerClip stacker && stacker.Data != null && stacker.Data.AutoSyncDuration)
                    return true;
                if (clip?.asset is StackerForkClip stackerFork &&
                    stackerFork.Data != null &&
                    stackerFork.Data.AutoSyncDuration)
                    return true;
                if (clip?.asset is RobotArmClip robotArm &&
                    robotArm.Data != null &&
                    robotArm.Data.AutoSyncDuration)
                    return true;
                if (clip?.asset is RobotArm5Clip robotArm5 &&
                    robotArm5.Data != null &&
                    robotArm5.Data.AutoSyncDuration)
                    return true;
                if (clip?.asset is FadeClip fade &&
                    fade.Data != null &&
                    fade.Data.AutoSyncDuration)
                    return true;
                if (clip?.asset is BlinkClip blink &&
                    blink.Data != null &&
                    blink.Data.AutoSyncDuration)
                    return true;
                if (clip?.asset is PoseChangeClip poseChange &&
                    poseChange.Data != null &&
                    poseChange.Data.AutoSyncDuration)
                    return true;
                if (clip?.asset is OpenBoxClip openBox &&
                    openBox.Data != null &&
                    openBox.Data.AutoSyncDuration)
                    return true;
                if (clip?.asset is FilmWrapClip filmWrap &&
                    filmWrap.Data != null &&
                    filmWrap.Data.AutoSyncDuration)
                    return true;
                if (clip?.asset is SwingFlipClip swingFlip &&
                    swingFlip.Data != null &&
                    swingFlip.Data.AutoSyncDuration)
                    return true;
                if (clip?.asset is SequentialPositionClip sequential &&
                    sequential.Data != null &&
                    sequential.Data.AutoSyncDuration)
                    return true;
            }

            return false;
        }

        public static Object GetTrackBinding(TimelineClip timelineClip)
        {
            if (timelineClip == null)
                return null;
            return GetTrackBinding(timelineClip.GetParentTrack());
        }

        public static Object GetTrackBinding(TrackAsset track)
        {
            if (track == null)
                return null;

            var director = ResolveDirector(track);
            if (director == null)
                return null;

            return director.GetGenericBinding(track);
        }

        /// <summary>优先 Timeline 窗口当前 Director，否则找引用该 Timeline 的场景 Director。</summary>
        public static PlayableDirector ResolveDirector(TrackAsset track)
        {
            if (TimelineEditor.inspectedDirector != null)
                return TimelineEditor.inspectedDirector;

            TimelineAsset asset = track != null ? track.timelineAsset : TimelineEditor.inspectedAsset;
            if (asset == null)
                return null;

            var directors = Object.FindObjectsByType<PlayableDirector>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var director in directors)
            {
                if (director != null && director.playableAsset == asset)
                    return director;
            }

            return null;
        }
    }
}
