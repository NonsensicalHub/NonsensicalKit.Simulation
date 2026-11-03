using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 将 Resources 精灵路径绑定到 Image。
/// JSON 格式：字符串 或 { "value": "Resources相对路径" }
/// </summary>
public class BindingSprite : BindingToken
{
    [SerializeField] private Image m_image;

    /// <summary>最近一次成功绑定的 Resources 路径，供反向导出。</summary>
    [SerializeField] private string m_boundPath;

    private void Reset()
    {
        m_image = GetComponent<Image>();
    }

    public override void BindToken(JToken token)
    {
        if (m_image == null)
        {
            LogWarning($"{name} 缺少 Image");
            return;
        }

        string path = token?.Type == JTokenType.Object
            ? token["value"]?.ToString()
            : token?.ToString();

        m_boundPath = path ?? string.Empty;
        if (string.IsNullOrEmpty(path))
        {
            m_image.sprite = null;
            return;
        }

        var sprite = Resources.Load<Sprite>(path);
        if (sprite == null)
        {
            LogWarning($"Resources.Load<Sprite> 找不到: \"{path}\"");
        }

        m_image.sprite = sprite;
    }

    public override JToken CollectJson()
    {
        if (!string.IsNullOrEmpty(m_boundPath))
        {
            return new JValue(m_boundPath);
        }

        // 未走过正向绑定：尽量给出可辨识提示，避免阻断导出
        string hint = m_image != null && m_image.sprite != null
            ? m_image.sprite.name
            : string.Empty;
        return new JValue(hint);
    }
}
