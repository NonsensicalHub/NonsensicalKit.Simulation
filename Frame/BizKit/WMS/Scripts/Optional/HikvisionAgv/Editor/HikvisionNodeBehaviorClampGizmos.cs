#if UNITY_EDITOR
using HikvisionAgv;
using NonsensicalKit.ScriptAnimation;
using UnityEditor;
using UnityEngine;

namespace HikvisionAgv.Editor
{
    /// <summary>
    /// 在 Scene 视图绘制点位行为的旋转钳制距离圆，不改动 PathNode / PathNetwork 源码。
    /// 显隐：菜单 Tools/海康AGV/显示点位行为钳制距离。
    /// </summary>
    [InitializeOnLoad]
    static class HikvisionNodeBehaviorClampGizmos
    {
        const string MenuPath = "Tools/海康AGV/显示点位行为钳制距离";
        const string PrefKey = "HikvisionAgv.ShowNodeBehaviorClampGizmos";

        static bool Visible
        {
            get => EditorPrefs.GetBool(PrefKey, false);
            set
            {
                if (EditorPrefs.GetBool(PrefKey, false) == value)
                    return;
                EditorPrefs.SetBool(PrefKey, value);
                SceneView.RepaintAll();
            }
        }

        static HikvisionNodeBehaviorClampGizmos()
        {
            SceneView.duringSceneGui += OnSceneGui;
        }

        [MenuItem(MenuPath, false, 200)]
        static void ToggleMenu()
        {
            Visible = !Visible;
            Menu.SetChecked(MenuPath, Visible);
        }

        [MenuItem(MenuPath, true)]
        static bool ToggleMenuValidate()
        {
            Menu.SetChecked(MenuPath, Visible);
            return true;
        }

        static void OnSceneGui(SceneView sceneView)
        {
            if (!Visible)
                return;

            var networks = Object.FindObjectsByType<PathNetwork>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (networks == null || networks.Length == 0)
                return;

            Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;

            for (int n = 0; n < networks.Length; n++)
            {
                var network = networks[n];
                if (network == null)
                    continue;

                HikvisionNodeBehaviorQuery.ForEachBehavior(network, (node, feature) =>
                {
                    if (feature == null || !feature.AllowInPlaceRotate)
                        return;

                    float radius = feature.RotateClampDistance;
                    if (radius <= 0f)
                        return;

                    Vector3 center = node.Position;
                    center.y += 0.02f;

                    Handles.color = new Color(1f, 0.55f, 0.1f, 0.9f);
                    Handles.DrawWireDisc(center, Vector3.up, radius);

                    Handles.color = new Color(1f, 0.55f, 0.1f, 0.12f);
                    Handles.DrawSolidDisc(center, Vector3.up, radius);

                    Handles.Label(
                        center + Vector3.forward * radius,
                        $"钳制 {radius:0.##}m",
                        EditorStyles.miniLabel);
                });
            }
        }
    }
}
#endif
