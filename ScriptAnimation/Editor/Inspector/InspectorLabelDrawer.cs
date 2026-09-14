using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NonsensicalKit.ScriptAnimation.Editor
{
    [CustomPropertyDrawer(typeof(InspectorLabelAttribute))]
    public sealed class InspectorLabelDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var attr = (InspectorLabelAttribute)attribute;
            if (!string.IsNullOrEmpty(attr.Label))
                label = new GUIContent(attr.Label, label.tooltip);

            var range = GetRange();
            if (range != null)
            {
                if (property.propertyType == SerializedPropertyType.Float)
                {
                    EditorGUI.Slider(position, property, range.min, range.max, label);
                    return;
                }

                if (property.propertyType == SerializedPropertyType.Integer)
                {
                    EditorGUI.IntSlider(position, property, (int)range.min, (int)range.max, label);
                    return;
                }
            }

            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(position, property, label, true);
            if (!EditorGUI.EndChangeCheck())
                return;

            ClampMin(property);
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var attr = (InspectorLabelAttribute)attribute;
            if (!string.IsNullOrEmpty(attr.Label))
                label = new GUIContent(attr.Label, label.tooltip);

            var range = GetRange();
            if (range != null &&
                (property.propertyType == SerializedPropertyType.Float ||
                 property.propertyType == SerializedPropertyType.Integer))
            {
                return EditorGUIUtility.singleLineHeight;
            }

            return EditorGUI.GetPropertyHeight(property, label, true);
        }

        private RangeAttribute GetRange()
        {
            if (fieldInfo == null)
                return null;
            return fieldInfo.GetCustomAttributes(typeof(RangeAttribute), true)
                .FirstOrDefault() as RangeAttribute;
        }

        private void ClampMin(SerializedProperty property)
        {
            if (fieldInfo == null)
                return;

            var min = fieldInfo.GetCustomAttributes(typeof(MinAttribute), true)
                .FirstOrDefault() as MinAttribute;
            if (min == null)
                return;

            if (property.propertyType == SerializedPropertyType.Float)
                property.floatValue = Mathf.Max(min.min, property.floatValue);
            else if (property.propertyType == SerializedPropertyType.Integer)
                property.intValue = Mathf.Max((int)min.min, property.intValue);
        }
    }
}
