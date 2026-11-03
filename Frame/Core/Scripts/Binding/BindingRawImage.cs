using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 绑定 RawImage 纹理（从 Resources 加载 Texture）。
/// JSON：字符串 或 { "value": "Resources相对路径" }
/// </summary>
public class BindingRawImage : BindingToken
{
    [SerializeField] private RawImage m_rawImage;
    [SerializeField] private string m_boundPath;

    private void Reset()
    {
        m_rawImage = GetComponent<RawImage>();
    }

    public override void BindToken(JToken token)
    {
        if (m_rawImage == null)
        {
            LogWarning($"{name} 缺少 RawImage");
            return;
        }

        string path = token?.Type == JTokenType.Object
            ? token["value"]?.ToString()
            : token?.ToString();

        m_boundPath = path ?? string.Empty;
        if (string.IsNullOrEmpty(path))
        {
            m_rawImage.texture = null;
            return;
        }

        var texture = Resources.Load<Texture>(path);
        if (texture == null)
        {
            LogWarning($"Resources.Load<Texture> 找不到: \"{path}\"");
        }

        m_rawImage.texture = texture;
    }

    public override JToken CollectJson()
    {
        if (!string.IsNullOrEmpty(m_boundPath))
        {
            return new JValue(m_boundPath);
        }

        string hint = m_rawImage != null && m_rawImage.texture != null
            ? m_rawImage.texture.name
            : string.Empty;
        return new JValue(hint);
    }
}
