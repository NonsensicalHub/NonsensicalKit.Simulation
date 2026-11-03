#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Frame.Core.Attributes;
using UnityEditor;
using UnityEngine;
using Frame.Core.Config;


namespace Frame.Core.Network.Editor
{
[CustomPropertyDrawer(typeof(TopicAttribute))]
public class TopicPropertyDrawer : PropertyDrawer
{
    /// <summary>
    /// 按设备名解析 SN 列表。Core 只处理 URLBaseConfig 主题；
    /// 报警设备表由 BizKit 注册。
    /// </summary>
    public static Func<TopicAttribute, List<string>> ResolveDeviceSnCodes;

    private List<string> _cachedTypes;
    private bool _needRefresh = true;

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var temp = (attribute as TopicAttribute);
        if (temp == null) return;
        if (temp.DeviceSNType == string.Empty)
        {
            if (_cachedTypes == null || _needRefresh)
            {
                _cachedTypes = Resources.Load<URLBaseConfig>(temp.ConfigPath).data.MqttTopics.Select(x => x.TopicName).ToList();
                _needRefresh = false;
            }
        }
        else if (temp.DeviceSNType.Contains("主题"))
        {
            if (_cachedTypes == null || _needRefresh)
            {
                _cachedTypes = Resources.Load<URLBaseConfig>(temp.ConfigPath).data.MqttTopics.Find(x => x.TopicName == temp.DeviceSNType).SNCodes.ToList();
                _needRefresh = false;
            }
        }
        else
        {
            if (_cachedTypes == null || _needRefresh)
            {
                _cachedTypes = ResolveDeviceSnCodes?.Invoke(temp) ?? new List<string>();
                _needRefresh = false;
            }
        }

        if (_cachedTypes == null)
        {
            _cachedTypes = new List<string>();
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
#endif
