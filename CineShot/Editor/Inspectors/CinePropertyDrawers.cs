using CineShot.Core;
using UnityEditor;
using UnityEngine;

namespace CineShot.Editor
{
    [CustomPropertyDrawer(typeof(ShotOverrideMask))]
    public sealed class ShotOverrideMaskDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.LabelField(position, "覆写掩码", "由下方各分区开关写入，一般无需手改。");
        }
    }

    [CustomPropertyDrawer(typeof(CineSequenceAsset.TimelineAssetRef))]
    public sealed class TimelineAssetRefDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var asset = property.FindPropertyRelative("asset");
            EditorGUI.PropertyField(position, asset, new GUIContent(string.IsNullOrEmpty(label.text) ? "Timeline 资产" : label.text));
        }
    }

    [CustomPropertyDrawer(typeof(RigParams))]
    public sealed class RigParamsDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!property.isExpanded)
                return EditorGUIUtility.singleLineHeight;
            RigKind kind = ResolveKind(property);
            string a, b, p0, p1, p2, p3, i0, i1;
            LabelsFor(kind, out a, out b, out p0, out p1, out p2, out p3, out i0, out i1);
            int n = CountDrawn(a, b, p0, p1, p2, p3, i0, i1);
            if (kind == RigKind.Pan)
                n++;
            return EditorGUIUtility.singleLineHeight * (1 + n) + n + 4f;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            RigKind kind = ResolveKind(property);
            label = new GUIContent(string.IsNullOrEmpty(label.text) ? "机位参数" : label.text);

            var line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            line = CineHelp.Trim(line, Hint(kind));
            property.isExpanded = EditorGUI.Foldout(line, property.isExpanded, label, true);
            if (!property.isExpanded)
                return;

            EditorGUI.indentLevel++;
            float y = line.yMax + 2f;
            float w = position.width;

            string a, b, p0, p1, p2, p3, i0, i1;
            LabelsFor(kind, out a, out b, out p0, out p1, out p2, out p3, out i0, out i1);

            Draw(ref y, w, position.x, property.FindPropertyRelative("A"), a);
            Draw(ref y, w, position.x, property.FindPropertyRelative("B"), b);
            if (kind == RigKind.Pan)
                Draw(ref y, w, position.x, property.FindPropertyRelative("C"), "注视弧终点");
            Draw(ref y, w, position.x, property.FindPropertyRelative("P0"), p0);
            Draw(ref y, w, position.x, property.FindPropertyRelative("P1"), p1);
            Draw(ref y, w, position.x, property.FindPropertyRelative("P2"), p2);
            Draw(ref y, w, position.x, property.FindPropertyRelative("P3"), p3);
            Draw(ref y, w, position.x, property.FindPropertyRelative("I0"), i0);
            Draw(ref y, w, position.x, property.FindPropertyRelative("I1"), i1);
            EditorGUI.indentLevel--;
        }

        static int CountDrawn(params string[] labels)
        {
            int n = 0;
            for (int i = 0; i < labels.Length; i++)
            {
                if (!string.IsNullOrEmpty(labels[i]))
                    n++;
            }
            return n;
        }

        static void Draw(ref float y, float w, float x, SerializedProperty p, string label)
        {
            if (p == null || string.IsNullOrEmpty(label))
                return;
            EditorGUI.PropertyField(new Rect(x, y, w, EditorGUIUtility.singleLineHeight), p, new GUIContent(label));
            y += EditorGUIUtility.singleLineHeight + 1f;
        }

        static RigKind ResolveKind(SerializedProperty property)
        {
            if (property.propertyPath.IndexOf("handheld") >= 0 || property.propertyPath.EndsWith("parameters"))
                return RigKind.Handheld;

            string parentPath = property.propertyPath;
            int dot = parentPath.LastIndexOf('.');
            string prefix = dot >= 0 ? parentPath.Substring(0, dot + 1) : string.Empty;
            var kindProp = property.serializedObject.FindProperty(prefix + "rigKind");
            if (kindProp == null && prefix.Length > 0)
            {
                // ShotOverride 等嵌套：再往上一层找
                int prev = prefix.TrimEnd('.').LastIndexOf('.');
                string up = prev >= 0 ? parentPath.Substring(0, prev + 1) : string.Empty;
                kindProp = property.serializedObject.FindProperty(up + "rigKind");
            }
            if (kindProp != null && kindProp.propertyType == SerializedPropertyType.Enum)
                return (RigKind)kindProp.enumValueIndex;
            return RigKind.Static;
        }

        internal static string Hint(RigKind kind)
        {
            switch (kind)
            {
                case RigKind.Static: return "锚点相对偏移；P0/P1 为偏航/俯仰。";
                case RigKind.DollyTrack: return "沿 A→B。侧向/高度相对轨道，不是世界 Y。";
                case RigKind.Orbit: return "有跟随绕跟随；无则绕世界点 B。P0 起始角（0=+Z）。";
                case RigKind.PushPull: return "距离推拉；A.x 可为结束 FOV，P3>0.5 联动。";
                case RigKind.Crane: return "A→B + 弧线。有跟随则 A/B 为相对偏移并整体跟随。";
                case RigKind.Shoulder: return "过肩。I0≥0 右肩，<0 左肩。";
                case RigKind.Handheld: return "相对偏移 A 为基座；P0 振幅 P1 频率。抖动后仍注视目标。";
                case RigKind.SplineDolly: return "沿样条走；只瞄注视（无则跟随）。P2 前馈仅目标明显移动时生效。";
                case RigKind.Pan: return "默认机位停、注视点摇。速度曲线控快慢；高级时机位可另走曲线。";
                default: return string.Empty;
            }
        }

        static void LabelsFor(RigKind kind,
            out string a, out string b, out string p0, out string p1, out string p2, out string p3, out string i0, out string i1)
        {
            a = null; b = null;
            p0 = null; p1 = null; p2 = null; p3 = null;
            i0 = null; i1 = null;
            switch (kind)
            {
                case RigKind.Static:
                    a = "相对偏移 A";
                    p0 = "偏航角（度）";
                    p1 = "俯仰角（度）";
                    break;
                case RigKind.DollyTrack:
                    a = "轨道起点 A";
                    b = "轨道终点 B";
                    p0 = "前馈预测时间（秒）";
                    p1 = "相对轨道侧向";
                    p2 = "相对轨道高度";
                    p3 = "Dutch 倾角";
                    break;
                case RigKind.Orbit:
                    a = "目标 FOV（用 X）";
                    b = "无跟随时的圆心";
                    p0 = "起始方位角（度）";
                    p1 = "角速度（度/秒）";
                    p2 = "半径";
                    p3 = "高度";
                    break;
                case RigKind.PushPull:
                    a = "结束 FOV（用 X）";
                    p0 = "起始距离";
                    p1 = "结束距离";
                    p2 = "高度";
                    p3 = "启用 FOV 联动（>0.5）";
                    break;
                case RigKind.Crane:
                    a = "起点（相对跟随）";
                    b = "终点（相对跟随）";
                    p0 = "弧线偏移 X";
                    p1 = "弧线偏移 Y";
                    p2 = "弧线偏移 Z";
                    p3 = null;
                    i0 = null;
                    break;
                case RigKind.Shoulder:
                    p0 = "肩侧偏移";
                    p1 = "肩高";
                    p2 = "注视偏航微调";
                    p3 = "Dutch 倾角";
                    i0 = "左右肩（≥0 右 / <0 左）";
                    break;
                case RigKind.Handheld:
                    a = "相对偏移 A";
                    p0 = "位置振幅";
                    p1 = "噪声频率";
                    p2 = "俯仰/方位抖动";
                    p3 = "滚动抖动";
                    break;
                case RigKind.SplineDolly:
                    a = "备用起点 A";
                    b = "备用终点 B";
                    p0 = null;
                    p1 = null;
                    p2 = "前馈（秒，目标在动才生效）";
                    p3 = "备用高度";
                    i0 = null;
                    break;
                case RigKind.Pan:
                    a = "机位";
                    b = "注视弧起点";
                    p0 = "注视弧线 X";
                    p1 = "注视弧线 Y";
                    p2 = "注视弧线 Z";
                    p3 = null;
                    i0 = "高级（非 0=机位走曲线）";
                    i1 = null;
                    break;
            }
        }
    }

    [CustomPropertyDrawer(typeof(TargetBinding))]
    public sealed class TargetBindingDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return property.isExpanded
                ? EditorGUIUtility.singleLineHeight * 3f + 4f
                : EditorGUIUtility.singleLineHeight;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(line, property.isExpanded,
                new GUIContent(string.IsNullOrEmpty(label.text) ? "目标偏移" : label.text, label.tooltip), true);
            if (!property.isExpanded) return;
            EditorGUI.indentLevel++;
            float y = line.yMax + 2f;
            float h = EditorGUIUtility.singleLineHeight;
            EditorGUI.PropertyField(new Rect(position.x, y, position.width, h), property.FindPropertyRelative("lookAtOffset"), new GUIContent("注视偏移"));
            y += h + 1f;
            EditorGUI.PropertyField(new Rect(position.x, y, position.width, h), property.FindPropertyRelative("followOffset"), new GUIContent("跟随偏移"));
            EditorGUI.indentLevel--;
        }
    }

    [CustomPropertyDrawer(typeof(FocusSettings))]
    public sealed class FocusSettingsDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!property.isExpanded)
                return EditorGUIUtility.singleLineHeight;
            return EditorGUIUtility.singleLineHeight * 9f + 7f;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(line, property.isExpanded,
                new GUIContent(string.IsNullOrEmpty(label.text) ? "焦点配置" : label.text, CineTips.DofMode), true);
            if (!property.isExpanded)
                return;
            EditorGUI.indentLevel++;
            float y = line.yMax + 2f;
            float h = EditorGUIUtility.singleLineHeight;
            float w = position.width;
            float x = position.x;
            var dofMode = property.FindPropertyRelative("dofMode");
            CineHelp.DofPopup(new Rect(x, y, w, h), dofMode);
            y += h + 1f;
            EditorGUI.PropertyField(new Rect(x, y, w, h), property.FindPropertyRelative("damping"), new GUIContent("对焦阻尼", CineTips.DofDamping));
            y += h + 1f;
            EditorGUI.PropertyField(new Rect(x, y, w, h), property.FindPropertyRelative("maxFocusSpeed"), new GUIContent("最大对焦速度", CineTips.DofMaxSpeed));
            y += h + 1f;
            EditorGUI.PropertyField(new Rect(x, y, w, h), property.FindPropertyRelative("pullDuration"), new GUIContent("拉焦时长", CineTips.DofPull));
            y += h + 1f;
            bool bokeh = dofMode != null && dofMode.intValue == (int)DofMode.Bokeh;
            bool dofOn = dofMode != null && dofMode.intValue != (int)DofMode.Off;
            using (new EditorGUI.DisabledScope(!dofOn))
            {
                EditorGUI.PropertyField(new Rect(x, y, w, h), property.FindPropertyRelative("aperture"), new GUIContent("光圈 F", CineTips.DofAperture));
                y += h + 1f;
            }
            using (new EditorGUI.DisabledScope(!bokeh))
            {
                EditorGUI.PropertyField(new Rect(x, y, w, h), property.FindPropertyRelative("focalLength"), new GUIContent("焦距（毫米）", CineTips.DofFocalLength));
                y += h + 1f;
                EditorGUI.PropertyField(new Rect(x, y, w, h), property.FindPropertyRelative("bladeCount"), new GUIContent("光圈叶片", CineTips.DofBlades));
                y += h + 1f;
            }
            EditorGUI.PropertyField(new Rect(x, y, w, h), property.FindPropertyRelative("hyperfocalCircleOfConfusion"), new GUIContent("超焦距弥散圈", CineTips.DofCoc));
            EditorGUI.indentLevel--;
        }
    }

    [CustomPropertyDrawer(typeof(HandheldLayer))]
    public sealed class HandheldLayerDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!property.isExpanded)
                return EditorGUIUtility.singleLineHeight;
            var parms = property.FindPropertyRelative("parameters");
            float ph = parms != null ? EditorGUI.GetPropertyHeight(parms, true) : EditorGUIUtility.singleLineHeight;
            return EditorGUIUtility.singleLineHeight * 2f + ph + 4f;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(line, property.isExpanded, new GUIContent("手持层"), true);
            if (!property.isExpanded) return;
            EditorGUI.indentLevel++;
            float y = line.yMax + 2f;
            EditorGUI.PropertyField(new Rect(position.x, y, position.width, EditorGUIUtility.singleLineHeight),
                property.FindPropertyRelative("enabled"), new GUIContent("启用手持抖动"));
            y += EditorGUIUtility.singleLineHeight + 2f;
            var parms = property.FindPropertyRelative("parameters");
            float ph = EditorGUI.GetPropertyHeight(parms, true);
            EditorGUI.PropertyField(new Rect(position.x, y, position.width, ph), parms, new GUIContent("抖动参数"), true);
            EditorGUI.indentLevel--;
        }
    }

    [CustomPropertyDrawer(typeof(MatchPoint))]
    public sealed class MatchPointDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return property.isExpanded
                ? EditorGUIUtility.singleLineHeight * 5f + 6f
                : EditorGUIUtility.singleLineHeight;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(line, property.isExpanded, new GUIContent("匹配帧锚点"), true);
            if (!property.isExpanded) return;
            EditorGUI.indentLevel++;
            float y = line.yMax + 2f;
            float h = EditorGUIUtility.singleLineHeight;
            EditorGUI.PropertyField(new Rect(position.x, y, position.width, h), property.FindPropertyRelative("valid"), new GUIContent("启用匹配点"));
            y += h + 1f;
            EditorGUI.PropertyField(new Rect(position.x, y, position.width, h), property.FindPropertyRelative("position"), new GUIContent("位置"));
            y += h + 1f;
            EditorGUI.PropertyField(new Rect(position.x, y, position.width, h), property.FindPropertyRelative("rotation"), new GUIContent("旋转"));
            y += h + 1f;
            EditorGUI.PropertyField(new Rect(position.x, y, position.width, h), property.FindPropertyRelative("fov"), new GUIContent("视场角"));
            EditorGUI.indentLevel--;
        }
    }

    [CustomPropertyDrawer(typeof(ShotOverride))]
    public sealed class ShotOverrideDrawer : PropertyDrawer
    {
        const float k_Pad = 6f;
        const float k_InnerPad = 4f;
        const float k_BlockGap = 6f;
        const float k_SectionGap = 4f;
        const float k_Btn = 52f;
        const string k_FoldRig = "CineShot.Override.Fold.Rig";
        const string k_FoldLook = "CineShot.Override.Fold.Look";
        const string k_FoldMisc = "CineShot.Override.Fold.Misc";

        struct BlockItem
        {
            public ShotOverrideMask Flag;
            public string Title;
            public string Tip;
            public SectionBody Body;
            public SectionSeed Seed;
            public BodyHeight Height;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!property.isExpanded)
                return EditorGUIUtility.singleLineHeight;
            float h = EditorGUIUtility.singleLineHeight + 6f;
            h += MeasureBlock(property, k_FoldRig, true, RigItems());
            h += MeasureBlock(property, k_FoldLook, true, LookItems());
            h += MeasureBlock(property, k_FoldMisc, false, MiscItems());
            return h;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            int n = CountBits(property.FindPropertyRelative("mask").intValue);
            var fold = new Rect(line.x, line.y, line.width - 88f, line.height);
            var status = new Rect(line.xMax - 84f, line.y, 84f, line.height);
            property.isExpanded = EditorGUI.Foldout(fold, property.isExpanded, new GUIContent("片段覆写", CineTips.Overrides), true);
            EditorGUI.LabelField(status, n > 0 ? n + " 项已改" : "全用模板", EditorStyles.miniLabel);
            if (!property.isExpanded)
                return;

            float y = line.yMax + 4f;
            float x = position.x;
            float w = position.width;
            var template = FindTemplate(property);

            DrawBlock(ref y, x, w, property, template, k_FoldRig, "运镜", true, RigItems());
            DrawBlock(ref y, x, w, property, template, k_FoldLook, "画面", true, LookItems());
            DrawBlock(ref y, x, w, property, template, k_FoldMisc, "其它", false, MiscItems());
        }

        static BlockItem[] RigItems()
        {
            return new[]
            {
                Item(ShotOverrideMask.RigParams, "机位参数", "主机位=手持时，抖动也在这里。", DrawRigBody, SeedRig, BodyH_Rig),
                Item(ShotOverrideMask.Handheld, "手持层", "叠在其它机位上的额外抖动。", DrawHandheldBody, SeedHandheld, BodyH_Handheld),
                Item(ShotOverrideMask.Composition, "构图", null, DrawCompositionBody, SeedComposition, BodyH_One),
                Item(ShotOverrideMask.TargetOffset, "目标偏移", "可同绑一物，用偏移区分头/脚。", DrawTargetBody, SeedTarget, BodyH_Two)
            };
        }

        static BlockItem[] LookItems()
        {
            return new[]
            {
                Item(ShotOverrideMask.Optics, "FOV / 裁剪", "视场角与近远裁剪面。", DrawOpticsBody, SeedOptics, BodyH_Optics),
                Item(ShotOverrideMask.Focus, "焦点 / 景深", null, DrawFocusBody, SeedFocus, BodyH_Focus)
            };
        }

        static BlockItem[] MiscItems()
        {
            return new[]
            {
                Item(ShotOverrideMask.Easing, "缓动", "片段上的混入/淡出优先；这里改模板缺省。", DrawEasingBody, SeedEasing, BodyH_Two),
                Item(ShotOverrideMask.PriorityTags, "优先级", CineTips.Priority, DrawPriorityBody, SeedPriority, BodyH_One),
                Item(ShotOverrideMask.MatchPoint, "匹配帧锚点", CineTips.MatchPoint, DrawMatchBody, SeedMatch, BodyH_Match)
            };
        }

        static BlockItem Item(ShotOverrideMask flag, string title, string tip, SectionBody body, SectionSeed seed, BodyHeight height)
        {
            return new BlockItem
            {
                Flag = flag,
                Title = title,
                Tip = tip,
                Body = body,
                Seed = seed,
                Height = height
            };
        }

        delegate void SectionBody(ref float y, float x, float w, SerializedProperty root);
        delegate void SectionSeed(SerializedProperty root, ShotAsset template);
        delegate float BodyHeight(SerializedProperty root);

        static float BodyH_Rig(SerializedProperty root)
        {
            return EditorGUI.GetPropertyHeight(root.FindPropertyRelative("rigParams"), true);
        }

        static float BodyH_Handheld(SerializedProperty root)
        {
            return EditorGUI.GetPropertyHeight(root.FindPropertyRelative("handheld"), true);
        }

        static float BodyH_One(SerializedProperty root)
        {
            return EditorGUIUtility.singleLineHeight;
        }

        static float BodyH_Two(SerializedProperty root)
        {
            return (EditorGUIUtility.singleLineHeight + 1f) * 2f - 1f;
        }

        static float BodyH_Optics(SerializedProperty root)
        {
            return (EditorGUIUtility.singleLineHeight + 1f) * 3f - 1f;
        }

        static float BodyH_Focus(SerializedProperty root)
        {
            return (EditorGUIUtility.singleLineHeight + 1f) * 2f
                   + EditorGUI.GetPropertyHeight(root.FindPropertyRelative("focusSettings"), true);
        }

        static float BodyH_Match(SerializedProperty root)
        {
            // 预览/修改 + valid/位置/旋转/视场角（不套嵌套折叠）
            return (EditorGUIUtility.singleLineHeight + 1f) * 5f + 3f;
        }

        static int CountActive(SerializedProperty property, BlockItem[] items)
        {
            int mask = property.FindPropertyRelative("mask").intValue;
            int n = 0;
            for (int i = 0; i < items.Length; i++)
            {
                if ((mask & (int)items[i].Flag) != 0)
                    n++;
            }
            return n;
        }

        static float MeasureBlock(SerializedProperty property, string foldKey, bool defaultOpen, BlockItem[] items)
        {
            float h = k_Pad * 2f + EditorGUIUtility.singleLineHeight + k_BlockGap;
            if (!SessionState.GetBool(foldKey, defaultOpen))
                return h;
            for (int i = 0; i < items.Length; i++)
                h += SectionHeight(property, items[i]) + k_SectionGap;
            return h;
        }

        static float SectionHeight(SerializedProperty property, BlockItem item)
        {
            float h = k_InnerPad * 2f + EditorGUIUtility.singleLineHeight;
            if ((property.FindPropertyRelative("mask").intValue & (int)item.Flag) != 0)
                h += 2f + item.Height(property);
            return h;
        }

        static void DrawBlock(ref float y, float x, float w, SerializedProperty root, ShotAsset template,
            string foldKey, string title, bool defaultOpen, BlockItem[] items)
        {
            int active = CountActive(root, items);
            bool open = SessionState.GetBool(foldKey, defaultOpen);

            float contentH = k_Pad * 2f + EditorGUIUtility.singleLineHeight;
            if (open)
            {
                for (int i = 0; i < items.Length; i++)
                    contentH += SectionHeight(root, items[i]) + k_SectionGap;
            }

            var box = new Rect(x, y, w, contentH);
            GUI.Box(box, GUIContent.none, EditorStyles.helpBox);

            float ix = box.x + k_Pad;
            float iw = box.width - k_Pad * 2f;
            float iy = box.y + k_Pad;

            var foldRect = new Rect(ix, iy, iw - 72f, EditorGUIUtility.singleLineHeight);
            var metaRect = new Rect(ix + iw - 70f, iy, 70f, EditorGUIUtility.singleLineHeight);
            open = EditorGUI.Foldout(foldRect, open, title, true);
            EditorGUI.LabelField(metaRect, active > 0 ? active + " 项覆写" : "未覆写", EditorStyles.miniLabel);
            SessionState.SetBool(foldKey, open);
            iy += EditorGUIUtility.singleLineHeight + 2f;

            if (open)
            {
                for (int i = 0; i < items.Length; i++)
                    DrawSection(ref iy, ix, iw, root, template, items[i]);
            }

            y = box.yMax + k_BlockGap;
        }

        static void DrawSection(ref float y, float x, float w, SerializedProperty root, ShotAsset template, BlockItem item)
        {
            var mask = root.FindPropertyRelative("mask");
            bool on = (mask.intValue & (int)item.Flag) != 0;

            float innerH = k_InnerPad * 2f + EditorGUIUtility.singleLineHeight;
            if (on)
                innerH += 2f + item.Height(root);

            var box = new Rect(x, y, w, innerH);
            GUI.Box(box, GUIContent.none, EditorStyles.helpBox);

            float ix = box.x + k_InnerPad;
            float iw = box.width - k_InnerPad * 2f;
            float iy = box.y + k_InnerPad;

            var head = new Rect(ix, iy, iw, EditorGUIUtility.singleLineHeight);
            var btn = new Rect(head.xMax - k_Btn, head.y, k_Btn, head.height);
            head.width -= k_Btn + 6f;
            if (!string.IsNullOrEmpty(item.Tip))
                head = CineHelp.Trim(head, item.Tip);

            EditorGUI.LabelField(head, item.Title);
            bool next = GUI.Toggle(btn, on, on ? "覆写" : "模板", EditorStyles.miniButton);
            iy += EditorGUIUtility.singleLineHeight + 2f;

            if (next != on)
            {
                if (next)
                {
                    mask.intValue |= (int)item.Flag;
                    if (item.Seed != null)
                        item.Seed(root, template);
                }
                else
                    mask.intValue &= ~(int)item.Flag;
            }
            if (next)
                item.Body(ref iy, ix, iw, root);

            y = box.yMax + k_SectionGap;
        }

        static void DrawRigBody(ref float y, float x, float w, SerializedProperty root)
        {
            var rig = root.FindPropertyRelative("rigParams");
            float rh = EditorGUI.GetPropertyHeight(rig, true);
            EditorGUI.PropertyField(new Rect(x, y, w, rh), rig, GUIContent.none, true);
            y += rh + 1f;
        }

        static void DrawHandheldBody(ref float y, float x, float w, SerializedProperty root)
        {
            var hand = root.FindPropertyRelative("handheld");
            float hh = EditorGUI.GetPropertyHeight(hand, true);
            EditorGUI.PropertyField(new Rect(x, y, w, hh), hand, GUIContent.none, true);
            y += hh + 1f;
        }

        static void DrawCompositionBody(ref float y, float x, float w, SerializedProperty root)
        {
            Field(ref y, x, w, root, "compositionOffset", "偏移", null);
        }

        static void DrawOpticsBody(ref float y, float x, float w, SerializedProperty root)
        {
            Field(ref y, x, w, root, "fov", "视场角", null);
            Field(ref y, x, w, root, "nearClip", "近裁剪", null);
            Field(ref y, x, w, root, "farClip", "远裁剪", null);
        }

        static void DrawFocusBody(ref float y, float x, float w, SerializedProperty root)
        {
            Field(ref y, x, w, root, "focusMode", "焦点模式", CineTips.FocusMode);
            Field(ref y, x, w, root, "focusDistance", "焦点距离", CineTips.FocusDistance);
            var fs = root.FindPropertyRelative("focusSettings");
            float fsh = EditorGUI.GetPropertyHeight(fs, true);
            EditorGUI.PropertyField(new Rect(x, y, w, fsh), fs, new GUIContent("景深外观"), true);
            y += fsh + 1f;
        }

        static void DrawEasingBody(ref float y, float x, float w, SerializedProperty root)
        {
            Field(ref y, x, w, root, "easeIn", "混入", CineTips.EaseIn);
            Field(ref y, x, w, root, "easeOut", "淡出", CineTips.EaseOut);
        }

        static void DrawTargetBody(ref float y, float x, float w, SerializedProperty root)
        {
            Field(ref y, x, w, root, "lookAtOffset", "注视", null);
            Field(ref y, x, w, root, "followOffset", "跟随", null);
        }

        static void DrawPriorityBody(ref float y, float x, float w, SerializedProperty root)
        {
            Field(ref y, x, w, root, "priority", "优先级", null);
        }

        static void DrawMatchBody(ref float y, float x, float w, SerializedProperty root)
        {
            var mp = root.FindPropertyRelative("matchPoint");
            if (mp == null)
                return;

            var clip = root.serializedObject != null
                ? root.serializedObject.targetObject as CineShotClip
                : null;
            float h = EditorGUIUtility.singleLineHeight;
            if (clip != null)
            {
                bool valid = mp.FindPropertyRelative("valid").boolValue;
                using (new EditorGUI.DisabledScope(!valid))
                {
                    MatchPointEditState.EnsureFor(clip);
                    float half = (w - 4f) * 0.5f;
                    bool preview = GUI.Toggle(new Rect(x, y, half, h), MatchPointEditState.Preview, "预览匹配点", EditorStyles.miniButton);
                    bool edit = GUI.Toggle(new Rect(x + half + 4f, y, half, h), MatchPointEditState.Edit, "修改匹配点", EditorStyles.miniButton);
                    if (preview != MatchPointEditState.Preview)
                        MatchPointEditState.SetPreview(preview);
                    if (edit != MatchPointEditState.Edit)
                        MatchPointEditState.SetEdit(edit);
                }
                y += h + 4f;
            }

            EditorGUI.PropertyField(new Rect(x, y, w, h), mp.FindPropertyRelative("valid"), new GUIContent("启用匹配点"));
            y += h + 1f;
            EditorGUI.PropertyField(new Rect(x, y, w, h), mp.FindPropertyRelative("position"), new GUIContent("位置"));
            y += h + 1f;
            EditorGUI.PropertyField(new Rect(x, y, w, h), mp.FindPropertyRelative("rotation"), new GUIContent("旋转"));
            y += h + 1f;
            EditorGUI.PropertyField(new Rect(x, y, w, h), mp.FindPropertyRelative("fov"), new GUIContent("视场角"));
            y += h + 1f;
        }

        static void Field(ref float y, float x, float w, SerializedProperty root, string name, string label, string tip)
        {
            var p = root.FindPropertyRelative(name);
            if (p == null)
                return;
            EditorGUI.PropertyField(new Rect(x, y, w, EditorGUIUtility.singleLineHeight), p, new GUIContent(label, tip));
            y += EditorGUIUtility.singleLineHeight + 1f;
        }

        static int CountBits(int value)
        {
            int n = 0;
            while (value != 0)
            {
                n += value & 1;
                value >>= 1;
            }
            return n;
        }

        static ShotAsset FindTemplate(SerializedProperty property)
        {
            var so = property.serializedObject;
            if (so == null)
                return null;
            var clip = so.targetObject as CineShotClip;
            return clip != null ? clip.shot : null;
        }

        static void SeedRig(SerializedProperty property, ShotAsset template)
        {
            if (template == null)
                return;
            var p = template.rigParams;
            var rig = property.FindPropertyRelative("rigParams");
            WriteRigParams(rig, in p);
        }

        static void SeedHandheld(SerializedProperty property, ShotAsset template)
        {
            if (template == null)
                return;
            var src = template.handheld;
            var hand = property.FindPropertyRelative("handheld");
            hand.FindPropertyRelative("enabled").boolValue = src.enabled;
            WriteRigParams(hand.FindPropertyRelative("parameters"), in src.parameters);
        }

        static void SeedComposition(SerializedProperty property, ShotAsset template)
        {
            property.FindPropertyRelative("compositionOffset").vector3Value =
                template != null ? template.compositionOffset : Vector3.zero;
        }

        static void SeedOptics(SerializedProperty property, ShotAsset template)
        {
            if (template == null)
                return;
            property.FindPropertyRelative("fov").floatValue = template.fov;
            property.FindPropertyRelative("nearClip").floatValue = template.nearClip;
            property.FindPropertyRelative("farClip").floatValue = template.farClip;
        }

        static void SeedFocus(SerializedProperty property, ShotAsset template)
        {
            if (template == null)
                return;
            property.FindPropertyRelative("focusMode").enumValueIndex = (int)template.focusMode;
            property.FindPropertyRelative("focusDistance").floatValue = template.focusDistance;
            var fs = property.FindPropertyRelative("focusSettings");
            var s = template.focusSettings;
            fs.FindPropertyRelative("dofMode").enumValueIndex = (int)s.dofMode;
            fs.FindPropertyRelative("damping").floatValue = s.damping;
            fs.FindPropertyRelative("maxFocusSpeed").floatValue = s.maxFocusSpeed;
            fs.FindPropertyRelative("pullDuration").floatValue = s.pullDuration;
            fs.FindPropertyRelative("aperture").floatValue = s.aperture;
            fs.FindPropertyRelative("focalLength").floatValue = s.focalLength;
            fs.FindPropertyRelative("bladeCount").intValue = s.bladeCount;
            fs.FindPropertyRelative("hyperfocalCircleOfConfusion").floatValue = s.hyperfocalCircleOfConfusion;
        }

        static void SeedEasing(SerializedProperty property, ShotAsset template)
        {
            if (template == null)
                return;
            property.FindPropertyRelative("easeIn").objectReferenceValue = template.easeIn;
            property.FindPropertyRelative("easeOut").objectReferenceValue = template.easeOut;
        }

        static void SeedTarget(SerializedProperty property, ShotAsset template)
        {
            if (template == null)
                return;
            property.FindPropertyRelative("lookAtOffset").vector3Value = template.targetBinding.lookAtOffset;
            property.FindPropertyRelative("followOffset").vector3Value = template.targetBinding.followOffset;
        }

        static void SeedPriority(SerializedProperty property, ShotAsset template)
        {
            if (template == null)
                return;
            property.FindPropertyRelative("priority").intValue = template.priority;
        }

        static void SeedMatch(SerializedProperty property, ShotAsset template)
        {
            if (template == null)
                return;
            var src = template.matchPoint;
            var mp = property.FindPropertyRelative("matchPoint");
            mp.FindPropertyRelative("valid").boolValue = src.valid;
            mp.FindPropertyRelative("position").vector3Value = src.position;
            mp.FindPropertyRelative("rotation").quaternionValue = src.rotation;
            mp.FindPropertyRelative("fov").floatValue = src.fov;
        }

        static void WriteRigParams(SerializedProperty rig, in RigParams p)
        {
            if (rig == null)
                return;
            rig.FindPropertyRelative("A").vector3Value = p.A;
            rig.FindPropertyRelative("B").vector3Value = p.B;
            rig.FindPropertyRelative("C").vector3Value = p.C;
            rig.FindPropertyRelative("P0").floatValue = p.P0;
            rig.FindPropertyRelative("P1").floatValue = p.P1;
            rig.FindPropertyRelative("P2").floatValue = p.P2;
            rig.FindPropertyRelative("P3").floatValue = p.P3;
            rig.FindPropertyRelative("I0").intValue = p.I0;
            rig.FindPropertyRelative("I1").intValue = p.I1;
        }
    }

    [CustomPropertyDrawer(typeof(StoryboardRow))]
    public sealed class StoryboardRowDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            int mode = property.FindPropertyRelative("blendMode").enumValueIndex;
            bool showBlend = mode == (int)ShotBlendMode.Blend || mode == (int)ShotBlendMode.MatchFrame;
            return property.isExpanded
                ? EditorGUIUtility.singleLineHeight * (showBlend ? 7f : 6f) + 9f
                : EditorGUIUtility.singleLineHeight;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            string title = label.text;
            if (string.IsNullOrEmpty(title) || title.StartsWith("Element"))
                title = "分镜行";
            property.isExpanded = EditorGUI.Foldout(line, property.isExpanded, new GUIContent(title), true);
            if (!property.isExpanded) return;
            EditorGUI.indentLevel++;
            float y = line.yMax + 2f;
            float h = EditorGUIUtility.singleLineHeight;
            float w = position.width;
            float x = position.x;
            ShotLibrary.ShotField(new Rect(x, y, w, h), new GUIContent("镜头"), property.FindPropertyRelative("shotGuid"));
            y += h + 1f;
            Draw(ref y, x, w, h, property, "duration", "时长（秒）");
            Draw(ref y, x, w, h, property, "gap", "间隔（秒）");
            Draw(ref y, x, w, h, property, "blendMode", "转场模式", CineTips.Blend);
            int mode = property.FindPropertyRelative("blendMode").enumValueIndex;
            if (mode == (int)ShotBlendMode.Blend || mode == (int)ShotBlendMode.MatchFrame)
                Draw(ref y, x, w, h, property, "blendDuration", "混合时间（秒）", CineTips.BlendDuration);
            Draw(ref y, x, w, h, property, "note", "备注");
            EditorGUI.indentLevel--;
        }

        static void Draw(ref float y, float x, float w, float h, SerializedProperty root, string name, string label)
        {
            Draw(ref y, x, w, h, root, name, label, null);
        }

        static void Draw(ref float y, float x, float w, float h, SerializedProperty root, string name, string label, string tip)
        {
            var p = root.FindPropertyRelative(name);
            if (p == null) return;
            var rect = new Rect(x, y, w, h);
            if (!string.IsNullOrEmpty(tip))
                rect = CineHelp.Trim(rect, tip);
            EditorGUI.PropertyField(rect, p, new GUIContent(label));
            y += h + 1f;
        }
    }
}
