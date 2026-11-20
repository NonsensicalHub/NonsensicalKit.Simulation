using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace VectorVisualizer
{
    [Serializable]
    public class VectorVisualizerSettings
    {
        private const string ATTRIBUTE_SCRIPT_DEFINE = "VECTOR_VISUALIZER_WITH_ATTRIBUTE";

        private float _focusDistance;
        private float _focusEulerAngleX;
        private float _focusEulerAngleY;
        private float _focusEulerAngleZ;
        private bool _onlyTargetWithAttribute;
        private bool _autoFocusOnSelection;
        private bool _autoFocusOnAdd;
        private bool _handleZTest;
        private float _windowX;
        private float _windowY;

        public float FocusDistance => _focusDistance;
        public Vector3 FocusEulerAngles => new Vector3(_focusEulerAngleX, _focusEulerAngleY, _focusEulerAngleZ);
        public bool OnlyTargetWithAttribute => _onlyTargetWithAttribute;
        public bool AutoFocusOnSelection => _autoFocusOnSelection;
        public bool AutoFocusOnAdd => _autoFocusOnAdd;

        public bool HandleZTest => _handleZTest;
        public float WindowX => _windowX;
        public float WindowY => _windowY;

        // Load settings from EditorPrefs
        public void LoadSettingsOnEditorPrefs()
        {
            _focusDistance = EditorPrefs.GetFloat("VectorVisualizerFocusDistance", 50);
            _onlyTargetWithAttribute = EditorPrefs.GetBool("VectorVisualizerOnlyTargetWithAttribute", false);
            _autoFocusOnSelection = EditorPrefs.GetBool("VectorVisualizerAutoFocusOnSelection", true);
            _autoFocusOnAdd = EditorPrefs.GetBool("VectorVisualizerAutoFocusOnAdd", true);
            _windowX = EditorPrefs.GetFloat("VectorVisualizerWindowX", 0);
            _windowY = EditorPrefs.GetFloat("VectorVisualizerWindowY", 0);
            _handleZTest = EditorPrefs.GetBool("VectorVisualizerHandleZTest", true);
            _focusEulerAngleX = EditorPrefs.GetFloat("VectorVisualizerFocusEulerAngleX", 30);
            _focusEulerAngleY = EditorPrefs.GetFloat("VectorVisualizerFocusEulerAngleY", 45);
            _focusEulerAngleZ = EditorPrefs.GetFloat("VectorVisualizerFocusEulerAngleZ", 0);
        }

        public void SetWindowPosition(float x, float y)
        {
            _windowX = x;
            _windowY = y;
            EditorPrefs.SetFloat("VectorVisualizerWindowX", x);
            EditorPrefs.SetFloat("VectorVisualizerWindowY", y);
        }

        public void SetFocusDistance(float focusDistance)
        {
            _focusDistance = focusDistance;
            EditorPrefs.SetFloat("VectorVisualizerFocusDistance", focusDistance);
        }

        public void SetOnlyTargetWithAttribute(bool onlyTargetWithAttribute)
        {
#if UNITY_2020_1_OR_NEWER
            _onlyTargetWithAttribute = onlyTargetWithAttribute;
            EditorPrefs.SetBool("VectorVisualizerOnlyTargetWithAttribute", onlyTargetWithAttribute);

            Type t = typeof(NamedBuildTarget);
            var flags = BindingFlags.Public | BindingFlags.Static;

            foreach (var field in t.GetFields(flags))
            {
                if (field.FieldType != typeof(NamedBuildTarget)) continue;

                NamedBuildTarget namedBuildTarget = (NamedBuildTarget)field.GetValue(null);

                // Unknown 或无效的构建目标跳过
                if (namedBuildTarget == NamedBuildTarget.Unknown)
                    continue;

                try
                {
                    // 使用新的 API 获取定义符号
                    string defines = PlayerSettings.GetScriptingDefineSymbols(namedBuildTarget);
                    bool hadDefine = defines.Contains(ATTRIBUTE_SCRIPT_DEFINE);
                    bool shouldHaveDefine = onlyTargetWithAttribute;

                    if (shouldHaveDefine && !hadDefine)
                    {
                        // 添加符号
                        string newDefines = string.IsNullOrEmpty(defines)
                            ? ATTRIBUTE_SCRIPT_DEFINE
                            : $"{defines};{ATTRIBUTE_SCRIPT_DEFINE}";
                        PlayerSettings.SetScriptingDefineSymbols(namedBuildTarget, newDefines);
                    }
                    else if (!shouldHaveDefine && hadDefine)
                    {
                        // 移除符号（注意处理开头、中间、结尾和单独存在的情况）
                        string[] parts = defines.Split(';');
                        string newDefines = string.Join(";", Array.FindAll(parts, define => define != ATTRIBUTE_SCRIPT_DEFINE));
                        PlayerSettings.SetScriptingDefineSymbols(namedBuildTarget, newDefines);
                    }
                }
                catch (Exception e)
                {
                    // 某些构建目标可能不支持（如某些平台未启用），可以忽略
                    Debug.Log($"Failed to update scripting define symbols for {namedBuildTarget}: {e.Message}");
                }
            }
#else
            _onlyTargetWithAttribute = onlyTargetWithAttribute;
            EditorPrefs.SetBool("VectorVisualizerOnlyTargetWithAttribute", onlyTargetWithAttribute);
            
            // Set Scripting Define Symbols for all build target groups
            foreach (BuildTargetGroup buildTargetGroup in (BuildTargetGroup[]) Enum.GetValues(
                         typeof(BuildTargetGroup)))
            {
                
                if (buildTargetGroup == BuildTargetGroup.Unknown) continue;
                
                try
                {
                    var defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(buildTargetGroup);
                    // Add the scripting define symbol if it does not exist
                    if (onlyTargetWithAttribute && !defines.Contains(ATTRIBUTE_SCRIPT_DEFINE))
                    {
                        defines += $";{ATTRIBUTE_SCRIPT_DEFINE}";
                        PlayerSettings.SetScriptingDefineSymbolsForGroup(buildTargetGroup, defines);
                    }

                    // Remove the scripting define symbol if it exists
                    if (!onlyTargetWithAttribute && defines.Contains(ATTRIBUTE_SCRIPT_DEFINE))
                    {
                        // Remove the scripting define symbol
                        defines = defines.Replace($";{ATTRIBUTE_SCRIPT_DEFINE}", "");
                        // Remove the scripting define symbol if it is the first one
                        defines = defines.Replace(ATTRIBUTE_SCRIPT_DEFINE, "");
                        PlayerSettings.SetScriptingDefineSymbolsForGroup(buildTargetGroup, defines);
                    }
                }
                catch
                {
                    // ignored if the build target group does not support scripting define symbols
                }
            }
#endif
        }

        public void SetAutoFocusOnSelection(bool autoFocusOnSelection)
        {
            _autoFocusOnSelection = autoFocusOnSelection;
            EditorPrefs.SetBool("VectorVisualizerAutoFocusOnSelection", autoFocusOnSelection);
        }

        public void SetAutoFocusOnAdd(bool autoFocusOnAdd)
        {
            _autoFocusOnAdd = autoFocusOnAdd;
            EditorPrefs.SetBool("VectorVisualizerAutoFocusOnAdd", autoFocusOnAdd);
        }

        public void SetHandleZTest(bool handleZTest)
        {
            _handleZTest = handleZTest;
            EditorPrefs.SetBool("VectorVisualizerHandleZTest", handleZTest);
        }

        public void SetFocusEulerAngles(Vector3 euler)
        {
            _focusEulerAngleX = euler.x;
            _focusEulerAngleY = euler.y;
            _focusEulerAngleZ = euler.z;
            EditorPrefs.SetFloat("VectorVisualizerFocusEulerAngleX", euler.x);
            EditorPrefs.SetFloat("VectorVisualizerFocusEulerAngleY", euler.y);
            EditorPrefs.SetFloat("VectorVisualizerFocusEulerAngleZ", euler.z);
        }
    }
}
