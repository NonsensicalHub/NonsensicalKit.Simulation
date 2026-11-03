using NonsensicalKit.Core.Service.Config;
using System.Collections.Generic;
using NaughtyAttributes;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UIElements;


namespace Frame.BizKit.WMS.Config
{
[CreateAssetMenu(fileName = "EquipmentImageConfig", menuName = "ScriptableObjects/EquipmentImageConfig")]
public class EquipmentImageConfig : ConfigObject
{
    public EquipmentImageData data;

    public override ConfigData GetData()
    {
        return data;
    }

    public override void SetData(ConfigData cd)
    {
        data = cd as EquipmentImageData;
    }
}

[System.Serializable]
public class EquipmentImageData : ConfigData
{
    public string m_SpritePath;
    public List<EquipmentImage> list_equipmentImage = new List<EquipmentImage>();

    public Sprite this[int type]
    {
        get
        {
            var item = list_equipmentImage?.Find(x => x.m_EquipmentType == type);
            if (item == null)
            {
                Debug.LogWarning($"[EquipmentImageData] 未找到 EquipmentType={type} 的配置。");
                return null;
            }

            return item.GetImg(m_SpritePath);
        }
    }
}

[System.Serializable]
public class EquipmentImage
{
    public string m_Name;
    public int m_EquipmentType;

    [ShowAssetPreview, JsonIgnore]
    public Sprite m_Image;

    public Sprite GetImg(string spritePath)
    {
        return Resources.Load<Sprite>(spritePath + m_Name);
    }
}
}
