#if UNITY_EDITOR
using UnityEditor;
using UnityEngine.UIElements;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Editor
{
    internal static class ConveyorMapGraphViewStyles
    {
        private static StyleSheet s_GraphViewStyle;
        private static StyleSheet s_GraphViewNodeStyle;

        public static void ApplyTo(VisualElement element)
        {
            if (element == null)
            {
                return;
            }

            EnsureLoaded();
            if (s_GraphViewStyle != null)
            {
                element.styleSheets.Add(s_GraphViewStyle);
            }

            if (s_GraphViewNodeStyle != null)
            {
                element.styleSheets.Add(s_GraphViewNodeStyle);
            }
        }

        private static void EnsureLoaded()
        {
            if (s_GraphViewStyle == null)
            {
                s_GraphViewStyle = EditorGUIUtility.Load("StyleSheets/GraphView/GraphView.uss") as StyleSheet;
            }

            if (s_GraphViewNodeStyle == null)
            {
                s_GraphViewNodeStyle = EditorGUIUtility.Load("StyleSheets/GraphView/GraphViewNode.uss") as StyleSheet;
            }
        }
    }
}
#endif
