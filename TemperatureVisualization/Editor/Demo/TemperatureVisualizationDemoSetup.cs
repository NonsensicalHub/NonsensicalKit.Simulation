using NaughtyAttributes;
using UnityEngine;
using UnityEngine.InputSystem.UI;
using System.Collections.Generic;
using TMPro;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
namespace TemperatureVisualization.Demo
{
    /// <summary>
    /// Demo 场景搭建工具（仅编辑器按钮触发，不在运行时自动生成）。
    /// 在 Inspector 中按需生成核心组件、UI，并完成绑定；材质等参数请生成后手动配置。
    /// </summary>
    public class TemperatureVisualizationDemoSetup : MonoBehaviour
    {
        private const string DefaultRootName = "TemperatureVisualization";
        private const string DefaultCanvasName = "TemperatureDemoUI";
        private const float PanelWidth = 360f;
        private const float SideMargin = 20f;

        [Header("生成配置")]
        [Label("根物体名称")]
        [SerializeField] private string m_RootObjectName = DefaultRootName;

        [Label("Canvas 物体名称")]
        [SerializeField] private string m_CanvasObjectName = DefaultCanvasName;

        [Label("体积尺寸")]
        [SerializeField] private Vector3 m_VolumeSize = new Vector3(10f, 6f, 8f);

        [Label("默认可视化模式")]
        [SerializeField] private VisualizationMode m_DefaultMode = VisualizationMode.Volume;

        [Label("温度下限 (℃)")]
        [SerializeField] private float m_TempMin = 10f;

        [Label("温度上限 (℃)")]
        [SerializeField] private float m_TempMax = 40f;

        [Header("引用（生成后可手动调整）")]
        [SerializeField] private TemperatureVisualizationController m_Controller;
        [SerializeField] private TemperatureDemoControlPanel m_ControlPanel;

#if UNITY_EDITOR
        private struct SliderRow
        {
            public Slider Slider;
            public TMP_Text ValueText;
        }

        [Button(" 生成 Demo UI")]
        private void EditorBuildAll()
        {
            EditorBuildCore();
            EditorBuildUi();
            EditorBindUi();
            EditorEnsureEventSystem();
            MarkDirtyAndLog("Demo 搭建完成。请在 Inspector 中配置材质等参数后保存场景。");
        }

        private void EditorBuildCore()
        {
            GameObject root = FindOrCreateRoot();
            Undo.RegisterFullObjectHierarchyUndo(root, "Build Temperature Visualization Core");

            var bounds = GetOrAddComponent<TemperatureVolumeBounds>(root);
            bounds.Size = m_VolumeSize;

            var sensorManager = GetOrAddComponent<TemperatureSensorManager>(root);
            sensorManager.Initialize(bounds);

            var interpolator = GetOrAddComponent<TemperatureFieldInterpolator>(root);
            interpolator.Configure(sensorManager, bounds);

            var colorRamp = GetOrAddComponent<TemperatureColorRamp>(root);

            var sliceRenderer = GetOrCreateChildComponent<TemperatureSliceRenderer>(root.transform, "SliceRenderer");
            var volumeRenderer = GetOrCreateChildComponent<TemperatureVolumeRenderer>(root.transform, "VolumeRenderer");
            var isoRenderer = GetOrCreateChildComponent<TemperatureIsosurfaceRenderer>(root.transform, "IsosurfaceRenderer");

            var controller = GetOrAddComponent<TemperatureVisualizationController>(root);
            controller.Initialize(bounds, sensorManager, interpolator, colorRamp, sliceRenderer, volumeRenderer, isoRenderer);
            controller.Mode = m_DefaultMode;
            ApplyTemperatureRange(controller);

            m_Controller = controller;
        }

        private void EditorBuildUi()
        {
            RemoveExistingCanvas();

            GetNormalizedTemperatureRange(out float rangeMin, out float rangeMax);
            float midTemp = (rangeMin + rangeMax) * 0.5f;
            float isoDefault = Mathf.Lerp(rangeMin, rangeMax, 0.6f);

            var canvasGo = CreateCanvasRoot();
            Undo.RegisterCreatedObjectUndo(canvasGo, "Create Temperature Demo UI");

            var panel = canvasGo.AddComponent<TemperatureDemoControlPanel>();
            var leftPanel = CreateSidePanel(canvasGo.transform, anchorLeft: true, "LeftPanel");
            var rightPanel = CreateSidePanel(canvasGo.transform, anchorLeft: false, "RightPanel");

            CreateSectionLabel(leftPanel, "可视化");
            var modeDropdown = CreateDropdownRow(leftPanel, "可视化模式", new[] { "切片 — 正交剖面", "体积 — 立方体云图", "等温面 — 温度等值面", "组合 — 体积+切片叠加" }, 1);
            var tempMin = CreateSliderRow(leftPanel, "温度下限", rangeMin, rangeMax, rangeMin);
            var tempMax = CreateSliderRow(leftPanel, "温度上限", rangeMin, rangeMax, rangeMax);
            var opacity = CreateSliderRow(leftPanel, "透明度", 0f, 1f, 0.75f);
            var colorPreset = CreateDropdownRow(leftPanel, "色标预设", new[] { "默认色标", "热力色标", "灰度色标" }, 0);

            CreateSectionLabel(leftPanel, "切片");
            var showSliceXY = CreateToggleRow(leftPanel, "显示 XY 切片", false);
            var showSliceXZ = CreateToggleRow(leftPanel, "显示 XZ 切片", false);
            var showSliceYZ = CreateToggleRow(leftPanel, "显示 YZ 切片", false);
            var sliceXY = CreateSliderRow(leftPanel, "XY 切片位置", 0f, 1f, 0.5f);
            var sliceXZ = CreateSliderRow(leftPanel, "XZ 切片位置", 0f, 1f, 0.5f);
            var sliceYZ = CreateSliderRow(leftPanel, "YZ 切片位置", 0f, 1f, 0.5f);

            CreateSectionLabel(rightPanel, "传感器");
            var simulation = CreateToggleRow(rightPanel, "启用温度模拟", false);
            var sensorDropdown = CreateDropdownRow(rightPanel, "选择传感器", new[] { "传感器 1" }, 0);
            var sensorTemp = CreateSliderRow(rightPanel, "传感器温度", rangeMin, rangeMax, midTemp);
            var isoTemp = CreateSliderRow(rightPanel, "等温面温度", rangeMin, rangeMax, isoDefault);
            var sensorListText = CreateTextBlock(rightPanel, "传感器列表", 180f);
            var statusText = CreateTextBlock(rightPanel, "状态", 48f);

            WirePanel(panel,
                modeDropdown,
                tempMin, tempMax, opacity,
                showSliceXY, showSliceXZ, showSliceYZ,
                sliceXY, sliceXZ, sliceYZ,
                simulation, sensorDropdown, sensorTemp, isoTemp,
                colorPreset, sensorListText, statusText);

            m_ControlPanel = panel;
        }

        private void EditorBindUi()
        {
            if (m_Controller == null)
                m_Controller = FindObjectOfType<TemperatureVisualizationController>();
            if (m_ControlPanel == null)
                m_ControlPanel = FindObjectOfType<TemperatureDemoControlPanel>();

            if (m_Controller == null || m_ControlPanel == null)
            {
                Debug.LogWarning("[TemperatureVisualizationDemoSetup] 请先「生成核心组件」和「生成 Demo UI」，或手动指定引用。");
                return;
            }

            ApplyTemperatureRange(m_Controller);
            m_ControlPanel.BindController(m_Controller);
            EditorUtility.SetDirty(m_ControlPanel);
        }

        private void ApplyTemperatureRange(TemperatureVisualizationController controller)
        {
            if (controller == null) return;

            GetNormalizedTemperatureRange(out float min, out float max);
            controller.TempMin = min;
            controller.TempMax = max;
            EditorUtility.SetDirty(controller);
        }

        private void GetNormalizedTemperatureRange(out float min, out float max)
        {
            min = m_TempMin;
            max = m_TempMax;
            if (max < min)
            {
                (min, max) = (max, min);
            }
        }

        private void EditorEnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null) return;

            var eventSystemGo = new GameObject("EventSystem");
            Undo.RegisterCreatedObjectUndo(eventSystemGo, "Create EventSystem");
            eventSystemGo.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
            eventSystemGo.AddComponent<InputSystemUIInputModule>();
#else
            eventSystemGo.AddComponent<StandaloneInputModule>();
#endif
        }
        
        private GameObject FindOrCreateRoot()
        {
            var existing = GameObject.Find(m_RootObjectName);
            if (existing != null) return existing;

            var root = new GameObject(m_RootObjectName);
            Undo.RegisterCreatedObjectUndo(root, "Create Temperature Visualization Root");
            return root;
        }

        private static T GetOrAddComponent<T>(GameObject go) where T : Component
        {
            var component = go.GetComponent<T>();
            return component != null ? component : Undo.AddComponent<T>(go);
        }

        private static T GetOrCreateChildComponent<T>(Transform parent, string childName) where T : Component
        {
            var child = parent.Find(childName);
            if (child == null)
            {
                var childGo = new GameObject(childName);
                Undo.RegisterCreatedObjectUndo(childGo, "Create " + childName);
                childGo.transform.SetParent(parent, false);
                child = childGo.transform;
            }

            var component = child.GetComponent<T>();
            return component != null ? component : Undo.AddComponent<T>(child.gameObject);
        }

        private void RemoveExistingCanvas()
        {
            var existing = GameObject.Find(m_CanvasObjectName);
            if (existing != null)
                Undo.DestroyObjectImmediate(existing);
        }

        private GameObject CreateCanvasRoot()
        {
            var canvasGo = new GameObject(m_CanvasObjectName, typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.618f;

            canvasGo.AddComponent<GraphicRaycaster>();
            return canvasGo;
        }

        private static RectTransform CreateSidePanel(Transform parent, bool anchorLeft, string name)
        {
            var panelGo = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            panelGo.transform.SetParent(parent, false);

            var rect = panelGo.GetComponent<RectTransform>();
            if (anchorLeft)
            {
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(SideMargin, -SideMargin);
            }
            else
            {
                rect.anchorMin = new Vector2(1f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(1f, 1f);
                rect.anchoredPosition = new Vector2(-SideMargin, -SideMargin);
            }

            rect.sizeDelta = new Vector2(PanelWidth, 0f);
            panelGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            var layout = panelGo.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.spacing = 8f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            var fitter = panelGo.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            return rect;
        }

        private static void CreateSectionLabel(RectTransform parent, string text)
        {
            var go = TMP_DefaultControls.CreateText(GetTmpResources());
            go.name = text + " Section";
            go.transform.SetParent(parent, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 16f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = new Color(0.7f, 0.85f, 1f);
            ApplyFont(tmp);
            var layout = go.AddComponent<LayoutElement>();
            layout.minHeight = 24f;
            layout.preferredHeight = 24f;
            layout.flexibleHeight = 0f;
        }

        private static TMP_FontAsset LoadDefaultFont()
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                "Assets/TextMesh Pro/Resources/Fonts & Materials/阿里妈妈数黑体 SDF.asset");
            if (font == null)
            {
                font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                    "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            }

            return font;
        }

        private static TMP_DefaultControls.Resources GetTmpResources()
        {
            return new TMP_DefaultControls.Resources
            {
                standard = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"),
                background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
                inputField = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd"),
                knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"),
                checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd"),
                dropdown = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd"),
                mask = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UIMask.psd")
            };
        }

        private static DefaultControls.Resources GetUiResources()
        {
            return new DefaultControls.Resources
            {
                standard = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"),
                background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
                knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"),
                checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd")
            };
        }

        private static void ApplyFont(TMP_Text text)
        {
            var font = LoadDefaultFont();
            if (font != null && text != null)
                text.font = font;
        }

        private static TMP_Dropdown CreateDropdownRow(RectTransform parent, string label, string[] options, int defaultIndex)
        {
            CreateRowLabel(parent, label);
            var go = TMP_DefaultControls.CreateDropdown(GetTmpResources());
            go.name = label + " Dropdown";
            go.transform.SetParent(parent, false);
            var dropdown = go.GetComponent<TMP_Dropdown>();
            dropdown.ClearOptions();
            dropdown.AddOptions(new List<string>(options));
            dropdown.value = defaultIndex;
            ApplyFont(dropdown.captionText);
            ApplyFont(dropdown.itemText);
            var layout = go.AddComponent<LayoutElement>();
            layout.minHeight = 36f;
            layout.preferredHeight = 36f;
            return dropdown;
        }

        private static SliderRow CreateSliderRow(RectTransform parent, string label, float min, float max, float value)
        {
            var header = new GameObject(label + " Header", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            header.transform.SetParent(parent, false);
            var headerLayout = header.GetComponent<HorizontalLayoutGroup>();
            headerLayout.spacing = 8f;
            headerLayout.childControlWidth = true;
            headerLayout.childControlHeight = true;
            headerLayout.childForceExpandWidth = true;
            headerLayout.childForceExpandHeight = false;
            var headerElement = header.AddComponent<LayoutElement>();
            headerElement.minHeight = 20f;
            headerElement.preferredHeight = 20f;

            var labelGo = TMP_DefaultControls.CreateText(GetTmpResources());
            labelGo.name = label + " Label";
            labelGo.transform.SetParent(header.transform, false);
            var labelTmp = labelGo.GetComponent<TextMeshProUGUI>();
            labelTmp.text = label;
            labelTmp.fontSize = 14f;
            labelTmp.color = new Color(0.85f, 0.9f, 1f);
            ApplyFont(labelTmp);
            var labelLayout = labelGo.AddComponent<LayoutElement>();
            labelLayout.flexibleWidth = 1f;

            var valueGo = TMP_DefaultControls.CreateText(GetTmpResources());
            valueGo.name = label + " Value";
            valueGo.transform.SetParent(header.transform, false);
            var valueTmp = valueGo.GetComponent<TextMeshProUGUI>();
            valueTmp.text = value.ToString("F1");
            valueTmp.fontSize = 14f;
            valueTmp.alignment = TextAlignmentOptions.Right;
            valueTmp.color = Color.white;
            ApplyFont(valueTmp);
            var valueLayout = valueGo.AddComponent<LayoutElement>();
            valueLayout.minWidth = 72f;
            valueLayout.preferredWidth = 72f;
            valueLayout.flexibleWidth = 0f;

            var sliderGo = DefaultControls.CreateSlider(GetUiResources());
            sliderGo.name = label + " Slider";
            sliderGo.transform.SetParent(parent, false);
            var slider = sliderGo.GetComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;
            var sliderLayout = sliderGo.AddComponent<LayoutElement>();
            sliderLayout.minHeight = 24f;
            sliderLayout.preferredHeight = 24f;

            return new SliderRow { Slider = slider, ValueText = valueTmp };
        }

        private static void CreateRowLabel(RectTransform parent, string text)
        {
            var go = TMP_DefaultControls.CreateText(GetTmpResources());
            go.name = text + " Label";
            go.transform.SetParent(parent, false);
            var label = go.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = 14f;
            label.color = new Color(0.85f, 0.9f, 1f);
            ApplyFont(label);
            var layout = go.AddComponent<LayoutElement>();
            layout.minHeight = 20f;
            layout.preferredHeight = 20f;
        }

        private static Toggle CreateToggleRow(RectTransform parent, string label, bool value)
        {
            var go = DefaultControls.CreateToggle(GetUiResources());
            go.name = label + " Toggle";
            go.transform.SetParent(parent, false);
            var toggle = go.GetComponent<Toggle>();
            toggle.isOn = value;
            var labelTmp = go.GetComponentInChildren<Text>();
            if (labelTmp != null)
            {
                labelTmp.text = label;
                labelTmp.fontSize = 14;
            }

            var layout = go.AddComponent<LayoutElement>();
            layout.minHeight = 28f;
            layout.preferredHeight = 28f;
            return toggle;
        }

        private static TextMeshProUGUI CreateTextBlock(RectTransform parent, string label, float height)
        {
            CreateRowLabel(parent, label);
            var go = TMP_DefaultControls.CreateText(GetTmpResources());
            go.name = label + " Text";
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.fontSize = 15f;
            text.color = Color.white;
            text.text = label;
            ApplyFont(text);
            var layout = go.AddComponent<LayoutElement>();
            layout.minHeight = height;
            layout.preferredHeight = height;
            return text;
        }

        private static void WirePanel(
            TemperatureDemoControlPanel panel,
            TMP_Dropdown modeDropdown,
            SliderRow tempMin,
            SliderRow tempMax,
            SliderRow opacity,
            Toggle showSliceXY,
            Toggle showSliceXZ,
            Toggle showSliceYZ,
            SliderRow sliceXY,
            SliderRow sliceXZ,
            SliderRow sliceYZ,
            Toggle simulation,
            TMP_Dropdown sensorDropdown,
            SliderRow sensorTemp,
            SliderRow isoTemp,
            TMP_Dropdown colorPreset,
            TextMeshProUGUI sensorListText,
            TextMeshProUGUI statusText)
        {
            var so = new SerializedObject(panel);
            so.FindProperty("m_ModeDropdown").objectReferenceValue = modeDropdown;
            so.FindProperty("m_TempMinSlider").objectReferenceValue = tempMin.Slider;
            so.FindProperty("m_TempMaxSlider").objectReferenceValue = tempMax.Slider;
            so.FindProperty("m_OpacitySlider").objectReferenceValue = opacity.Slider;
            so.FindProperty("m_TempMinValueText").objectReferenceValue = tempMin.ValueText;
            so.FindProperty("m_TempMaxValueText").objectReferenceValue = tempMax.ValueText;
            so.FindProperty("m_OpacityValueText").objectReferenceValue = opacity.ValueText;
            so.FindProperty("m_ShowSliceXYToggle").objectReferenceValue = showSliceXY;
            so.FindProperty("m_ShowSliceXZToggle").objectReferenceValue = showSliceXZ;
            so.FindProperty("m_ShowSliceYZToggle").objectReferenceValue = showSliceYZ;
            so.FindProperty("m_SliceXYSlider").objectReferenceValue = sliceXY.Slider;
            so.FindProperty("m_SliceXZSlider").objectReferenceValue = sliceXZ.Slider;
            so.FindProperty("m_SliceYZSlider").objectReferenceValue = sliceYZ.Slider;
            so.FindProperty("m_SliceXYValueText").objectReferenceValue = sliceXY.ValueText;
            so.FindProperty("m_SliceXZValueText").objectReferenceValue = sliceXZ.ValueText;
            so.FindProperty("m_SliceYZValueText").objectReferenceValue = sliceYZ.ValueText;
            so.FindProperty("m_SimulationToggle").objectReferenceValue = simulation;
            so.FindProperty("m_SensorDropdown").objectReferenceValue = sensorDropdown;
            so.FindProperty("m_SensorTemperatureSlider").objectReferenceValue = sensorTemp.Slider;
            so.FindProperty("m_IsoTemperatureSlider").objectReferenceValue = isoTemp.Slider;
            so.FindProperty("m_SensorTemperatureValueText").objectReferenceValue = sensorTemp.ValueText;
            so.FindProperty("m_IsoTemperatureValueText").objectReferenceValue = isoTemp.ValueText;
            so.FindProperty("m_ColorPresetDropdown").objectReferenceValue = colorPreset;
            so.FindProperty("m_SensorListText").objectReferenceValue = sensorListText;
            so.FindProperty("m_StatusText").objectReferenceValue = statusText;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private void MarkDirtyAndLog(string message)
        {
            EditorUtility.SetDirty(this);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log($"[TemperatureVisualizationDemoSetup] {message}");
        }
#endif
    }
}
