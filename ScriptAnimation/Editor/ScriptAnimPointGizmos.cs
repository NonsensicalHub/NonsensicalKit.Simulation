using UnityEditor;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    public static class ScriptAnimPointGizmos
    {
        private static GUIStyle s_labelStyle;
        private static Color s_cachedLabelColor;

        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected | GizmoType.Active)]
        private static void DrawLabel(ScriptAnimPoint point, GizmoType gizmoType)
        {
            if (point == null || !point.ShowLabel || !point.ShowGizmo)
                return;

            float height = Mathf.Max(0f, point.LabelHeight);
            Handles.Label(
                point.Position + Vector3.up * height,
                $"[{point.name}]",
                GetLabelStyle(point.GizmoColor));
        }

        private static GUIStyle GetLabelStyle(Color color)
        {
            if (s_labelStyle == null)
            {
                s_labelStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.LowerCenter,
                    fontSize = 12
                };
            }

            if (s_cachedLabelColor != color)
            {
                s_labelStyle.normal.textColor = color;
                s_labelStyle.hover.textColor = color;
                s_labelStyle.active.textColor = color;
                s_cachedLabelColor = color;
            }

            return s_labelStyle;
        }
    }
}
