#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using NonsensicalKit.Simulation.WarehouseSimulation.Config;

namespace NonsensicalKit.Simulation.WarehouseSimulation.Editor
{
    [CustomPropertyDrawer(typeof(SimConveyorMapNode))]
    public sealed class SimConveyorMapNodeDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var line = EditorGUIUtility.singleLineHeight;
            var y = position.y;
            var width = position.width;

            var logicalProp = property.FindPropertyRelative("LogicalId");
            var nodeIdProp = property.FindPropertyRelative("NodeId");
            var kindProp = property.FindPropertyRelative("Kind");
            var kind = kindProp != null ? (SimConveyorNodeKind)kindProp.intValue : SimConveyorNodeKind.Junction;

            var headerRect = new Rect(position.x, y, width, line);
            var header = logicalProp != null && !string.IsNullOrWhiteSpace(logicalProp.stringValue)
                ? $"{label.text} · {logicalProp.stringValue}"
                : label.text;
            EditorGUI.LabelField(headerRect, header, EditorStyles.boldLabel);
            y += line + EditorGUIUtility.standardVerticalSpacing;

            if (logicalProp != null)
            {
                var rect = new Rect(position.x, y, width, line);
                EditorGUI.PropertyField(rect, logicalProp, new GUIContent("逻辑 ID"));
                y += line + EditorGUIUtility.standardVerticalSpacing;
            }

            if (nodeIdProp != null)
            {
                var rect = new Rect(position.x, y, width, line);
                EditorGUI.BeginDisabledGroup(true);
                EditorGUI.PropertyField(rect, nodeIdProp, new GUIContent("节点 ID（不可改）"));
                EditorGUI.EndDisabledGroup();
                y += line + EditorGUIUtility.standardVerticalSpacing;
            }

            if (kindProp != null)
            {
                var rect = new Rect(position.x, y, width, line);
                EditorGUI.PropertyField(rect, kindProp, new GUIContent("节点类型"));
                y += line + EditorGUIUtility.standardVerticalSpacing;
            }

            y = DrawKindFields(position.x, y, width, line, kind, property);

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var kindProp = property.FindPropertyRelative("Kind");
            var kind = kindProp != null ? (SimConveyorNodeKind)kindProp.intValue : SimConveyorNodeKind.Junction;
            var height = EditorGUIUtility.singleLineHeight * 2f
                         + EditorGUIUtility.standardVerticalSpacing * 2f;

            height += PropertyHeight(property, "LogicalId");
            height += PropertyHeight(property, "NodeId");
            height += PropertyHeight(property, "Kind");
            height += KindFieldsHeight(property, kind);
            return height;
        }

        private static float PropertyHeight(SerializedProperty parent, string relativePath)
        {
            var prop = parent.FindPropertyRelative(relativePath);
            if (prop == null)
            {
                return 0f;
            }

            return EditorGUI.GetPropertyHeight(prop, true) + EditorGUIUtility.standardVerticalSpacing;
        }

        private static float KindFieldsHeight(SerializedProperty property, SimConveyorNodeKind kind)
        {
            var height = 0f;
            switch (kind)
            {
                case SimConveyorNodeKind.InfeedPort:
                    height += PropertyHeight(property, "InfeedServiceSeconds");
                    break;
                case SimConveyorNodeKind.PickupPoint:
                    height += PropertyHeight(property, "StackerInteractionMode");
                    height += PropertyHeight(property, "StackerId");
                    height += PropertyHeight(property, "PickupColumn");
                    height += PropertyHeight(property, "PickupRow");
                    break;
                case SimConveyorNodeKind.OutfeedPort:
                    height += PropertyHeight(property, "OutfeedServiceSeconds");
                    break;
            }

            height += PropertyHeight(property, "MaxReservations");
            height += PropertyHeight(property, "EditorPosition");
            return height;
        }

        private static float DrawKindFields(
            float x,
            float y,
            float width,
            float line,
            SimConveyorNodeKind kind,
            SerializedProperty property)
        {
            switch (kind)
            {
                case SimConveyorNodeKind.InfeedPort:
                    y = DrawField(x, y, width, line, property, "InfeedServiceSeconds");
                    break;
                case SimConveyorNodeKind.PickupPoint:
                    y = DrawField(x, y, width, line, property, "StackerInteractionMode");
                    y = DrawField(x, y, width, line, property, "StackerId");
                    y = DrawField(x, y, width, line, property, "PickupColumn");
                    y = DrawField(x, y, width, line, property, "PickupRow");
                    break;
                case SimConveyorNodeKind.OutfeedPort:
                    y = DrawField(x, y, width, line, property, "OutfeedServiceSeconds");
                    break;
            }

            y = DrawField(x, y, width, line, property, "MaxReservations");
            DrawField(x, y, width, line, property, "EditorPosition");
            return y + line;
        }

        private static float DrawField(
            float x,
            float y,
            float width,
            float line,
            SerializedProperty parent,
            string relativePath)
        {
            var prop = parent.FindPropertyRelative(relativePath);
            if (prop == null)
            {
                return y;
            }

            var rect = new Rect(x, y, width, EditorGUI.GetPropertyHeight(prop, true));
            EditorGUI.PropertyField(rect, prop, true);
            return rect.yMax + EditorGUIUtility.standardVerticalSpacing;
        }

    }
}
#endif
