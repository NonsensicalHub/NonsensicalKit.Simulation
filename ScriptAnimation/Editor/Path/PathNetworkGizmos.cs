using UnityEditor;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    public static class PathNetworkGizmos
    {
        private static GUIStyle _labelStyle;
        private static Color _cachedLabelColor;

        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected | GizmoType.Active)]
        private static void DrawPathNodeLabel(PathNode node, GizmoType gizmoType)
        {
            if (node == null)
                return;

            var network = node.Network;
            if (network == null || !network.ShowNodeLabels)
                return;

            float height = Mathf.Max(0f, network.LabelHeight);
            Handles.Label(node.Position + Vector3.up * height, $"[{node.name}]", GetLabelStyle(network.LabelColor));
        }

        private static GUIStyle GetLabelStyle(Color color)
        {
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.LowerCenter,
                    fontSize = 12
                };
            }

            if (_cachedLabelColor != color)
            {
                _labelStyle.normal.textColor = color;
                _labelStyle.hover.textColor = color;
                _labelStyle.active.textColor = color;
                _cachedLabelColor = color;
            }

            return _labelStyle;
        }
    }
}
