using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using NonsensicalKit.Core.Service.Config;
using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "EquipmentElementIconConfig", menuName = "ScriptableObjects/EquipmentElementIconConfig")]
public class EquipmentElementIconConfig : ConfigObject
{
    public EquipmentElementIconData data;

    public override ConfigData GetData()
    {
        foreach (var icon in data.Types.SelectMany(type => type.m_Config.Where(icon => icon.Sprite != null)))
        {
            icon.m_SpritePath = ExtractResourcePath(icon.Sprite);
        }

        return data;
    }

    public override void SetData(ConfigData cd)
    {
        data = cd as EquipmentElementIconData;
        if (data == null) return;
        foreach (var icon in data.Types.SelectMany(type => type.m_Config))
        {
            icon.GetSprite();
        }
    }

    /// <summary>
    /// 从 Sprite 资源中提取 Resources 相对路径（不含扩展名）
    /// </summary>
    private string ExtractResourcePath(Sprite sprite)
    {
#if UNITY_EDITOR
        string assetPath = UnityEditor.AssetDatabase.GetAssetPath(sprite);
        if (string.IsNullOrEmpty(assetPath)) return string.Empty;

        const string resourcesTag = "/Resources/";
        int index = assetPath.IndexOf(resourcesTag, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            Debug.LogWarning($"Sprite [{sprite.name}] 不在 Resources 目录下，无法提取路径");
            return string.Empty;
        }

        // 截取 Resources/ 之后的路径，并去掉扩展名
        string relativePath = assetPath.Substring(index + resourcesTag.Length);
        return System.IO.Path.ChangeExtension(relativePath, null);
#else
    return string.Empty;
#endif
    }
}

[Serializable]
public class EquipmentElementIconData : ConfigData
{
    public List<IconConfigType> Types = new();
}

[Serializable]
public class IconConfigType
{
    public string m_BelongNode;
    public bool m_IsDefault;
    public List<IconConfig> m_Config;
}

[Serializable]
public class IconConfig
{
    public string Name;
    public int Count = 1;

    public string PrefixID;

    [JsonIgnore]
    public Sprite Sprite;

    public string Type;

    [Tooltip("为True时,生成设备图标已此参数为准")]
    public bool CustomID;

    public string[] CustomIDStrings;

    [HideInInspector] public string m_SpritePath;

    public Sprite GetSprite()
    {
        if (Sprite == null)
        {
            Sprite = Resources.Load<Sprite>(m_SpritePath);
        }

        return Sprite;
    }
}
