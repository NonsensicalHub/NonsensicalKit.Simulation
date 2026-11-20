using System.Collections.Generic;
using CineShot.Core;
using UnityEditor;
using UnityEngine;

namespace CineShot.Editor
{
    static class CineTips
    {
        public const string Blend =
            "作用在本镜进入时。有重叠用重叠时长；无重叠才用混合时间。\n" +
            "混入曲线控快慢（缺省用上一镜淡出，再缺省平滑步进）。\n" +
            "硬切直接跳；匹配帧先贴住锚点，再按剩余窗口混到本镜。";
        public const string BlendDuration = "仅无重叠时生效：叠化窗口时长。匹配帧时也是整段窗口 W。";
        public const string MatchHold =
            "匹配帧开头贴住锚点的秒数。到期后从锚点按剩余窗口 W−H 混到本镜。\n" +
            "若贴住时长 ≥ 叠化窗口，将硬切到本镜。";
        public const string MatchPoint =
            "转场=匹配帧时进入贴住的位姿。勾选启用后可在 Scene 看到异色相机框；用预览/修改推相机或拖手柄。";
        public const string EaseIn = "进入混入快慢。空则用上一镜淡出曲线。";
        public const string EaseOut = "给下一镜当混入曲线用（下一镜未指定混入时）。";
        public const string LookFollow =
            "可空。注视决定朝向（缺省用跟随）；跟随决定锚点/圆心。\n" +
            "可绑同一物体：用各自偏移（如注视抬高到头、跟随在脚）。";
        public const string MatchKey = "铺轨内部键：镜头编号#行号，不是镜头参数。";
        public const string WriteTemplate = "勾选改模板（同模板片段一起变）；不勾选只改本片段覆写。";
        public const string FocusMode =
            "对焦距离怎么取：无=不驱动；跟随目标=每帧用机位到目标距离；固定距离=用下方数值；\n" +
            "超焦距=按焦距、光圈、弥散圈算出超焦距。跟的是清晰面距离，不是「焦距（毫米）」。关虚化用景深模式=关闭。";
        public const string FocusDistance =
            "对焦距离（米）：仅「固定距离」使用。跟随目标、超焦距时由模块自己算。";
        public const string DofMode =
            "URP Volume 虚化。关闭=不虚化；高斯轻量（清晰带跟对焦距离）；散景更真（写 Focus Distance）。";
        public const string DofDamping = "跟焦平滑：越大越跟手、越小越「飘」。";
        public const string DofMaxSpeed = "跟焦速度硬上限（单位/秒），防止瞬间跳焦。";
        public const string DofPull = "焦点源切换时的过渡秒数（例如目标变了）。";
        public const string DofAperture =
            "光圈 F。散景：越小景深越浅。高斯：兼作清晰带宽度参考（越大过渡越宽）。";
        public const string DofFocalLength =
            "镜头焦距（毫米），只影响虚化强弱，不随目标移动。对焦距离才跟目标。";
        public const string DofBlades = "仅散景：光圈叶片数，影响散景多边形轮廓。";
        public const string DofCoc = "超焦距模式用的弥散圈（米），全画幅常用 0.00003。越小算出的超焦距越远。";
        public const string Rig =
            "绑定到 CineShotTrack。输出虚拟相机写运镜；游戏虚拟相机打断时交还。";
        public const string OutputVcam = "运镜位姿写入并在播放时接管主相机。";
        public const string GameVcam = "打断/结束时从此混合回玩法相机。";
        public const string MinDist = "机位与焦点目标的最小距离，防穿模。";
        public const string Player = "代码调用 Play / Interrupt / Resume / Stop 控制播放、打断、续播、停止。";
        public const string ShotAsset = "库级模板。场景目标在 Timeline 片段上绑，不在这里绑。";
        public const string Track = "绑定场景 CineShotRig。镜头混合在 Mixer 内完成。";
        public const string Easing = "混合快慢曲线。运行时查 65 点表，改类型或曲线后自动重建。";
        public const string Spline = "弧长 Catmull-Rom。样条机位或摇镜注视走这条。";
        public const string Sequence = "分镜行序列；用分镜表幂等铺到 CineShotTrack。";
        public const string Storyboard = "按 shotGuid#行号匹配铺轨，不会整轨清空。";
        public const string PanSpeed = "时间→路程。空=平滑步进。";
        public const string CameraSpline = "摇镜高级开启后，机位沿此曲线动。";
        public const string Overrides =
            "点「覆写」改本片段；「模板」跟随镜头资产。手持层≠主机位「手持」参数。";
        public const string Priority =
            "同刻叠片（开拍时间几乎相同）时数值大者做当前镜；普通前后叠化仍以后开镜为准。";
        public const string Tags = "只给镜头库搜索用（名称也匹配标签；#标签 只筛标签），播放不读。";
    }

    static class CineHelp
    {
        public static Rect Line(string tip)
        {
            var rect = EditorGUILayout.GetControlRect();
            return Trim(rect, tip);
        }

        public static Rect Trim(Rect rect, string tip)
        {
            if (string.IsNullOrEmpty(tip))
                return rect;
            // 多行属性（数组等）只把帮助图标钉在首行，避免被拉成整块高度
            var icon = new Rect(rect.xMax - 18f, rect.y, 18f, EditorGUIUtility.singleLineHeight);
            rect.width -= 20f;
            Draw(icon, tip);
            return rect;
        }

        public static void Draw(Rect rect, string tip)
        {
            var source = EditorGUIUtility.IconContent("_Help");
            var content = new GUIContent(source != null ? source.image : null, tip);
            if (content.image == null)
                content.text = "?";
            GUI.Label(rect, content);
        }

        public static void Prop(string tip, SerializedProperty prop, GUIContent label)
        {
            if (prop == null)
                return;
            float height = EditorGUI.GetPropertyHeight(prop, label, true);
            var rect = EditorGUILayout.GetControlRect(true, height);
            EditorGUI.PropertyField(Trim(rect, tip), prop, label, true);
        }

        static readonly DofMode[] k_DofModes = { DofMode.Off, DofMode.Gaussian, DofMode.Bokeh };
        static readonly string[] k_DofNames = { "关闭", "高斯模糊", "散景 Bokeh" };

        public static DofMode DofPopup(string label, DofMode current)
        {
            int idx = IndexOf(current);
            int next = EditorGUILayout.Popup(new GUIContent(label, CineTips.DofMode), idx, k_DofNames);
            return k_DofModes[Mathf.Clamp(next, 0, k_DofModes.Length - 1)];
        }

        public static void DofPopup(Rect rect, SerializedProperty prop)
        {
            if (prop == null)
                return;
            int idx = IndexOf((DofMode)prop.intValue);
            rect = Trim(rect, CineTips.DofMode);
            int next = EditorGUI.Popup(rect, "景深模式", idx, k_DofNames);
            prop.intValue = (int)k_DofModes[Mathf.Clamp(next, 0, k_DofModes.Length - 1)];
        }

        static int IndexOf(DofMode mode)
        {
            for (int i = 0; i < k_DofModes.Length; i++)
            {
                if (k_DofModes[i] == mode)
                    return i;
            }
            return 0;
        }
    }

    [CustomEditor(typeof(CineShotRig))]
    public sealed class CineShotRigEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            CineHelp.Prop(CineTips.Rig, serializedObject.FindProperty("m_OutputVcam"), new GUIContent("输出虚拟相机", CineTips.OutputVcam));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_GameVcam"), new GUIContent("游戏虚拟相机", CineTips.GameVcam));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_MinTargetDistance"), new GUIContent("与目标最小距离", CineTips.MinDist));
            serializedObject.ApplyModifiedProperties();

            var rig = (CineShotRig)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("运行时", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("输出适配器", rig.Output != null ? rig.Output.GetType().Name : "无");
            EditorGUILayout.LabelField("已有位姿", rig.HasPose ? "是" : "否");
            if (GUILayout.Button("重建输出适配器"))
            {
                rig.RebuildOutputs();
                EditorUtility.SetDirty(rig);
            }
        }
    }

    [CustomEditor(typeof(CineShotPlayer))]
    public sealed class CineShotPlayerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            CineHelp.Prop(CineTips.Player, serializedObject.FindProperty("m_Director"), new GUIContent("Timeline 导演"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Rig"), new GUIContent("运镜绑定"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_DefaultInterruptBlend"), new GUIContent("默认打断混合时长（秒）"));
            serializedObject.ApplyModifiedProperties();

            var player = (CineShotPlayer)target;
            if (player.Player != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("当前状态", StateZh(player.Player.State));
                EditorGUILayout.LabelField("当前镜头", player.Player.CurrentShot != null ? player.Player.CurrentShot.displayName : "-");
            }
        }

        static string StateZh(CineState state)
        {
            switch (state)
            {
                case CineState.Idle: return "空闲";
                case CineState.Playing: return "播放中";
                case CineState.PausedResumable: return "已打断可续播";
                case CineState.BlendingOut: return "交权混合中";
                default: return state.ToString();
            }
        }
    }

    [CustomEditor(typeof(CineShotClip))]
    public sealed class CineShotClipInspector : UnityEditor.Editor
    {
        static bool s_LayFoldout;

        void OnDisable()
        {
            var clip = target as CineShotClip;
            if (clip != null && MatchPointEditState.Clip == clip)
                MatchPointEditState.Clear(true);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            if (GUILayout.Button("打开镜头配置"))
                CineShotClipConfigWindow.Open();
            DrawShotPopup(serializedObject.FindProperty("shot"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("overrides"), new GUIContent("片段覆写", CineTips.Overrides), true);
            CineHelp.Prop(CineTips.LookFollow, serializedObject.FindProperty("lookAt"), new GUIContent("注视目标"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("follow"), new GUIContent("跟随目标"));
            var blendMode = serializedObject.FindProperty("blendMode");
            CineHelp.Prop(CineTips.Blend, blendMode, new GUIContent("转场模式"));
            bool isBlend = blendMode.enumValueIndex == (int)ShotBlendMode.Blend;
            bool isMatch = blendMode.enumValueIndex == (int)ShotBlendMode.MatchFrame;
            if (isBlend || isMatch)
                EditorGUILayout.PropertyField(serializedObject.FindProperty("blendDuration"), new GUIContent("混合时间（秒）", CineTips.BlendDuration));
            if (isMatch)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("matchHoldDuration"),
                    new GUIContent("匹配贴住时长（秒）", CineTips.MatchHold));
                DrawMatchHoldWarning();
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("easeIn"), new GUIContent("混入曲线", CineTips.EaseIn));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("easeOut"), new GUIContent("淡出曲线", CineTips.EaseOut));
            var shot = serializedObject.FindProperty("shot").objectReferenceValue as ShotAsset;
            bool pan = shot != null && shot.rigKind == RigKind.Pan;
            EditorGUILayout.PropertyField(serializedObject.FindProperty("spline"), new GUIContent(pan ? "注视曲线" : "样条资产", CineTips.Spline));
            if (pan)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("cameraSpline"), new GUIContent("机位曲线", CineTips.CameraSpline));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("panSpeed"), new GUIContent("摇镜速度", CineTips.PanSpeed));
            }
            s_LayFoldout = EditorGUILayout.Foldout(s_LayFoldout, "分镜铺轨", true);
            if (s_LayFoldout)
            {
                CineHelp.Prop(CineTips.MatchKey, serializedObject.FindProperty("matchKey"), new GUIContent("铺轨匹配键"));
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("sourceHash"), new GUIContent("源哈希"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("lastAppliedDuration"), new GUIContent("上次铺轨时长"));
                }
            }
            serializedObject.ApplyModifiedProperties();
        }

        void DrawMatchHoldWarning()
        {
            var clip = target as CineShotClip;
            if (clip == null)
                return;
            float hold = serializedObject.FindProperty("matchHoldDuration").floatValue;
            float blend = serializedObject.FindProperty("blendDuration").floatValue;
            double overlap = 0d;
            var ctx = CineShotSceneEditor.BuildContext();
            if (ctx != null && ctx.Clip == clip)
                overlap = CineShotSceneEditor.EstimateOverlap(ctx);
            if (!ShotBlend.MatchHoldExceedsWindow(hold, overlap, blend))
                return;
            string tip = overlap > 0.01d
                ? "匹配贴住时长 ≥ 与上一条的重叠窗口，将硬切到本镜，不做过渡。"
                : "匹配贴住时长 ≥ 混合时间（无重叠时的窗口），将硬切到本镜，不做过渡。";
            EditorGUILayout.HelpBox(tip, MessageType.Warning);
        }

        static void DrawShotPopup(SerializedProperty shotProp)
        {
            var current = shotProp.objectReferenceValue as ShotAsset;
            string[] guids = AssetDatabase.FindAssets("t:ShotAsset");
            var assets = new List<ShotAsset>();
            var labels = new List<string>();
            assets.Add(null);
            labels.Add("（无）");
            int selected = 0;
            for (int i = 0; i < guids.Length; i++)
            {
                var asset = AssetDatabase.LoadAssetAtPath<ShotAsset>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (asset == null)
                    continue;
                assets.Add(asset);
                labels.Add(string.IsNullOrEmpty(asset.displayName) ? asset.name : asset.displayName);
                if (asset == current)
                    selected = assets.Count - 1;
            }
            if (current != null && selected == 0)
            {
                assets.Add(current);
                labels.Add(string.IsNullOrEmpty(current.displayName) ? current.name : current.displayName);
                selected = assets.Count - 1;
            }
            int next = EditorGUILayout.Popup(new GUIContent("镜头资产"), selected, labels.ToArray());
            if (next != selected)
                shotProp.objectReferenceValue = next <= 0 ? null : assets[next];
            var row = GUILayoutUtility.GetLastRect();
            var evt = Event.current;
            if (evt.type == EventType.ContextClick && row.Contains(evt.mousePosition))
            {
                var asset = selected > 0 ? assets[selected] : null;
                var menu = new GenericMenu();
                if (asset != null)
                    menu.AddItem(new GUIContent("选中镜头资产"), false, () => SelectShotAsset(asset));
                else
                    menu.AddDisabledItem(new GUIContent("选中镜头资产"));
                menu.ShowAsContext();
                evt.Use();
            }
        }

        static void SelectShotAsset(ShotAsset asset)
        {
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }
    }
}
