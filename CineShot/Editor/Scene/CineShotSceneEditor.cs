using System;
using System.Linq.Expressions;
using System.Reflection;
using CineShot.Core;
using CineShot.Output;
using UnityEditor;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace CineShot.Editor
{
    /// <summary>匹配点预览/修改：仅当次选中有效，切选中清空并恢复相机。</summary>
    static class MatchPointEditState
    {
        public static CineShotClip Clip;
        public static bool Preview;
        public static bool Edit;
        static bool s_HasSaved;
        static Vector3 s_Pos;
        static Quaternion s_Rot;
        static float s_Fov;
        static float s_Near;
        static float s_Far;

        public static bool Active
        {
            get { return Clip != null && (Preview || Edit); }
        }

        public static bool IsFor(CineShotClip clip)
        {
            return Active && clip != null && Clip == clip;
        }

        public static void EnsureFor(CineShotClip clip)
        {
            if (clip == null)
            {
                Clear(true);
                return;
            }
            if (Clip != clip)
                Clear(true);
            Clip = clip;
        }

        public static void SetPreview(bool on)
        {
            if (on == Preview)
                return;
            if (on)
            {
                CaptureIfNeeded();
                Preview = true;
            }
            else
            {
                Preview = false;
                if (!Edit)
                    Restore();
            }
            SceneView.RepaintAll();
        }

        public static void SetEdit(bool on)
        {
            if (on == Edit)
                return;
            if (on)
            {
                CaptureIfNeeded();
                Edit = true;
                Preview = true;
            }
            else
            {
                Edit = false;
                if (!Preview)
                    Restore();
            }
            SceneView.RepaintAll();
        }

        public static void Clear(bool restoreCamera)
        {
            if (restoreCamera)
                Restore();
            Clip = null;
            Preview = false;
            Edit = false;
        }

        static void CaptureIfNeeded()
        {
            if (s_HasSaved)
                return;
            var cam = Camera.main;
            if (cam == null)
                return;
            s_Pos = cam.transform.position;
            s_Rot = cam.transform.rotation;
            s_Fov = cam.fieldOfView;
            s_Near = cam.nearClipPlane;
            s_Far = cam.farClipPlane;
            s_HasSaved = true;
        }

        static void Restore()
        {
            if (!s_HasSaved)
                return;
            var cam = Camera.main;
            if (cam != null)
            {
                cam.transform.SetPositionAndRotation(s_Pos, s_Rot);
                cam.fieldOfView = s_Fov;
                cam.nearClipPlane = s_Near;
                cam.farClipPlane = s_Far;
            }
            s_HasSaved = false;
            SceneView.RepaintAll();
        }
    }

    [InitializeOnLoad]
    static class CineShotSceneEditor
    {
        internal const string k_FollowKey = "CineShot.FollowPlayhead";
        internal const string k_OverrideKey = "CineShot.WriteClip";

        static CineShotRig s_Rig;
        static bool s_Held;
        static bool s_GoWasActive;
        static double s_LastDirectorTime = double.NaN;
        static object s_HookedTimelineState;

        static CineShotSceneEditor()
        {
            EditorApplication.update += OnEditorUpdate;
            SceneView.duringSceneGui += OnSceneGui;
            EditorApplication.playModeStateChanged += OnPlayMode;
            AssemblyReloadEvents.beforeAssemblyReload += OnReload;
            Undo.undoRedoPerformed += OnUndo;
            Selection.selectionChanged += OnSelectionChanged;
        }

        static void OnReload()
        {
            MatchPointEditState.Clear(true);
            Release();
        }

        static void OnSelectionChanged()
        {
            var clip = TimelineEditor.selectedClip != null
                ? TimelineEditor.selectedClip.asset as CineShotClip
                : null;
            if (MatchPointEditState.Clip != null && MatchPointEditState.Clip != clip)
                MatchPointEditState.Clear(true);
        }

        static void OnPlayMode(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                MatchPointEditState.Clear(true);
                Release();
            }
        }

        static void OnUndo()
        {
            SceneView.RepaintAll();
        }

        static void OnEditorUpdate()
        {
            if (Application.isPlaying)
            {
                MatchPointEditState.Clear(true);
                Release();
                return;
            }

            var selectedClip = TimelineEditor.selectedClip != null
                ? TimelineEditor.selectedClip.asset as CineShotClip
                : null;
            if (MatchPointEditState.Clip != null && MatchPointEditState.Clip != selectedClip)
                MatchPointEditState.Clear(true);

            TryHookTimelineButton();
            var ctx = BuildContext();
            if (ctx == null)
            {
                Release();
                return;
            }

            ApplyPreview(ctx);
            if (!Mathf.Approximately((float)s_LastDirectorTime, (float)ctx.DirectorTime))
            {
                s_LastDirectorTime = ctx.DirectorTime;
                SceneView.RepaintAll();
            }
        }

        static void OnSceneGui(SceneView view)
        {
            if (Application.isPlaying)
                return;

            var ctx = BuildContext();
            if (ctx == null)
                return;

            DrawFrustumsAndTargets(ctx);
            if (ctx.Selected)
            {
                DrawMatchPointOverlay(ctx);
                DrawGizmos(ctx);
            }
            ApplyPreview(ctx);

            if (ctx.Selected && GUI.changed)
                view.Repaint();
        }

        internal sealed class PreviewContext
        {
            public PlayableDirector Director;
            public TimelineClip TimelineClip;
            public CineShotClip Clip;
            public ShotAsset Shot;
            public CineShotRig Rig;
            public Transform Look;
            public Transform Follow;
            public float LocalTime;
            public float Duration;
            public bool InsidePlayhead;
            public bool Selected;
            public bool WriteOverride;
            public double DirectorTime;
            public RigParams Params;
            public CameraPose Pose;
            public float AuthoredFov;
            public Vector3 LookOffset;
            public Vector3 FollowOffset;
            public TargetSample LookSample;
            public TargetSample FollowSample;
            public Vector3 Anchor;
            public Quaternion AnchorRot;
            public FocusMode FocusMode;
            public FocusSettings FocusSettings;
        }

        static double ReadPlayhead(PlayableDirector director)
        {
            var prop = typeof(TimelineEditor).GetProperty("inspectedSequenceTime", BindingFlags.NonPublic | BindingFlags.Static);
            if (prop != null)
            {
                object value = prop.GetValue(null, null);
                if (value is double)
                    return (double)value;
            }
            return director != null ? director.time : 0d;
        }

        internal static PreviewContext BuildContext()
        {
            TimelineClip selectedTimeline = TimelineEditor.selectedClip;
            CineShotClip selectedClip = selectedTimeline != null ? selectedTimeline.asset as CineShotClip : null;
            bool selectedValid = selectedClip != null && selectedClip.shot != null && selectedTimeline != null;

            PlayableDirector director = null;
            if (selectedValid)
                director = FindDirector(selectedTimeline.GetParentTrack() != null ? selectedTimeline.GetParentTrack().timelineAsset : null);
            if (director == null)
                director = TimelineEditor.inspectedDirector;
            if (director == null || director.playableAsset == null)
                return null;

            double playhead = ReadPlayhead(director);
            bool follow = SessionState.GetBool(k_FollowKey, true);
            TimelineClip timelineClip;
            CineShotClip selected;
            if (follow)
            {
                timelineClip = FindClipAtTime(director, playhead);
                selected = timelineClip != null ? timelineClip.asset as CineShotClip : null;
                if (selected == null || selected.shot == null)
                    return null;
            }
            else if (selectedValid)
            {
                timelineClip = selectedTimeline;
                selected = selectedClip;
            }
            else
            {
                timelineClip = FindClipAtTime(director, playhead);
                selected = timelineClip != null ? timelineClip.asset as CineShotClip : null;
                if (selected == null || selected.shot == null)
                    return null;
            }

            var ctx = new PreviewContext();
            ctx.Director = director;
            ctx.TimelineClip = timelineClip;
            ctx.Clip = selected;
            ctx.Shot = selected.shot;
            ctx.Selected = TimelineEditor.selectedClip != null && TimelineEditor.selectedClip.asset == selected;
            ctx.DirectorTime = playhead;
            ctx.Duration = Mathf.Max(0.05f, (float)timelineClip.duration);
            ctx.InsidePlayhead = playhead >= timelineClip.start && playhead < timelineClip.end;
            ctx.WriteOverride = SessionState.GetBool(k_OverrideKey, true);

            if (follow && ctx.InsidePlayhead)
                ctx.LocalTime = Mathf.Clamp((float)timelineClip.ToLocalTimeUnbound(playhead), 0f, ctx.Duration);
            else if (follow)
                ctx.LocalTime = 0f;
            else
                ctx.LocalTime = Mathf.Clamp(SessionState.GetFloat("CineShot.ManualTime", 0f), 0f, ctx.Duration);

            ctx.Look = selected.lookAt.Resolve(director);
            ctx.Follow = selected.follow.Resolve(director);
            ctx.Rig = ResolveRig(director, timelineClip);

            RigParams rigParams;
            Vector3 composition;
            float fov, near, far, focusDistance;
            FocusMode mode;
            FocusSettings focusSettings;
            selected.overrides.ApplyTo(ctx.Shot, out rigParams, out composition, out fov, out near, out far,
                out mode, out focusDistance, out focusSettings);
            ctx.FocusMode = mode;
            ctx.FocusSettings = focusSettings;
            ctx.Params = rigParams;
            ctx.AuthoredFov = fov;
            ctx.LookOffset = selected.overrides.ResolveLookOffset(ctx.Shot);
            ctx.FollowOffset = selected.overrides.ResolveFollowOffset(ctx.Shot);

            if (ctx.Rig != null)
            {
                ctx.LookSample = ctx.Rig.SampleLookAt(ctx.Look, ctx.LookOffset, 0f);
                ctx.FollowSample = ctx.Rig.SampleFollow(ctx.Follow, ctx.FollowOffset, 0f);
            }
            else
            {
                ctx.LookSample = SampleRaw(ctx.Look, ctx.LookOffset);
                ctx.FollowSample = SampleRaw(ctx.Follow, ctx.FollowOffset);
            }

            ctx.Anchor = ctx.FollowSample.Valid ? ctx.FollowSample.Position
                : (ctx.LookSample.Valid ? ctx.LookSample.Position : Vector3.zero);
            ctx.AnchorRot = ctx.FollowSample.Valid ? ctx.FollowSample.Rotation : Quaternion.identity;

            var solve = selected.BuildContext(ctx.LocalTime, ctx.Duration, in ctx.LookSample, in ctx.FollowSample, composition);
            var pose = CameraPose.Identity;
            pose.Fov = fov;
            pose.NearClip = near;
            pose.FarClip = far;
            pose.FocusDistance = focusDistance;
            SolverDispatch.Solve(ctx.Shot.rigKind, in solve, in rigParams, ref pose);
            pose = BlendAtPlayhead(ctx, pose);
            var layer = selected.overrides.ResolveHandheld(ctx.Shot);
            if (layer.enabled)
            {
                var shake = solve;
                shake.LocalTime = (float)ctx.DirectorTime;
                HandheldSolver.ApplyLayer(in shake, in layer.parameters, ref pose);
            }
            ctx.Pose = pose;
            return ctx;
        }

        static CameraPose BlendAtPlayhead(PreviewContext ctx, CameraPose current)
        {
            if (ctx.Clip == null || ctx.Clip.blendMode == ShotBlendMode.Cut || ctx.Director == null || ctx.TimelineClip == null)
                return Match(ctx, current);
            TimelineClip prev = FindPreviousClip(ctx.Director, ctx.TimelineClip);
            if (prev == null)
                return Match(ctx, current);
            bool overlapping = ctx.DirectorTime < prev.end && ctx.DirectorTime >= ctx.TimelineClip.start;
            float prevLocal = overlapping
                ? Mathf.Clamp((float)prev.ToLocalTimeUnbound(ctx.DirectorTime), 0f, (float)prev.duration)
                : Mathf.Max(0f, (float)prev.duration - 0.02f);
            CameraPose prevPose;
            if (!TrySolveClip(ctx.Director, prev, prevLocal, ctx.Rig, out prevPose))
                return Match(ctx, current);
            var prevClip = prev.asset as CineShotClip;
            EasingAsset curve = ShotBlend.BlendCurve(ctx.Clip, prevClip);
            double overlap = prev.end - ctx.TimelineClip.start;
            CameraPose blended = ShotBlend.Apply(ctx.Clip.blendMode, ctx.Clip.blendDuration, overlap, ctx.LocalTime, true, in prevPose, in current, curve);
            return Match(ctx, blended, overlap, curve);
        }

        static CameraPose Match(PreviewContext ctx, CameraPose pose)
        {
            double overlap = 0d;
            EasingAsset curve = null;
            if (ctx.Clip != null && ctx.Director != null && ctx.TimelineClip != null)
            {
                TimelineClip prev = FindPreviousClip(ctx.Director, ctx.TimelineClip);
                if (prev != null)
                    overlap = prev.end - ctx.TimelineClip.start;
                curve = ShotBlend.BlendCurve(ctx.Clip, prev != null ? prev.asset as CineShotClip : null);
            }
            return Match(ctx, pose, overlap, curve);
        }

        static CameraPose Match(PreviewContext ctx, CameraPose pose, double overlap, EasingAsset curve)
        {
            if (ctx.Clip == null || ctx.Shot == null)
                return pose;
            MatchPoint point = ctx.Clip.overrides.ResolveMatchPoint(ctx.Shot);
            return ShotBlend.ApplyMatch(
                ctx.Clip.blendMode,
                in point,
                ctx.Clip.matchHoldDuration,
                ctx.Clip.blendDuration,
                overlap,
                ctx.LocalTime,
                curve,
                in pose);
        }

        static TimelineClip FindPreviousClip(PlayableDirector director, TimelineClip current)
        {
            var timeline = director.playableAsset as TimelineAsset;
            if (timeline == null || current == null)
                return null;
            TimelineClip best = null;
            foreach (var track in timeline.GetOutputTracks())
            {
                if (!(track is CineShotTrack))
                    continue;
                foreach (var clip in track.GetClips())
                {
                    if (!(clip.asset is CineShotClip) || clip == current)
                        continue;
                    if (clip.start >= current.start)
                        continue;
                    if (best == null || clip.start > best.start)
                        best = clip;
                }
            }
            return best;
        }

        static bool TrySolveClip(PlayableDirector director, TimelineClip timelineClip, float localTime, CineShotRig rig, out CameraPose pose)
        {
            pose = CameraPose.Identity;
            var cine = timelineClip != null ? timelineClip.asset as CineShotClip : null;
            if (cine == null || cine.shot == null)
                return false;
            float duration = Mathf.Max(0.05f, (float)timelineClip.duration);
            localTime = Mathf.Clamp(localTime, 0f, duration);
            RigParams rigParams;
            Vector3 composition;
            float fov, near, far, focusDistance;
            FocusMode mode;
            FocusSettings focusSettings;
            cine.overrides.ApplyTo(cine.shot, out rigParams, out composition, out fov, out near, out far,
                out mode, out focusDistance, out focusSettings);
            Vector3 lookOffset = cine.overrides.ResolveLookOffset(cine.shot);
            Vector3 followOffset = cine.overrides.ResolveFollowOffset(cine.shot);
            Transform look = cine.lookAt.Resolve(director);
            Transform follow = cine.follow.Resolve(director);
            TargetSample lookSample = rig != null ? rig.SampleLookAt(look, lookOffset, 0f) : SampleRaw(look, lookOffset);
            TargetSample followSample = rig != null ? rig.SampleFollow(follow, followOffset, 0f) : SampleRaw(follow, followOffset);
            var solve = cine.BuildContext(localTime, duration, in lookSample, in followSample, composition);
            pose.Fov = fov;
            pose.NearClip = near;
            pose.FarClip = far;
            pose.FocusDistance = focusDistance;
            SolverDispatch.Solve(cine.shot.rigKind, in solve, in rigParams, ref pose);
            return true;
        }

        static TargetSample SampleRaw(Transform target, Vector3 offset)
        {
            if (target == null)
                return default(TargetSample);
            return new TargetSample
            {
                Position = target.TransformPoint(offset),
                Rotation = target.rotation,
                Valid = true
            };
        }

        static PlayableDirector FindDirector(TimelineAsset asset)
        {
            var inspected = TimelineEditor.inspectedDirector;
            if (asset == null)
                return inspected;
            if (inspected != null && inspected.playableAsset == asset)
                return inspected;
#if UNITY_2023_1_OR_NEWER
            var all = UnityEngine.Object.FindObjectsByType<PlayableDirector>(FindObjectsSortMode.None);
#else
            var all = UnityEngine.Object.FindObjectsOfType<PlayableDirector>();
#endif
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].playableAsset == asset)
                    return all[i];
            }
            return inspected;
        }

        static TimelineClip FindClipAtTime(PlayableDirector director, double time)
        {
            var timeline = director.playableAsset as TimelineAsset;
            if (timeline == null)
                return null;
            TimelineClip best = null;
            foreach (var track in timeline.GetOutputTracks())
            {
                if (!(track is CineShotTrack))
                    continue;
                foreach (var clip in track.GetClips())
                {
                    if (clip.asset is CineShotClip && time >= clip.start && time < clip.end)
                    {
                        if (best == null || clip.start >= best.start)
                            best = clip;
                    }
                }
            }
            return best;
        }

        static CineShotRig ResolveRig(PlayableDirector director, TimelineClip clip)
        {
            var track = clip != null ? clip.GetParentTrack() as CineShotTrack : null;
            if (track != null && director != null)
            {
                var bound = director.GetGenericBinding(track) as CineShotRig;
                if (bound != null)
                    return bound;
            }
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindFirstObjectByType<CineShotRig>();
#else
            return UnityEngine.Object.FindObjectOfType<CineShotRig>();
#endif
        }

        static void ApplyPreview(PreviewContext ctx)
        {
            if (ctx.Rig == null || ctx.Rig.OutputVcam == null)
                return;

            if (MatchPointEditState.IsFor(ctx.Clip))
            {
                MatchPoint point = ctx.Clip.overrides.ResolveMatchPoint(ctx.Shot);
                if (point.valid)
                {
                    CameraPose matchPose = ShotBlend.ToPose(in point, in ctx.Pose);
                    PushGameCamera(matchPose);
                    EditorApplication.QueuePlayerLoopUpdate();
                    RepaintGameViews();
                    return;
                }
            }

            Acquire(ctx.Rig);
            Transform focus = ctx.Look != null ? ctx.Look : ctx.Follow;
            bool hasAim = ctx.LookSample.Valid || ctx.FollowSample.Valid;
            Vector3 aim = ctx.LookSample.Valid
                ? ctx.LookSample.Position
                : (ctx.FollowSample.Valid ? ctx.FollowSample.Position : ctx.Pose.Position + Vector3.forward);
            ctx.Rig.SubmitPose(in ctx.Pose, 0f, ctx.Pose.FocusDistance, ctx.FocusMode, focus, in ctx.FocusSettings, aim, hasAim);
            ctx.Rig.SetControlling(true);
            PushGameCamera(ctx.Rig.LastPose);
            EditorApplication.QueuePlayerLoopUpdate();
            RepaintGameViews();
        }

        static void PushGameCamera(CameraPose pose)
        {
            var cam = Camera.main;
            if (cam == null)
                return;
            Quaternion rot = pose.Rotation * Quaternion.AngleAxis(pose.Dutch, Vector3.forward);
            cam.transform.SetPositionAndRotation(pose.Position, rot);
            cam.fieldOfView = Mathf.Clamp(pose.Fov, 1f, 179f);
            cam.nearClipPlane = Mathf.Max(0.01f, pose.NearClip);
            cam.farClipPlane = Mathf.Max(cam.nearClipPlane + 0.01f, pose.FarClip);
        }

        static void RepaintGameViews()
        {
            var gameViewType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
            if (gameViewType == null)
                return;
            var views = Resources.FindObjectsOfTypeAll(gameViewType);
            for (int i = 0; i < views.Length; i++)
            {
                var window = views[i] as EditorWindow;
                if (window != null)
                    window.Repaint();
            }
        }

        static void Acquire(CineShotRig rig)
        {
            if (s_Held && s_Rig == rig)
                return;
            Release();
            var go = rig.OutputVcam.gameObject;
            s_GoWasActive = go.activeSelf;
            if (!go.activeSelf)
                go.SetActive(true);
            OutputBootstrap.Register();
            rig.RebuildOutputs();
            s_Rig = rig;
            s_Held = true;
        }

        static void Release()
        {
            if (!s_Held)
                return;
            if (s_Rig != null)
            {
                s_Rig.SetControlling(false);
                if (s_Rig.OutputVcam != null && !s_GoWasActive)
                    s_Rig.OutputVcam.gameObject.SetActive(false);
            }
            s_Held = false;
            s_Rig = null;
        }

        static void DrawFrustumsAndTargets(PreviewContext ctx)
        {
            var pose = ctx.Pose;
            Handles.color = new Color(0.35f, 0.75f, 1f, 0.95f);
            DrawFrustum(pose.Position, pose.Rotation, pose.Fov, pose.NearClip, Mathf.Min(pose.FarClip, 8f));
            Handles.Label(pose.Position, " 镜头 " + ctx.Shot.displayName);

            if (ctx.LookSample.Valid)
            {
                Handles.color = new Color(1f, 0.85f, 0.2f, 0.9f);
                Handles.DrawDottedLine(pose.Position, ctx.LookSample.Position, 4f);
                Handles.SphereHandleCap(0, ctx.LookSample.Position, Quaternion.identity, HandleUtility.GetHandleSize(ctx.LookSample.Position) * 0.08f, EventType.Repaint);
                Handles.Label(ctx.LookSample.Position, " 注视");
            }
            if (ctx.FollowSample.Valid)
            {
                Handles.color = new Color(0.4f, 1f, 0.55f, 0.9f);
                Handles.DrawWireDisc(ctx.Anchor, Vector3.up, HandleUtility.GetHandleSize(ctx.Anchor) * 0.15f);
                Handles.Label(ctx.Anchor, " 跟随");
            }
        }

        static void DrawMatchPointOverlay(PreviewContext ctx)
        {
            if (ctx.Clip == null || ctx.Shot == null)
                return;
            MatchPoint point = ctx.Clip.overrides.ResolveMatchPoint(ctx.Shot);
            if (!point.valid)
                return;

            float fov = point.fov > 1f ? point.fov : ctx.Pose.Fov;
            float near = Mathf.Max(0.05f, ctx.Pose.NearClip);
            Handles.color = new Color(1f, 0.45f, 0.85f, 0.95f);
            DrawFrustum(point.position, point.rotation, fov, near, Mathf.Min(ctx.Pose.FarClip, 8f));
            Handles.Label(point.position, " 匹配帧");

            if (!MatchPointEditState.IsFor(ctx.Clip) || !MatchPointEditState.Edit)
                return;

            EditorGUI.BeginChangeCheck();
            Vector3 pos = Handles.PositionHandle(point.position, point.rotation);
            Quaternion rot = Handles.RotationHandle(point.rotation, pos);
            if (!EditorGUI.EndChangeCheck())
                return;

            point.position = pos;
            point.rotation = rot;
            WriteMatchPoint(ctx, point, "调整匹配帧锚点");
            PushGameCamera(ShotBlend.ToPose(in point, in ctx.Pose));
            RepaintGameViews();
        }

        internal static void WriteMatchPoint(PreviewContext ctx, MatchPoint point, string undoName)
        {
            if (ctx == null || ctx.Clip == null || ctx.Shot == null)
                return;
            if (ctx.WriteOverride)
            {
                Undo.RecordObject(ctx.Clip, undoName);
                var over = ctx.Clip.overrides;
                over.mask |= ShotOverrideMask.MatchPoint;
                over.matchPoint = point;
                ctx.Clip.overrides = over;
                EditorUtility.SetDirty(ctx.Clip);
            }
            else
            {
                Undo.RecordObject(ctx.Shot, undoName);
                ctx.Shot.matchPoint = point;
                EditorUtility.SetDirty(ctx.Shot);
            }
            SceneView.RepaintAll();
        }

        internal static MatchPoint ResolveSelectedMatchPoint(PreviewContext ctx)
        {
            if (ctx == null || ctx.Clip == null || ctx.Shot == null)
                return default(MatchPoint);
            return ctx.Clip.overrides.ResolveMatchPoint(ctx.Shot);
        }

        internal static double EstimateOverlap(PreviewContext ctx)
        {
            if (ctx == null || ctx.Director == null || ctx.TimelineClip == null)
                return 0d;
            TimelineClip prev = FindPreviousClip(ctx.Director, ctx.TimelineClip);
            return prev != null ? prev.end - ctx.TimelineClip.start : 0d;
        }

        static void DrawFrustum(Vector3 pos, Quaternion rot, float fov, float near, float far)
        {
            float aspect = 16f / 9f;
            Vector3[] n = FrustumCorners(pos, rot, fov, aspect, Mathf.Max(0.05f, near));
            Vector3[] f = FrustumCorners(pos, rot, fov, aspect, Mathf.Max(near + 0.2f, far));
            for (int i = 0; i < 4; i++)
            {
                Handles.DrawLine(n[i], n[(i + 1) % 4]);
                Handles.DrawLine(f[i], f[(i + 1) % 4]);
                Handles.DrawLine(n[i], f[i]);
            }
            Handles.DrawLine(pos, n[0]);
        }

        static Vector3[] FrustumCorners(Vector3 pos, Quaternion rot, float fov, float aspect, float dist)
        {
            float halfY = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * dist;
            float halfX = halfY * aspect;
            Vector3 right = rot * Vector3.right;
            Vector3 up = rot * Vector3.up;
            Vector3 fwd = rot * Vector3.forward;
            Vector3 c = pos + fwd * dist;
            return new[]
            {
                c - right * halfX + up * halfY,
                c + right * halfX + up * halfY,
                c + right * halfX - up * halfY,
                c - right * halfX - up * halfY
            };
        }

        static void DrawGizmos(PreviewContext ctx)
        {
            var p = ctx.Params;
            switch (ctx.Shot.rigKind)
            {
                case RigKind.Static:
                    DrawStatic(ctx, p);
                    break;
                case RigKind.DollyTrack:
                    DrawDolly(ctx, p);
                    break;
                case RigKind.Orbit:
                    DrawOrbit(ctx, p);
                    break;
                case RigKind.PushPull:
                    DrawPush(ctx, p);
                    break;
                case RigKind.Crane:
                    DrawCrane(ctx, p);
                    break;
                case RigKind.Shoulder:
                    DrawShoulder(ctx, p);
                    break;
                case RigKind.SplineDolly:
                    DrawSpline(ctx);
                    break;
                case RigKind.Handheld:
                    DrawHandheld(ctx, p);
                    break;
                case RigKind.Pan:
                    DrawPan(ctx, p);
                    break;
            }

            if (ctx.Look != null)
            {
                Vector3 world = ctx.Look.TransformPoint(ctx.LookOffset);
                float size = HandleUtility.GetHandleSize(world) * 0.12f;
                Handles.color = new Color(1f, 0.75f, 0.15f, 1f);
                EditorGUI.BeginChangeCheck();
#if UNITY_2022_1_OR_NEWER
                Vector3 moved = Handles.FreeMoveHandle(world, size, Vector3.one * 0.05f, Handles.SphereHandleCap);
#else
                Vector3 moved = Handles.FreeMoveHandle(world, Quaternion.identity, size, Vector3.one * 0.05f, Handles.SphereHandleCap);
#endif
                if (EditorGUI.EndChangeCheck())
                    WriteLookOffset(ctx, ctx.Look.InverseTransformPoint(moved));
            }
        }

        static void DrawStatic(PreviewContext ctx, RigParams p)
        {
            Vector3 world = ctx.Anchor + ctx.AnchorRot * p.A;
            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.PositionHandle(world, ctx.AnchorRot);
            if (EditorGUI.EndChangeCheck())
            {
                p.A = Quaternion.Inverse(ctx.AnchorRot) * (moved - ctx.Anchor);
                WriteRig(ctx, p, "调整静态偏移");
            }
            Handles.color = new Color(0.4f, 0.8f, 1f, 0.8f);
            Handles.DrawDottedLine(ctx.Anchor, world, 4f);
            Handles.Label(world, " 机位偏移");
        }

        static void DrawDolly(PreviewContext ctx, RigParams p)
        {
            EditorGUI.BeginChangeCheck();
            Vector3 a = Handles.PositionHandle(p.A, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                p.A = a;
                WriteRig(ctx, p, "调整轨道起点");
            }
            EditorGUI.BeginChangeCheck();
            Vector3 b = Handles.PositionHandle(p.B, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                p.B = b;
                WriteRig(ctx, p, "调整轨道终点");
            }
            Handles.color = new Color(0.4f, 0.85f, 1f, 0.45f);
            Handles.DrawLine(p.A, p.B);
            Handles.color = Color.cyan;
            Vector3 prev = DollyTrackSolver.PointOnTrack(in p, 0f);
            for (int s = 1; s <= 16; s++)
            {
                Vector3 point = DollyTrackSolver.PointOnTrack(in p, s / 16f);
                Handles.DrawLine(prev, point);
                prev = point;
            }
            float u = ctx.Duration > 1e-4f ? Mathf.Clamp01(ctx.LocalTime / ctx.Duration) : 0f;
            float eased = u * u * (3f - 2f * u);
            Vector3 along, side, up;
            DollyTrackSolver.TrackFrame(p.A, p.B, out along, out side, out up);
            Vector3 onTrack = Vector3.Lerp(p.A, p.B, eased);
            Vector3 cam = onTrack + side * p.P1 + up * p.P2;
            Handles.DrawDottedLine(onTrack, cam, 3f);
            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.PositionHandle(cam, Quaternion.LookRotation(along, up));
            if (EditorGUI.EndChangeCheck())
            {
                Vector3 delta = moved - onTrack;
                p.P1 = Vector3.Dot(delta, side);
                p.P2 = Vector3.Dot(delta, up);
                WriteRig(ctx, p, "调整相对轨道偏移");
            }
            Handles.Label(p.A, " 轨道起点");
            Handles.Label(p.B, " 轨道终点");
            Handles.Label(cam, " 相对轨道偏移");
        }

        static void DrawOrbit(PreviewContext ctx, RigParams p)
        {
            bool follow = ctx.FollowSample.Valid;
            Vector3 center = follow ? ctx.FollowSample.Position : p.B;
            float ang = (p.P0 + p.P1 * ctx.LocalTime) * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang));
            float radius = Mathf.Max(0.2f, Mathf.Abs(p.P2));
            Vector3 handle = center + dir * radius + Vector3.up * p.P3;
            Handles.color = new Color(0.5f, 0.85f, 1f, 0.7f);
            Handles.DrawWireDisc(center + Vector3.up * p.P3, Vector3.up, radius);
            if (!follow)
            {
                EditorGUI.BeginChangeCheck();
                Vector3 movedCenter = Handles.PositionHandle(p.B, Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    p.B = movedCenter;
                    WriteRig(ctx, p, "调整环绕圆心");
                }
                Handles.Label(p.B, " 圆心");
            }
            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.PositionHandle(handle, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                Vector3 flat = moved - center;
                p.P3 = flat.y;
                flat.y = 0f;
                p.P2 = Mathf.Max(0.2f, flat.magnitude);
                float newAng = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
                p.P0 = newAng - p.P1 * ctx.LocalTime;
                WriteRig(ctx, p, "调整环绕");
            }
            Handles.Label(handle, " 环绕机位");
        }

        static void DrawPush(PreviewContext ctx, RigParams p)
        {
            Vector3 back = ctx.FollowSample.Valid ? -(ctx.AnchorRot * Vector3.forward) : Vector3.back;
            if (back.sqrMagnitude < 1e-6f)
                back = Vector3.back;
            back.Normalize();
            Vector3 start = ctx.Anchor + back * p.P0 + Vector3.up * p.P2;
            Vector3 end = ctx.Anchor + back * p.P1 + Vector3.up * p.P2;
            Handles.color = Color.white;
            Handles.DrawLine(start, end);
            EditorGUI.BeginChangeCheck();
            Vector3 ms = Handles.PositionHandle(start, Quaternion.LookRotation(back));
            if (EditorGUI.EndChangeCheck())
            {
                p.P0 = Mathf.Max(0.2f, Vector3.Dot(ms - ctx.Anchor, back));
                p.P2 = ms.y - ctx.Anchor.y;
                WriteRig(ctx, p, "调整推拉起点");
            }
            EditorGUI.BeginChangeCheck();
            Vector3 me = Handles.PositionHandle(end, Quaternion.LookRotation(back));
            if (EditorGUI.EndChangeCheck())
            {
                p.P1 = Mathf.Max(0.2f, Vector3.Dot(me - ctx.Anchor, back));
                WriteRig(ctx, p, "调整推拉终点");
            }
            Handles.Label(start, " 起始距离");
            Handles.Label(end, " 结束距离");
        }

        static void DrawCrane(PreviewContext ctx, RigParams p)
        {
            Vector3 origin = ctx.FollowSample.Valid ? ctx.FollowSample.Position : Vector3.zero;
            Handles.color = Color.cyan;
            Vector3 prev = CraneSolver.Point(in p, 0f, origin);
            for (int s = 1; s <= 24; s++)
            {
                Vector3 point = CraneSolver.Point(in p, s / 24f, origin);
                Handles.DrawLine(prev, point);
                prev = point;
            }
            Vector3 start = CraneSolver.Point(in p, 0f, origin);
            Vector3 end = CraneSolver.Point(in p, 1f, origin);
            Vector3 mid = CraneSolver.Point(in p, 0.5f, origin);
            EditorGUI.BeginChangeCheck();
            Vector3 ms = Handles.PositionHandle(start, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                p.A = ms - origin;
                WriteRig(ctx, p, "调整摇臂起点");
            }
            EditorGUI.BeginChangeCheck();
            Vector3 me = Handles.PositionHandle(end, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                p.B = me - origin;
                WriteRig(ctx, p, "调整摇臂终点");
            }
            EditorGUI.BeginChangeCheck();
            Vector3 mm = Handles.PositionHandle(mid, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                Vector3 bulge = (mm - origin) - Vector3.Lerp(p.A, p.B, 0.5f);
                p.P0 = bulge.x;
                p.P1 = bulge.y;
                p.P2 = bulge.z;
                WriteRig(ctx, p, "调整摇臂弧线");
            }
            Handles.Label(start, " 起点");
            Handles.Label(end, " 终点");
            Handles.Label(mid, " 弧线");
            if (ctx.LookSample.Valid)
            {
                Handles.color = new Color(1f, 0.75f, 0.15f, 0.8f);
                Handles.DrawDottedLine(ctx.Pose.Position, ctx.LookSample.Position, 3f);
            }
        }

        static void DrawShoulder(PreviewContext ctx, RigParams p)
        {
            float side = p.I0 >= 0 ? 1f : -1f;
            Vector3 shoulder = ctx.Anchor
                               + ctx.AnchorRot * (Vector3.right * (side * p.P0))
                               + Vector3.up * p.P1
                               + ctx.AnchorRot * (Vector3.back * 0.35f);
            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.PositionHandle(shoulder, ctx.AnchorRot);
            if (EditorGUI.EndChangeCheck())
            {
                p.P1 = moved.y - ctx.Anchor.y;
                Vector3 horizontal = moved - ctx.Anchor - Vector3.up * p.P1;
                horizontal -= ctx.AnchorRot * (Vector3.back * 0.35f);
                Vector3 local = Quaternion.Inverse(ctx.AnchorRot) * horizontal;
                p.P0 = Mathf.Abs(local.x);
                p.I0 = local.x >= 0f ? 1 : -1;
                WriteRig(ctx, p, "调整过肩机位");
            }
            Handles.Label(shoulder, p.I0 >= 0 ? " 右肩" : " 左肩");
        }

        static void DrawPan(PreviewContext ctx, RigParams p)
        {
            var solve = ctx.Clip.BuildContext(ctx.LocalTime, ctx.Duration, in ctx.LookSample, in ctx.FollowSample, Vector3.zero);
            Vector3 cameraOrigin = ctx.FollowSample.Valid ? ctx.FollowSample.Position : Vector3.zero;
            Vector3 lookOrigin = ctx.LookSample.Valid ? ctx.LookSample.Position : cameraOrigin;
            bool lookSpline = PanSolver.HasPath(solve.Spline);
            bool cameraSpline = PanSolver.Advanced(in p) && PanSolver.HasPath(solve.CameraSpline);

            Handles.color = new Color(1f, 0.75f, 0.15f, 0.9f);
            if (lookSpline)
                DrawSplinePath(ctx.Clip.spline, "调整摇镜曲线");
            else
            {
                Vector3 prev = lookOrigin + p.B;
                for (int s = 1; s <= 24; s++)
                {
                    float k = s / 24f;
                    Vector3 point = lookOrigin + Vector3.Lerp(p.B, p.C, k) + new Vector3(p.P0, p.P1, p.P2) * Mathf.Sin(k * Mathf.PI);
                    Handles.DrawLine(prev, point);
                    prev = point;
                }
                DrawMoved(lookOrigin + p.B, " 注视起点", out Vector3 startMoved, out bool startChanged);
                if (startChanged)
                {
                    p.B = startMoved - lookOrigin;
                    WriteRig(ctx, p, "调整摇镜注视起点");
                }
                DrawMoved(lookOrigin + p.C, " 注视终点", out Vector3 endMoved, out bool endChanged);
                if (endChanged)
                {
                    p.C = endMoved - lookOrigin;
                    WriteRig(ctx, p, "调整摇镜注视终点");
                }
                Vector3 mid = lookOrigin + Vector3.Lerp(p.B, p.C, 0.5f) + new Vector3(p.P0, p.P1, p.P2);
                EditorGUI.BeginChangeCheck();
                Vector3 movedMid = Handles.PositionHandle(mid, Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    Vector3 bulge = movedMid - lookOrigin - Vector3.Lerp(p.B, p.C, 0.5f);
                    p.P0 = bulge.x;
                    p.P1 = bulge.y;
                    p.P2 = bulge.z;
                    WriteRig(ctx, p, "调整摇镜注视弧线");
                }
                Handles.Label(mid, " 注视弧线");
            }

            Handles.color = new Color(0.4f, 0.85f, 1f, 0.9f);
            if (cameraSpline)
                DrawSplinePath(ctx.Clip.cameraSpline, "调整摇镜曲线");
            else
            {
                DrawMoved(cameraOrigin + p.A, " 机位", out Vector3 camMoved, out bool camChanged);
                if (camChanged)
                {
                    p.A = camMoved - cameraOrigin;
                    WriteRig(ctx, p, "调整摇镜机位");
                }
            }

            Handles.color = new Color(1f, 0.92f, 0.4f, 0.7f);
            Handles.DrawDottedLine(ctx.Pose.Position, PanSolver.LookPoint(in solve, in p, PanSolver.Progress(in solve)), 3f);
        }

        static void DrawMoved(Vector3 world, string label, out Vector3 moved, out bool changed)
        {
            EditorGUI.BeginChangeCheck();
            moved = Handles.PositionHandle(world, Quaternion.identity);
            changed = EditorGUI.EndChangeCheck();
            Handles.Label(world, label);
        }

        static void DrawSplinePath(CineSpline spline, string undoName)
        {
            if (spline == null || spline.knots == null)
                return;
            Vector3 prev = spline.Evaluate(0f);
            for (int s = 1; s <= 24; s++)
            {
                Vector3 point = spline.Evaluate(s / 24f);
                Handles.DrawLine(prev, point);
                prev = point;
            }
            for (int i = 0; i < spline.knots.Length; i++)
            {
                EditorGUI.BeginChangeCheck();
                Vector3 moved = Handles.PositionHandle(spline.knots[i], Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(spline, undoName);
                    spline.knots[i] = moved;
                    EditorUtility.SetDirty(spline);
                }
                Handles.Label(spline.knots[i], " 点" + (i + 1));
            }
        }

        static void DrawHandheld(PreviewContext ctx, RigParams p)
        {
            if (ctx.Shot != null && ctx.Shot.rigKind == RigKind.Handheld)
            {
                Vector3 world = ctx.Anchor + ctx.AnchorRot * p.A;
                EditorGUI.BeginChangeCheck();
                Vector3 movedBase = Handles.PositionHandle(world, ctx.AnchorRot);
                if (EditorGUI.EndChangeCheck())
                {
                    p.A = Quaternion.Inverse(ctx.AnchorRot) * (movedBase - ctx.Anchor);
                    WriteRig(ctx, p, "调整手持基座");
                }
                Handles.Label(world, " 手持基座");
            }

            // 橙色球 = 抖动振幅 P0：沿机位右轴拖动，半径即振幅
            Vector3 origin = ctx.Pose.Position;
            Vector3 axis = ctx.Pose.Rotation * Vector3.right;
            float amp = Mathf.Max(0.02f, p.P0);
            float handle = Mathf.Clamp(HandleUtility.GetHandleSize(origin) * 0.12f, 0.04f, 0.2f);
            Handles.color = new Color(1f, 0.45f, 0.3f, 0.85f);
            Handles.DrawWireDisc(origin, ctx.Pose.Rotation * Vector3.forward, amp);
            Handles.DrawDottedLine(origin, origin + axis * amp, 3f);
            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.Slider(origin + axis * amp, axis, handle, Handles.SphereHandleCap, 0.01f);
            if (EditorGUI.EndChangeCheck())
            {
                p.P0 = Mathf.Max(0f, Vector3.Dot(moved - origin, axis));
                WriteRig(ctx, p, "调整手持振幅");
            }
            Handles.Label(origin + axis * amp, " 振幅 " + p.P0.ToString("0.00"));
        }

        static void DrawSpline(PreviewContext ctx)
        {
            var spline = ctx.Clip != null ? ctx.Clip.spline : null;
            if (spline == null || spline.knots == null)
            {
                Handles.Label(ctx.Pose.Position, " 未指定样条");
                return;
            }
            Handles.color = new Color(1f, 0.55f, 0.2f, 0.9f);
            DrawSplinePath(spline, "调整样条控制点");
            if (ctx.LookSample.Valid)
            {
                Handles.color = new Color(1f, 0.85f, 0.2f, 0.85f);
                Handles.DrawDottedLine(ctx.Pose.Position, ctx.LookSample.Position, 3f);
                Handles.Label(ctx.LookSample.Position, " 注视");
            }
        }

        internal static void SaveClipToTemplate(PreviewContext ctx)
        {
            if (ctx == null || ctx.Clip == null || ctx.Shot == null)
                return;
            Undo.RecordObject(ctx.Shot, "保存至模板");
            var over = ctx.Clip.overrides;
            ctx.Shot.rigParams = ctx.Params;
            ctx.Shot.fov = ctx.AuthoredFov;
            if ((over.mask & ShotOverrideMask.Optics) != 0)
            {
                ctx.Shot.nearClip = over.nearClip;
                ctx.Shot.farClip = over.farClip;
            }
            if ((over.mask & ShotOverrideMask.Composition) != 0)
                ctx.Shot.compositionOffset = over.compositionOffset;
            if ((over.mask & ShotOverrideMask.Focus) != 0)
            {
                ctx.Shot.focusMode = over.focusMode;
                ctx.Shot.focusDistance = over.focusDistance;
                ctx.Shot.focusSettings = over.focusSettings;
            }
            if ((over.mask & ShotOverrideMask.Easing) != 0)
            {
                if (over.easeIn != null)
                    ctx.Shot.easeIn = over.easeIn;
                if (over.easeOut != null)
                    ctx.Shot.easeOut = over.easeOut;
            }
            if ((over.mask & ShotOverrideMask.PriorityTags) != 0)
                ctx.Shot.priority = over.priority;
            if ((over.mask & ShotOverrideMask.TargetOffset) != 0)
            {
                var binding = ctx.Shot.targetBinding;
                binding.lookAtOffset = over.lookAtOffset;
                binding.followOffset = over.followOffset;
                ctx.Shot.targetBinding = binding;
            }
            if ((over.mask & ShotOverrideMask.MatchPoint) != 0)
                ctx.Shot.matchPoint = over.matchPoint;
            if ((over.mask & ShotOverrideMask.Handheld) != 0)
                ctx.Shot.handheld = over.handheld;
            EditorUtility.SetDirty(ctx.Shot);
            SceneView.RepaintAll();
        }

        internal static void WriteRig(PreviewContext ctx, RigParams p, string undoName)
        {
            if (ctx.WriteOverride)
            {
                Undo.RecordObject(ctx.Clip, undoName);
                var over = ctx.Clip.overrides;
                over.mask |= ShotOverrideMask.RigParams;
                over.rigParams = p;
                ctx.Clip.overrides = over;
                EditorUtility.SetDirty(ctx.Clip);
            }
            else if (ctx.Shot != null)
            {
                Undo.RecordObject(ctx.Shot, undoName);
                ctx.Shot.rigParams = p;
                EditorUtility.SetDirty(ctx.Shot);
                if (ctx.Clip != null && (ctx.Clip.overrides.mask & ShotOverrideMask.RigParams) != 0)
                {
                    Undo.RecordObject(ctx.Clip, undoName);
                    var over = ctx.Clip.overrides;
                    over.mask &= ~ShotOverrideMask.RigParams;
                    ctx.Clip.overrides = over;
                    EditorUtility.SetDirty(ctx.Clip);
                }
            }
            SceneView.RepaintAll();
        }

        internal static void WriteLookOffset(PreviewContext ctx, Vector3 lookOffset)
        {
            if (ctx.Clip == null)
                return;
            if (ctx.WriteOverride)
            {
                Undo.RecordObject(ctx.Clip, "调整注视偏移");
                var over = ctx.Clip.overrides;
                bool had = (over.mask & ShotOverrideMask.TargetOffset) != 0;
                over.mask |= ShotOverrideMask.TargetOffset;
                if (!had && ctx.Shot != null)
                    over.followOffset = ctx.Shot.targetBinding.followOffset;
                over.lookAtOffset = lookOffset;
                ctx.Clip.overrides = over;
                EditorUtility.SetDirty(ctx.Clip);
            }
            else if (ctx.Shot != null)
            {
                Undo.RecordObject(ctx.Shot, "调整注视偏移");
                var binding = ctx.Shot.targetBinding;
                binding.lookAtOffset = lookOffset;
                ctx.Shot.targetBinding = binding;
                EditorUtility.SetDirty(ctx.Shot);
                if ((ctx.Clip.overrides.mask & ShotOverrideMask.TargetOffset) != 0)
                {
                    Undo.RecordObject(ctx.Clip, "调整注视偏移");
                    var over = ctx.Clip.overrides;
                    over.mask &= ~ShotOverrideMask.TargetOffset;
                    ctx.Clip.overrides = over;
                    EditorUtility.SetDirty(ctx.Clip);
                }
            }
            SceneView.RepaintAll();
        }

        internal static void WriteFov(PreviewContext ctx, float fov)
        {
            if (ctx.Clip == null || ctx.Shot == null)
                return;
            if (ctx.WriteOverride)
            {
                Undo.RecordObject(ctx.Clip, "调整视场角");
                var over = ctx.Clip.overrides;
                bool had = (over.mask & ShotOverrideMask.Optics) != 0;
                over.mask |= ShotOverrideMask.Optics;
                over.fov = fov;
                if (!had)
                {
                    over.nearClip = ctx.Shot.nearClip;
                    over.farClip = ctx.Shot.farClip;
                }
                ctx.Clip.overrides = over;
                EditorUtility.SetDirty(ctx.Clip);
            }
            else
            {
                Undo.RecordObject(ctx.Shot, "调整视场角");
                ctx.Shot.fov = fov;
                EditorUtility.SetDirty(ctx.Shot);
                if ((ctx.Clip.overrides.mask & ShotOverrideMask.Optics) != 0)
                {
                    Undo.RecordObject(ctx.Clip, "调整视场角");
                    var over = ctx.Clip.overrides;
                    over.mask &= ~ShotOverrideMask.Optics;
                    ctx.Clip.overrides = over;
                    EditorUtility.SetDirty(ctx.Clip);
                }
            }
        }

        internal static void AlignScene(CameraPose pose)
        {
            var view = SceneView.lastActiveSceneView;
            if (view == null)
                return;
            float distance = Mathf.Max(0.5f, view.cameraDistance);
            view.rotation = pose.Rotation;
            view.pivot = pose.Position + pose.Rotation * Vector3.forward * distance;
            view.Repaint();
        }

        internal static void SampleFromScene(PreviewContext ctx)
        {
            var view = SceneView.lastActiveSceneView;
            if (view == null || view.camera == null)
                return;
            Vector3 camPos = view.camera.transform.position;
            var p = ctx.Params;
            switch (ctx.Shot.rigKind)
            {
                case RigKind.Static:
                    p.A = Quaternion.Inverse(ctx.AnchorRot) * (camPos - ctx.Anchor);
                    WriteRig(ctx, p, "从视口采样静态机位");
                    break;
                case RigKind.DollyTrack:
                    if ((camPos - p.A).sqrMagnitude <= (camPos - p.B).sqrMagnitude)
                        p.A = camPos;
                    else
                        p.B = camPos;
                    WriteRig(ctx, p, "从视口采样轨道端点");
                    break;
                case RigKind.Orbit:
                {
                    Vector3 center = ctx.FollowSample.Valid ? ctx.FollowSample.Position : p.B;
                    Vector3 flat = camPos - center;
                    p.P3 = flat.y;
                    flat.y = 0f;
                    p.P2 = Mathf.Max(0.2f, flat.magnitude);
                    float ang = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
                    p.P0 = ang - p.P1 * ctx.LocalTime;
                    WriteRig(ctx, p, "从视口采样环绕");
                    break;
                }
                case RigKind.PushPull:
                {
                    Vector3 back = ctx.FollowSample.Valid ? -(ctx.AnchorRot * Vector3.forward) : Vector3.back;
                    if (back.sqrMagnitude < 1e-6f)
                        back = Vector3.back;
                    back.Normalize();
                    float dist = Mathf.Max(0.2f, Vector3.Dot(camPos - ctx.Anchor, back));
                    if (ctx.LocalTime < ctx.Duration * 0.5f)
                        p.P0 = dist;
                    else
                        p.P1 = dist;
                    p.P2 = camPos.y - ctx.Anchor.y;
                    WriteRig(ctx, p, "从视口采样推拉");
                    break;
                }
                case RigKind.Crane:
                {
                    Vector3 origin = ctx.FollowSample.Valid ? ctx.FollowSample.Position : Vector3.zero;
                    if (ctx.LocalTime < ctx.Duration * 0.5f)
                        p.A = camPos - origin;
                    else
                        p.B = camPos - origin;
                    WriteRig(ctx, p, "从视口采样摇臂");
                    break;
                }
                case RigKind.Shoulder:
                {
                    p.P1 = camPos.y - ctx.Anchor.y;
                    Vector3 horizontal = camPos - ctx.Anchor - Vector3.up * p.P1;
                    horizontal -= ctx.AnchorRot * (Vector3.back * 0.35f);
                    Vector3 local = Quaternion.Inverse(ctx.AnchorRot) * horizontal;
                    p.P0 = Mathf.Abs(local.x);
                    p.I0 = local.x >= 0f ? 1 : -1;
                    WriteRig(ctx, p, "从视口采样过肩");
                    break;
                }
                case RigKind.SplineDolly:
                    if (ctx.Clip.spline != null && ctx.Clip.spline.knots != null && ctx.Clip.spline.knots.Length > 0)
                    {
                        int nearest = 0;
                        float best = float.MaxValue;
                        for (int i = 0; i < ctx.Clip.spline.knots.Length; i++)
                        {
                            float d = (ctx.Clip.spline.knots[i] - camPos).sqrMagnitude;
                            if (d < best)
                            {
                                best = d;
                                nearest = i;
                            }
                        }
                        Undo.RecordObject(ctx.Clip.spline, "从视口采样样条点");
                        ctx.Clip.spline.knots[nearest] = camPos;
                        EditorUtility.SetDirty(ctx.Clip.spline);
                    }
                    break;
                case RigKind.Pan:
                {
                    Vector3 origin = ctx.FollowSample.Valid ? ctx.FollowSample.Position : Vector3.zero;
                    p.A = camPos - origin;
                    WriteRig(ctx, p, "从视口采样摇镜机位");
                    break;
                }
            }
        }

        static void TryHookTimelineButton()
        {
            var stateProp = typeof(TimelineEditor).GetProperty("state", BindingFlags.NonPublic | BindingFlags.Static);
            if (stateProp == null)
                return;
            var state = stateProp.GetValue(null, null);
            if (state == null || ReferenceEquals(state, s_HookedTimelineState))
                return;
            var evt = state.GetType().GetEvent("windowOnGuiStarted", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (evt == null)
                return;
            var handlerType = evt.EventHandlerType;
            var invoke = handlerType.GetMethod("Invoke");
            if (invoke == null)
                return;
            var parameters = invoke.GetParameters();
            if (parameters.Length != 2)
                return;
            var p0 = Expression.Parameter(parameters[0].ParameterType, "s");
            var p1 = Expression.Parameter(parameters[1].ParameterType, "e");
            var call = Expression.Call(typeof(CineShotSceneEditor).GetMethod("DrawTimelineOpenButton", BindingFlags.NonPublic | BindingFlags.Static));
            var lambda = Expression.Lambda(handlerType, call, p0, p1);
            evt.AddEventHandler(state, lambda.Compile());
            s_HookedTimelineState = state;
        }

        static void DrawTimelineOpenButton()
        {
            var clip = TimelineEditor.selectedClip;
            if (clip == null || !(clip.asset is CineShotClip))
                return;
            var window = TimelineEditor.GetWindow();
            if (window == null)
                return;
            var rect = new Rect(window.position.width - 132f, 1f, 124f, 17f);
            if (GUI.Button(rect, "打开镜头配置"))
                CineShotClipConfigWindow.Open();
        }

        internal static string RigLabel(RigKind kind)
        {
            switch (kind)
            {
                case RigKind.Static: return "静态";
                case RigKind.DollyTrack: return "轨道";
                case RigKind.Orbit: return "环绕";
                case RigKind.PushPull: return "推拉";
                case RigKind.Crane: return "摇臂";
                case RigKind.Shoulder: return "过肩";
                case RigKind.Handheld: return "手持";
                case RigKind.SplineDolly: return "样条";
                case RigKind.Pan: return "摇镜";
                default: return kind.ToString();
            }
        }
    }
}
