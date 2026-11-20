using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

using Frame.Core;
using Frame.BizKit.WMS.Config;
namespace Frame.BizKit.WMS.Editor
{
[CustomPropertyDrawer(typeof(SerializeFieldStringAttribute))]
public class SerializeFieldStringPropertyDrawer : PropertyDrawer
{
    private List<string> _cachedTypes;
    private bool _needRefresh = true;

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var temp = (attribute as SerializeFieldStringAttribute);
        if (temp == null) return;

            if (_cachedTypes == null || _needRefresh)
            {
                _cachedTypes = Resources.Load<DeviceIDConfig>(temp.ConfigPath).data.m_DeviceIDs;
                _needRefresh = false;
            }
        

        string crtValue = property.stringValue;
        int index = _cachedTypes.IndexOf(crtValue);

        Rect labelRect = new Rect(position.x, position.y, 80, position.height); // 标签宽度设为80
        Rect popupRect = new Rect(position.x + labelRect.width, position.y, position.width - labelRect.width, position.height);

        EditorGUI.LabelField(labelRect, temp?.Describe);

        EditorGUI.BeginChangeCheck();
        int newIndex = EditorGUI.Popup(popupRect, index, _cachedTypes.ToArray());
        if (EditorGUI.EndChangeCheck() && index != newIndex)
        {
            property.stringValue = _cachedTypes[newIndex];
            _needRefresh = true;
        }
    }
}

}
