using System.Collections.Generic;
using CineShot.Core;
using UnityEditor;
using UnityEditor.Timeline.Actions;
using UnityEngine;
using UnityEngine.Timeline;

namespace CineShot.Editor
{
    public sealed class CineShotClipConfigWindow : EditorWindow
    {
        Vector2 m_Scroll;

        void OnEnable()
        {
            minSize = new Vector2(260f, 180f);
        }

        [MenuItem("Tools/Cine/镜头配置")]
        public static void Open()
        {
            var win = GetWindow<CineShotClipConfigWindow>("镜头配置");
            win.Show();
            win.Focus();
        }

        void OnInspectorUpdate()
        {
            Repaint();
        }

        void OnGUI()
        {
            float previousLabel = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Clamp(position.width * 0.42f, 72f, 150f);
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            var ctx = CineShotSceneEditor.BuildContext();
            if (ctx == null || ctx.Shot == null)
            {
                EditorGUILayout.LabelField("在 Timeline 选中一个 CineShot 片段。", EditorStyles.miniLabel);
                EndScroll(previousLabel);
                return;
            }

            EditorGUI.LabelField(CineHelp.Line(RigParamsDrawer.Hint(ctx.Shot.rigKind)), ctx.Shot.displayName + "  ·  " + CineShotSceneEditor.RigLabel(ctx.Shot.rigKind), EditorStyles.boldLabel);
            EditorGUILayout.LabelField("预览时间", ctx.LocalTime.ToString("0.00") + " / " + ctx.Duration.ToString("0.00") + " 秒");
            if (ctx.Selected && !ctx.InsidePlayhead && SessionState.GetBool(CineShotSceneEditor.k_FollowKey, true))
                EditorGUILayout.LabelField("播放头不在片段内 · Scene 显示起始帧", EditorStyles.miniLabel);

            bool writeOverride = SessionState.GetBool(CineShotSceneEditor.k_OverrideKey, true);
            bool writeTemplate = !writeOverride;
            bool nextTemplate = EditorGUI.ToggleLeft(CineHelp.Line(CineTips.WriteTemplate), "改为直接写入镜头模板", writeTemplate);
            if (nextTemplate != writeTemplate)
                SessionState.SetBool(CineShotSceneEditor.k_OverrideKey, !nextTemplate);

            bool follow = SessionState.GetBool(CineShotSceneEditor.k_FollowKey, true);
            bool nextFollow = EditorGUILayout.ToggleLeft("预览时间跟随 Timeline 播放头", follow);
            if (nextFollow != follow)
                SessionState.SetBool(CineShotSceneEditor.k_FollowKey, nextFollow);
            if (!nextFollow)
            {
                float manual = SessionState.GetFloat("CineShot.ManualTime", ctx.LocalTime);
                EditorGUI.BeginChangeCheck();
                manual = EditorGUILayout.Slider("预览时间", manual, 0f, ctx.Duration);
                if (EditorGUI.EndChangeCheck())
                    SessionState.SetFloat("CineShot.ManualTime", manual);
            }

            if (position.width < 340f)
            {
                if (GUILayout.Button("Scene 对齐镜头"))
                    CineShotSceneEditor.AlignScene(ctx.Pose);
                if (GUILayout.Button("从 Scene 取样"))
                    CineShotSceneEditor.SampleFromScene(ctx);
            }
            else
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Scene 对齐镜头"))
                        CineShotSceneEditor.AlignScene(ctx.Pose);
                    if (GUILayout.Button("从 Scene 取样"))
                        CineShotSceneEditor.SampleFromScene(ctx);
                }
            }
            if (GUILayout.Button("保存至模板"))
                CineShotSceneEditor.SaveClipToTemplate(ctx);

            DrawBlend(ctx);
            DrawParamSliders(ctx);
            if (ctx.Rig == null)
                EditorGUILayout.LabelField("无 CineShotRig · 仅 Scene 线框", EditorStyles.miniLabel);
            EndScroll(previousLabel);
        }

        void EndScroll(float previousLabel)
        {
            EditorGUILayout.EndScrollView();
            EditorGUIUtility.labelWidth = previousLabel;
        }

        static void DrawBlend(CineShotSceneEditor.PreviewContext ctx)
        {
            if (ctx.Clip == null)
                return;
            EditorGUILayout.Space(4f);
            EditorGUI.BeginChangeCheck();
            var mode = (ShotBlendMode)EditorGUI.EnumPopup(CineHelp.Line(CineTips.Blend), "转场模式", ctx.Clip.blendMode);
            float duration = ctx.Clip.blendDuration;
            float hold = ctx.Clip.matchHoldDuration;
            if (mode == ShotBlendMode.Blend || mode == ShotBlendMode.MatchFrame)
                duration = EditorGUI.FloatField(CineHelp.Line(CineTips.BlendDuration), "混合时间（秒）", duration);
            if (mode == ShotBlendMode.MatchFrame)
            {
                hold = EditorGUI.FloatField(CineHelp.Line(CineTips.MatchHold), "匹配贴住时长（秒）", hold);
                double overlap = CineShotSceneEditor.EstimateOverlap(ctx);
                if (ShotBlend.MatchHoldExceedsWindow(hold, overlap, duration))
                    EditorGUILayout.HelpBox("贴住时长 ≥ 叠化窗口，将硬切到本镜。", MessageType.Warning);
            }
            var easeIn = (EasingAsset)EditorGUILayout.ObjectField(new GUIContent("混入曲线", CineTips.EaseIn), ctx.Clip.easeIn, typeof(EasingAsset), false);
            var easeOut = (EasingAsset)EditorGUILayout.ObjectField(new GUIContent("淡出曲线", CineTips.EaseOut), ctx.Clip.easeOut, typeof(EasingAsset), false);
            if (!EditorGUI.EndChangeCheck())
                return;
            Undo.RecordObject(ctx.Clip, "调整转场");
            ctx.Clip.blendMode = mode;
            ctx.Clip.blendDuration = duration;
            ctx.Clip.matchHoldDuration = Mathf.Max(0f, hold);
            ctx.Clip.easeIn = easeIn;
            ctx.Clip.easeOut = easeOut;
            EditorUtility.SetDirty(ctx.Clip);
        }

        static void DrawParamSliders(CineShotSceneEditor.PreviewContext ctx)
        {
            var p = ctx.Params;
            switch (ctx.Shot.rigKind)
            {
                case RigKind.DollyTrack:
                    if (Field("相对轨道侧向", ref p.P1)) CineShotSceneEditor.WriteRig(ctx, p, "调整轨道侧向");
                    if (Field("相对轨道高度", ref p.P2)) CineShotSceneEditor.WriteRig(ctx, p, "调整轨道高度");
                    break;
                case RigKind.Orbit:
                    if (Field("半径", ref p.P2)) CineShotSceneEditor.WriteRig(ctx, p, "调整环绕半径");
                    if (Field("高度", ref p.P3)) CineShotSceneEditor.WriteRig(ctx, p, "调整环绕高度");
                    EditorGUI.BeginChangeCheck();
                    p.P0 = EditorGUILayout.FloatField(new GUIContent("起始角度（度）", "0° 为世界 +Z。"), p.P0);
                    if (EditorGUI.EndChangeCheck()) CineShotSceneEditor.WriteRig(ctx, p, "调整环绕起始角度");
                    EditorGUI.BeginChangeCheck();
                    p.P1 = EditorGUILayout.FloatField(new GUIContent("角速度（度/秒）", "负值反向。"), p.P1);
                    if (EditorGUI.EndChangeCheck()) CineShotSceneEditor.WriteRig(ctx, p, "调整环绕速度");
                    break;
                case RigKind.PushPull:
                    if (Field("起始距离", ref p.P0)) CineShotSceneEditor.WriteRig(ctx, p, "调整推拉起点");
                    if (Field("结束距离", ref p.P1)) CineShotSceneEditor.WriteRig(ctx, p, "调整推拉终点");
                    break;
                case RigKind.Crane:
                    if (GUILayout.Button("从跟随目标读取起点"))
                    {
                        if (ctx.Follow != null)
                        {
                            p.A = Vector3.zero;
                            CineShotSceneEditor.WriteRig(ctx, p, "从跟随目标读取摇臂起点");
                        }
                    }
                    break;
                case RigKind.Shoulder:
                    if (Field("肩侧距离", ref p.P0)) CineShotSceneEditor.WriteRig(ctx, p, "调整过肩距离");
                    if (Field("肩高", ref p.P1)) CineShotSceneEditor.WriteRig(ctx, p, "调整过肩高度");
                    if (GUILayout.Button(p.I0 >= 0 ? "当前右肩，点此改左肩" : "当前左肩，点此改右肩"))
                    {
                        p.I0 = p.I0 >= 0 ? -1 : 1;
                        CineShotSceneEditor.WriteRig(ctx, p, "切换左右肩");
                    }
                    break;
                case RigKind.Handheld:
                    if (Field("振幅", ref p.P0)) CineShotSceneEditor.WriteRig(ctx, p, "调整手持振幅");
                    if (Field("频率", ref p.P1)) CineShotSceneEditor.WriteRig(ctx, p, "调整手持频率");
                    if (Field("俯仰/方位抖动", ref p.P2)) CineShotSceneEditor.WriteRig(ctx, p, "调整手持角抖");
                    if (Field("滚动抖动", ref p.P3)) CineShotSceneEditor.WriteRig(ctx, p, "调整手持滚抖");
                    break;
                case RigKind.Static:
                case RigKind.SplineDolly:
                    break;
                case RigKind.Pan:
                    DrawPanFields(ctx, ref p);
                    break;
            }

            EditorGUI.BeginChangeCheck();
            float fov = EditorGUILayout.FloatField("视场角", ctx.AuthoredFov);
            if (EditorGUI.EndChangeCheck())
                CineShotSceneEditor.WriteFov(ctx, fov);
        }

        static void DrawPanFields(CineShotSceneEditor.PreviewContext ctx, ref RigParams p)
        {
            EditorGUI.BeginChangeCheck();
            var speed = (EasingAsset)EditorGUILayout.ObjectField(new GUIContent("摇镜速度", CineTips.PanSpeed), ctx.Clip.panSpeed, typeof(EasingAsset), false);
            var lookPath = (CineSpline)EditorGUILayout.ObjectField(new GUIContent("注视曲线", CineTips.Spline), ctx.Clip.spline, typeof(CineSpline), false);
            bool advanced = p.I0 != 0;
            bool nextAdvanced = EditorGUILayout.ToggleLeft(new GUIContent("高级：机位也沿曲线运动", CineTips.CameraSpline), advanced);
            CineSpline cameraPath = ctx.Clip.cameraSpline;
            if (nextAdvanced)
                cameraPath = (CineSpline)EditorGUILayout.ObjectField(new GUIContent("机位曲线", CineTips.CameraSpline), cameraPath, typeof(CineSpline), false);
            if (!EditorGUI.EndChangeCheck())
                return;
            Undo.RecordObject(ctx.Clip, "调整摇镜曲线");
            ctx.Clip.panSpeed = speed;
            ctx.Clip.spline = lookPath;
            ctx.Clip.cameraSpline = cameraPath;
            EditorUtility.SetDirty(ctx.Clip);
            if (nextAdvanced != advanced)
            {
                p.I0 = nextAdvanced ? 1 : 0;
                CineShotSceneEditor.WriteRig(ctx, p, "切换摇镜高级");
            }
        }

        static bool Field(string label, ref float field)
        {
            EditorGUI.BeginChangeCheck();
            float value = EditorGUILayout.FloatField(label, field);
            if (EditorGUI.EndChangeCheck())
            {
                field = value;
                return true;
            }
            return false;
        }
    }

    [MenuEntry("打开镜头配置", 100)]
    sealed class OpenCineShotConfigAction : ClipAction
    {
        public override ActionValidity Validate(IEnumerable<TimelineClip> clips)
        {
            foreach (var clip in clips)
            {
                if (clip != null && clip.asset is CineShotClip)
                    return ActionValidity.Valid;
            }
            return ActionValidity.NotApplicable;
        }

        public override bool Execute(IEnumerable<TimelineClip> clips)
        {
            CineShotClipConfigWindow.Open();
            return true;
        }
    }
}
